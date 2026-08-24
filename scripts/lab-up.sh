#!/usr/bin/env bash
# Start local SQL Server (lab overlay) and restore _data/*.bak as database MARS.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SA_PASS="${ARTSYNC_LOCAL_PASSWORD:-ArtSync_Test@2026}"
DB_NAME="${ARTSYNC_LOCAL_DB:-MARS}"
COMPOSE=(docker compose -f "$REPO_ROOT/docker-compose.yml" -f "$REPO_ROOT/docker-compose.lab.yml")

sqlcmd() {
    "${COMPOSE[@]}" exec -T sqlserver \
        /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASS" -C -b "$@"
}

wait_for_sql() {
    echo "==> Waiting for SQL Server…"
    local i
    for i in $(seq 1 30); do
        if sqlcmd -Q "SELECT 1" >/dev/null 2>&1; then
            echo "   ready."
            return 0
        fi
        sleep 5
    done
    echo "ERROR: SQL Server did not start." >&2
    "${COMPOSE[@]}" logs --tail 80 sqlserver || true
    exit 1
}

echo "==> Starting Docker SQL Server (lab)…"
"${COMPOSE[@]}" up -d --wait --wait-timeout 180
wait_for_sql

BAK_HOST="$(ls -1t "$REPO_ROOT"/_data/*.bak 2>/dev/null | head -1 || true)"
if [[ -z "$BAK_HOST" ]]; then
    echo "ERROR: no .bak under _data/. Put the MARS backup there first." >&2
    exit 1
fi
BAK_NAME="$(basename "$BAK_HOST")"
BAK_CT="/bak/$BAK_NAME"
echo "==> Backup: $BAK_NAME"

if sqlcmd -Q "IF DB_ID('$DB_NAME') IS NOT NULL SELECT 1 ELSE SELECT 0" -h -1 -W | grep -qx 1; then
    echo "==> Database [$DB_NAME] already exists. Skipping restore (drop it to re-restore)."
    sqlcmd -Q "SELECT name, state_desc, compatibility_level FROM sys.databases WHERE name = '$DB_NAME'"
    exit 0
fi

echo "==> Restoring [$DB_NAME] (this can take several minutes)…"
# Captured MARS.bak (SQL 2014 SIMGP) uses these logical names; FILELISTONLY if they change.
sqlcmd -Q "RESTORE DATABASE [$DB_NAME] FROM DISK = N'$BAK_CT' WITH REPLACE, STATS = 10, MOVE N'MARS_Data' TO N'/var/opt/mssql/data/${DB_NAME}.mdf', MOVE N'MARS_Log' TO N'/var/opt/mssql/data/${DB_NAME}_log.ldf'"

echo "==> Restored [$DB_NAME]."
sqlcmd -d "$DB_NAME" -Q "SELECT DB_NAME() AS db, DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS collation, (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0) AS user_tables"
