# Deployment

This chapter describes how to run ReviewCenter for a team: where it keeps its data, how to configure it, how to
reach it over the network, how to keep it running, and how to back it up and upgrade it. It is written for the
person who operates the server. The other chapters describe how people use it.

ReviewCenter is one self-contained process. It serves the web pages and the API, keeps its data in a SQLite file, and
runs the `git` program to read repositories. It has no separate database server, no worker process, and no
installer.

## What Has Been Verified

The project verified the commands and outputs in this chapter by running a win-x64 build on Windows (with a
throwaway database in a temporary folder). The Linux build is exercised by the project's continuous integration
(health check, self-validation, and a smoke test of the pages), but the Linux-specific recipes in this chapter
(systemd, nginx, file permissions) were **not run** by the project. Wherever a recipe is a generic approach that was
not run on your platform, the text says so. Treat such recipes as a starting point and test them on a copy first.

ReviewCenter does not integrate with the Windows Service Control Manager or with systemd. It is a console process that
you start with `reviewcenter serve`, and anything that keeps it running has to be an outside tool that starts that
process (see Keep It Running).

## System Requirements

- **Platform.** The released builds are win-x64 and linux-x64. No .NET runtime needs to be installed, because the
  executable carries its own.
- **git.** Reading a repository needs the `git` program on the `PATH` of the account that runs the server, version
  2.31 or later. An operation that needs git fails with a message that says so if git is missing or too old. Check
  it with `reviewcenter --validate` (see Check an Installation below).
- **Disk space.** The executable is about 110 MB. The database grows with the content that is stored: the files of
  reviews, the comments, and the audit trail. The git cache (see Where the Data Lives) holds one bare mirror per
  repository, so plan for the size of those repositories.
- **A user account for the service.** Run the server under an account of its own that can write to the data folder
  and nothing else. It needs no administrator rights when it listens on a port above 1023 (the default port is 5000).

## Download and Verify

Download the archive of your platform and its `.sha256` file from the project's GitHub Releases page, verify the
checksum, and extract the archive. The archive holds one folder, named like the archive, with the executable and the
files that must stay beside it. The commands are in the Installation chapter; the files in the folder are (leave
them all in place):

| File                                                         | Purpose                                          |
| ------------------------------------------------------------ | ------------------------------------------------ |
| `reviewcenter.exe` (`reviewcenter` on Linux)                 | The server and the command-line tool             |
| `e_sqlite3.dll` (`libe_sqlite3.so` on Linux)                 | The native SQLite library; keep it in the folder |
| `appsettings.json`                                           | The default settings (see Configuration)         |
| `wwwroot` folder                                             | The static web assets                            |
| `DemaConsulting.ReviewCenter.staticwebassets.endpoints.json` | Endpoint map of the static web assets            |
| `aspnetcorev2_inprocess.dll` (Windows only)                  | IIS hosting module; not used by `serve`          |

The native SQLite library and the endpoint map must stay beside the executable. The `aspnetcorev2_inprocess.dll` file
is the ASP.NET Core module for hosting under IIS; `reviewcenter serve` does not use it, and it is safe to ignore.

Extract the archive to a stable folder instead of a download folder, for example `C:\Program Files\ReviewCenter`
(Windows) or `/opt/reviewcenter` (Linux), and make the folder read-only for the service account. On Linux, make sure
that the `reviewcenter` file is executable.

