# Meridian Vote Tracker

Imports Assembly vote CSVs, detects conflicts with approved data, lets analysts decide them, lets the admin set positions and weights, and produces immutable, reproducible country ranking snapshots.

> **Status:** in progress. Sections marked TODO are filled in as the matching feature lands.

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

TODO: which seed files go in `data/`.

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

TODO: time to import the historic file, the machine it ran on, and the database it ran against.

## Known limitations

TODO
