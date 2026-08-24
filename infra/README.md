# ArtSync lab

Disposable **source** (Azure SQL) and **target** (Docker SQL Server) for the captured MARS job. Not production.

| Side | What | How |
|---|---|---|
| Target (on-prem stand-in) | SQL Server 2022 in Docker, database `MARS` | `./scripts/lab-up.sh` restores `_data/*.bak` |
| Source (Azure stand-in) | Azure SQL Database `MARS`, serverless | `./scripts/lab-azure.sh` |

`.env` at the repo root is gitignored. `_data/` stays gitignored.

## One-time

```bash
# 1. Azure SQL server + database + firewall for this IP
./scripts/lab-azure.sh

# 2. Local SQL + restore the captured backup (several minutes)
./scripts/lab-up.sh
```

Resource group: `rg-artsync`. Azure SQL defaults to **westus2** (this Microsoft Partner Network subscription blocks SQL in eastus / eastus2). SKU: serverless `GP_S_Gen5_1` (auto-pause after 60 minutes).

## Compare without applying

```bash
set +H
set -a && source .env && set +a
dotnet run --project src/ArtSync.Cli -- \
  /datacompare \
  /compfile:"$PWD/_data/TheSync!.dcomp" \
  /source connection:"$ARTSYNC_SRC_CS" \
  /target connection:"$ARTSYNC_TGT_CS"
```

Empty Azure vs restored local will show many source-only / different rows until you seed Azure (schema then data `/sync` local → Azure, or import a bacpac). Do not `/sync` Azure → local until you mean to overwrite the restored copy.

## Tear down Azure

```bash
./scripts/lab-down.sh
# also drop local SQL volume: ARTSYNC_LAB_DOWN_DOCKER=1 ./scripts/lab-down.sh
```

## Smoke test

```bash
./scripts/lab-smoke.sh
```