No release has been published yet. Until one is, publish the server from source with the .NET 10 SDK, for example
`dotnet publish src/DemaConsulting.ReviewCenter -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
(use `linux-x64` for Linux). The release archives are built the same way, but the release workflow renames the
executable to `reviewcenter.exe` (`reviewcenter` on Linux). A build from source produces
`DemaConsulting.ReviewCenter.exe` (`DemaConsulting.ReviewCenter` on Linux); use that name wherever this guide says
`reviewcenter`.

## Where the Data Lives

All state is in one data folder. Nothing is written beside the executable unless the account has no application data
folder (see below). The folder holds these items:

| Item                             | Default location                         | Setting that moves it          |
| -------------------------------- | ---------------------------------------- | ------------------------------ |
| Database `reviewcenter.db`       | The `ReviewCenter` folder of the account | `Database:ConnectionString`    |
| Write-ahead log (`-wal`, `-shm`) | Beside the database file                 | Moves with the database        |
| Data Protection key ring         | `keys` folder beside the database        | `DataProtection:KeysPath`      |
| Protection of the key ring       | DPAPI, certificate, or permissions       | `DataProtection:Protection`    |
| Self-signed attestation key      | `keys/attestation-selfsigned` folder     | `Attestation:SelfSignedFolder` |
| Git mirrors                      | `git-cache` folder beside the database   | `Git:CachePath`                |

The default `ReviewCenter` folder is `%LOCALAPPDATA%\ReviewCenter` on Windows and `~/.local/share/ReviewCenter` on
Linux. When the account has no such folder (for example a Linux service account without a home folder), the data goes
to `data` beside the executable. **For a service, set `Database:ConnectionString` to an explicit file, so that the
location does not depend on the account.** The keys, the attestation key, and the git cache then sit beside that
file. A relative path is resolved against the working directory of the process, so use an absolute path.

On a Windows machine the following was observed with a new database, a running server, and a graceful stop:

- While the server runs, the data folder holds the database file (named `rc.db` in the test through
  `Database:ConnectionString`), its `-wal` and `-shm` files, the `keys` folder with one `key-<guid>.xml` file, and
  `keys\attestation-selfsigned\attestation-selfsigned.pfx`. The server log shows that it switches the database to the
  write-ahead log mode.
- After a graceful stop (Ctrl+C on Windows; SIGTERM from systemd is also a stop request of ASP.NET Core, but the
  project did not run that) the `-wal` and `-shm` files are gone and the database file is complete. After a crash or
  a killed process they stay, and the next start reads them. **Never delete the `-wal` file of a database that was
  not stopped cleanly**: it can hold committed changes that are not yet in the database file.

The git cache can be rebuilt (the server fetches the repositories again). The database and the keys cannot be
rebuilt; see Back Up and Restore.

The key files of the ring are encrypted at rest when the platform or you provide a protection (see Key Ring Protection
in the Administration chapter). **A key ring that Windows DPAPI protects can be read only by the same account on the
same machine (with `DpapiScope` `LocalMachine`, by any account on the same machine).** A copy of the `keys` folder
restored on another machine or under another account is unreadable and the stored tokens are lost, so use a
certificate (`DataProtection:CertificatePath`) for a ring that must move or be restored elsewhere. Without encryption
(`PermissionsOnly`) the server restricts the `keys` folder and every key file in it to its own account, as the files are
created (mode 0700 and 0600 on Linux and macOS; on Windows an access rule that admits only the account and the system,
set at every start), so a backup account needs its own way to read it.

## Configuration

The server reads its settings from these sources; a later source wins:

1. `appsettings.json` beside the executable, with the defaults.
2. `appsettings.<Environment>.json` beside the executable, when it exists. The environment is the value of
   `ASPNETCORE_ENVIRONMENT` (or `DOTNET_ENVIRONMENT`), and `Production` when neither is set. The project verified
   on Windows that an `appsettings.Production.json` with an invalid value stopped the server without any variable
   set, and that an `appsettings.Staging.json` was read when `ASPNETCORE_ENVIRONMENT` was `Staging`.
3. Environment variables. A setting `Section:Key` is the variable `Section__Key` (two underscores).

There is no command-line option for a setting other than `--urls`. The verbs `user add` and `audit verify`, and the
option `--check-config`, read the same sources, so set the same environment for them as for `serve`.

Edit `appsettings.json` only for values that stay the same for the installation; a new release replaces that file.
Put the settings of an installation in environment variables of the service, or in an `appsettings.Production.json`
that is not part of the release archive. The defaults are:

| Setting                                   | Default                        | Described in                            |
| ----------------------------------------- | ------------------------------ | --------------------------------------- |
| `Database:Provider`                       | `Sqlite`                       | Database (Administration)               |
| `Database:ConnectionString`               | empty (the default location)   | Database (Administration)               |
| `Auth:SessionHours`                       | 8 hours                        | Users and Sign-in                       |
| `Auth:RevalidationSeconds`                | 30 seconds (at least 5)        | Users and Sign-in                       |
| `Identity:Throttle:MaxFailuresPerName`    | 5 attempts for one user name   | Password Rules and Lockout (Sign-in)    |
| `Identity:Throttle:MaxFailuresPerAddress` | 20 attempts for all user names | Password Rules and Lockout (Sign-in)    |
| `Identity:Throttle:WindowSeconds`         | 300 seconds (1 to 86400)       | Password Rules and Lockout (Sign-in)    |
| `Identity:Totp:Enabled`                   | true                           | Two-Factor Sign-in (Sign-in)            |
| `Identity:Oidc:*`                         | off                            | Sign In with an OpenID Connect Provider |
| `Identity:Oidc:ClientSecretFile`          | empty (use `ClientSecret`)     | Identity Providers                      |
| `DataProtection:KeysPath`                 | `keys` in the data folder      | Repositories and Credentials            |
| `DataProtection:Protection`               | `Auto`                         | Key Ring Protection (Administration)    |
| `DataProtection:DpapiScope`               | `CurrentUser` (Windows only)   | Key Ring Protection (Administration)    |
| `DataProtection:CertificatePath`          | empty (no certificate)         | Key Ring Protection (Administration)    |
| `DataProtection:CertificateKeyPath`       | empty (key in the certificate) | Key Ring Protection (Administration)    |
| `DataProtection:CertificatePasswordFile`  | empty (no password file)       | Key Ring Protection (Administration)    |
| `DataProtection:PreviousCertificatePaths` | empty list                     | Key Ring Protection (Administration)    |
| `DataProtection:RequireEncryptedKeys`     | false                          | Key Ring Protection (Administration)    |
| `DataProtection:AllowNewKeyRing`          | false                          | Back Up and Restore (Deployment)        |
| `Attestation:*`                           | a self-signed key              | The Signing Key (Reviewing)             |
| `Git:CachePath`                           | `git-cache` in the data folder | Repositories and Credentials            |
| `Git:TimeoutSeconds`                      | 120 seconds (1 to 3600)        | Repositories and Credentials            |

The two attempt limits and the window belong together: one client address may make at most the given number of
sign-in attempts (a successful sign-in gives its attempt back) within one window.

Changes take effect when the server is restarted. The server checks its settings at start, and an invalid value stops
it with a message that names the setting (see Troubleshooting). Which settings are checked depends on the group:

- **Always checked**: `Auth` (`SessionHours` above 0, `RevalidationSeconds` at least 5), `Identity:Throttle` (both
  limits above 0, the address limit not below the name limit, the window 1 to 86400 seconds), `Identity:Totp` (the
  issuer must be 1 to 64 characters without a colon or control character, also while `Enabled` is `false`; a value of
  `Enabled` that is not a boolean also stops the server), `Git:TimeoutSeconds`, the database provider and connection
  string, the Data Protection folder (it must be writable), and the attestation key (the provider name, and the
  settings of the provider that you chose, must give a usable key).
- **Checked only when switched on**: `Identity:Oidc`. While `Identity:Oidc:Enabled` is `false` (the default), nothing
  else of that section is read or checked, so a wrong value there shows up only when you turn the sign-in on. The
  project observed that with `Enabled` set to `true` and no `Authority`, the server stopped with `Invalid OpenID
  Connect sign-in settings: Identity:Oidc:Authority is required when sign-in with OpenID Connect is enabled.`

A disabled feature is therefore not checked until you enable it. Start the server by hand with the feature enabled to
check its settings before you rely on them.

Set an environment variable for one run like this:

```powershell
# Windows PowerShell
$env:Database__ConnectionString = 'D:\ReviewCenterData\reviewcenter.db'
.\reviewcenter.exe serve --urls http://localhost:8080
```

```bash
# Linux
Database__ConnectionString=/var/lib/reviewcenter/reviewcenter.db reviewcenter serve --urls http://127.0.0.1:8080
```

The Linux command was not run by the project; the Windows form was.

## Choose the Address and Port

By default the server listens on `http://localhost:5000`, which only the same machine can reach. Choose the address
with the option `--urls`, which takes an absolute `http` or `https` URL:

