#!/usr/bin/env bash
# Start the Docker SQL Server, seed the test databases, and run integration tests.
# Usage: ./scripts/run-integration-tests.sh [dotnet test args…]
#   e.g. ./scripts/run-integration-tests.sh --logger "console;verbosity=detailed"

set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

SA_PASS="ArtSync_Test@2026"
SQL_HOST="localhost,1433"
SETUP_SQL="$REPO_ROOT/tests/fixtures/setup.sql"

echo "==> Starting SQL Server via Docker Compose…"
docker compose -f "$REPO_ROOT/docker-compose.yml" up -d --wait

echo "==> Waiting for SQL Server to accept connections…"
MAX_TRIES=20
for i in $(seq 1 $MAX_TRIES); do
    if docker compose -f "$REPO_ROOT/docker-compose.yml" exec -T sqlserver \
        /opt/mssql-tools18/bin/sqlcmd \
        -S localhost -U sa -P "$SA_PASS" -C \
        -Q "SELECT 1" -b > /dev/null 2>&1; then
        echo "   SQL Server is ready."
        break
    fi
    echo "   Attempt $i/$MAX_TRIES — retrying in 5 s…"
    sleep 5
done

echo "==> Running test fixture setup…"
docker compose -f "$REPO_ROOT/docker-compose.yml" exec -T sqlserver \
    /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$SA_PASS" -C \
    -i /dev/stdin < "$SETUP_SQL"

export ARTSYNC_INTEGRATION=true
export ARTSYNC_SRC_CS="Server=$SQL_HOST;Database=artsync_src;User ID=sa;Password=$SA_PASS;TrustServerCertificate=True"
export ARTSYNC_TGT_CS="Server=$SQL_HOST;Database=artsync_tgt;User ID=sa;Password=$SA_PASS;TrustServerCertificate=True"

echo "==> Running integration tests…"
dotnet test "$REPO_ROOT/tests/ArtSync.Integration.Tests/" "$@"
