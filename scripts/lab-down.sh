#!/usr/bin/env bash
# Tear down the disposable lab (Azure RG + optional local Docker SQL).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ENV_FILE="$REPO_ROOT/.env"
RG="${ARTSYNC_RESOURCE_GROUP:-rg-artsync}"
DROP_DOCKER="${ARTSYNC_LAB_DOWN_DOCKER:-0}"

if [[ -f "$ENV_FILE" ]]; then
    set +H
    set -a
    # shellcheck disable=SC1090
    source "$ENV_FILE"
    set +a
    RG="${ARTSYNC_RESOURCE_GROUP:-$RG}"
fi

echo "==> Deleting Azure resource group: $RG"
az group delete --name "$RG" --yes --no-wait
echo "   deletion started (async)."

if [[ "$DROP_DOCKER" == "1" ]]; then
    echo "==> Stopping local SQL (docker compose down -v)…"
    docker compose -f "$REPO_ROOT/docker-compose.yml" -f "$REPO_ROOT/docker-compose.lab.yml" down -v
fi

echo "Done. Local [MARS] is kept unless ARTSYNC_LAB_DOWN_DOCKER=1."
