using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows.Media;

namespace Hypershell.ViewModels;

public sealed class NetworkClientTabViewModel : ObservableObject, IWorkspaceTab
{
    private const int MaximumTranscriptEntries = 5000;

    private readonly NetworkClientLaunchRequest _launchRequest;
    private NetworkClientTabView? _networkView;
    private TcpClient? _tcpClient;
    private UdpClient? _udpClient;
    private Stream? _stream;
    private CancellationTokenSource? _sessionCancellation;
    private string _host;
    private string _portText;
    private string _localBindAddress = string.Empty;
    private string _localBindPortText = "0";
    private bool _useTls;
    private bool _allowInvalidCertificate;
    private string _sniHost = string.Empty;
    private NetworkPayloadFormat _payloadFormat = NetworkPayloadFormat.Text;
    private NetworkLineEnding _lineEnding = NetworkLineEnding.CrLf;
    private NetworkTextEncodingKind _textEncoding = NetworkTextEncodingKind.Utf8;
    private string _inputText = string.Empty;
    private string _statusMessage = "Ready.";
    private string _certificateDetails = string.Empty;
    private bool _isConnected;
    private bool _isBusy;
    private bool _isAiLiveAssistEnabled;
    private bool _isDisposed;

    public NetworkClientTabViewModel(NetworkClientLaunchRequest request)
    {
        _launchRequest = request;
        SessionId = Guid.NewGuid().ToString("N");
        _host = request.Host;
        _portText = request.Port > 0 ? request.Port.ToString() : string.Empty;
    }

    public WorkspaceTabKind Kind => WorkspaceTabKind.Network;
    public string SessionId { get; }
    public string AiLiveSourceId => $"network:{SessionId}";
    public NetworkClientTabView NetworkView =>
        _networkView ??= new NetworkClientTabView(this);
    public NetworkTransport Transport => _launchRequest.Transport;
    public bool IsTcp => Transport == NetworkTransport.Tcp;
    public bool IsUdp => Transport == NetworkTransport.Udp;
    public bool AutoConnect => _launchRequest.AutoConnect;
    public string ContextDescription => _launchRequest.ContextDescription;
    public IReadOnlyList<NetworkPayloadFormat> PayloadFormats { get; } =
        Enum.GetValues<NetworkPayloadFormat>();
    public IReadOnlyList<NetworkLineEnding> LineEndings { get; } =
        Enum.GetValues<NetworkLineEnding>();
    public IReadOnlyList<NetworkTextEncodingKind> TextEncodings { get; } =
        Enum.GetValues<NetworkTextEncodingKind>();
    public ObservableCollection<NetworkTranscriptEntry> Transcript { get; } = [];

    public string Title => string.IsNullOrWhiteSpace(Host) || !TryGetPort(out var port)
        ? $"{Transport.ToString().ToUpperInvariant()} Client"
        : $"{Transport.ToString().ToUpperInvariant()} {Host}:{port}";
    public string DisplayTitle => Title;
    public string RenameText
    {
        get => Title;
        set
        {
        }
    }

    public bool IsRenaming => false;
    public bool CanRename => false;
    public bool HasUnsavedChanges => false;
    public string TabColor => "#163B3A";
    public Brush TabBackgroundBrush { get; } =
        new SolidColorBrush(Color.FromRgb(22, 59, 58));
    public Brush TabForegroundBrush { get; } =
        new SolidColorBrush(Color.FromRgb(230, 237, 243));

