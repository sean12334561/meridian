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

All commands below are PowerShell, and every one of them must run from the repository's root folder (the one with `docker-compose.yml`). Before running anything, go to that folder:

```
cd <path to the cloned repository>
ls docker-compose.yml
```

If `ls` can't find `docker-compose.yml`, you're in the wrong folder. Running from anywhere else gives errors such as `The argument 'scripts/pull-secrets.ps1' to the -File parameter does not exist` or `no configuration file provided: not found`.

Any of these opens a terminal in the right place:

- **A terminal.** Open PowerShell or Windows Terminal and `cd` to the repository folder as above.
- **VS Code.** Open the repository folder, then **Terminal > New Terminal**. It opens in the root folder, in PowerShell by default on Windows.
- **Visual Studio.** Open `meridian.slnx`, then **View > Terminal**. It opens a Developer PowerShell in the solution's folder, which is the root folder.

On macOS or Linux, install [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell) and run the script with `pwsh -File scripts/pull-secrets.ps1 ...` instead of `powershell -ExecutionPolicy Bypass -File ...`.

Docker Desktop must be running before any `docker compose` command.

When a block below has more than one command, run them **one line at a time**: paste one line, press Enter, and wait for it to finish and check its output before the next. Pasting the whole block at once can hide an error from an earlier line.

### Run locally

Requires Docker with Compose. Ports 8080, 5432 and 10000 must be free. Run these one line at a time. The first line also undoes an Azure run (see [Going back to local](#going-back-to-local-after-an-azure-run)), so don't skip it.

```
cp .env.example .env
docker compose up --build
```

Open http://localhost:8080, pick a user, and upload a CSV from the Imports screen. The vote files aren't in this repo: use the ones supplied with the assignment in `candidate_data`.

**A vote whose rows disagree inside one file.** If a file gives different details (title, date or session) for a vote that isn't stored yet, all its rows are marked Contradiction and the vote isn't created, because the file doesn't say which details are right. No conflict is opened and nothing appears on the Review screen: it shows only in that import's report. Uploading the same file again gives the same result. The vote is created once a file with consistent details for it arrives. In the sample data, this happens to MA/2024/025 in `assembly_export_2024_q1.csv`, which `assembly_export_2024_q1_rev2.csv` corrects.

To stop, press Ctrl+C, then run `docker compose down`. Add `-v` to delete the local data as well.

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

A browser window opens for you to sign in. Back in the terminal, the CLI lists your subscriptions and asks you to pick one: pick the subscription that holds the vault, even if it's the only one. If there's only one, type `1` and press Enter. To change it later (one line at a time):

```
az account list -o table
az account set --subscription "<subscription name or ID>"
```

You only need to sign in once per machine, not before every run. It works from any of the terminals above.

**3. Build `.env` from the vault.** Run these two lines **one at a time**, and check the output of each before the next.

First, the dry run. It lists what would come from the vault and **writes nothing**, so it's a safe check that sign-in and vault access work:

```
powershell -ExecutionPolicy Bypass -File scripts/pull-secrets.ps1 -VaultName dotnetcandidate9kv -DryRun
```

It ends with `... setting(s) would come from 'dotnetcandidate9kv'. Nothing written.` Then the real run, which writes `.env`:

```
powershell -ExecutionPolicy Bypass -File scripts/pull-secrets.ps1 -VaultName dotnetcandidate9kv
```

It must end with `Wrote .env with N setting(s) from 'dotnetcandidate9kv'.` If you don't see that line, `.env` was not changed and still has its previous settings, for example the local ones, so don't start the system yet. `-ExecutionPolicy Bypass` applies only to that command, and it's needed because Windows blocks scripts by default.

The script overwrites `.env`, which is git-ignored, so secrets never reach the repo. A setting takes its value from the secret of the matching name, with `__` written as `--` and `_` as `-`. Settings with no matching secret keep the value from `.env.example`.

**4. Start the system.**

```
docker compose up --build
```

Then open http://localhost:8080. The local `postgres` and `azurite` containers still start, but nothing uses their data.

### Going back to local after an Azure run

Once the script has run, `.env` holds the Azure values, and they win over the local ones:

- `ConnectionStrings__Default` points at the Azure database, so `migrator`, `api` and `worker` all use it.
- When `Storage__ContainerSasUrl` has a value, files go to Azure Blob Storage, even though the Azurite settings are still in the file.
- `APPLICATIONINSIGHTS_CONNECTION_STRING` keeps sending telemetry to Application Insights.

So until `.env` is replaced, `docker compose up` migrates, seeds and writes to Azure, even when you meant to run locally. To switch back, run these one line at a time:

```
docker compose down
cp .env.example .env
docker compose up --build
```

`cp` overwrites `.env` with the local template, which removes every Azure value. The local database and files are still in their Docker volumes, so you get back what you had before the Azure run. To go back to Azure later, run step 3 again.

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
