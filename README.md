# Meridian Vote Tracker

Imports Assembly vote CSVs, detects conflicts with approved data, lets analysts decide them, lets the admin set positions and weights, and produces immutable, reproducible country ranking snapshots.

## Documents

- [Decisions and their reasoning](DECISIONS.md)
- [How AI was used](AI_USAGE.md)
  
## Run the system

Docker Compose runs both versions. The only difference is what's in `.env`, so running locally needs no Azure account.

| | Local | Azure |
| --- | --- | --- |
| Database | `postgres:17` container | Azure Database for PostgreSQL |
| File storage | Azurite container | Azure Blob Storage |
| Telemetry | Console logs | Console logs and Application Insights |
| Needs | Docker | Docker, the Azure CLI, PowerShell and access to the Key Vault |

In both, the system runs on your machine at http://localhost:8080. The `migrator` container applies migrations and seeds the users and `data/member_states.csv`, then exits. That's expected, and it's safe to run again because it only adds rows that are missing.

### Before you start: where to run the commands

All commands below are PowerShell, run from the repository's root folder (the one with `docker-compose.yml`). Any of these works:

- **A terminal.** Open PowerShell or Windows Terminal and `cd` to the repository folder.
- **VS Code.** Open the repository folder, then **Terminal > New Terminal**. It opens in the root folder, in PowerShell by default on Windows.
- **Visual Studio.** Open `meridian.slnx`, then **View > Terminal**. It opens a Developer PowerShell in the solution's folder, which is the root folder.

On macOS or Linux, install [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell) and run the script with `pwsh -File scripts/pull-secrets.ps1 ...` instead of `powershell -ExecutionPolicy Bypass -File ...`.

Docker Desktop must be running before any `docker compose` command.

### Run locally

Requires Docker with Compose. Ports 8080, 5432 and 10000 must be free.

```
cp .env.example .env
docker compose up --build
```

Open http://localhost:8080, pick a user, and upload a CSV from the Imports screen. The vote files aren't in this repo: use the ones supplied with the assignment in `candidate_data`.

To stop, press Ctrl+C, then run `docker compose down`. Add `-v` to delete the local data as well.

**If `migrator` fails with `password authentication failed for user "meridian"`.** The local database keeps the password it was created with, in a Docker volume, and ignores later changes to `.env`. This happens when the volume was created earlier with a different password. Delete the local data and start again:

```
docker compose down -v
docker compose up --build
```

If it still fails, check that no other PostgreSQL is running on port 5432, such as one installed directly on Windows.

### Run with Azure

You need:

- The [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli). The script calls it to read the vault, so it must be installed even if you never use it yourself. After installing, open a new terminal and check it with `az version`.
- An Azure account with permission to read the secrets in the `dotnetcandidate9kv` Key Vault.
- Access to the PostgreSQL server from your IP address, through the server's firewall rules.

The vault already holds a valid SAS for an existing blob container, and the database is already set up.

The vault holds these secrets:

| Secret | Setting | Required |
| --- | --- | --- |
| `ConnectionStrings--Default` | Server, port, database and SSL settings, with no login | Yes |
| `Azure-Postgres-User`, `Azure-Postgres-Password` | The database login, which the script adds to `ConnectionStrings--Default` | Yes |
| `Storage--ContainerSasUrl` | The blob container's SAS URL | Yes |
| `APPLICATIONINSIGHTS-CONNECTION-STRING` | Application Insights | No |

**1. Stop the local system if it's running.** Press Ctrl+C in its terminal, then run `docker compose down`. Otherwise Compose keeps the containers that were started with the local settings.

**2. Sign in to Azure.**

```
az login
```

A browser window opens for you to sign in. Back in the terminal, the CLI lists your subscriptions and asks you to pick one: pick the subscription that holds the vault, even if it's the only one. To change it later:

```
az account list -o table
az account set --subscription "<subscription name or ID>"
```

You only need to sign in once per machine, not before every run. It works from any of the terminals above.

**3. Build `.env` from the vault.**

```
powershell -ExecutionPolicy Bypass -File scripts/pull-secrets.ps1 -VaultName dotnetcandidate9kv -DryRun
powershell -ExecutionPolicy Bypass -File scripts/pull-secrets.ps1 -VaultName dotnetcandidate9kv
```

The dry run lists what would come from the vault and writes nothing, so it's a safe check that sign-in and vault access work. `-ExecutionPolicy Bypass` applies only to that command, and it's needed because Windows blocks scripts by default.

The script overwrites `.env`, which is git-ignored, so secrets never reach the repo. A setting takes its value from the secret of the matching name, with `__` written as `--` and `_` as `-`. Settings with no matching secret keep the value from `.env.example`.

**4. Start the system.**

```
docker compose up --build
```

