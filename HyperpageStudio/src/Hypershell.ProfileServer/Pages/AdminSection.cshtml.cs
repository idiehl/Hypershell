using Hypershell.Profiles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Hypershell.ProfileServer.Pages;

[RequestSizeLimit(Hypershell.Profiles.HypershellCloudContract.MaximumSshCredentialMutationBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = Hypershell.Profiles.HypershellCloudContract.MaximumSshCredentialMutationBytes)]
public sealed class AdminSectionModel : PageModel
{
    private static readonly IReadOnlyDictionary<string, SectionDefinition> Definitions =
        new Dictionary<string, SectionDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["downloads"] = new("↓", "Downloads", "Signed Windows installers and portable recovery builds.", "No release published", "The signed release pipeline will populate this section."),
            ["client-pcs"] = new("▣", "Client PCs", "Signed-in Hypershell installations.", "No client PCs yet", "The first successful desktop sign-in will register its revocable rotating credential here."),
            ["linked-hosts"] = new("⌘", "Linked Hosts", "SSH devices and pinned connection identities.", "No linked host loaded", "The encrypted SSH credential and host registry will populate this section."),
            ["profiles"] = new("◎", "Profiles", "Portable workspace configurations.", "Profile API online", "Existing profile-v1 data remains available while profile-v2 resources are added."),
            ["sessions"] = new("◷", "Sessions", "Browser and desktop authentication sessions.", "Current browser session active", "Session inventory and revocation controls will appear here."),
            ["storage"] = new("▰", "Storage", "Cloud metadata and content-addressed data.", "SQLite metadata online", "The dedicated block volume will replace the temporary system-disk location."),
            ["backups"] = new("↺", "Backups", "Snapshots, encrypted exports, and restore evidence.", "Consistent baseline captured", "Verified backup inventory and export downloads will appear here.")
        };

    private readonly RemoteHostStore _hosts;
    private readonly SshCredentialVaultStore _credentials;

    public AdminSectionModel(
        RemoteHostStore hosts,
        SshCredentialVaultStore credentials)
    {
        _hosts = hosts;
        _credentials = credentials;
    }

    [BindProperty(SupportsGet = true)]
    public string Section { get; set; } = string.Empty;

    public string Icon { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public string Detail { get; private set; } = string.Empty;

    public IReadOnlyList<RemoteHostSummary> Hosts { get; private set; } = [];

    public IReadOnlyList<SshCredentialSummary> Credentials { get; private set; }
        = [];

    public bool IsLinkedHosts => string.Equals(
        Section,
        "linked-hosts",
        StringComparison.OrdinalIgnoreCase);

    [BindProperty]
    public CredentialForm Credential { get; set; } = new();

    [BindProperty]
    public HostForm Host { get; set; } = new();

    [BindProperty]
    public HostKeyForm HostKey { get; set; } = new();

    [TempData]
    public string Notice { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        if (!LoadDefinition())
        {
            return NotFound();
        }

        await LoadLinkedHostsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAddCredentialAsync(
        CancellationToken cancellationToken)
    {
        if (!LoadDefinition() || !IsLinkedHosts)
        {
            return NotFound();
        }

        RemoveModelStatePrefix(nameof(Host));
        RemoveModelStatePrefix(nameof(HostKey));
        if (!ModelState.IsValid || Credential.PrivateKey is null)
        {
            await LoadLinkedHostsAsync(cancellationToken);
            return Page();
        }

        byte[] privateKey = [];
        byte[] passphrase = [];
        try
        {
            privateKey = await ReadUploadAsync(
                Credential.PrivateKey,
                1024 * 1024,
                cancellationToken);
            passphrase = Encoding.UTF8.GetBytes(
                Credential.Passphrase ?? string.Empty);
            var accountId = RequireAccountId();
            var result = string.IsNullOrWhiteSpace(Credential.CredentialId)
                ? await _credentials.CreateAsync(
                    accountId,
                    new SshCredentialMutationRequest
                    {
                        DisplayName = Credential.DisplayName,
                        PrivateKeyUtf8 = privateKey,
                        PassphraseUtf8 = passphrase
                    },
                    cancellationToken)
                : await _credentials.RotateAsync(
                    accountId,
                    Credential.CredentialId,
                    new SshCredentialMutationRequest
                    {
                        DisplayName = Credential.DisplayName,
                        PrivateKeyUtf8 = privateKey,
                        PassphraseUtf8 = passphrase,
                        ExpectedKeyVersion = Credential.ExpectedKeyVersion
                    },
                    cancellationToken);
            if (result.Kind is SshVaultMutationKind.Conflict
                or SshVaultMutationKind.NotFound)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "The credential changed or no longer exists. Refresh before rotating it.");
                await LoadLinkedHostsAsync(cancellationToken);
                return Page();
            }

            Notice = string.IsNullOrWhiteSpace(Credential.CredentialId)
                ? "SSH credential encrypted and stored."
                : $"SSH credential rotated to key version {result.Credential!.KeyVersion}.";
            return RedirectToPage(new { section = "linked-hosts" });
        }
        catch (Exception exception) when (exception is
                                          ArgumentException
                                          or InvalidDataException
                                          or IOException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadLinkedHostsAsync(cancellationToken);
            return Page();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
            CryptographicOperations.ZeroMemory(passphrase);
            Credential.Passphrase = string.Empty;
        }
    }

    public async Task<IActionResult> OnPostAddHostAsync(
        CancellationToken cancellationToken)
    {
        if (!LoadDefinition() || !IsLinkedHosts)
        {
            return NotFound();
        }

        RemoveModelStatePrefix(nameof(Credential));
        RemoveModelStatePrefix(nameof(HostKey));
        if (!ModelState.IsValid)
        {
            await LoadLinkedHostsAsync(cancellationToken);
            return Page();
        }

        try
        {
            var result = await _hosts.CreateAsync(
                RequireAccountId(),
                new RemoteHostMutationRequest
                {
                    DisplayName = Host.DisplayName,
                    HostName = Host.HostName,
                    Port = Host.Port,
                    UserName = Host.UserName,
                    OperatingSystem = Host.OperatingSystem,
                    DefaultRoot = Host.DefaultRoot,
                    CredentialId = Host.CredentialId,
                    Notes = Host.Notes
                },
                cancellationToken);
            Notice = $"Linked host '{result.Host!.DisplayName}' created.";
            return RedirectToPage(new { section = "linked-hosts" });
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadLinkedHostsAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostPinHostKeyAsync(
        CancellationToken cancellationToken)
    {
        if (!LoadDefinition() || !IsLinkedHosts)
        {
            return NotFound();
        }

        RemoveModelStatePrefix(nameof(Credential));
        RemoveModelStatePrefix(nameof(Host));
        if (!ModelState.IsValid)
        {
            await LoadLinkedHostsAsync(cancellationToken);
            return Page();
        }

        try
        {
            var result = await _hosts.PinHostKeyAsync(
                RequireAccountId(),
                HostKey.HostId,
                new RemoteHostKeyPinRequest
                {
                    Algorithm = HostKey.Algorithm,
                    Fingerprint = HostKey.Fingerprint,
                    ExpectedCurrentFingerprint =
                        HostKey.ExpectedCurrentFingerprint
                },
                cancellationToken);
            if (result.Kind is SshVaultMutationKind.Conflict
                or SshVaultMutationKind.NotFound)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "The current host-key pin did not match. No change was saved.");
                await LoadLinkedHostsAsync(cancellationToken);
                return Page();
            }

            Notice = "SSH host-key pin saved.";
            return RedirectToPage(new { section = "linked-hosts" });
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadLinkedHostsAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDisableHostAsync(
        string hostId,
        CancellationToken cancellationToken)
    {
        if (!LoadDefinition() || !IsLinkedHosts)
        {
            return NotFound();
        }

        try
        {
            Notice = await _hosts.DisableAsync(
                RequireAccountId(),
                hostId,
                cancellationToken)
                ? "Linked host disabled."
                : "The linked host was already disabled or missing.";
            return RedirectToPage(new { section = "linked-hosts" });
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadLinkedHostsAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRevokeCredentialAsync(
        string credentialId,
        CancellationToken cancellationToken)
    {
        if (!LoadDefinition() || !IsLinkedHosts)
        {
            return NotFound();
        }

        try
        {
            Notice = await _credentials.RevokeAsync(
                RequireAccountId(),
                credentialId,
                cancellationToken)
                ? "SSH credential revoked. New releases are blocked immediately."
                : "The SSH credential was already revoked or missing.";
            return RedirectToPage(new { section = "linked-hosts" });
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadLinkedHostsAsync(cancellationToken);
            return Page();
        }
    }

    private bool LoadDefinition()
    {
        if (!Definitions.TryGetValue(Section, out var definition))
        {
            return false;
        }

        Icon = definition.Icon;
        Title = definition.Title;
        Description = definition.Description;
        Status = definition.Status;
        Detail = definition.Detail;
        return true;
    }

    private async Task LoadLinkedHostsAsync(CancellationToken cancellationToken)
    {
        if (!IsLinkedHosts)
        {
            return;
        }

        var accountId = RequireAccountId();
        Hosts = await _hosts.ListAsync(accountId, cancellationToken);
        Credentials = await _credentials.ListAsync(
            accountId,
            cancellationToken);
    }

    private string RequireAccountId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException(
            "The portal account identity is unavailable.");

    private static async Task<byte[]> ReadUploadAsync(
        IFormFile upload,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (upload.Length is <= 0 || upload.Length > maximumBytes)
        {
            throw new InvalidDataException(
                $"The private-key file must contain 1-{maximumBytes:N0} bytes.");
        }

        await using var stream = upload.OpenReadStream();
        using var destination = new MemoryStream((int)upload.Length);
        await stream.CopyToAsync(destination, cancellationToken);
        if (destination.Length != upload.Length)
        {
            throw new InvalidDataException(
                "The private-key upload was truncated.");
        }

        return destination.ToArray();
    }

    private void RemoveModelStatePrefix(string prefix)
    {
        foreach (var key in ModelState.Keys
                     .Where(key => key.Equals(prefix, StringComparison.Ordinal)
                                   || key.StartsWith(
                                       prefix + ".",
                                       StringComparison.Ordinal))
                     .ToArray())
        {
            ModelState.Remove(key);
        }
    }

    private sealed record SectionDefinition(
        string Icon,
        string Title,
        string Description,
        string Status,
        string Detail);

    public sealed class CredentialForm
    {
        [Required]
        [StringLength(120)]
        public string DisplayName { get; set; } = string.Empty;

        [StringLength(32)]
        public string CredentialId { get; set; } = string.Empty;

        public int ExpectedKeyVersion { get; set; }

        [Required]
        public IFormFile? PrivateKey { get; set; }

        [DataType(DataType.Password)]
        [StringLength(16384)]
        public string Passphrase { get; set; } = string.Empty;
    }

    public sealed class HostForm
    {
        [Required]
        [StringLength(120)]
        public string DisplayName { get; set; } = string.Empty;

        [Required]
        [StringLength(253)]
        public string HostName { get; set; } = string.Empty;

        [Range(1, 65535)]
        public int Port { get; set; } = 22;

        [Required]
        [StringLength(128)]
        public string UserName { get; set; } = string.Empty;

        public RemoteHostOperatingSystem OperatingSystem { get; set; } =
            RemoteHostOperatingSystem.Unknown;

        [Required]
        [StringLength(2048)]
        public string DefaultRoot { get; set; } = "/";

        [StringLength(32)]
        public string CredentialId { get; set; } = string.Empty;

        [StringLength(2000)]
        public string Notes { get; set; } = string.Empty;
    }

    public sealed class HostKeyForm
    {
        [Required]
        [StringLength(32)]
        public string HostId { get; set; } = string.Empty;

        [Required]
        [StringLength(80)]
        public string Algorithm { get; set; } = "ssh-ed25519";

        [Required]
        [StringLength(80)]
        public string Fingerprint { get; set; } = string.Empty;

        [StringLength(80)]
        public string ExpectedCurrentFingerprint { get; set; } = string.Empty;
    }
}
