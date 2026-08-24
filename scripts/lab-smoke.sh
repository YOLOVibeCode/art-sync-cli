#!/usr/bin/env bash
# Verify local MARS and Azure SQL lab connections (no sync).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ENV_FILE="$REPO_ROOT/.env"

if [[ ! -f "$ENV_FILE" ]]; then
    echo "ERROR: $ENV_FILE missing. Run ./scripts/lab-azure.sh first." >&2
    exit 1
fi
set +H
set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

SA_PASS="${ARTSYNC_LOCAL_PASSWORD:-ArtSync_Test@2026}"
COMPOSE=(docker compose -f "$REPO_ROOT/docker-compose.yml" -f "$REPO_ROOT/docker-compose.lab.yml")

echo "==> Local [MARS]"
"${COMPOSE[@]}" exec -T sqlserver \
    /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASS" -C -d MARS -h -1 -W \
    -Q "SET NOCOUNT ON; SELECT CONCAT(DB_NAME(), ' tables=', CONVERT(varchar(12), (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0)));"

echo "==> Azure [$ARTSYNC_AZURE_DB] @ $ARTSYNC_AZURE_SERVER"
"${COMPOSE[@]}" exec -T sqlserver \
    /opt/mssql-tools18/bin/sqlcmd \
    -S "$ARTSYNC_AZURE_SERVER" \
    -U "$ARTSYNC_SQL_ADMIN" \
    -P "$ARTSYNC_SQL_PASSWORD" \
    -d "$ARTSYNC_AZURE_DB" \
    -C \
    -h -1 -W \
    -Q "SET NOCOUNT ON; SELECT CONCAT(DB_NAME(), ' tables=', CONVERT(varchar(12), (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0)));"

echo "==> Both reachable."
