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

### Run locally

Requires Docker with Compose. Ports 8080, 5432 and 10000 must be free.

```
cp .env.example .env
docker compose up --build
```

Open http://localhost:8080, pick a user, and upload a CSV from the Imports screen. The vote files aren't in this repo: use the ones supplied with the assignment in `candidate_data`.

To stop, run `docker compose down`. Add `-v` to delete the local data as well.

### Run with Azure

You need:

- The [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), signed in with `az login`, with permission to read the vault's secrets.
- Access to the PostgreSQL server from your IP address.
- A blob container that already exists, because a container SAS can't create one (The container already exists). Its SAS needs read, add, create, write and list, and must not expire during the run (Valid SAS is pulled from KV).

The vault holds these secrets:

| Secret | Setting | Required |
| --- | --- | --- |
| `ConnectionStrings--Default` | Server, database, SSL settings and login | Yes |
| `Storage--ContainerSasUrl` | The blob container's SAS URL | Yes |
| `APPLICATIONINSIGHTS-CONNECTION-STRING` | Application Insights | No |
| `Azure-Postgres-User`, `Azure-Postgres-Password` | Replace the login in `ConnectionStrings--Default` | No, but add both or neither |

Build `.env` from the vault, then start the system:

```
powershell -ExecutionPolicy Bypass -File scripts/pull-secrets.ps1 -VaultName <vault> -DryRun
powershell -ExecutionPolicy Bypass -File scripts/pull-secrets.ps1 -VaultName <vault>
docker compose up --build
```

The dry run lists what would come from the vault and writes nothing. `-ExecutionPolicy Bypass` applies only to that command, and it's needed because Windows blocks scripts by default.

The script overwrites `.env`, which is git-ignored, so secrets never reach the repo. A setting takes its value from the secret of the matching name, with `__` written as `--` and `_` as `-`. Settings with no matching secret keep the value from `.env.example`.

The local `postgres` and `azurite` containers still start, but nothing uses their data.

**First run on an empty database.** Every container uses the same login, including `migrator`, so that login must be able to create tables. If `migrator` stops with `permission denied for schema public`, run this once as a server admin, against the database named in the connection string, because this permission is per database:

```
GRANT USAGE, CREATE ON SCHEMA public TO <user>;
```

The tables `migrator` creates then belong to that user. An admin who signs in with Microsoft Entra uses a token as the password, from `az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv`.

**Going back to local.** Run `cp .env.example .env`. While `.env` points at Azure, `docker compose up` migrates, seeds and writes to the Azure database.

## Users

The UI's user switcher picks who you are; the API receives it as the `X-User-Id` header. Authentication is deliberately simple and spoofable: production auth is out of scope.

| User | Role | Can |
| --- | --- | --- |
| Dana | Analyst | Upload files, decide conflicts, create snapshots |
| Omer | Analyst | Same as Dana |
| Lena | Admin | All of the above, plus set positions and weights and publish snapshots |

## Development

Requires the .NET 10 SDK, Node.js and Docker.

`dotnet run` doesn't read `.env`, so set the settings in your shell first. `.env.example` has a "From the host" value for each of these. In PowerShell:

```
docker compose up -d postgres azurite                  # local infrastructure, needs .env
$env:ConnectionStrings__Default = "<From the host value>"
$env:Storage__ConnectionString = "UseDevelopmentStorage=true"
$env:Seed__MemberStatesPath = "../../data/member_states.csv"
$env:Worker__Enabled = "true"                          # runs the worker inside the API

dotnet run --project src/Meridian.Api -- --migrate-and-seed
dotnet run --project src/Meridian.Api                  # http://localhost:5284
```

Then, in a second terminal:

```
cd web
npm install
npm run generate:api                                   # only if the API contract changed
npx ng serve                                           # http://localhost:4200, proxies /api to port 5284
```

## Tests

```
dotnet test
cd web && npm test
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
	
