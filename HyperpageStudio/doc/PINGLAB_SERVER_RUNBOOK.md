# Pinglab server administration runbook

Status: Ubuntu 26.04 LTS upgrade and the first Hypershell profile-server
deployment completed and verified on 2026-08-02 UTC.

This document contains operational metadata only. Credentials, private keys,
connection strings, database contents, n8n encryption material, and application
secrets must never be added.

## Access and ownership

- Provider: DigitalOcean
- Droplet hostname: MyDrop1
- SSH alias: pinglab
- Administrative login: root
- Public IPv4: 167.71.179.90
- Public IPv6: 2604:a880:800:14:0:1:aa80:1000
- The Linux account atlas is a non-login service identity with
  /usr/sbin/nologin. It is not an administrative login.
- A separate SSH administrator is not required for the current bootstrap.
  Host-level changes use root; application services and containers must
  continue to run as unprivileged identities.

Functional SSH verification:

~~~powershell
ssh -o BatchMode=yes -o ConnectTimeout=10 pinglab "id -un; hostname"
~~~

Expected identity is root on MyDrop1.

## Host baseline and current state

| Area | Pre-upgrade state | Current state |
| --- | --- | --- |
| Operating system | Ubuntu 25.10, end-of-life since 2026-07-09 | Ubuntu 26.04 LTS |
| Running kernel | 6.17.0-29-generic | 7.0.0-28-generic; 6.17.0-41 retained as fallback |
| CPU / memory | 2 vCPU / approximately 3.9 GiB | unchanged |
| Root filesystem | 24 GiB total; 19 GiB used before recovery backup | approximately 6.3 GiB free after Hypershell build and deployment |
| Public listeners | SSH 22, Nginx 80 and 443 on IPv4 and IPv6 | unchanged; Hypershell backend is loopback-only |
| Host firewall | UFW inactive | unchanged; DigitalOcean cloud-firewall state still requires control-panel review |
| Reverse proxy | Nginx 1.28 | Nginx 1.28.3 |
| Containers | Docker 28.2.2; n8n only | Docker 29.1.3; n8n and Hypershell |
| Compose / Buildx | not installed | Compose 2.40.3; Buildx 0.30.1 |

The release upgrade reconciled the obsolete Ubuntu sources. Do not reintroduce
Plucky or Questing package entries.

## DNS and certificates

Hypershell DNS:

| Type | Host | Value |
| --- | --- | --- |
| A | hypershell.atlasuniversalis.com | 167.71.179.90 |
| AAAA | hypershell.atlasuniversalis.com | 2604:a880:800:14:0:1:aa80:1000 |

Both records resolved correctly from the administration workstation on
2026-08-02 UTC.

Certificate baseline:

- atlasuniversalis.com-0001: valid through 2026-10-13; covers the primary
  Atlas, Apply, ElectraCast, Forge, and www names.
- atlasbattlegrounds.com: valid through 2026-10-13.
- pinglab.atlasuniversalis.com: valid through 2026-10-17.
- sonar.atlasuniversalis.com: valid through 2026-10-16.
- hypershell.atlasuniversalis.com: dedicated certificate valid through
  2026-10-31; isolated renewal dry run passed on 2026-08-02.
- atlasuniversalis.com: an older certificate expired on 2026-05-02 and is still
  referenced by the dormant QuickPro site.
- certbot.timer is enabled and active. A full renewal run can still report
  failure because renewal of the obsolete atlasuniversalis.com certificate
  fails; the Hypershell lineage itself renews successfully.

Do not delete or rewrite an existing certificate lineage until every Nginx
reference has been mapped and a replacement/retirement decision is explicit.

## Hosted applications

| Surface | Runtime / storage | Internal route | Pre-upgrade baseline |
| --- | --- | --- | --- |
| atlasuniversalis.com and www | Atlas.Web, self-contained executable targeting .NET 10 | 127.0.0.1:5000 | Root and /healthz return 200 |
| apply.atlasuniversalis.com | Atlas.Apply, .NET 10, PostgreSQL | 127.0.0.1:5010 | Root and /healthz return 200 |
| forge.atlasuniversalis.com | Static frontend plus Atlas.Forge .NET 10 backend | backend 127.0.0.1:5020 | Backend /healthz is 200; public root is pre-existing 500 because atlasforge/dist is missing |
| atlasbattlegrounds.com | Bundled Node 22 runtime under /opt/atlas-battlegrounds | 127.0.0.1:4173 | Root returns 200; www redirects |
| pinglab.atlasuniversalis.com | Static site under /var/www/pinglab.atlasuniversalis.com/current | Nginx static | Root returns 200 |
| sonar.atlasuniversalis.com | Nginx migration/redirect surface | Nginx static | Redirects to pinglab |
| electracast.atlasuniversalis.com | Configured static site | Nginx static | Pre-existing 500 because electracast/dist is missing |
| quickpro.atlasuniversalis.com | Dormant static/API site | expected backend 127.0.0.1:8000 | No DNS, no backend listener, missing frontend/dist, expired legacy certificate |
| n8n.atlasuniversalis.com | n8nio/n8n container; bind data at /var/lib/n8n | 127.0.0.1:5678 | Local root returns 200; no public DNS and only an HTTP Nginx server block |
| hypershell.atlasuniversalis.com | Hypershell profile server, ASP.NET Core in Docker | 127.0.0.1:5088 | Public HTTPS and IPv4/IPv6 health return 200; backend port is not public |

