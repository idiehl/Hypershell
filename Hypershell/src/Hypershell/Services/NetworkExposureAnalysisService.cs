using Hypershell.Models;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Hypershell.Services;

public interface INetworkExposureAnalysisService
{
    Task<IReadOnlyList<NetworkExposureSnapshot>> AnalyzeAsync(
        NetworkOverviewSnapshot overview,
        IReadOnlyList<NetworkSocketSnapshot> sockets,
        CancellationToken cancellationToken = default);
}

public sealed class NetworkExposureAnalysisService
    : INetworkExposureAnalysisService
{
    private static readonly HashSet<int> AdministrativePorts =
    [
        22, 135, 139, 445, 3389, 50080, 50443, 5985, 5986
    ];

    public async Task<IReadOnlyList<NetworkExposureSnapshot>> AnalyzeAsync(
        NetworkOverviewSnapshot overview,
        IReadOnlyList<NetworkSocketSnapshot> sockets,
        CancellationToken cancellationToken = default)
    {
        var listeners = sockets
            .Where(socket => (socket.IsListener
                              || socket.Transport == NetworkTransport.Udp)
                             && socket.LocalPort > 0)
            .ToList();
        using var concurrency = new SemaphoreSlim(8, 8);
        var tasks = listeners.Select(async socket =>
        {
            var bindScope = ClassifyBindScope(socket, overview.Interfaces);
            var profiles = ResolveProfiles(socket, overview.Interfaces);
            var firewall = EvaluateFirewall(
                socket,
                profiles,
                overview.FirewallProfiles,
                overview.FirewallRules);
            NetworkCertificateSnapshot? certificate = null;
            if (socket.Transport == NetworkTransport.Tcp)
            {
                await concurrency.WaitAsync(cancellationToken);
                try
                {
                    certificate = await TryInspectCertificateAsync(
                        socket,
                        cancellationToken);
                }
                finally
                {
                    concurrency.Release();
                }
            }

            var risk = ClassifyRisk(
                socket.LocalPort,
                bindScope,
                firewall.Signal);
            var profileText = profiles.Count == 0
                ? "Unknown"
                : string.Join(", ", profiles);
            return new NetworkExposureSnapshot(
                socket.Key,
                socket.Transport,
                socket.LocalAddress,
                socket.LocalPort,
                socket.ProcessName,
                socket.ProcessId,
                socket.ExecutablePath,
                bindScope,
                firewall.Signal,
                firewall.Scope,
                profileText,
                risk,
                certificate,
                BuildExplanation(bindScope, firewall.Signal, profileText));
        });

        return (await Task.WhenAll(tasks))
            .OrderByDescending(item => item.Risk)
            .ThenBy(item => item.LocalPort)
            .ThenBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static NetworkBindScope ClassifyBindScope(
        NetworkSocketSnapshot socket,
        IReadOnlyList<NetworkInterfaceSnapshot> interfaces)
    {
        if (!IPAddress.TryParse(socket.LocalAddress, out var address))
        {
            return NetworkBindScope.Unknown;
        }

        if (IPAddress.IsLoopback(address))
        {
            return NetworkBindScope.LoopbackOnly;
        }

        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return NetworkBindScope.AllInterfaces;
        }

        var matchingInterface = interfaces.FirstOrDefault(item =>
            item.Addresses.Any(value => string.Equals(
                value.Split('/')[0],
                socket.LocalAddress,
                StringComparison.OrdinalIgnoreCase)));
        if (matchingInterface is null)
        {
            return address.IsIPv6LinkLocal
                   || IsPrivateIpv4(address)
                ? NetworkBindScope.LanAddress
                : NetworkBindScope.Unknown;
        }

        return matchingInterface.IsVirtual
            ? NetworkBindScope.VirtualOrVpn
            : NetworkBindScope.LanAddress;
    }

    public static FirewallEvaluation EvaluateFirewall(
        NetworkSocketSnapshot socket,
        IReadOnlyList<string> profiles,
        IReadOnlyList<NetworkFirewallProfileSnapshot> firewallProfiles,
        IReadOnlyList<NetworkFirewallRuleSnapshot> rules)
    {
        if (profiles.Count == 0)
        {
            return new FirewallEvaluation(
                NetworkFirewallSignal.Unknown,
                "profile unavailable");
        }

        var relevantProfiles = firewallProfiles
            .Where(item => profiles.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (relevantProfiles.Count > 0 && relevantProfiles.All(item => !item.Enabled))
        {
            return new FirewallEvaluation(
                NetworkFirewallSignal.FirewallDisabled,
                "all matching profiles");
        }

        var matchingRules = rules
            .Where(rule => RuleMatches(rule, socket, profiles))
            .ToList();
        var blockingRule = matchingRules.FirstOrDefault(rule => string.Equals(
            rule.Action,
            "Block",
            StringComparison.OrdinalIgnoreCase));
        if (blockingRule is not null)
        {
            return new FirewallEvaluation(
                NetworkFirewallSignal.BlockedOrNoAllowRule,
                $"blocked by {blockingRule.Name}");
        }

        var allowingRules = matchingRules
            .Where(rule => string.Equals(
                rule.Action,
                "Allow",
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (allowingRules.Count > 0)
        {
            return new FirewallEvaluation(
                NetworkFirewallSignal.Allowed,
                SummarizeRemoteScope(allowingRules));
        }

        if (relevantProfiles.Count > 0)
        {
            var defaultAllows = relevantProfiles.Any(item =>
                string.Equals(
                    item.DefaultInboundAction,
                    "Allow",
                    StringComparison.OrdinalIgnoreCase));
            return defaultAllows
                ? new FirewallEvaluation(
                    NetworkFirewallSignal.Allowed,
                    "profile default allows inbound")
                : new FirewallEvaluation(
                    NetworkFirewallSignal.BlockedOrNoAllowRule,
                    "profile default blocks inbound");
        }

        return new FirewallEvaluation(
            NetworkFirewallSignal.Unknown,
            "effective firewall profile unavailable");
    }

    public static bool PortSpecMatches(
        IReadOnlyList<string> specifications,
        int port)
    {
        if (specifications.Count == 0)
        {
            return true;
        }

        foreach (var value in specifications.SelectMany(item => item.Split(
                     ',',
                     StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries)))
        {
            if (value is "*" || value.Equals("Any", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (int.TryParse(value, out var exact) && exact == port)
            {
                return true;
            }

            var range = value.Split('-', StringSplitOptions.TrimEntries);
            if (range.Length == 2
                && int.TryParse(range[0], out var start)
                && int.TryParse(range[1], out var end)
                && port >= Math.Min(start, end)
                && port <= Math.Max(start, end))
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<NetworkServiceCandidateSnapshot>
        CreateServiceCandidates(IReadOnlyList<NetworkExposureSnapshot> exposures)
    {
        return
        [
            CreateCandidate(
                exposures,
                "Secure Shell",
                "Remote PowerShell, commands and secure file transfer",
                [22],
                "Prefer key authentication and restrict the firewall to a trusted client."),
            CreateCandidate(
                exposures,
                "Remote Desktop",
                "Full graphical control from another computer",
                [3389],
                "Keep Network Level Authentication enabled; use only on a trusted LAN or VPN."),
            CreateCandidate(
                exposures,
                "SMB file share",
                "Share a dedicated folder or printer with authenticated users",
                [445],
                "Avoid guest access and never forward TCP 445 to the public Internet."),
            CreateCandidate(
                exposures,
                "Windows Device Portal",
                "Browser-based diagnostics and device administration",
                [50080, 50443],
                "Use HTTPS and a unique Device Portal credential; scope access narrowly."),
            CreateCandidate(
                exposures,
                "Local web apps",
                "Hypershell, Ping Lab, dashboards and development tools",
                [80, 443, 5000, 5001, 5190, 5197, 8000, 8080, 8443],
                "Add authentication before changing a loopback-only app to a LAN binding.")
        ];
    }

    private static NetworkServiceCandidateSnapshot CreateCandidate(
        IReadOnlyList<NetworkExposureSnapshot> exposures,
        string name,
        string purpose,
        IReadOnlyList<int> ports,
        string guidance)
    {
        var matches = exposures
            .Where(item => ports.Contains(item.LocalPort))
            .ToList();
        if (matches.Count == 0)
        {
            return new NetworkServiceCandidateSnapshot(
                name,
                purpose,
                "Not listening",
                "—",
                guidance,
                false);
        }

        return new NetworkServiceCandidateSnapshot(
            name,
            purpose,
            string.Join(", ", matches.Select(item => item.Classification)
                .Distinct(StringComparer.OrdinalIgnoreCase)),
            string.Join(", ", matches.Select(item => item.Endpoint)
                .Distinct(StringComparer.OrdinalIgnoreCase)),
            guidance,
            true);
    }

    private static IReadOnlyList<string> ResolveProfiles(
        NetworkSocketSnapshot socket,
        IReadOnlyList<NetworkInterfaceSnapshot> interfaces)
    {
        if (!IPAddress.TryParse(socket.LocalAddress, out var address)
            || IPAddress.IsLoopback(address))
        {
            return [];
        }

        var candidates = address.Equals(IPAddress.Any)
                         || address.Equals(IPAddress.IPv6Any)
            ? interfaces.Where(item => item.Status == OperationalStatus.Up)
            : interfaces.Where(item => item.Addresses.Any(value => string.Equals(
                value.Split('/')[0],
                socket.LocalAddress,
                StringComparison.OrdinalIgnoreCase)));
        return candidates
            .Select(item => item.NetworkCategory)
            .SelectMany(value => value.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool RuleMatches(
        NetworkFirewallRuleSnapshot rule,
        NetworkSocketSnapshot socket,
        IReadOnlyList<string> profiles)
    {
        if (!ProfileMatches(rule.Profiles, profiles)
            || !ProtocolMatches(rule.Protocols, socket.Transport)
            || !PortSpecMatches(rule.LocalPorts, socket.LocalPort)
            || !ProgramMatches(rule.Programs, socket.ExecutablePath))
        {
            return false;
        }

        return AddressMatches(rule.LocalAddresses, socket.LocalAddress);
    }

    private static bool ProfileMatches(
        IReadOnlyList<string> ruleProfiles,
        IReadOnlyList<string> activeProfiles)
    {
        if (ruleProfiles.Count == 0
            || ruleProfiles.Any(value => value.Equals(
                "Any",
                StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var tokens = ruleProfiles.SelectMany(Tokenize).ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        return activeProfiles.Any(tokens.Contains);
    }

    private static bool ProtocolMatches(
        IReadOnlyList<string> protocols,
        NetworkTransport transport)
    {
        if (protocols.Count == 0)
        {
            return true;
        }

        var expectedName = transport == NetworkTransport.Tcp ? "TCP" : "UDP";
        var expectedNumber = transport == NetworkTransport.Tcp ? "6" : "17";
        return protocols.SelectMany(Tokenize).Any(value =>
            value.Equals("Any", StringComparison.OrdinalIgnoreCase)
            || value is "*" or "256"
            || value.Equals(expectedName, StringComparison.OrdinalIgnoreCase)
            || value == expectedNumber);
    }

    private static bool ProgramMatches(
        IReadOnlyList<string> programs,
        string executablePath)
    {
        var filters = programs
            .SelectMany(Tokenize)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        if (filters.Count == 0
            || filters.Any(value => value is "*"
                || value.Equals("Any", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var expanded = Environment.ExpandEnvironmentVariables(executablePath);
        return filters.Any(value => string.Equals(
            Environment.ExpandEnvironmentVariables(value),
            expanded,
            StringComparison.OrdinalIgnoreCase));
    }

    private static bool AddressMatches(
        IReadOnlyList<string> addresses,
        string localAddress)
    {
        var filters = addresses.SelectMany(Tokenize).ToList();
        if (filters.Count == 0
            || filters.Any(value => value is "*"
                || value.Equals("Any", StringComparison.OrdinalIgnoreCase)
                || value.Equals("LocalSubnet", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return filters.Any(value => string.Equals(
            value,
            localAddress,
            StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> Tokenize(string value) => value.Split(
        [',', ' '],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string SummarizeRemoteScope(
        IReadOnlyList<NetworkFirewallRuleSnapshot> rules)
    {
        var scopes = rules
            .SelectMany(rule => rule.RemoteAddresses)
            .SelectMany(Tokenize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (scopes.Count == 0
            || scopes.Any(value => value is "*"
                || value.Equals("Any", StringComparison.OrdinalIgnoreCase)))
        {
            return "any remote address permitted by rule";
        }

        if (scopes.All(value => value.Equals(
                "LocalSubnet",
                StringComparison.OrdinalIgnoreCase)))
        {
            return "local subnet only";
        }

        return "restricted remote addresses";
    }

    private static NetworkExposureRisk ClassifyRisk(
        int port,
        NetworkBindScope bindScope,
        NetworkFirewallSignal firewallSignal)
    {
        if (bindScope == NetworkBindScope.LoopbackOnly)
        {
            return NetworkExposureRisk.Local;
        }

        if (firewallSignal == NetworkFirewallSignal.BlockedOrNoAllowRule)
        {
            return NetworkExposureRisk.Low;
        }

        if (firewallSignal == NetworkFirewallSignal.FirewallDisabled
            || AdministrativePorts.Contains(port)
               && firewallSignal == NetworkFirewallSignal.Allowed)
        {
            return NetworkExposureRisk.Elevated;
        }

        return NetworkExposureRisk.Review;
    }

    private static string BuildExplanation(
        NetworkBindScope bindScope,
        NetworkFirewallSignal firewallSignal,
        string profiles)
    {
        var binding = bindScope switch
        {
            NetworkBindScope.LoopbackOnly => "The process accepts connections only from this PC.",
            NetworkBindScope.LanAddress => "The process is bound to a LAN address.",
            NetworkBindScope.AllInterfaces => "The process is bound to every matching interface.",
            NetworkBindScope.VirtualOrVpn => "The process is bound to a virtual or VPN interface.",
            _ => "The listener binding could not be classified."
        };
        var firewall = firewallSignal switch
        {
            NetworkFirewallSignal.Allowed => "A matching inbound allow rule was observed.",
            NetworkFirewallSignal.BlockedOrNoAllowRule =>
                "No effective inbound allow was observed on the active profile.",
            NetworkFirewallSignal.FirewallDisabled =>
                "Windows Firewall is disabled for the matching profile.",
            _ => "Firewall reachability could not be confirmed."
        };
        return $"{binding} {firewall} Profiles: {profiles}. Router/NAT exposure is not inspected.";
    }

    private static async Task<NetworkCertificateSnapshot?> TryInspectCertificateAsync(
        NetworkSocketSnapshot socket,
        CancellationToken cancellationToken)
    {
        var host = AtlasMyNetWebService.NormalizeListenerAddress(
            socket.LocalAddress,
            socket.AddressFamily);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromMilliseconds(650));
        try
        {
            using var client = new TcpClient(socket.AddressFamily);
            await client.ConnectAsync(host, socket.LocalPort, timeoutSource.Token);
            SslPolicyErrors policyErrors = SslPolicyErrors.None;
            using var ssl = new SslStream(
                client.GetStream(),
                leaveInnerStreamOpen: false,
                (_, _, _, errors) =>
                {
                    policyErrors = errors;
                    return true;
                });
            await ssl.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = host,
                    EnabledSslProtocols = SslProtocols.None,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                },
                timeoutSource.Token);
            if (ssl.RemoteCertificate is null)
            {
                return null;
            }

            using var certificate = X509CertificateLoader.LoadCertificate(
                ssl.RemoteCertificate.Export(X509ContentType.Cert));
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            var chainTrusted = chain.Build(certificate);
            var nameMismatch = policyErrors.HasFlag(
                SslPolicyErrors.RemoteCertificateNameMismatch);
            var trustStatus = new List<string>();
            if (nameMismatch)
            {
                trustStatus.Add("name mismatch");
            }

            trustStatus.AddRange(chain.ChainStatus
                .Select(status => status.StatusInformation.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            if (trustStatus.Count == 0)
            {
                trustStatus.Add("Windows trust chain valid");
            }

            return new NetworkCertificateSnapshot(
                certificate.Subject,
                certificate.Issuer,
                certificate.Thumbprint,
                certificate.NotBefore,
                certificate.NotAfter,
                chainTrusted && !nameMismatch,
                string.Join(" · ", trustStatus.Distinct(
                    StringComparer.OrdinalIgnoreCase)));
        }
        catch (Exception exception) when (exception is SocketException
                                           or IOException
                                           or AuthenticationException
                                           or OperationCanceledException
                                           or InvalidOperationException)
        {
            if (exception is OperationCanceledException
                && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return null;
        }
    }

    private static bool IsPrivateIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
               || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
               || bytes[0] == 192 && bytes[1] == 168
               || bytes[0] == 169 && bytes[1] == 254;
    }

    public sealed record FirewallEvaluation(
        NetworkFirewallSignal Signal,
        string Scope);
}
