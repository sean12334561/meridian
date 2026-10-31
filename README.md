# Meridian Vote Tracker

Imports Assembly vote CSVs, detects conflicts with approved data, lets analysts decide them, lets the admin set positions and weights, and produces immutable, reproducible country ranking snapshots.

## Design documents

- [High-level design](docs/HLD.md)
- [Low-level design](docs/LLD.md)
- [Decisions and their reasoning](DECISIONS.md)
- [How AI was used](AI_USAGE.md)

## Run the full system

Requires Docker with Compose.

```
cp .env.example .env
docker compose up --build
```

Then open http://localhost:8080.

To use the Azure services, keep their settings in Key Vault and build `.env` from it instead of copying the template:

```
az login
./scripts/pull-secrets.ps1 -VaultName <vault>
docker compose up --build
```

A setting takes its value from the secret of the matching name, with `__` written as `--` and `_` as `-` (`ConnectionStrings--Default`, `Storage--ContainerSasUrl`). Settings with no matching secret keep the value from `.env.example`.

The `migrator` container applies migrations and seeds the users and `data/member_states.csv`, which is committed. It's safe to run again: it only adds rows that are missing. It uses `ConnectionStrings__Default` from `.env`, so a `.env` pointing at Azure migrates and seeds the Azure database.

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

## Measured load time

`assembly_historic.csv` (104 MB, 962,171 rows), from upload to Completed:

| Run | Database | Time |
| --- | --- | --- |
| Compose stack through nginx, first import into an empty database | `postgres:17` container | 56 s and 58 s |
| Compose stack through nginx, same file again | `postgres:17` container | 44 s and 46 s |
| Azure | Azure Database for PostgreSQL | Not measured yet |

The local runs used Docker Desktop on Windows with an AMD Ryzen 7 7800X3D, 16 CPUs and 16 GB given to Docker. The first import gave 962,109 new rows, 32 invalid and 30 duplicates. The second gave 0 new rows and 0 conflicts, and its report named the earlier identical file.

Before the `(import_id, vote_ref)` index on `import_rows`, the same load took between 57 and 105 seconds.

## Known limitations

- Header authentication can be spoofed by anyone who can reach the API. Production auth is out of scope.
- A conflict decision waits for the import batch holding the same lock. Eight decisions made while the historic file was applying took 0.8 to 2.2 seconds each.
- Automated Angular tests cover only the shared helpers. The screens have no tests in the repo; the backend has unit and integration suites.
- Accepting a change to a vote's date doesn't re-check that its countries were members on the new date.
- Each CSV row must be on one line. A quoted field with a line break is rejected, so a stray quote can't swallow the rows after it.
- A snapshot's numbers never change, but its breakdown shows a vote's current title, not the title at snapshot time.
- Application Insights export has not been checked against a real resource. It is off unless `APPLICATIONINSIGHTS_CONNECTION_STRING` is set.
