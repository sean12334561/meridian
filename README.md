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

```
docker compose up -d postgres azurite                  # local infrastructure
dotnet run --project src/Meridian.Api -- --migrate-and-seed
dotnet run --project src/Meridian.Api                  # Worker__Enabled=true to run the worker in-process
cd web && npm run generate:api && ng serve             # proxies /api to the local API
```

`.env.example` lists every setting, including the values to use when running from the host instead of a container.

## Tests

```
dotnet test
```

Integration tests start a throwaway PostgreSQL through Testcontainers, so Docker must be running. They never touch the Azure database.

## Measured load time

`assembly_historic.csv` (104 MB, 962,171 rows), from upload to Completed:

| Run | Database | Time |
| --- | --- | --- |
| Compose stack, upload to the API port | `postgres:17` container | 57 s |
| Compose stack, upload through nginx | `postgres:17` container | 105 s |
| Azure | Azure Database for PostgreSQL | Not measured yet |

Both local runs used Docker Desktop on Windows with an AMD Ryzen 7 7800X3D, 16 CPUs and 16 GB given to Docker. The file was the first large import in a nearly empty database. The results were 962,109 new rows, 32 invalid and 30 duplicates.

## Known limitations

- Header authentication can be spoofed by anyone who can reach the API. Production auth is out of scope.
- A conflict decision waits for the import batch holding the same lock. It waited up to about 4.6 seconds while the historic file was applying.
- The Angular app has no automated tests in the repo. The backend has unit and integration suites.
- Application Insights export has not been checked against a real resource. It is off unless `APPLICATIONINSIGHTS_CONNECTION_STRING` is set.