The public Forge and ElectraCast 500 responses predate the Ubuntu upgrade and
must not be reported as upgrade regressions. Their missing static distributions
are separate application-deployment work.

## Data services and runtimes

- PostgreSQL 17.10, cluster 17/main, listening only on loopback.
- Non-template databases: atlasops, html_db, and postgres.
- Redis 8.0.2, listening only on loopback, RDB persistence at
  /var/lib/redis/dump.rdb; append-only persistence is disabled.
- .NET SDK 10.0.103 and ASP.NET Core/.NET runtime 10.0.3 are installed manually
  beneath /usr/share/dotnet and linked from /usr/bin/dotnet.
- Atlas.Web, Atlas.Apply, and Atlas.Forge target Microsoft.NETCore.App and
  Microsoft.AspNetCore.App 10.0.0.
- System Node is 22.22.1. Atlas Battlegrounds does not depend on
  it at runtime; it uses a bundled Node 22 executable.
- n8n uses the existing n8nio/n8n:latest image created 2026-02-13 and a
  restart-unless-stopped policy. Do not pull latest during an OS upgrade.
- PostgreSQL 18 packages are installed, but no PostgreSQL 18 cluster was
  created. The preserved 17/main cluster remains the only active cluster on
  port 5432.

## Atlas development and deployment conventions

- Server repository: /var/www/atlasuniversalis.com
- Prior local Cursor repository: D:\www\atlasuniversalis
- Git branch/commit at baseline: master at 6febc0c4293d
- The server working tree contains 44 modified, deleted, or untracked entries.
  Preserve it exactly; do not reset, clean, commit, or overwrite those changes
  as part of host maintenance.
- Production deployments live under /opt/atlas/web, /opt/atlas/apply, and
  /opt/atlas/forge.
- Historical deployment backups live under /opt/atlas/backups.
- System services are atlas-web, atlas-apply, and atlas-forge, running as
  www-data. Atlas Battlegrounds runs separately as atlasbg.
- The repository runbook is deploy/RUNBOOK.md. Rollback and blue/green routing
  use deploy/deploy-dotnet.sh and the Nginx route map.
- For future Atlas code changes, the established Cursor policy requires
  pwsh -File deploy/live-sync.ps1 after each completed implementation task.
  Host maintenance must not invoke that workflow unless Atlas source code
  actually changed.

## Pre-upgrade recovery checkpoint

Root-only recovery set:

~~~text
/root/pre-upgrade-26.04-20260802T032155Z
~~~

It contains:

- complete PostgreSQL logical dump;
- completed Redis RDB snapshot;
- n8n SQLite/WAL data copied during a brief container stop;
- n8n container and image metadata, including sensitive recovery metadata;
- Nginx, Let's Encrypt, systemd, SSH, Redis, PostgreSQL, APT, update-manager,
  Atlas Battlegrounds, and Docker configuration;
- deployed Atlas applications, Atlas Battlegrounds, static sites, and the
  complete dirty Atlas repository;
- package/manual-package, enabled-service, listener, and container baselines;
- SHA-256 checksums for every recovery artifact.

The archive is approximately 1.2 GiB and mode 0600 beneath a mode 0700
directory. Its PostgreSQL gzip, tar archives, checksums, n8n restart, and local
n8n HTTP response were verified when created.

This recovery set is not a substitute for an off-host DigitalOcean snapshot.
A powered-off DigitalOcean snapshot was confirmed before the release upgrade.

## Ubuntu 26.04 upgrade result

- The supported noninteractive release upgrade completed with exit code 0 and
  the server rebooted into kernel 7.0.0-28-generic.
- `systemctl --failed`, `dpkg --audit`, and `apt-get check` were clean after
  reboot, with no pending package updates or reboot requirement.
- Nginx, SSH, netplan, PostgreSQL 17, Redis, Docker, n8n, Atlas Web, Apply,
  Forge backend, Atlas Battlegrounds, Pinglab, and their established public
  routes were revalidated.
- Redis returned PONG, PostgreSQL 17/main remained online, and the n8n
  loopback endpoint returned 200.
- DigitalOcean do-agent and droplet-agent are installed and active.
- Forge public root and ElectraCast still return their pre-existing 500
  responses because their static `dist` directories are missing; this is not
  an upgrade regression.
- The Atlas server repository still has its original 44 dirty entries.

## Hypershell profile-server operations

Public endpoint:

~~~text
https://hypershell.atlasuniversalis.com/
~~~

Deployment layout:

