using Hypershell.ProfileServer;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class ProfileHardeningVerifier
{
    public static async Task VerifyAsync(string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "profile-hardening");
        var dataRoot = Path.Combine(root, "data");
        var exportRoot = Path.Combine(root, "encrypted-exports");
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(exportRoot);
        var options = new ProfileServerOptions
        {
            DataRoot = dataRoot,
            BackupExportsRoot = exportRoot,
            RequireHttpsPortalCookies = false,
            PublicPortalBaseUri = new Uri("http://127.0.0.1")
        }.Normalize();

        await VerifyBackupInventoryAsync(exportRoot, options);
        VerifySessionInventory(options);
        VerifyOperationsContracts();
        Console.WriteLine(
            "PROFILE_HARDENING_OK|encrypted-export-inventory|hash-before-download|tamper-rejected|account-scoped-session-revocation|block-volume-preflight|mount-aware-backup|isolated-restore|digitalocean-retention|systemd-schedule|parallel-cutover|data-source-migration|named-rollback|release-signer-bootstrap|stable-release-publisher|admin-site-cutover|finished-product-preflight");
    }

    private static async Task VerifyBackupInventoryAsync(
        string exportRoot,
        ProfileServerOptions options)
    {
        const string id = "hypershell-recovery-20260814T120000Z";
        var fileName = $"{id}.tar.gz.gpg";
        var content = RandomNumberGenerator.GetBytes(8192);
        var contentPath = Path.Combine(exportRoot, fileName);
        await File.WriteAllBytesAsync(contentPath, content);
        var hash = Convert.ToHexString(SHA256.HashData(content))
            .ToLowerInvariant();
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            id,
            createdAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            fileName,
            size = content.LongLength,
            sha256 = hash,
            restoreValidatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-6),
            snapshotId = "snapshot-verifier"
        });
        await File.WriteAllBytesAsync(
            Path.Combine(exportRoot, $"{id}.manifest.json"),
            manifest);
        await File.WriteAllTextAsync(
            Path.Combine(
                exportRoot,
                "hypershell-recovery-20260814T120001Z.manifest.json"),
            "{\"schemaVersion\":1,\"unexpected\":true}");

        var store = new BackupExportStore(options);
        var inventory = await store.InspectAsync();
        Require(
            inventory.Exports.Single() is { } summary
            && summary.Id == id
            && summary.ByteLength == content.LongLength
            && summary.RestoreValidatedAtUtc is not null
            && inventory.RejectedManifestCount == 1,
            "The backup portal inventory did not isolate strict, complete encrypted-export manifests.");

        var verified = await store.OpenVerifiedAsync(id)
                       ?? throw new InvalidOperationException(
                           "The verified recovery export was not opened.");
        await using (verified.Content)
        {
            using var copy = new MemoryStream();
            await verified.Content.CopyToAsync(copy);
            Require(
                copy.ToArray().AsSpan().SequenceEqual(content),
                "The verified encrypted export content changed during download preparation.");
        }

        await File.AppendAllTextAsync(contentPath, "tamper", Encoding.UTF8);
        await ExpectInvalidDataAsync(() => store.OpenVerifiedAsync(id));
    }

    private static void VerifySessionInventory(ProfileServerOptions options)
    {
        var sessions = new ProfileSessionTokenService(options);
        var first = sessions.Issue(new ProfileAccountState(
            "account-one",
            "Account One",
            IsDisabled: false,
            CredentialVersion: 1),
            "device-one");
        _ = sessions.Issue(new ProfileAccountState(
            "account-two",
            "Account Two",
            IsDisabled: false,
            CredentialVersion: 1),
            "device-two");
        var listed = sessions.ListActive("account-one");
        Require(
            listed.Single() is { ClientDeviceId: "device-one" } summary
            && !sessions.RevokeById("account-two", summary.Id)
            && sessions.TryAuthenticate(first.AccessToken, out _)
            && sessions.RevokeById("account-one", summary.Id)
            && !sessions.TryAuthenticate(first.AccessToken, out _),
            "Portal session inventory or account-scoped revocation crossed an account boundary.");
    }

    private static void VerifyOperationsContracts()
    {
        var root = FindRepositoryRoot();
        var deployment = Path.Combine(root, "deploy", "hypershell-server");
        var prepareStorage = Read(Path.Combine(
            deployment,
            "prepare-storage.sh"));
        var provisionVolume = Read(Path.Combine(
            deployment,
            "provision-digitalocean-volume.sh"));
        var installSourceSnapshot = Read(Path.Combine(
            deployment,
            "install-source-snapshot.sh"));
        var backup = Read(Path.Combine(
            deployment,
            "backup-profile-server.sh"));
        var restore = Read(Path.Combine(
            deployment,
            "restore-profile-server.sh"));
        var export = Read(Path.Combine(
            deployment,
            "export-profile-server.sh"));
        var snapshot = Read(Path.Combine(
            deployment,
            "snapshot-digitalocean-volume.sh"));
        var snapshotEnrollment = Read(Path.Combine(
            deployment,
            "enroll-digitalocean-snapshot-token.sh"));
        var verifyVolume = Read(Path.Combine(
            deployment,
            "verify-digitalocean-volume.sh"));
        var installSnapshotSchedule = Read(Path.Combine(
            deployment,
            "install-volume-snapshot-schedule.sh"));
        var configureAdminSite = Read(Path.Combine(
            deployment,
            "configure-admin-site.sh"));
        var provisionAdminTls = Read(Path.Combine(
            deployment,
            "provision-admin-tls.sh"));
        var certbotReloadNginx = Read(Path.Combine(
            deployment,
            "certbot-reload-nginx.sh"));
        var publishRelease = Read(Path.Combine(
            deployment,
            "publish-profile-release.sh"));
        var promote = Read(Path.Combine(
            deployment,
            "promote-profile-server.sh"));
        var rollback = Read(Path.Combine(
            deployment,
            "rollback-profile-server.sh"));
        var timer = Read(Path.Combine(
            deployment,
            "systemd",
            "hypershell-volume-snapshot.timer"));
        var snapshotService = Read(Path.Combine(
            deployment,
            "systemd",
            "hypershell-volume-snapshot.service"));
        var profileDeployment = Path.Combine(
            root,
            "deploy",
            "profile-server");
        var apiNginx = Read(Path.Combine(
            profileDeployment,
            "nginx-https.conf"));
        var adminNginx = Read(Path.Combine(
            profileDeployment,
            "nginx-admin-https.conf"));
        var upstream = Read(Path.Combine(
            profileDeployment,
            "nginx-upstream.conf"));
        var finishedProductAudit = Read(Path.Combine(
            root,
            "tools",
            "Test-HypershellFinishedProduct.ps1"));
        var releaseSignerEnrollment = Read(Path.Combine(
            root,
            "tools",
            "Set-HypershellReleaseSigner.ps1"));
        var stableReleasePublisher = Read(Path.Combine(
            root,
            "tools",
            "Publish-HypershellStableRelease.ps1"));
        var provisionVolumeFixture = Read(Path.Combine(
            root,
            "tools",
            "Test-HypershellDigitalOceanVolumeProvisioner.sh"));
        var sourceSnapshotFixture = Read(Path.Combine(
            root,
            "tools",
            "Test-HypershellSourceSnapshotInstaller.sh"));
        var prepareStorageFixture = Read(Path.Combine(
            root,
            "tools",
            "Test-HypershellPrepareStorage.sh"));
        var snapshotEnrollmentFixture = Read(Path.Combine(
            root,
            "tools",
            "Test-HypershellDigitalOceanSnapshotEnrollment.sh"));
        var adminTlsFixture = Read(Path.Combine(
            root,
            "tools",
            "Test-HypershellAdminTlsProvisioner.sh"));
        Require(
            provisionVolume.Contains(
                "--confirm-create-billed-volume", StringComparison.Ordinal)
            && provisionVolume.Contains(
                "--confirm-attach-to-this-droplet", StringComparison.Ordinal)
            && provisionVolume.Contains(
                "https://api.digitalocean.com/v2/volumes", StringComparison.Ordinal)
            && provisionVolume.Contains(
                "\"size_gigabytes\": 50", StringComparison.Ordinal)
            && provisionVolume.Contains(
                "volume.get(\"filesystem_type\") in (None, \"\")", StringComparison.Ordinal)
            && provisionVolume.Contains(
                "/dev/disk/by-id/scsi-0DO_Volume_", StringComparison.Ordinal)
            && provisionVolume.Contains(
                "without --format-empty-device", StringComparison.Ordinal)
            && provisionVolume.Contains(
                "install -m 0600 -o root -g root", StringComparison.Ordinal)
            && !provisionVolume.Contains("mkfs.ext4", StringComparison.Ordinal)
            && !provisionVolume.Contains("--request DELETE", StringComparison.Ordinal)
            && !provisionVolume.Contains(
                "echo \"$digitalocean_token", StringComparison.Ordinal)
            && provisionVolumeFixture.Contains(
                "assert \"filesystem_type\" not in request", StringComparison.Ordinal)
            && provisionVolumeFixture.Contains(
                "HYPERSHELL_VOLUME_FIXTURE_CONTAINER", StringComparison.Ordinal)
            && provisionVolumeFixture.Contains(
                "token-redaction", StringComparison.Ordinal),
            "The DigitalOcean volume provisioner lost its billing, attachment, raw-device, non-destructive, or secret-containment boundary.");
        Require(
            installSourceSnapshot.Contains(
                "refs/heads/main", StringComparison.Ordinal)
            && installSourceSnapshot.Contains(
                "((${#verification_specs[@]} >= 1))", StringComparison.Ordinal)
            && installSourceSnapshot.Contains(
                "git -c tar.umask=0022", StringComparison.Ordinal)
            && installSourceSnapshot.Contains(
                "normalize_source_permissions", StringComparison.Ordinal)
            && installSourceSnapshot.Contains(
                "Deployment or test shell source is not executable", StringComparison.Ordinal)
            && installSourceSnapshot.Contains(
                "find \"$source_root\" -xdev -type f -perm /0022", StringComparison.Ordinal)
            && installSourceSnapshot.Contains(
                "hypershell-profile-deploy.lock", StringComparison.Ordinal)
            && installSourceSnapshot.Contains(
                "hypershell-profile-backup.lock", StringComparison.Ordinal)
            && installSourceSnapshot.Contains(
                "rollback_required=1", StringComparison.Ordinal)
            && installSourceSnapshot.Contains(
                "mv \"$previous\" \"$active_root\"", StringComparison.Ordinal)
            && installSourceSnapshot.Contains(
                "{{.RestartCount}}", StringComparison.Ordinal)
            && installSourceSnapshot.Contains(
                "https://hypershell.atlasuniversalis.com/healthz", StringComparison.Ordinal)
            && installSourceSnapshot.Contains(
                "HYPERSHELL_SOURCE_INSTALLED", StringComparison.Ordinal)
            && !installSourceSnapshot.Contains("docker stop", StringComparison.Ordinal)
            && !installSourceSnapshot.Contains("docker restart", StringComparison.Ordinal)
            && !installSourceSnapshot.Contains("docker rm", StringComparison.Ordinal)
            && sourceSnapshotFixture.Contains(
                "failed SHA-256 verification", StringComparison.Ordinal)
            && sourceSnapshotFixture.Contains(
                "not the exact server origin/main commit", StringComparison.Ordinal)
            && sourceSnapshotFixture.Contains(
                "public health failed after", StringComparison.Ordinal)
            && sourceSnapshotFixture.Contains(
                "failed-source-", StringComparison.Ordinal)
            && sourceSnapshotFixture.Contains(
                "executable-shell-sources", StringComparison.Ordinal)
            && sourceSnapshotFixture.Contains(
                "container-unchanged", StringComparison.Ordinal),
            "The source-only installer lost its pushed-commit, hash, mode, lock, rollback, or live-container boundary.");
        Require(
            prepareStorage.Contains(
                "/etc/fstab is missing, symbolic, or not owned by root:root", StringComparison.Ordinal)
            && prepareStorage.Contains(
                "mount point already exists in an unsafe form", StringComparison.Ordinal)
            && prepareStorage.Contains(
                "contains data or unexpected entries", StringComparison.Ordinal)
            && prepareStorage.Contains(
                "Host group for runtime GID $runtime_gid is missing", StringComparison.Ordinal)
            && prepareStorage.IndexOf(
                "getent group \"$runtime_gid\"", StringComparison.Ordinal)
               < prepareStorage.IndexOf(
                   "mkfs.ext4 -L hypershell-data", StringComparison.Ordinal)
            && prepareStorage.Contains(
                "before the device has a filesystem", StringComparison.Ordinal)
            && prepareStorage.IndexOf(
                "existing_mount=$(awk", StringComparison.Ordinal)
               < prepareStorage.IndexOf(
                   "mkfs.ext4 -L hypershell-data", StringComparison.Ordinal)
            && prepareStorage.Contains(
                "mounted_source=$(readlink -f", StringComparison.Ordinal)
            && prepareStorage.Contains(
                "mounted_source\" != \"$resolved_device", StringComparison.Ordinal)
            && prepareStorage.Contains(
                "umount \"$mount_point\"", StringComparison.Ordinal)
            && prepareStorage.Contains(
                "fstab-before-storage-$(date -u +%Y%m%dT%H%M%S%NZ)-$$", StringComparison.Ordinal)
            && prepareStorageFixture.Contains(
                "HYPERSHELL_STORAGE_FIXTURE_CONTAINER", StringComparison.Ordinal)
            && prepareStorageFixture.Contains(
                "mount point already exists in an unsafe form", StringComparison.Ordinal)
            && prepareStorageFixture.Contains(
                "did not mount from the approved device", StringComparison.Ordinal)
            && prepareStorageFixture.Contains(
                "legacy-export-guard", StringComparison.Ordinal)
            && prepareStorageFixture.Contains(
                "pre-format-refusal", StringComparison.Ordinal),
            "The storage preparer lost its before-format filesystem checks, exact mount-source proof, rollback, or disposable test boundary.");
        Require(
            prepareStorage.Contains("/dev/disk/by-id/*", StringComparison.Ordinal)
            && prepareStorage.Contains("minimum_bytes=$((45 * 1024 * 1024 * 1024))", StringComparison.Ordinal)
            && prepareStorage.Contains("maximum_bytes=$((55 * 1024 * 1024 * 1024))", StringComparison.Ordinal)
            && prepareStorage.Contains("fstab-before-storage-", StringComparison.Ordinal)
            && prepareStorage.Contains("x-systemd.device-timeout=30", StringComparison.Ordinal)
            && prepareStorage.Contains("chown root:\"$runtime_gid\"", StringComparison.Ordinal)
            && promote.Contains(
                "Host user for runtime UID 1654 is missing", StringComparison.Ordinal)
            && promote.Contains(
                "Host group for runtime GID 1654 is missing", StringComparison.Ordinal)
            && promote.IndexOf(
                "getent passwd 1654", StringComparison.Ordinal)
               < promote.IndexOf(
                   "\"$script_root/restore-profile-server.sh\"", StringComparison.Ordinal)
            && promote.IndexOf(
                "getent group 1654", StringComparison.Ordinal)
               < promote.IndexOf(
                   "docker stop --time 30 \"$container\"", StringComparison.Ordinal)
            && backup.Contains(".Destination \"/var/lib/hypershell-profile-server\"", StringComparison.Ordinal)
            && backup.Contains("docker stop --time 30", StringComparison.Ordinal)
            && backup.Contains("/srv/hypershell/server/*", StringComparison.Ordinal)
            && restore.Contains("hypershell-profile-restore-", StringComparison.Ordinal)
            && restore.Contains("sha256sum -c SHA256SUMS", StringComparison.Ordinal)
            && restore.Contains("127.0.0.1:$port:8080", StringComparison.Ordinal)
            && restore.Contains("local status=$?", StringComparison.Ordinal)
            && restore.Contains("exit \"$status\"", StringComparison.Ordinal)
            && !restore.Contains(
                "SHA256SUMS backup-metadata.env container-inspect.json",
                StringComparison.Ordinal)
            && export.Contains("gpg --batch", StringComparison.Ordinal)
            && export.Contains("ssh-vault-master-key.bin", StringComparison.Ordinal)
            && export.Contains("chown root:1654", StringComparison.Ordinal)
            && snapshot.Contains(
                "api.digitalocean.com/v2/volumes/$HYPERSHELL_VOLUME_ID/snapshots",
                StringComparison.Ordinal)
            && snapshot.Contains(
                "api.digitalocean.com/v2/volumes/snapshots/$stale_id",
                StringComparison.Ordinal)
            && snapshot.Contains("sync -f /srv/hypershell", StringComparison.Ordinal)
            && !snapshot.Contains(
                "api.digitalocean.com/v2/snapshots/$stale_id",
                StringComparison.Ordinal)
            && snapshot.Contains("source \"$config_file\"", StringComparison.Ordinal)
            && snapshot.Contains(
                "retain=\"${HYPERSHELL_SNAPSHOT_RETENTION:-8}\"",
                StringComparison.Ordinal)
            && snapshot.IndexOf("source \"$config_file\"", StringComparison.Ordinal)
               < snapshot.IndexOf("retain=\"${HYPERSHELL_SNAPSHOT_RETENTION:-8}\"", StringComparison.Ordinal)
            && snapshot.Contains("$retain", StringComparison.Ordinal)
            && !snapshot.Contains("echo \\\"$DIGITALOCEAN_TOKEN", StringComparison.Ordinal)
            && snapshotEnrollment.Contains(
                "read -r -s token < /dev/tty", StringComparison.Ordinal)
            && snapshotEnrollment.Contains(
                "DIGITALOCEAN_TOKEN=%q", StringComparison.Ordinal)
            && snapshotEnrollment.Contains(
                "\"$verifier\" \"$stage\"", StringComparison.Ordinal)
            && snapshotEnrollment.Contains(
                "mv -f -- \"$stage\" \"$config_file\"", StringComparison.Ordinal)
            && snapshotEnrollment.Contains(
                "use --replace only for deliberate token rotation", StringComparison.Ordinal)
            && !snapshotEnrollment.Contains(
                "echo \"$token", StringComparison.Ordinal)
            && snapshotEnrollmentFixture.Contains(
                "HYPERSHELL_SNAPSHOT_ENROLLMENT_FIXTURE_CONTAINER", StringComparison.Ordinal)
            && snapshotEnrollmentFixture.Contains(
                "token-redaction", StringComparison.Ordinal)
            && verifyVolume.Contains(
                "http://169.254.169.254/metadata/v1/id",
                StringComparison.Ordinal)
            && verifyVolume.Contains("size_gigabytes", StringComparison.Ordinal)
            && verifyVolume.Contains("droplet_ids", StringComparison.Ordinal)
            && verifyVolume.Contains("scsi-0DO_Volume_", StringComparison.Ordinal)
            && installSnapshotSchedule.Contains(
                "verify-digitalocean-volume.sh",
                StringComparison.Ordinal)
            && installSnapshotSchedule.Contains(
                "\"$source\" -ef \"$destination\"",
                StringComparison.Ordinal)
            && installSnapshotSchedule.Contains(
                "systemctl enable --now hypershell-volume-snapshot.timer",
                StringComparison.Ordinal)
            && snapshotService.Contains(
                "backup-profile-server.sh /srv/hypershell/backups",
                StringComparison.Ordinal)
            && snapshotService.Contains(
                "/bin/sync -f /srv/hypershell",
                StringComparison.Ordinal)
            && timer.Contains("OnCalendar=Sun", StringComparison.Ordinal)
            && timer.Contains("Persistent=true", StringComparison.Ordinal)
            && configureAdminSite.Contains(
                "bootstrap|activate", StringComparison.Ordinal)
            && configureAdminSite.Contains(
                "return 404;", StringComparison.Ordinal)
            && configureAdminSite.Contains(
                "openssl x509 -in \"$certificate\" -noout", StringComparison.Ordinal)
            && configureAdminSite.Contains(
                "-checkhost admin.atlasuniversalis.com", StringComparison.Ordinal)
            && configureAdminSite.Contains(
                "rollback_on_exit", StringComparison.Ordinal)
            && configureAdminSite.Contains(
                "nginx -t", StringComparison.Ordinal)
            && configureAdminSite.Contains(
                "for _ in $(seq 1 30)", StringComparison.Ordinal)
            && configureAdminSite.Contains(
                "--noproxy '*'", StringComparison.Ordinal)
            && configureAdminSite.Contains(
                "--resolve admin.atlasuniversalis.com:443:127.0.0.1", StringComparison.Ordinal)
            && configureAdminSite.Contains(
                "HYPERSHELL_ADMIN_SITE_READY", StringComparison.Ordinal),
            "The deployment hardening scripts lost a containment, consistency, encryption, or scheduling boundary.");
        Require(
            provisionAdminTls.Contains(
                "verify_dns_record A \"$expected_a\"", StringComparison.Ordinal)
            && provisionAdminTls.Contains(
                "verify_dns_record AAAA \"$expected_aaaa\"", StringComparison.Ordinal)
            && provisionAdminTls.Contains(
                "fetch_challenge --ipv4", StringComparison.Ordinal)
            && provisionAdminTls.Contains(
                "fetch_challenge --ipv6", StringComparison.Ordinal)
            && provisionAdminTls.Contains(
                "certbot certonly", StringComparison.Ordinal)
            && provisionAdminTls.Contains(
                "--webroot-path \"$webroot\"", StringComparison.Ordinal)
            && provisionAdminTls.Contains(
                "-checkhost \"$domain\"", StringComparison.Ordinal)
            && provisionAdminTls.Contains(
                "certificate-sha256:", StringComparison.Ordinal)
            && provisionAdminTls.Contains(
                "restoring the HTTP-only bootstrap", StringComparison.Ordinal)
            && certbotReloadNginx.Contains(
                "nginx -t", StringComparison.Ordinal)
            && certbotReloadNginx.Contains(
                "systemctl reload nginx", StringComparison.Ordinal)
            && adminTlsFixture.Contains(
                "HYPERSHELL_ADMIN_TLS_FIXTURE_CONTAINER", StringComparison.Ordinal)
            && adminTlsFixture.Contains(
                "fail-closed-activation", StringComparison.Ordinal),
            "The admin TLS provisioner lost its exact DNS, dual-stack ACME, certificate, renewal, or fail-closed activation boundary.");
        Require(
            promote.Contains("restore-profile-server.sh", StringComparison.Ordinal)
            && promote.Contains("docker stop --time 30", StringComparison.Ordinal)
            && promote.Contains("local status=$?", StringComparison.Ordinal)
            && promote.Contains("exit \"$status\"", StringComparison.Ordinal)
            && promote.Contains("chown root:1654", StringComparison.Ordinal)
            && promote.Contains("wait_public_health", StringComparison.Ordinal)
            && promote.Contains("for _ in $(seq 1 30)", StringComparison.Ordinal)
            && promote.Contains("--target-data-root", StringComparison.Ordinal)
            && promote.Contains("--release-signer-sha256", StringComparison.Ordinal)
            && promote.Contains("old_signer_fingerprint", StringComparison.Ordinal)
            && promote.Contains("rsync -aHAX --numeric-ids --delete", StringComparison.Ordinal)
            && promote.Contains("HYPERSHELL_CUTOVER_DATA_MIGRATION", StringComparison.Ordinal)
            && promote.Contains("HYPERSHELL_CUTOVER_NEW_DATA_SOURCE", StringComparison.Ordinal)
            && promote.Contains("HYPERSHELL_CUTOVER_OLD_RELEASE_SIGNER_SHA256", StringComparison.Ordinal)
            && promote.Contains("HYPERSHELL_CUTOVER_NEW_RELEASE_SIGNER_SHA256", StringComparison.Ordinal)
            && promote.Contains("$container-rollback-$stamp", StringComparison.Ordinal)
            && promote.Contains("rollback_root=\"/var/lib/hypershell-deployments\"", StringComparison.Ordinal)
            && promote.Contains("cutover-$stamp.env", StringComparison.Ordinal)
            && promote.Contains("systemctl reload nginx", StringComparison.Ordinal)
            && promote.Contains("--resolve hypershell.atlasuniversalis.com:443:127.0.0.1", StringComparison.Ordinal)
            && publishRelease.Contains(
                "/srv/hypershell/releases/incoming/", StringComparison.Ordinal)
            && publishRelease.Contains(
                "findmnt -nro TARGET -T", StringComparison.Ordinal)
            && publishRelease.Contains(
                "Another Hypershell deployment is running", StringComparison.Ordinal)
            && publishRelease.Contains(
                "A Hypershell backup is running", StringComparison.Ordinal)
            && publishRelease.Contains(
                "--network none", StringComparison.Ordinal)
            && publishRelease.Contains(
                "HYPERSHELL_RELEASE_SIGNER_SHA256=$signer_fingerprint", StringComparison.Ordinal)
            && publishRelease.Contains(
                "--expected-git-commit", StringComparison.Ordinal)
            && publishRelease.Contains(
                "mv -T --no-clobber", StringComparison.Ordinal)
            && publishRelease.Contains(
                "declare -A retained_versions", StringComparison.Ordinal)
            && publishRelease.Contains(
                "archives_to_prune", StringComparison.Ordinal)
            && publishRelease.Contains(
                "rm -rf --one-file-system", StringComparison.Ordinal)
            && publishRelease.Contains(
                "HYPERSHELL_RELEASE_PRUNED_ARCHIVES", StringComparison.Ordinal)
            && publishRelease.Contains(
                "RELEASE_(CREATED|ALREADYPRESENT)", StringComparison.Ordinal)
            && publishRelease.Contains(
                "HYPERSHELL_RELEASE_PUBLISHED", StringComparison.Ordinal)
            && rollback.Contains("$container-forward-$stamp", StringComparison.Ordinal)
            && rollback.Contains("local status=$?", StringComparison.Ordinal)
            && rollback.Contains("exit \"$status\"", StringComparison.Ordinal)
            && rollback.Contains("wait_public_health", StringComparison.Ordinal)
            && rollback.Contains("sync_data \"$new_data_source\" \"$old_data_source\"", StringComparison.Ordinal)
            && rollback.Contains("HYPERSHELL_ROLLBACK_DATA_MIGRATION", StringComparison.Ordinal)
            && rollback.Contains("HYPERSHELL_ROLLBACK_OK", StringComparison.Ordinal)
            && apiNginx.Contains("proxy_pass http://hypershell_profile_backend", StringComparison.Ordinal)
            && adminNginx.Contains("proxy_pass http://hypershell_profile_backend", StringComparison.Ordinal)
            && upstream.Contains("server 127.0.0.1:5088", StringComparison.Ordinal),
            "The parallel deployment or named rollback contract regressed.");
        Require(
            releaseSignerEnrollment.Contains(
                "SupportsShouldProcess", StringComparison.Ordinal)
            && releaseSignerEnrollment.Contains(
                "1.3.6.1.5.5.7.3.3", StringComparison.Ordinal)
            && releaseSignerEnrollment.Contains(
                "X509KeyUsageFlags]::DigitalSignature", StringComparison.Ordinal)
            && releaseSignerEnrollment.Contains(
                "X509RevocationMode]::Online", StringComparison.Ordinal)
            && releaseSignerEnrollment.Contains(
                "Windows does not trust the code-signing certificate chain", StringComparison.Ordinal)
            && releaseSignerEnrollment.Contains(
                "changed during signer enrollment", StringComparison.Ordinal)
            && releaseSignerEnrollment.Contains(
                "[IO.File]::Replace($temporaryPath, $trustPath, $null, $true)", StringComparison.Ordinal)
            && releaseSignerEnrollment.Contains(
                "[IO.FileAttributes]::ReparsePoint", StringComparison.Ordinal)
            && releaseSignerEnrollment.Contains(
                "HYPERSHELL_RELEASE_SIGNER_READY", StringComparison.Ordinal)
            && !releaseSignerEnrollment.Contains(
                ".Export(", StringComparison.Ordinal),
            "Release-signer enrollment no longer validates trust and usage or preserves the private-key boundary.");
        Require(
            stableReleasePublisher.Contains(
                "Set-HypershellReleaseSigner.ps1", StringComparison.Ordinal)
            && stableReleasePublisher.Contains(
                "'status', '--porcelain', '--untracked-files=all'", StringComparison.Ordinal)
            && stableReleasePublisher.Contains(
                "rev-parse', 'origin/main'", StringComparison.Ordinal)
            && stableReleasePublisher.Contains(
                "findmnt', '-nro', 'TARGET', '-T', '/srv/hypershell'", StringComparison.Ordinal)
            && stableReleasePublisher.Contains(
                "The server release publisher does not match the audited release commit", StringComparison.Ordinal)
            && stableReleasePublisher.Contains(
                "failed SHA-256 comparison", StringComparison.Ordinal)
            && stableReleasePublisher.Contains(
                "/srv/hypershell/releases/.upload-", StringComparison.Ordinal)
            && stableReleasePublisher.Contains(
                "/srv/hypershell/releases/incoming/", StringComparison.Ordinal)
            && stableReleasePublisher.Contains(
                "mv', '-T', '--no-clobber'", StringComparison.Ordinal)
            && stableReleasePublisher.Contains(
                "rm', '-rf', '--', $remoteStage", StringComparison.Ordinal)
            && stableReleasePublisher.Contains(
                "HYPERSHELL_STABLE_RELEASE_PUBLISHED", StringComparison.Ordinal),
            "The stable release workstation publisher lost a source, signer, volume, upload-integrity, or no-clobber boundary.");
        Require(
            finishedProductAudit.Contains("Repository is clean", StringComparison.Ordinal)
            && finishedProductAudit.Contains("Trusted code-signing certificate", StringComparison.Ordinal)
            && finishedProductAudit.Contains("Dedicated Hypershell block volume", StringComparison.Ordinal)
            && finishedProductAudit.Contains("Nginx blue-green upstream", StringComparison.Ordinal)
            && finishedProductAudit.Contains("Scheduled volume snapshots", StringComparison.Ordinal)
            && finishedProductAudit.Contains("Admin DNS A record", StringComparison.Ordinal)
            && finishedProductAudit.Contains("Private admin portal TLS", StringComparison.Ordinal)
            && finishedProductAudit.Contains("Two-PC end-to-end acceptance", StringComparison.Ordinal)
            && finishedProductAudit.Contains("exit 1", StringComparison.Ordinal),
            "The finished-product audit no longer fails closed across local, server, DNS, signing, and manual acceptance gates.");
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[]
                 {
                     Environment.CurrentDirectory,
                     AppContext.BaseDirectory
                 })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))
                    && Directory.Exists(Path.Combine(
                        current.FullName,
                        "deploy",
                        "hypershell-server")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "The Hypershell repository root could not be located.");
    }

    private static string Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "A required deployment hardening artifact is missing.",
                path);
        }

        return File.ReadAllText(path);
    }

    private static async Task ExpectInvalidDataAsync(
        Func<Task<VerifiedBackupExport?>> action)
    {
        try
        {
            _ = await action();
            throw new InvalidOperationException(
                "A tampered encrypted recovery export was accepted.");
        }
        catch (InvalidDataException)
        {
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