| Goal                                                | Option                          |
| --------------------------------------------------- | ------------------------------- |
| Only this machine (behind a reverse proxy)          | `--urls http://127.0.0.1:5000`  |
| Another port, only this machine                     | `--urls http://localhost:8080`  |
| Every network interface of the machine (all IPv4)   | `--urls http://0.0.0.0:8080`    |

Use only `--urls` for this. **The environment variable `ASPNETCORE_URLS` is ignored**: with it set to another port,
the server still listened on `http://localhost:5000` (the server always applies the `--urls` value or its default).
A startup message `ReviewCenter server started and listening on <address>` in the log shows the address that is in
use.

If the port is taken, the server writes `Failed to start the server on <url>: Failed to bind to address <url>:
address already in use.` and exits with code 1.

When the server listens on an address that other machines can reach, put TLS in front of it (see TLS and Reverse
Proxy). Open the port in the firewall of the host only for the machines that need it; the project did not test any
firewall configuration.

The path `/health` answers `200` with the text `Healthy` and needs no sign-in. Use it as the check of a load
balancer, a proxy, or a monitoring system.

## Keep It Running

The server runs until it is stopped, and it does not restart itself. In production, use a tool that starts it at
boot, restarts it when it stops, captures its output, and stops it with a termination request (Ctrl+C, or SIGTERM on
Linux) so that the database is closed cleanly.

**ReviewCenter has no built-in Windows service or systemd support.** The executable does not talk to the Service
Control Manager, so registering `reviewcenter.exe` directly with `sc.exe create` does not give a working service. The
recipes below are generic approaches for tools that run any console program as a service. **The project did not test
any of them.**

### Logging

The server writes its log to the console (standard output and standard error), and nothing else. By default it logs
at the `Information` level, including every HTTP request, and on the first start of a new database also the statements
that create the tables. Capture the output with the service tool: the journal of systemd, or the log file settings of
a Windows service wrapper. To log only warnings and errors, set `Logging__LogLevel__Default=Warning`; with that
setting, the log of a normal start held only the version banner and a warning that the attestation key is
self-signed (see the hardening checklist).

### Linux with systemd (generic, not verified)

A unit file like the following runs the server under its own account. Replace the paths, the account, and the port.