| Item | Value |
| --- | --- |
| Deployment root | `/opt/hypershell/profile-server` |
| Compose root | `/opt/hypershell/profile-server/deploy/profile-server` |
| Container | `hypershell-profile-server` |
| Image | `hypershell-profile-server:2026.08.02` |
| Durable volume | `hypershell-profile-data` |
| Backend listener | `127.0.0.1:5088` only |
| Nginx site | `/etc/nginx/sites-available/hypershell.atlasuniversalis.com` |
| Access/error logs | `/var/log/nginx/hypershell-profile-access.log` and `hypershell-profile-error.log` |
| Certificate lineage | `/etc/letsencrypt/live/hypershell.atlasuniversalis.com` |

The container runs as the unprivileged `app` identity with a read-only root
filesystem, all Linux capabilities dropped, `no-new-privileges`, a 512 MiB
memory limit, one CPU, a 128-process limit, bounded JSON logs, and a
restart-unless-stopped policy. Only one replica may mount the file store.

The first account is `ihigg`, display name `Primary account`, credential
version 2. Its access key is intentionally absent from this runbook and the
server filesystem.

~~~bash
cd /opt/hypershell/profile-server/deploy/profile-server
docker compose ps
docker compose logs --tail 100 profile-server
docker compose restart profile-server
curl -fsS http://127.0.0.1:5088/healthz
curl -fsS https://hypershell.atlasuniversalis.com/healthz

# Keep --interactive=false when called from automation.
docker compose run --rm --no-deps --interactive=false profile-server account list
docker compose run --rm --no-deps --interactive=false profile-server account rotate-key --id ihigg
docker compose run --rm --no-deps --interactive=false profile-server account disable --id ihigg
docker compose run --rm --no-deps --interactive=false profile-server account enable --id ihigg

certbot renew --dry-run --no-random-sleep-on-renew \
  --cert-name hypershell.atlasuniversalis.com
~~~

Verified deployment behavior includes valid TLS on IPv4 and IPv6, HTTP 308 to
HTTPS, closed unknown routes, 401 for unauthenticated profile access, 415 for
invalid sign-in media, external refusal of port 5088, sign-out revocation,
in-memory session invalidation on restart, and credential persistence.

Initial root-only data backup:

~~~text
/root/hypershell-profile-backups/hypershell-profile-data-20260802T045449Z.tar.gz
~~~

Its adjacent SHA-256 file and archive integrity were verified. The archive
contains the complete Docker volume with the hashed account record; it does
not contain the one-time plaintext access key. This is an on-host recovery
copy, not an off-host backup policy.

## Upgrade gates

1. Confirm the powered-off DigitalOcean snapshot completed.
2. Confirm SSH access after powering the Droplet back on.
3. Verify application recovery hashes and all baseline health checks.
4. Recover safe disk space from bounded journals and reproducible caches.
5. Reconcile obsolete Ubuntu sources and fully update the current release.
6. Reboot into the already-installed current kernel and re-run the baseline.
7. Run the supported Ubuntu 26.04 LTS release upgrade interactively.
8. Preserve reviewed local SSH, Nginx, PostgreSQL, Redis, and systemd
   configuration when package prompts occur.
9. Reboot and verify every service, route, certificate, data store, listener,
   container, and IPv4/IPv6 endpoint.
10. Only after the host is healthy, deploy Hypershell on loopback and add its
    isolated HTTPS Nginx server block.

All ten gates completed successfully on 2026-08-02.

## Routine verification

~~~bash
systemctl --failed
systemctl status atlas-web atlas-apply atlas-forge atlas-battlegrounds
curl -fsS http://127.0.0.1:5000/healthz
curl -fsS http://127.0.0.1:5010/healthz
curl -fsS http://127.0.0.1:5020/healthz
curl -fsS http://127.0.0.1:4173/
curl -fsS http://127.0.0.1:5678/
nginx -t
docker ps
curl -fsS http://127.0.0.1:5088/healthz
curl -fsS https://hypershell.atlasuniversalis.com/healthz
pg_lsclusters
redis-cli PING
df -h /
certbot certificates
~~~

Before any mutation, identify its rollback path and exact target. Do not
silently repair pre-existing application failures during unrelated host work.

## Deferred maintenance reminder

The Ubuntu upgrade and Hypershell deployment have now completed cleanly. This
checklist is due for the next server-maintenance cycle:

- repair or retire the obsolete QuickPro certificate lineage so Certbot
  renewals return to a healthy state;
- decide whether QuickPro, ElectraCast, Forge, n8n, and Sonar are active,
  dormant, redirected, or ready for removal;
- restore missing Forge and ElectraCast frontend distributions if those
  applications remain active;
- reconcile the 44-entry dirty Atlas server working tree with the intended
  source of truth without discarding Cursor-era work;
- review and prune deployment backups only after a documented retention policy;
- establish tested off-host PostgreSQL, Redis, n8n, configuration, and
  application backups;
- configure bounded journal retention, disk alerts, certificate alerts,
  external health monitoring, and regular patch windows;
- review UFW/DigitalOcean firewall rules and the long-term root-SSH policy;
- pin or deliberately update the n8n image rather than relying on latest;
- review obsolete package repositories and unused runtimes.