    public string Host
    {
        get => _host;
        set
        {
            if (SetProperty(ref _host, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public string PortText
    {
        get => _portText;
        set
        {
            if (SetProperty(ref _portText, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public string LocalBindAddress
    {
        get => _localBindAddress;
        set => SetProperty(ref _localBindAddress, value?.Trim() ?? string.Empty);
    }

    public string LocalBindPortText
    {
        get => _localBindPortText;
        set => SetProperty(ref _localBindPortText, value?.Trim() ?? "0");
    }

    public bool UseTls
    {
        get => _useTls;
        set => SetProperty(ref _useTls, value);
    }

    public bool AllowInvalidCertificate
    {
        get => _allowInvalidCertificate;
        set => SetProperty(ref _allowInvalidCertificate, value);
    }

    public string SniHost
    {
        get => _sniHost;
        set => SetProperty(ref _sniHost, value?.Trim() ?? string.Empty);
    }

    public NetworkPayloadFormat PayloadFormat
    {
        get => _payloadFormat;
        set => SetProperty(ref _payloadFormat, value);
    }

    public NetworkLineEnding LineEnding
    {
        get => _lineEnding;
        set => SetProperty(ref _lineEnding, value);
    }

    public NetworkTextEncodingKind TextEncoding
    {
        get => _textEncoding;
        set => SetProperty(ref _textEncoding, value);
    }

    public string InputText
    {
        get => _inputText;
        set => SetProperty(ref _inputText, value ?? string.Empty);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string CertificateDetails
    {
        get => _certificateDetails;
        private set
        {
            if (SetProperty(ref _certificateDetails, value))
            {
                OnPropertyChanged(nameof(HasCertificateDetails));
            }
        }
    }

    public bool HasCertificateDetails =>
        !string.IsNullOrWhiteSpace(CertificateDetails);

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(CanEditConnection));
                OnPropertyChanged(nameof(CanSend));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanEditConnection));
                OnPropertyChanged(nameof(CanSend));
            }
        }
    }

    public bool CanEditConnection => !IsConnected && !IsBusy;
    public bool CanSend => IsConnected && !IsBusy;

    public bool IsAiLiveAssistEnabled
    {
        get => _isAiLiveAssistEnabled;
        set => SetProperty(ref _isAiLiveAssistEnabled, value);
    }

    public async Task<bool> RequiresPublicConfirmationAsync(
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            return false;
        }

        return await NetworkAddressPolicy.IsPublicDestinationAsync(
            Host,
            cancellationToken);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (IsConnected || IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Host) || !TryGetPort(out var port))
        {
            StatusMessage = "Enter a host and a port from 1 through 65535.";
            return;
        }

        IsBusy = true;
        CertificateDetails = string.Empty;
        StatusMessage = $"Connecting to {Host}:{port}…";
        _sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        try
        {
            if (IsTcp)
            {
                await ConnectTcpAsync(Host, port, _sessionCancellation.Token);
            }
            else
            {
                await ConnectUdpAsync(Host, port, _sessionCancellation.Token);
            }

            IsConnected = true;
            StatusMessage = $"Connected to {Host}:{port}.";
            AddTranscript("STATUS", StatusMessage, 0);
            _ = IsTcp
                ? ReceiveTcpAsync(_sessionCancellation.Token)
                : ReceiveUdpAsync(_sessionCancellation.Token);
        }
        catch (Exception exception) when (exception is SocketException
                                           or IOException
                                           or AuthenticationException
                                           or InvalidOperationException
                                           or ArgumentException
                                           or OperationCanceledException)
        {
            DisconnectCore();
            StatusMessage = exception is OperationCanceledException
                ? "Connection cancelled."
                : $"Connection failed: {exception.Message}";
            AddTranscript("ERROR", StatusMessage, 0);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSend)
        {
            return;
        }

        try
        {
            var bytes = CreatePayload(InputText);
            if (bytes.Length == 0)
            {
                StatusMessage = "Nothing to send.";
                return;
            }

            if (IsTcp)
            {
                if (_stream is null)
                {
                    throw new InvalidOperationException("The TCP stream is unavailable.");
                }

                await _stream.WriteAsync(bytes, cancellationToken);
                await _stream.FlushAsync(cancellationToken);
            }
            else
            {
                if (_udpClient is null)
                {
                    throw new InvalidOperationException("The UDP client is unavailable.");
                }

                await _udpClient.SendAsync(bytes, cancellationToken);
            }

            AddTranscript("OUT", FormatPayload(bytes), bytes.Length);
            StatusMessage = $"Sent {bytes.Length} byte{(bytes.Length == 1 ? string.Empty : "s")}.";
        }
        catch (Exception exception) when (exception is SocketException
                                           or IOException
                                           or InvalidOperationException
                                           or FormatException
                                           or OperationCanceledException)
        {
            StatusMessage = $"Send failed: {exception.Message}";
            AddTranscript("ERROR", StatusMessage, 0);
        }
    }

    public void Disconnect()
    {
        if (!IsConnected && !IsBusy)
        {
            return;
        }

        DisconnectCore();
        StatusMessage = "Disconnected.";
        AddTranscript("STATUS", StatusMessage, 0);
    }

    public string CreateTranscriptText() => string.Join(
        Environment.NewLine,
        Transcript.Select(entry =>
            $"{entry.Timestamp:O}\t{entry.Direction}\t{entry.ByteCount}\t{entry.Text}"));

    public void ClearTranscript() => Transcript.Clear();

    private async Task ConnectTcpAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port, cancellationToken);
        var networkStream = _tcpClient.GetStream();
        if (!UseTls)
        {
            _stream = networkStream;
            return;
        }