```ini
[Unit]
Description=ReviewCenter
After=network.target

[Service]
Type=simple
User=reviewcenter
WorkingDirectory=/opt/reviewcenter
ExecStart=/opt/reviewcenter/reviewcenter serve --urls http://127.0.0.1:5000
Environment=Database__ConnectionString=/var/lib/reviewcenter/reviewcenter.db
Environment=Logging__LogLevel__Default=Warning
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

Create the account and the data folder first, and let only that account use the folder (`chown reviewcenter` and mode
`0700`). Keep secrets, such as the password of a certificate, out of the unit file when the file is readable by other
people; use a file with restricted permissions that you name with `EnvironmentFile=` instead.

### Windows (generic, not verified)

Use a tool that runs a console program as a service (a service wrapper for console programs) or the Task
Scheduler. Whatever you use, it has to:

- run `reviewcenter.exe serve --urls <address>` with the installation folder as the working directory;
- run it under an account that can write to the data folder (an account of its own is better than `LocalSystem`),
  and set `Database__ConnectionString` explicitly, because the default folder depends on the account profile;
- set the environment variables of the settings you use;
- capture standard output and standard error to a file with rotation;
- start it at boot and restart it when it exits;
- stop it with a Ctrl+C style request (not by killing the process) so that the database is closed cleanly.

Follow the documentation of the tool for the commands. In the project's experiment on Windows, a process that
received a Ctrl+C logged `Application is shutting down...` and exited, and removed the `-wal` and `-shm` files.

## TLS and Reverse Proxy

Anyone who can reach the server over a network sees the passwords and the session cookies of the people who sign in,
unless the traffic is encrypted. Use HTTPS whenever the server can be reached from other machines. There are two
ways to do it.

### Terminate TLS in a Reverse Proxy (Recommended)

Run the server on the loopback address (`--urls http://127.0.0.1:5000`) and let a reverse proxy (for example nginx,
Caddy, or a load balancer) end TLS and forward plain HTTP to it. The proxy must do all of the following,
and Secure Cookie Behind a Reverse Proxy below explains why:

- Forward the original scheme in the `X-Forwarded-Proto` header and the client address in `X-Forwarded-For`, and
  **overwrite** these headers with its own values instead of passing on what the client sent.
- Keep the `Host` header of the client request (the addresses of the attestation PDFs in the evidence index of the API
  are built from it).
- Allow WebSocket connections, because the pages are interactive and keep a connection to the server open, and do not
  close idle connections early.
- Be the only route to the server (the server listens on the loopback address, or a firewall allows only the proxy).

