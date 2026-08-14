# Integration test fixtures

## Quick start (Docker)

```bash
# 1. Start SQL Server
docker compose up -d --wait

# 2. Wait ~30 s for SQL Server to finish initialising, then run the setup script
docker exec -i $(docker compose ps -q sqlserver) \
  /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "ArtSync_Test@2026" -C \
  < tests/fixtures/setup.sql

# 3. Run integration tests
ARTSYNC_INTEGRATION=true dotnet test tests/ArtSync.Integration.Tests/
```

Or use the helper script (does all three steps):

```bash
./scripts/run-integration-tests.sh
```

## What gets created

| Database      | Purpose                              |
|---------------|--------------------------------------|
| `artsync_src` | Source — has `dbo.AuditLog` extra    |
| `artsync_tgt` | Target — same data, no `AuditLog`    |

### Tables in both databases (unless noted)

| Table            | Rows (src/tgt) | Notes                          |
|------------------|----------------|--------------------------------|
| `dbo.Customers`  | 3 / 3          | Identical at setup             |
| `dbo.Products`   | 3 / 3          | Identical at setup             |
| `dbo.Orders`     | 3 / 3          | Identical at setup             |
| `dbo.AuditLog`   | src only       | Schema-diff trigger (src only) |

## Connection strings

| Role   | Connection string                                                    |
|--------|----------------------------------------------------------------------|
| Source | `Server=localhost,1433;Database=artsync_src;User ID=sa;Password=ArtSync_Test@2026;TrustServerCertificate=True` |
| Target | `Server=localhost,1433;Database=artsync_tgt;User ID=sa;Password=ArtSync_Test@2026;TrustServerCertificate=True` |

Set via environment variables before running tests:

```bash
export ARTSYNC_INTEGRATION=true
export ARTSYNC_SRC_CS="Server=localhost,1433;Database=artsync_src;User ID=sa;Password=ArtSync_Test@2026;TrustServerCertificate=True"
export ARTSYNC_TGT_CS="Server=localhost,1433;Database=artsync_tgt;User ID=sa;Password=ArtSync_Test@2026;TrustServerCertificate=True"
dotnet test tests/ArtSync.Integration.Tests/
```

If the env vars are not set, the integration tests default to the Docker connection strings above.

## Resetting the databases

Re-run `setup.sql` at any time to drop and recreate both databases from scratch:

```bash
docker exec -i $(docker compose ps -q sqlserver) \
  /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "ArtSync_Test@2026" -C \
  < tests/fixtures/setup.sql
```

## Local SQL Server (no Docker)

Set `ARTSYNC_SRC_CS` and `ARTSYNC_TGT_CS` to point at your local instance, then run `setup.sql` against it with any SQL client. The `sa` credentials are only for Docker; adjust for your local auth.