Then open http://localhost:8080. The local `postgres` and `azurite` containers still start, but nothing uses their data.

**Which step failed?**

| Where the error appears | What it means |
| --- | --- |
| The script, with `Can't list secrets` or `Can't read secret` | Azure sign-in or vault access. Run `az login` again, check the subscription with `az account show`, and check that your account can read the vault's secrets |
| `migrator` in the `docker compose up` output, with `password authentication failed` | The database login stored in the vault |
| `migrator`, with a timeout or `no pg_hba.conf entry` | The server's firewall doesn't allow your IP address |

**Going back to local.** Stop the system with Ctrl+C and `docker compose down`, then run `cp .env.example .env`. While `.env` points at Azure, `docker compose up` migrates, seeds and writes to the Azure database.

**Only when setting up a new database.** This isn't needed to run the submission. Every container uses the same login, including `migrator`, so that login must be able to create tables. If `migrator` stops with `permission denied for schema public`, run this once as a server admin, against the database named in the connection string, because this permission is per database:

```
GRANT USAGE, CREATE ON SCHEMA public TO <user>;
```

The tables `migrator` creates then belong to that user. An admin who signs in with Microsoft Entra uses a token as the password, from `az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv`.

## Users

The UI's user switcher picks who you are; the API receives it as the `X-User-Id` header. Authentication is deliberately simple and spoofable: production auth is out of scope.

| User | Role | Can |
| --- | --- | --- |
| Dana | Analyst | Upload files, decide conflicts, create snapshots |
| Omer | Analyst | Same as Dana |
| Lena | Admin | All of the above, plus set positions and weights and publish snapshots |

## Tests

Requires the .NET 10 SDK, Node.js and Docker.

```
dotnet test
cd web
npm install
npm test
```

Integration tests start a throwaway PostgreSQL through Testcontainers, so Docker must be running. They never touch the Azure database.

## Moving to production

Deploying the application to Azure and production-grade authentication were out of scope, so none of this is built. It's what I'd do next, in this order of importance.

**Authentication.** Replace the `X-User-Id` header with JWT bearer tokens from Microsoft Entra ID. People sign in through Entra, and Analyst and Admin become app roles in the token. Other systems calling the API use their own Entra application with an application role, through the client credentials flow, not a shared key. The `Analyst` and `Admin` policies stay as they are, and the acting user still comes from the principal, so the change is mainly the authentication handler and sign-in in the Angular app.

**Access between Azure resources.** Use managed identities, so `api` and `worker` each reach Blob Storage, Key Vault and PostgreSQL with no stored credentials. That removes the SAS URL, the database password and `scripts/pull-secrets.ps1`, which only exists because the containers can't use `az login`. Key Vault is left for anything that still has to be a secret, such as third-party keys.

**Network.** Keep PostgreSQL and storage off the public internet with private endpoints, in place of firewall rules for individual IP addresses.

**Hosting.** The images are published to Azure Container Registry. `web`, `api` and `worker` run as separate Azure Container Apps, so the worker can scale on its own, and `migrator` runs as a one-off job before each release, with its own database login that is the only one allowed to change the schema. An Azure Function or WebJob could replace the `worker` container, but neither is worth it on its own: the image already runs the worker, a large import takes far longer than a Consumption plan Function's 10-minute limit allows, and a WebJob ties the worker to App Service.

**Database performance.** Move off `Standard_B1ms`, but start with the disk, not the CPU. During the historic import on Azure, disk IOPS sat at 100% of the limit once loading began, with up to 85 operations queued, while CPU averaged 26% and burst credits barely moved. So raise the provisioned IOPS and memory first, then measure again before paying for more cores. The import's batch size is also worth revisiting, because a shorter batch means a shorter wait for a conflict decision.

**Background jobs.** Test whether Azure Service Bus is worth it before replacing the job table. The table has real advantages: a job is saved in the same transaction as its import, so one can't exist without the other, and it needs no extra service. Service Bus would need an outbox to keep that guarantee, and lock renewal for jobs that run for many minutes. It would add dead-lettering and scaling on queue length, and would suit a Function worker. Switch only if measurements show the table or its polling becoming a limit.

### Dividing the work between four developers

I would try and devied the work so Each developer owns one area, so two people rarely change the same code at once.

One developer on azure and running the system - Container Apps, Container Registry, managed identities, private endpoints, the separate migrations login, alerts, and separate service names for 'api' and 'worker' in Application Insights
Two developers on backend work:
	One for authentication and the API - JWT and Entra ID, app roles, application access for API callers, and removing the header authentication.
	One for Database and background work - Sizing the database by measurement, batch size and decision waits, and the Service Bus test.
One developer on frontend and quality - Front design improvments, Entra sign-in, tests for the screens that have none, and end-to-end tests of the main flows (for after the other developers had finished their work).
	