Set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` in the environment of the server, so that it reads the forwarded
scheme and client address. The project verified the effect on Windows: with that variable and a request that carried
`X-Forwarded-Proto: https`, the sign-in cookie was marked `secure`, also when the request came from an address that
was not the loopback address; without the variable, the same request produced a cookie without `secure`.

The following nginx server block is a generic example **that was not run by the project**. It assumes that the
certificate files exist, and it must be adapted:

```nginx
server {
    listen 443 ssl;
    server_name reviewcenter.example.com;
    ssl_certificate     /etc/ssl/reviewcenter.example.com.pem;
    ssl_certificate_key /etc/ssl/reviewcenter.example.com.key;

    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-For $remote_addr;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;
        proxy_read_timeout 1h;
    }
}
```

The variable `$connection_upgrade` comes from a `map` block in the `http` section of the nginx configuration (see the
nginx documentation for WebSocket proxying). `X-Forwarded-For $remote_addr` replaces the header instead of adding to
what the client sent.

### Terminate TLS in the Server

The server can serve HTTPS itself. Give it an `https` address and a certificate file with the standard Kestrel
settings, here as environment variables:

```powershell
# Windows PowerShell
$env:Kestrel__Certificates__Default__Path = 'C:\ReviewCenter\tls\reviewcenter.pfx'
$env:Kestrel__Certificates__Default__Password = '<the password of the pfx file>'
.\reviewcenter.exe serve --urls https://0.0.0.0:8443
```

The project verified this on Windows with a generated test certificate: the server logged that it listened on the
`https` address, answered `/health` with `200`, and presented that certificate (TLS 1.3). It did not verify other
certificate formats (for example PEM files with a separate key file), certificate renewal, or Linux. A certificate
file with a password in the environment is readable by anybody who can read the environment of the process, so protect
the service definition. Reload a renewed certificate by restarting the server. In this mode the request itself is
HTTPS, so the sign-in cookie is marked Secure without further settings, and `ASPNETCORE_FORWARDEDHEADERS_ENABLED`
is not needed.

### Host Filtering, HTTPS Redirection, and HSTS

In both ways of terminating TLS, the server itself does none of the following, so the reverse proxy or the front end
has to do them where you need them:

- **No host filtering.** The shipped settings do not set `AllowedHosts`, and the server code does not add a host
  check. The server answers a request with any `Host` header: in the project's test, a request to `/health` with the
  host `evil.test` got `200`. This matters because the addresses of the attestation PDFs in the evidence index of the
  API are built from the scheme and the `Host` header of the request (see the nginx example above). Let the proxy
  accept only the host names of your installation, and refuse every other name. The ASP.NET Core setting
  `AllowedHosts` is not a documented ReviewCenter setting; in a quick test, setting it as an environment variable did
  make the server refuse other host names, but the project did not test that further.
- **No HTTP-to-HTTPS redirection.** The server does not redirect a plain `http` request to `https`. In the project's
  test, a plain request got its answer directly, without a `Location` header. Let the proxy redirect port 80 to HTTPS,
  or do not offer plain HTTP to other machines at all.
- **No HSTS.** The responses of the server carry no `Strict-Transport-Security` header. If you want browsers to use
  only HTTPS for your host name, let the proxy add that header (to its HTTPS responses only).

## Secure Cookie Behind a Reverse Proxy

The sign-in cookie is marked Secure when the request reaches the server over HTTPS. When a reverse proxy ends TLS and
forwards plain HTTP to ReviewCenter, set the environment variable `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` for the
server, so that it trusts the forwarded scheme of the proxy and still marks the cookie Secure. Use HTTPS whenever the
server can be reached from other machines.

The sign-in throttle (see Sign In and Out in the Users and Sign-in chapter) uses the client address, and the same
setting makes the server take that address from the `X-Forwarded-For` header. The server then accepts that header from
any sender and does not restrict which proxies it trusts. Keeping the header trustworthy is the responsibility of the
deployment: the proxy must overwrite it and be the only route to the server. If the server can be reached directly, a
client can choose its own address and avoid the per-address and per-name limits (the lockout of the user name still
applies). Without the setting, behind a proxy, every user appears with the address of the proxy and shares one set of
limits.

## Check an Installation

Before you let people use a new installation, set the same environment as the service will have, and run the
self-validation with the configured database check:

```powershell
$env:Database__ConnectionString = 'D:\ReviewCenterData\reviewcenter.db'
.\reviewcenter.exe --validate --check-config
```

The run takes a few seconds. It checks that git is available (and its version), runs a review workflow, a signed
attestation, and a short start of the web server on a free loopback port, and last runs two checks of the
deployment itself, both **read-only**:

- `ReviewCenter_ConfiguredDatabase` opens the configured database and compares its tables and columns with what this
  version expects.
- `ReviewCenter_ConfiguredKeys` reads the configured key ring and tries to decrypt every stored repository token and
  second-factor secret with it. It reports the protection in force, the number of key files (and how many are not
  encrypted), the number of protected secrets, and how many of them cannot be decrypted. It creates no key, folder, or
  file, and prints no key, secret, password, or path other than the key folder.

With a database and a key ring in place, the run ends with `Passed: 11` and exit code 0, and the last two lines read like
`ReviewCenter_ConfiguredDatabase - Passed (...): provider Sqlite; opened read-only; schema matches (18 tables, 148
columns)` and `ReviewCenter_ConfiguredKeys - Passed (...): protection Dpapi; key files 1 (0 not encrypted); protected
secrets 3 (repository tokens 2, second-factor secrets 1); unreadable 0`; the numbers are those of the installation and,
for the schema, grow when a release adds tables or columns. The first of the two lines was observed on a Windows
machine of the project; the second shows the form of the report. A non-zero exit code means that a
check failed; the message names the check (see Self-Validation in the Command-Line Reference chapter). The key check
**fails** when secrets cannot be decrypted (the message gives the counts and the repository and user ids), when the key
folder is empty or missing but the database holds secrets, when the ring is not encrypted and
`DataProtection:RequireEncryptedKeys` is true, and when the newest key cannot be decrypted. A fresh
installation has no database file yet: both checks are then **skipped** (the report shows `SKIPPED` and the run does not
fail), and the server creates the file at the first start. The key check is also skipped when the database exists and
holds no secret and the key folder does not exist yet.

Then start the server and check `/health`. A first start with a new database creates the data folder, the database,
the key ring, and the attestation key. Create the first system administrator before anyone signs in, with the command
that Create the First Administrator in the Users and Sign-in chapter describes; run it with the same environment as
the service. After that, sign in and add the users and projects that Manage Users and Access and Visibility in the
Administration chapter describe.

## Back Up and Restore

Four things belong together, and a backup that misses one of them is not a backup:

1. **The database** (`reviewcenter.db`), with its `-wal` and `-shm` files when they exist.
2. **The key ring folder** (`keys`, or `DataProtection:KeysPath`). It holds the keys that encrypt the stored repository
   tokens and the second-factor secrets of the database, and, by default, the self-signed attestation key
   (`attestation-selfsigned`). If you moved the attestation key with `Attestation:SelfSignedFolder`, back that folder up
   too. If the signing key is in the certificate store of the machine (`Attestation:Provider` set to `CertificateStore`),
   back up that certificate and its private key by the means of your organization.
3. **The secret that protects the key ring**, which is not in the folder: for DPAPI, the Windows account (and the machine,
   or only the machine with `DpapiScope` `LocalMachine`) that wrote the keys; for a certificate, the certificate file
   (`DataProtection:CertificatePath`, and any `PreviousCertificatePaths`) **and its password** (or the password file).
   Back them up apart from the keys, so that one lost backup does not give an attacker both.
4. **The settings** of the installation: the environment variables or `appsettings.Production.json` that name these
   locations and the protection.

The `git-cache` folder is optional: the server rebuilds it from the repositories.

To take a backup:

1. Stop the server (a graceful stop closes the database and removes the `-wal` and `-shm` files). A plain copy of a
   database that is in use may be inconsistent, because recent changes can be in the `-wal` file that the copy does not
   take together with the database file; so either stop the server, or copy the `-wal` and `-shm` files with the
   database file and use a backup method that understands SQLite. The project did not test any online backup.
2. Copy the database file and its `-wal` and `-shm` files, and the key ring folder (and the attestation folder when it
   is elsewhere), to the backup location. Keep the certificate and its password as described above.
3. Start the server again.

The keys and the pfx file are not encrypted at rest unless the key ring is protected (see Key Ring Protection in the
Administration chapter; the pfx file of the attestation key has no password). Protect the backup like the live data,
and let only the service account and the backup operator read the files.

**A key ring that Windows DPAPI protects cannot be restored on another machine or under another account.** DPAPI
binds the keys to the account (or machine) that wrote them, so a copy restored elsewhere is unreadable, and with it
every stored repository token and second-factor secret. If the installation may have to be restored on another machine
(a disaster recovery, a migration), protect the key ring with a certificate (`DataProtection:Protection` `Certificate`
and `DataProtection:CertificatePath`; see Key Ring Protection in the Administration chapter) and keep the certificate
and its password in the backup.

What happens when the keys are missing at the restore:

- **Restoring all of it works.** A copy of the stopped data folder, started with `Database:ConnectionString` pointing
  at the copy, accepted the password sign-in of the existing user.
- **Restoring the database without its key ring is refused.** When the key folder holds no key file but the database
  holds protected secrets, the server does not start. Data Protection would otherwise create a new key ring silently,
  and every stored repository token and second-factor secret, which the old key ring encrypted, could never be read
  again. The server stops with
  `Key ring '<folder>' is empty but the database holds N protected secrets. Restore the keys folder that belongs to this
  database, or set DataProtection:AllowNewKeyRing=true for one start to accept that they become unreadable.`
  The right action is to restore the key folder that belongs to this database. If you accept the loss (the tokens
  must then be entered again, and an administrator resets the two-factor secret of each user), set
  `DataProtection:AllowNewKeyRing` to `true` for **one start** (for example `DataProtection__AllowNewKeyRing=true` in
  the environment of that start, and remove it afterwards). The server then starts, creates a new key ring, and logs a
  warning with the number of secrets that became unreadable. A fresh installation (no secrets) is not affected and
  needs no setting.
- **A database that cannot be read, next to an empty key folder, is refused too.** When the server cannot read the
  database to check whether it holds protected secrets (it is locked, damaged, or not a database) and the key folder
  holds no key file, it does not start and creates no key, because it cannot tell a lost key ring from a new
  installation. Fix the access to the database and start again; `DataProtection:AllowNewKeyRing` does not bypass this,
  because it only accepts the loss of secrets that are known to exist. When the key folder has keys, the server starts
  and logs a warning, and the start-up that opens the database reports the problem.
- **A key ring that does not belong to the database is not refused, but reported.** When the folder has keys but some
  secrets cannot be decrypted with them (a key folder of another installation, or an older backup), the server starts,
  because the rest of the data is usable, and logs an error at every start with the counts of unreadable repository
  tokens and second-factor secrets and their repository and user ids. Enter the repository tokens again, and have an
  administrator reset the two-factor secret of each user listed; or restore the right key folder. Run
  `reviewcenter --validate --check-config` to see the same counts without starting the server.
- The attestation key is affected only with the default `SelfSigned` provider: when its pfx file is missing (it is in the
  key folder by default), the server generates a new key, so the signatures of new attestation PDFs come from a new key,
  and a verifier that trusts the old key does not trust them. A key of the `CertificateStore` provider is not in the data
  folder; it stays in the certificate store, and the server does not generate one (a missing certificate stops the start
  with a message).
- A ring protected by a certificate that is missing or wrong is not replaced: the server refuses to start with a message
  that names `DataProtection:CertificatePath`. Fix the certificate or list the old one in
  `DataProtection:PreviousCertificatePaths`.

### Test a Restore

Test the backup before you need it, in a scratch location, never over the live data. The restore must happen with the
same environment as the real installation, because a DPAPI ring is bound to the account and machine, and a certificate
ring needs the certificate and its password:

1. Restore the database file (with its `-wal` and `-shm` files if the backup has them) and the key ring folder to a
   scratch folder, and the certificate to where the settings expect it.
2. Point the settings at the scratch folder (`Database__ConnectionString`, and `DataProtection__KeysPath` and
   `Attestation__SelfSignedFolder` when the backup does not use the default places beside the database), and set the same
   protection settings and certificate password as the installation.
3. Run `reviewcenter --validate --check-config`. The `ReviewCenter_ConfiguredKeys` line must pass with `unreadable 0`.
   It fails, without creating anything, when the key ring does not belong to the database, when the ring is empty, or
   when the certificate does not open it; it does not start the server and changes nothing in the scratch folder.
4. Run `reviewcenter audit verify` with the same environment (exit code 0 when the audit chain is intact, 1 when it is
   broken). It never creates a database, so a wrong path is reported as an error with exit code 1. The project's test on
   a new database printed `Audit chain intact: 1 entries`.
5. Delete the scratch copy, which holds the same secrets as the live data.

The checks of steps 3 and 4 show that the database can be read and that the keys decrypt what it holds. They do not
show that the attestation key is the one that signed earlier PDFs; compare the certificate of a new and an old
attestation PDF if you must be sure.

## Upgrade

**This release has no database migrations.** The server creates the tables when the database file is new, and it does
not change the tables of an existing file (the guide chapter Database in the Administration chapter says the same).
So the database that a new version needs may differ from the one of the old version, and the server does not detect
and does not repair a difference: in the project's experiment, a server started normally against a database that
lacked a table, and `/health` answered `200`. Only `--validate --check-config` reported the difference, with the
message `The database schema does not match this version of ReviewCenter: table 'UserTotps' is missing.` and exit
code 1. Planned Work in the Introduction lists a migration command as future work.

The supported upgrade procedure is therefore:

1. Read the release notes of the new version for changes of the database, and of the settings.
2. Stop the server, and take a backup (see Back Up and Restore).
3. Replace the folder of the executable with the folder of the new release (keep your `appsettings.<Environment>.json`
   or the environment of the service).
4. Run `reviewcenter --validate --check-config` with the environment of the service. If the check of the database
   or of the keys fails, do not start the server on that file: the file does not fit the new version. Restore the old
   version and the backup, or, **for development data only**, start with a new database file as the Database section
   describes.
5. Start the server and check `/health` and a sign-in.

To go back, stop the server, restore the old executable folder and the backup that you took in step 2 (restore the
database file and the keys together), and start it again. The project did not test an upgrade between two releases,
because no release has been published yet.

## Hardening Checklist

- Use HTTPS (see TLS and Reverse Proxy), and keep the server on the loopback address when a proxy is in front of it.
- Behind a proxy, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, and make the proxy overwrite `X-Forwarded-For` and
  keep `Host` (see Secure Cookie Behind a Reverse Proxy).
- Let the proxy accept only your host names, redirect HTTP to HTTPS, and add HSTS where you need it; the server does
  none of these (see Host Filtering, HTTPS Redirection, and HSTS).
- Run the server under an account of its own that can write only to the data folder, and let only that account read the
  database, the `keys` folder, and the attestation pfx file. On Windows the key ring is encrypted with DPAPI by
  default; on Linux and macOS it is not encrypted unless you set `DataProtection:CertificatePath` (the server warns at
  every start), and the attestation pfx file is never encrypted. Without encryption the server restricts the `keys`
  folder and the key files to its own account (also the files that it creates when the ring rolls; on Windows,
  `PermissionsOnly` sets an owner-only access rule on the folder).
- Set `DataProtection:RequireEncryptedKeys` to true where the key ring must be encrypted: the server then refuses to
  start with a ring that is not encrypted at rest. Protect the disk or volume with disk encryption as well, because
  encrypted keys do not protect the database, the logs, or a running process.
- Back up the database and the key folder together, and test a restore (see Test a Restore). Back up a certificate of
  the key ring, and its password, apart from the keys. Use a certificate, not DPAPI, for a ring that may have to be
  restored on another machine.
- Keep the built-in protections on: the sign-in throttle (5 attempts per user name and 20 per address in 300 seconds by
  default), passwords of 12 to 128 characters, the optional two-factor sign-in, and the limit of 30 requests a minute
  for each address on anonymous evidence downloads (see Password Rules and Lockout in the Users and Sign-in chapter,
  and Anonymous Access in the Administration chapter).
- Replace the default self-signed attestation key. The server logs a warning at every start that the self-signed key
  is for development and test only. Use `Attestation:Provider` `CertificateStore` with a certificate of your
  organization, and consider `Attestation:RequireTrustedKey` (see The Signing Key in the Reviewing chapter).
- Prefer an account at your identity provider where one exists (see Sign In with an OpenID Connect Provider in the
  Users and Sign-in chapter), and turn anonymous evidence access on only for projects that need it.
- The server checks its settings when it starts, and refuses to start when an enabled setting is invalid, when the
  key folder cannot be written, or when the attestation key is not usable (the `Identity:Oidc` settings are not checked
  while that sign-in is off; see Configuration). Start it once by hand after every change of the
  settings, and run `--validate --check-config` (see Check an Installation).
- Review the audit trail regularly (see Audit Trail in the Administration chapter), and verify its chain from the
  command line.
- Keep the server and `git` up to date with the releases.

## Troubleshooting

When the server cannot start, it writes one line to the console and exits with code 1. These are the messages that
the project produced on purpose with a wrong setting, and what to do:

- `Error: Unsupported argument '--unknown'`: a wrong option. See `reviewcenter --help`.
- `Failed to start the server on <url>: Failed to bind to address <url>: address already in use.`: the port is taken.
  Stop the other program or use another port with `--urls`.
- `Failed to prepare the database: The database provider 'X' is not supported. Supported providers: Sqlite.`: set
  `Database:Provider` to `Sqlite`.
- `Failed to prepare the database: An in-memory SQLite database is not supported ...`: give a database file path
  instead of `:memory:`.
- `Invalid sign-in settings: Auth:SessionHours must be greater than 0.`: correct the `Auth` or `Identity` setting that
  the message names.
- `Invalid git settings: Git:TimeoutSeconds must be between 1 and 3600 seconds.`: correct the `Git` setting that the
  message names.
- `The Data Protection key ring folder '<folder>' could not be read or written: ...`: set `DataProtection:KeysPath` to
  a folder that the account can read and write and that is kept. The message was produced with a path that is an
  existing file.
- `Invalid key ring settings: DataProtection:Protection 'X' is not supported. Use Auto, Dpapi, Certificate or
  PermissionsOnly.`: correct `DataProtection:Protection` (or `DataProtection:DpapiScope`: `CurrentUser` or
  `LocalMachine`).
- `Invalid key ring settings: DataProtection:Protection Dpapi is only available on Windows.`: use `Certificate` or
  `PermissionsOnly`, or `Auto`, on Linux and macOS.
- `Invalid key ring settings: DataProtection:CertificatePath '<file>' could not be loaded: wrong password or not a
  certificate with a private key.`: check the file, its password (`DataProtection:CertificatePassword` or
  `CertificatePasswordFile`), and that the certificate is an RSA certificate with a private key. The message never
  holds the password.
- `The key ring is not encrypted at rest and DataProtection:RequireEncryptedKeys is true.`: set
  `DataProtection:CertificatePath` (or, on Windows, leave `Protection` at `Auto`), or turn `RequireEncryptedKeys` off.
- `The key ring '<folder>' holds key files that are not encrypted (...) and DataProtection:RequireEncryptedKeys is
  true.`: the ring was made before encryption was set, and a key stays plain until Data Protection replaces it
  (every 90 days). Move the plain key files away only if you accept that what they protected (the stored tokens and
  second-factor secrets) is lost.
- `The key ring '<folder>' holds N key(s), but the newest key cannot be decrypted: ...`: the ring was made with another
  certificate, or with none, or by DPAPI for another account or machine. Set the right certificate in
  `DataProtection:CertificatePath`, or list the old certificate in `DataProtection:PreviousCertificatePaths`. The
  server stopped before it created a new key; a new key would have made the stored tokens unreadable.
- `The key ring '<folder>' (DataProtection:KeysPath) holds key files that could not be read: they are malformed or not
  supported. Nothing was changed. ...`: the folder holds `key-*.xml` files, but no key can be read from them (a
  damaged or truncated file, or a file from another product or version). The server stopped before it created a new
  key, which would have made the stored tokens unreadable. Restore the key folder that belongs to this database (for
  example from a backup). Only if you accept that the stored tokens and second-factor secrets are lost, remove the
  damaged key files from the folder to start a new ring.
- `Key ring '<folder>' is empty but the database holds N protected secrets. Restore the keys folder that belongs to
  this database, or set DataProtection:AllowNewKeyRing=true for one start to accept that they become unreadable.`: the
  database was restored (or the key folder was lost) without its key ring. Restore the key folder that belongs to the
  database. Only if you accept that the stored repository tokens and second-factor secrets are lost, start once with
  `DataProtection:AllowNewKeyRing` set to `true` (see Back Up and Restore).
- `Key ring '<folder>' is empty and the database (Database:ConnectionString) could not be read to check whether it
  holds protected secrets, so no key was created. Fix the access to the database and start again; run 'reviewcenter
  --validate --check-config' for the details.`: the key folder has no key file and the server could not read the
  database (it is locked, damaged, or not a database), so it cannot tell whether a key ring was lost. Fix the access to
  the database (a lock is usually gone after a moment) and start again; `DataProtection:AllowNewKeyRing` does not
  bypass this.
