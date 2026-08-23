# Gitea RCE / Monero miner incident handover

**Status date:** 2026-08-23 (Europe/Berlin)  
**Host:** `62.171.161.19` / `vmd194319`  
**Affected service:** public Gitea container `gitea`  
**Verdict:** confirmed exploitation of CVE-2026-60004 through Gitea's
`diffpatch` API, followed by installation of XMRig and a watchdog. Treat the
entire Gitea application trust boundary as compromised.

This document is the durable handover for investigation and remediation. It
contains the complete attacker indicators recovered so far. It deliberately
does not copy victim application/database secrets into Git; their locations and
rotation requirements are listed instead.

## Executive finding

The miner did not enter through Wareborn and there is no evidence that it
entered through host SSH. A public client used Gitea's enabled self-registration
to create an ordinary account and repository, then exploited the critical
`diffpatch` Git-hook RCE. The miner binary's filesystem birth time is the exact
second as the exploit request.

The exploited installation is Gitea `1.26.4`. The upstream advisory
[GHSA-rcr6-4jqh-j84m / CVE-2026-60004](https://github.com/go-gitea/gitea/security/advisories/GHSA-rcr6-4jqh-j84m)
marks `>=1.17, <1.27.1` vulnerable and `1.27.1` as the first patched version.
The advisory was public on 2026-07-28; this host was exploited on 2026-08-08.

The vulnerability lets a repository writer submit the same executable hook
patch twice. Git's three-way fallback writes `hooks/post-index-change` into a
bare temporary clone, after which Git executes it as the Gitea OS user. Open
registration turns that authenticated repository-writer primitive into an
attack available to anyone on the Internet.

## Confirmed timeline

All HTTP timestamps below are Gitea container local time (CEST).

| Time | Confirmed event |
|---|---|
| 2026-08-08 12:36:02–03 | Source `104.28.237.52` requested `/user/sign_up` and `/api/v1/version`. This may be a proxy/Cloudflare address rather than the attacker's origin. |
| 12:36:04 | `POST /user/sign_up` returned 303 and created `pocf0ashe`. |
| 12:36:05 | The new account authenticated through `/api/v1/user`. |
| 12:36:07 | `POST /api/v1/user/repos` created `pocf0ashe/poc-r7hj9v`. |
| 12:36:09–31 | Repeated successful `POST /api/v1/repos/pocf0ashe/poc-r7hj9v/diffpatch` requests delivered the Git-hook exploit. The client repeatedly fetched `raw/proof?ref=rce-proof`. |
| **12:36:31** | A slow `diffpatch` request completed in 3.380 seconds. `kmod-cache-update` was born at **12:36:31.183**, inside Gitea's writable home. This timestamp identity is the strongest filesystem-to-request correlation. |
| 12:36:32–36 | Miner log/PID/watchdog files were born. More exploit calls continued through 12:36:56, including one 27.702-second request. |
| 2026-08-10 18:38 | Wallet, PID and watchdog contents were updated and the retained XMRig log begins. The four-thread miner entered continuous operation. |
| 2026-08-10 through 2026-08-23 | XMRig ran as the Gitea service user. Its eventual process showed more than 50 CPU-days and approximately 396% CPU. Authenticated clones of the three `poc-*` repositories recurred roughly every six hours. |
| 2026-08-23 10:55 | Wareborn performance investigation identified the miner and four watchdog shells. Evidence capture began. |
| 10:55–10:56 | Miner/watchdogs were killed and the directory was moved to quarantine. One last watchdog respawn started XMRig at 10:56:04; it was also killed. No further matching process has been observed. |

## Complete attacker indicators

### Gitea accounts and repositories

The following accounts are not legitimate project users and must be preserved
in the database snapshot before being disabled/deleted:

| Account | Email | Repository observed |
|---|---|---|
| `pocf0ashe` | `pocf0ashe@example.com` | `poc-r7hj9v` |
| `poch7fcd5` | `poch7fcd5@example.com` | `poc-mvkknh` |
| `pocagatzz` | `pocagatzz@example.com` | `poc-f00pxz` |

Observed exploit/ref vocabulary:

- `/diffpatch`
- `rce-proof`
- `raw/proof?ref=rce-proof`
- Git hook `hooks/post-index-change`

### Network and mining indicators

- Initial recorded HTTP source: `104.28.237.52`
- Mining pool: `gulf.moneroocean.stream:10128`
- Resolved pool addresses observed in the log: `82.23.163.207`,
  `103.7.55.233`
- Algorithm: `rx/0`
- Miner: `XMRig/6.26.0`
- Threads: `4`
- Wallet:
  `4AjneeUV2MmW8XTusJCditAFk9PQtHm1gV3ZD6RvfHzBf2yd5zZ6Zt27fNDmMhXBCN5NY1EDgKiqoVj25wLK2uwpMb1n3hu`
- Worker/password value: `b6269d8505c1`

### Files and hashes

Original path inside the Gitea container/bind mount:

`/data/gitea/home/.cache/kmod-cache/`

Files:

- `kmod-cache-update` — executable XMRig, 8,350,992 bytes
- `miner-watchdog.sh` — infinite 60-second restart loop
- `miner.log` — retained mining log, approximately 10 MiB
- `miner.pid`
- `wallet-xmr`
- `watchdog.log`

SHA-256 of the captured running executable:

`b20f39fc00d242e706b6c30367ad811c676e0575050a4ec2f30104b696944b49`

Recovered command line:

```text
/data/gitea/home/.cache/kmod-cache/kmod-cache-update --url=gulf.moneroocean.stream:10128 --user=4AjneeUV2MmW8XTusJCditAFk9PQtHm1gV3ZD6RvfHzBf2yd5zZ6Zt27fNDmMhXBCN5NY1EDgKiqoVj25wLK2uwpMb1n3hu --pass=b6269d8505c1 --donate-level=1 --algo=rx/0 --randomx-mode=auto --no-color --threads=4 --background --log-file=/data/gitea/home/.cache/kmod-cache/miner.log
```

## Evidence locations and integrity caution

Root-only evidence directory:

`/opt/wareborn/backups/security-miner-20260823T085500Z`

It contains process/cmdline/socket captures, the SHA-256 record, and a copied
`kmod-cache/` directory. The top-level directory is mode `0700`; the nested
attacker files retain their original `matrixadmin` ownership/modes.

Recoverable quarantine in the still-live Gitea bind mount:

`/root/forgejo-server/data/gitea/home/.cache/kmod-cache.quarantined-20260823T085500Z`

The quarantine is **not an ideal final evidence store** because it remains in a
writable live application volume and is owned by the compromised service user.
Before rebuilding Gitea, make a root-owned read-only copy on independent storage,
record hashes for every file, and preserve the Docker JSON logs and database.
Do not execute the binary or hook content.

Other evidence sources:

- `docker logs gitea` — contains the full exploit request sequence.
- `/root/forgejo-server/data/gitea/` — bind-mounted Gitea state.
- Gitea database — suspicious accounts, repositories, tokens, OAuth grants,
  hooks and audit timestamps.
- Container image digest:
  `sha256:8e25c717b8f748445e15ec46e0390f577cb628101184cb0a150d1dae126c1f39`
  (`gitea/gitea:latest`, image created 2026-06-21).

## Exposure and blast-radius assessment

Confirmed configuration at discovery:

- Gitea `1.26.4`, still vulnerable to CVE-2026-60004.
- `DISABLE_REGISTRATION = false`
- `REQUIRE_SIGNIN_VIEW = false`
- `ENABLE_CAPTCHA = true` (did not stop the automated registration)
- `DEFAULT_ALLOW_CREATE_ORGANIZATION = true`
- Host published container ports `3000` and `222` on all interfaces.
- Bind mount `/root/forgejo-server/data:/data:rw`.
- Container restart policy `always`.
- Container was not privileged, did not share the host PID namespace and had no
  Docker socket mount.

The attacker obtained command execution as the Gitea service account
(`matrixadmin` on the host-owned bind data). This access was sufficient to read
or alter:

- `app.ini` and its application/internal/JWT secrets;
- Gitea database credentials and reachable database contents;
- all repositories and server-side Git hooks;
- access tokens, OAuth applications/grants, deploy keys and webhook secrets;
- configured mirror/integration credentials available to Gitea;
- any internal service reachable from the Gitea container network.

No accepted host SSH login was found between August 7 and August 11, and the
host login ledger has no interactive login after August 2 before discovery.
There is currently no evidence of container escape or host-root execution. This
is a bounded finding, not proof that escape/exfiltration did not occur.

Wareborn game/login services did not provide the entry path. The incident
affected them indirectly by consuming roughly four CPU cores, producing server
timing pressure and visible ship stutter.

## Containment already performed

- Captured process, command line, connections, binary hash and malicious files.
- Killed the XMRig process and all four discovered watchdog shells.
- Killed one final watchdog-triggered respawn.
- Moved the malicious directory to the named recoverable quarantine.
- Checked host cron locations; no miner persistence was found there.
- Repeated process scans show no `kmod-cache`, `moneroocean` or `xmrig` process.
- Host CPU idle recovered to approximately 90–95 percent.

Not yet performed:

- Gitea has not been stopped or rebuilt.
- Registration has not been disabled.
- Public ports have not been restricted.
- Suspicious accounts/repos have not been removed.
- Gitea-accessible credentials have not been rotated.
- Repository hooks, database records and internal lateral movement have not
  received a complete forensic audit.

The service therefore remains **compromised and vulnerable**, even though the
specific miner is not running.

## Required next actions

Perform these in order. Preserve evidence before eradication.

### 1. Preserve

1. Announce a maintenance window and prevent further Gitea writes.
2. Capture `docker inspect gitea`, image metadata, complete Docker logs, a Gitea
   database dump and a filesystem snapshot of `/root/forgejo-server/data`.
3. Copy both miner evidence trees to independent root-owned storage and generate
   a recursive SHA-256 manifest.
4. Record account, token, OAuth, webhook, deploy-key, repository-hook and Actions
   runner tables before altering them.
5. Preserve the three `poc-*` bare repositories, every ref (especially
   `rce-proof`) and their reflogs/objects.

### 2. Contain

1. Disable public registration immediately.
2. Remove direct Internet exposure of ports 3000/222; place required access
   behind the intended proxy/firewall/VPN only.
3. Suspend the three `poc*` accounts after the evidence snapshot.
4. Stop or isolate the compromised container and Actions runner during rebuild.
5. Block the mining pool/domain and retained pool IPs as detection/containment
   indicators, not as the primary fix.

### 3. Eradicate and rebuild

1. Rebuild from a clean pinned Gitea image at **1.27.1 or newer**. Do not merely
   restart `latest`, and do not reuse the compromised container filesystem.
2. Restore data selectively from the preserved snapshot after auditing all bare
   repository hooks, custom hooks, Git config, templates, attachments and
   Actions workflows.
3. Remove the quarantined malicious directory only after independent evidence
   preservation and explicit operator approval.
4. Re-register a clean Actions runner with a new token and least privilege.

### 4. Rotate credentials

Assume every secret readable from the Gitea process was exposed. Rotate:

- Gitea `SECRET_KEY`, `INTERNAL_TOKEN`, JWT/LFS and session secrets;
- Gitea database password;
- Gitea admin and all active user passwords; require 2FA for administrators;
- personal/API access tokens and OAuth application/client secrets;
- repository deploy keys, webhook secrets and Actions runner tokens;
- GitHub/mirror/integration credentials formerly stored in Gitea;
- SMTP or other service credentials referenced by `app.ini`.

Review host SSH keys and unrelated infrastructure credentials for evidence of
use, but do not claim they were exposed solely from this container-level RCE.

### 5. Validate and monitor

- Confirm the fixed version rejects the recorded `diffpatch` exploit sequence.
- Confirm registration and public port policy from outside the VPS.
- Recursively audit every repository for executable hooks and unexpected refs.
- Search the host, containers and persistent volumes for the complete IOC set.
- Monitor process creation, outbound pool traffic, new Gitea users/repos and
  authentication events.
- Keep the Wareborn loop-stage telemetry: it was the signal that uncovered the
  compromise.

## Open investigation questions

1. What exact commands did each `rce-proof` ref record, and did they read or
   exfiltrate Gitea secrets before installing XMRig?
2. When and how were `poch7fcd5` and `pocagatzz` created, and did they exploit
   the same endpoint independently?
3. What generated the recurring six-hour authenticated clones of all three
   attacker repositories?
4. Were any access tokens, OAuth grants, deploy keys, hooks or Actions workflows
   created/modified by the attacker?
5. Did the attacker access private repositories or internal services reachable
   through the Gitea container network?
6. Are there additional payloads in Git objects, temporary clones, package
   storage, attachments or deleted-but-recoverable refs?
7. Did the August 10 modification/start event use the original RCE foothold, a
   second exploit run, or another persistence mechanism?

## Operator decision boundary

Stopping Gitea, disabling accounts, changing network exposure, deleting the
quarantine and rotating credentials are material external-state changes. They
have not been inferred from the diagnostic request. The next operator should
obtain explicit approval for the maintenance window, then execute the preserve
and containment sequence above without postponing the upgrade.