        var certificateDetails = string.Empty;
        var sslStream = new SslStream(
            networkStream,
            leaveInnerStreamOpen: false,
            (_, certificate, chain, errors) =>
            {
                certificateDetails = CreateCertificateDetails(
                    certificate,
                    chain,
                    errors);
                return errors == SslPolicyErrors.None || AllowInvalidCertificate;
            });
        var targetHost = string.IsNullOrWhiteSpace(SniHost) ? host : SniHost;
        await sslStream.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions
            {
                TargetHost = targetHost,
                EnabledSslProtocols = SslProtocols.None,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            },
            cancellationToken);
        _stream = sslStream;
        if (string.IsNullOrWhiteSpace(certificateDetails)
            && sslStream.RemoteCertificate is { } certificate)
        {
            certificateDetails = CreateCertificateDetails(
                certificate,
                chain: null,
                SslPolicyErrors.None);
        }

        CertificateDetails = certificateDetails;
    }

    private async Task ConnectUdpAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        var remoteAddress = addresses.FirstOrDefault(address =>
                                address.AddressFamily is AddressFamily.InterNetwork
                                    or AddressFamily.InterNetworkV6)
                            ?? throw new SocketException(
                                (int)SocketError.HostNotFound);
        _udpClient = new UdpClient(remoteAddress.AddressFamily);
        if (TryGetLocalBindPort(out var bindPort) && bindPort > 0)
        {
            var bindAddress = ResolveBindAddress(
                LocalBindAddress,
                remoteAddress.AddressFamily);
            _udpClient.Client.Bind(new IPEndPoint(bindAddress, bindPort));
        }

        _udpClient.Connect(new IPEndPoint(remoteAddress, port));
    }

    private async Task ReceiveTcpAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream is not null)
            {
                var read = await _stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    StatusMessage = "The remote endpoint closed the connection.";
                    break;
                }

                var bytes = buffer.AsSpan(0, read).ToArray();
                AddTranscript("IN", FormatPayload(bytes), read);
            }
        }
        catch (Exception exception) when (exception is IOException
                                           or SocketException
                                           or OperationCanceledException
                                           or ObjectDisposedException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                StatusMessage = $"Receive stopped: {exception.Message}";
                AddTranscript("ERROR", StatusMessage, 0);
            }
        }
        finally
        {
            DisconnectCore();
        }
    }

    private async Task ReceiveUdpAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _udpClient is not null)
            {
                var received = await _udpClient.ReceiveAsync(cancellationToken);
                AddTranscript(
                    $"IN {received.RemoteEndPoint}",
                    FormatPayload(received.Buffer),
                    received.Buffer.Length);
            }
        }
        catch (Exception exception) when (exception is SocketException
                                           or OperationCanceledException
                                           or ObjectDisposedException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                StatusMessage = $"Receive stopped: {exception.Message}";
                AddTranscript("ERROR", StatusMessage, 0);
            }
        }
        finally
        {
            DisconnectCore();
        }
    }

    private byte[] CreatePayload(string input)
    {
        if (PayloadFormat == NetworkPayloadFormat.Hex)
        {
            var hex = new string(input
                .Where(character => !char.IsWhiteSpace(character)
                                    && character is not '-' and not ':')
                .ToArray());
            if (hex.Length % 2 != 0)
            {
                throw new FormatException("Hex input must contain complete byte pairs.");
            }

            return Convert.FromHexString(hex);
        }

        var suffix = LineEnding switch
        {
            NetworkLineEnding.Lf => "\n",
            NetworkLineEnding.CrLf => "\r\n",
            _ => string.Empty
        };
        return ResolveEncoding().GetBytes(input + suffix);
    }

    private string FormatPayload(byte[] bytes) =>
        PayloadFormat == NetworkPayloadFormat.Hex
            ? string.Join(
                " ",
                Convert.ToHexString(bytes)
                    .Chunk(2)
                    .Select(chunk => new string(chunk)))
            : ResolveEncoding().GetString(bytes);

    private Encoding ResolveEncoding() => TextEncoding switch
    {
        NetworkTextEncodingKind.Ascii => Encoding.ASCII,
        NetworkTextEncodingKind.Utf16LittleEndian => Encoding.Unicode,
        NetworkTextEncodingKind.Latin1 => Encoding.Latin1,
        _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
    };

    private void AddTranscript(string direction, string text, int byteCount)
    {
        Transcript.Add(new NetworkTranscriptEntry(
            DateTimeOffset.Now,
            direction,
            text,
            byteCount));
        while (Transcript.Count > MaximumTranscriptEntries)
        {
            Transcript.RemoveAt(0);
        }
    }

    private bool TryGetPort(out int port) =>
        int.TryParse(
            PortText,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out port)
        && port is >= 1 and <= 65535;

    private bool TryGetLocalBindPort(out int port) =>
        int.TryParse(
            LocalBindPortText,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out port)
        && port is >= 0 and <= 65535;

    private static IPAddress ResolveBindAddress(
        string input,
        AddressFamily family)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return family == AddressFamily.InterNetworkV6
                ? IPAddress.IPv6Any
                : IPAddress.Any;
        }

        if (!IPAddress.TryParse(input, out var address)
            || address.AddressFamily != family)
        {
            throw new ArgumentException(
                "The local bind address does not match the destination address family.");
        }

        return address;
    }

    private static string CreateCertificateDetails(
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors errors)
    {
        if (certificate is null)
        {
            return $"No remote certificate was supplied. Validation: {errors}.";
        }

        using var certificate2 = new X509Certificate2(certificate);
        var chainStatus = chain is null || chain.ChainStatus.Length == 0
            ? string.Empty
            : string.Join(
                "; ",
                chain.ChainStatus.Select(status => status.StatusInformation.Trim()));
        return string.Join(
            Environment.NewLine,
            $"Subject: {certificate2.Subject}",
            $"Issuer: {certificate2.Issuer}",
            $"Thumbprint: {certificate2.Thumbprint}",
            $"Valid: {certificate2.NotBefore:u} through {certificate2.NotAfter:u}",
            $"Validation: {errors}",
            string.IsNullOrWhiteSpace(chainStatus)
                ? "Chain: no additional status"
                : $"Chain: {chainStatus}");
    }

    private void DisconnectCore()
    {
        _sessionCancellation?.Cancel();
        _sessionCancellation?.Dispose();
        _sessionCancellation = null;
        _stream?.Dispose();
        _stream = null;
        _tcpClient?.Dispose();
        _tcpClient = null;
        _udpClient?.Dispose();
        _udpClient = null;
        IsConnected = false;
        IsBusy = false;
    }

    public void BeginRename()
    {
    }

    public void CommitRename()
    {
    }

    public void CancelRename()
    {
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        DisconnectCore();
    }
}
