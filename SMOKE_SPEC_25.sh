#!/usr/bin/env bash
# Smoke kit para SPEC 25 — RoleSystemOptions bulk upsert + matrix.
# Requisitos:
#   - WebApi corriendo en $BASE (default http://localhost:5000).
#   - TOKEN JWT con claim "CompanyId" y rol "SuperAdminCompany" (las acciones del controller lo requieren).
#   - ROLE_ID: un rol existente y activo en tu tenant.
#   - OPTION_1 / OPTION_2: GUIDs reales de SystemOptions (sacalos de GET /SystemOptions paged).

set -euo pipefail
BASE="${BASE:-http://localhost:5000}"
TOKEN="${TOKEN:?set TOKEN=<jwt>}"
COMPANY="${COMPANY:-00000000-0000-0000-0000-000000000001}"
ROLE_ID="${ROLE_ID:?set ROLE_ID=<guid>}"
OPTION_1="${OPTION_1:?set OPTION_1=<guid>}"
OPTION_2="${OPTION_2:?set OPTION_2=<guid>}"

H_AUTH="Authorization: Bearer ${TOKEN}"
H_JSON="Content-Type: application/json"

echo "════════════════════════════════════════════════════════════════"
echo "  SPEC 25 — bulk upsert + matrix smoke"
echo "════════════════════════════════════════════════════════════════"

# ─── 0) GET /SystemOptions paged (para descubrir GUIDs reales) ────────────────
echo
echo "── 0) GET /api/v1/SystemOptions?pageNumber=1&pageSize=20"
curl -sS "$BASE/api/v1/SystemOptions?pageNumber=1&pageSize=20" -H "$H_AUTH" | jq '.data.items[] | {id, name}' | head -20

# ─── 1) GET /matrix?roleId= (SPEC 25) ────────────────────────────────────────
echo
echo "── 1) GET /api/v1/RoleSystemOptions/matrix?roleId=$ROLE_ID  (happy path)"
curl -sS -i "$BASE/api/v1/RoleSystemOptions/matrix?roleId=$ROLE_ID" -H "$H_AUTH"

echo
echo "── 1.b) Matrix de un rol SIN permisos  → 200 con granted=false en todo"
EMPTY_ROLE_ID="${EMPTY_ROLE_ID:-$ROLE_ID}"   # override si tenés uno vacío
curl -sS "$BASE/api/v1/RoleSystemOptions/matrix?roleId=$EMPTY_ROLE_ID" -H "$H_AUTH" | jq '.data.modules[].options[] | {name, granted}'

echo
echo "── 1.c) Matrix de un rol inexistente  → 404 ROLE_NOT_FOUND"
curl -sS -i "$BASE/api/v1/RoleSystemOptions/matrix?roleId=00000000-0000-0000-0000-000000000000" -H "$H_AUTH"

# ─── 2) PUT /bulk (SPEC 25) ──────────────────────────────────────────────────
echo
echo "── 2) PUT /api/v1/RoleSystemOptions/bulk  (happy path)"
curl -sS -i -X PUT "$BASE/api/v1/RoleSystemOptions/bulk" \
  -H "$H_AUTH" -H "$H_JSON" \
  -d "{
    \"roleId\": \"$ROLE_ID\",
    \"items\": [
      { \"systemOptionId\": \"$OPTION_1\", \"canRead\": true,  \"canCreate\": false, \"canUpdate\": false, \"canDelete\": false, \"canDownload\": false, \"canExport\": false, \"canExecute\": false },
      { \"systemOptionId\": \"$OPTION_2\", \"canRead\": true,  \"canCreate\": true,  \"canUpdate\": true,  \"canDelete\": false, \"canDownload\": true,  \"canExport\": true,  \"canExecute\": false }
    ]
  }"

echo
echo "── 2.b) bulk con roleId inexistente  → 404 ROLE_NOT_FOUND"
curl -sS -i -X PUT "$BASE/api/v1/RoleSystemOptions/bulk" \
  -H "$H_AUTH" -H "$H_JSON" \
  -d "{
    \"roleId\": \"00000000-0000-0000-0000-000000000000\",
    \"items\": [{ \"systemOptionId\": \"$OPTION_1\", \"canRead\": true, \"canCreate\": false, \"canUpdate\": false, \"canDelete\": false, \"canDownload\": false, \"canExport\": false, \"canExecute\": false }]
  }"

echo
echo "── 2.c) bulk con items vacío  → 400 BULK_EMPTY"
curl -sS -i -X PUT "$BASE/api/v1/RoleSystemOptions/bulk" \
  -H "$H_AUTH" -H "$H_JSON" \
  -d "{\"roleId\": \"$ROLE_ID\", \"items\": []}"

echo
echo "── 2.d) bulk con items duplicados  → 400 BULK_DUPLICATE_OPTION"
curl -sS -i -X PUT "$BASE/api/v1/RoleSystemOptions/bulk" \
  -H "$H_AUTH" -H "$H_JSON" \
  -d "{
    \"roleId\": \"$ROLE_ID\",
    \"items\": [
      { \"systemOptionId\": \"$OPTION_1\", \"canRead\": true, \"canCreate\": false, \"canUpdate\": false, \"canDelete\": false, \"canDownload\": false, \"canExport\": false, \"canExecute\": false },
      { \"systemOptionId\": \"$OPTION_1\", \"canRead\": false, \"canCreate\": true, \"canUpdate\": false, \"canDelete\": false, \"canDownload\": false, \"canExport\": false, \"canExecute\": false }
    ]
  }"

echo
echo "── 2.e) bulk con >500 items  → 400 BULK_TOO_LARGE"
# Generá los 501 items con un script auxiliar (jq) y mandá el payload.
ITEMS=$(printf ',"systemOptionId":"00000000-0000-0000-0000-%012d","canRead":true,"canCreate":false,"canUpdate":false,"canDelete":false,"canDownload":false,"canExport":false,"canExecute":false' $(seq 1 501) | sed 's/^,//')
BODY=$(jq -n --arg roleId "$ROLE_ID" --argjson items "[$ITEMS]" '{roleId: $roleId, items: $items}')
echo "$BODY" | head -c 200; echo "..."
curl -sS -i -X PUT "$BASE/api/v1/RoleSystemOptions/bulk" \
  -H "$H_AUTH" -H "$H_JSON" \
  -d "$BODY"

# ─── 3) Verificación post-bulk ────────────────────────────────────────────────
echo
echo "── 3.a) Re-pedí matrix → granted ahora refleja el bulk"
curl -sS "$BASE/api/v1/RoleSystemOptions/matrix?roleId=$ROLE_ID" -H "$H_AUTH" \
  | jq '.data.modules[].options[] | select(.systemOptionId == "'"$OPTION_1"'" or .systemOptionId == "'"$OPTION_2"'") | {name, granted}'

echo
echo "── 3.b) GET paginado normal — totalCount = count(items[])"
curl -sS "$BASE/api/v1/RoleSystemOptions?roleId=$ROLE_ID&pageNumber=1&pageSize=50" -H "$H_AUTH" \
  | jq '.data.totalCount'

echo
echo "── 3.c) Verificación de cache (opcional)"
echo "       Si un usuario tenía UserRoleCompany activo en este rol, su"
echo "       permission snapshot (clave permissions:v2:{companyId}:{userId})"
echo "       fue invalidado. Próximo request a un endpoint protegido reconstruye."