- `The attestation signing key is not usable: Attestation:Provider 'X' is not supported. Use SelfSigned or
  CertificateStore.`: correct `Attestation:Provider` (see The Signing Key in the Reviewing chapter).

Other things to check:

- **The sign-in cookie has no Secure flag behind a proxy.** Check that the proxy forwards `X-Forwarded-Proto` and that
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` is set (see Secure Cookie Behind a Reverse Proxy).
- **Every user seems to come from one address, and shares one set of sign-in limits.** The server sees the address of
  the proxy for every client; see Secure Cookie Behind a Reverse Proxy.
- **The pages load but are not interactive, or are disconnected after a short time, behind a proxy.** The proxy must
  allow WebSocket connections and must not close idle connections quickly (not verified by the project).
- **A repository cannot be read.** Check that `git` is on the `PATH` of the service account and is version 2.31 or
  later; see Repositories and Credentials in the Administration chapter.
- **Stored repository tokens or two-factor secrets cannot be used after a restore.** The key ring of the old
  installation was not restored, or not restored completely. The server logs an error at every start with the counts
  and the repository and user ids, and `reviewcenter --validate --check-config` reports the same in the
  `ReviewCenter_ConfiguredKeys` line (it fails when secrets are unreadable). Restore the key folder that belongs to the
  database, or enter the tokens again and have an administrator reset the two-factor secrets; see Back Up and Restore.
- **The setting `ASPNETCORE_URLS` has no effect.** It is ignored; use `--urls`.
