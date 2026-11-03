#!/usr/bin/env bash
# Deploys release <sha> on the VM (as the deploy user). Idempotent; safe to re-run.
#   deploy-release.sh <sha>
# Expects image gameya-api:<sha> already loaded and releases/<sha>/{web/,efbundle,roles.sql} uploaded.
# Env overrides (local rehearsal only): GAMEYA_ROOT (default /opt/gameya), GAMEYA_INSECURE_CURL=1 (curl -k, for
# Caddy's internal CA), HEALTH_TIMEOUT_SECONDS (default 300).
set -Eeuo pipefail

SHA="${1:?usage: deploy-release.sh <sha>}"
[[ "$SHA" =~ ^[0-9a-zA-Z._-]+$ ]] || { echo "invalid release id: $SHA" >&2; exit 2; }

ROOT="${GAMEYA_ROOT:-/opt/gameya}"
PROJECT="${GAMEYA_PROJECT:-gameya}"
HEALTH_TIMEOUT_SECONDS="${HEALTH_TIMEOUT_SECONDS:-300}"
RELEASES="$ROOT/releases"
RELEASE="$RELEASES/$SHA"
cd "$ROOT"

log() { printf '[deploy %s] %s\n' "$SHA" "$*"; }

# Reads KEY from .env (written as KEY='value'; compose reads the same file).
env_get() {
  local value
  value="$(sed -n "s/^$1=//p" "$ROOT/.env" | head -n 1)"
  value="${value#\'}"
  value="${value%\'}"
  printf '%s' "$value"
}

compose() {
  docker compose --project-name "$PROJECT" --project-directory "$ROOT" \
    --env-file "$ROOT/.env" --env-file "$ROOT/image.env" -f "$ROOT/compose.prod.yml" "$@"
}

exec 9> "$ROOT/.deploy.lock"
flock -n 9 || { echo "another deploy is running" >&2; exit 1; }

[[ -f "$ROOT/.env" ]] || { echo "$ROOT/.env is missing" >&2; exit 1; }
[[ -d "$RELEASE/web" && -f "$RELEASE/efbundle" && -f "$RELEASE/roles.sql" ]] \
  || { echo "release $SHA is incomplete in $RELEASE" >&2; exit 1; }
docker image inspect "gameya-api:$SHA" > /dev/null \
  || { echo "image gameya-api:$SHA is not loaded" >&2; exit 1; }

SITE_ADDRESS="$(env_get SITE_ADDRESS)"
[[ -n "$SITE_ADDRESS" ]] || { echo "SITE_ADDRESS is not set in .env" >&2; exit 1; }
DB_MIGRATOR_PASSWORD="$(env_get DB_MIGRATOR_PASSWORD)"
DB_APP_PASSWORD="$(env_get DB_APP_PASSWORD)"
[[ -n "$DB_MIGRATOR_PASSWORD" && -n "$DB_APP_PASSWORD" ]] || { echo "DB passwords are not set in .env" >&2; exit 1; }
# The passwords are quoted into psql \set lines below.
if [[ "$DB_MIGRATOR_PASSWORD$DB_APP_PASSWORD" == *"'"* ]]; then
  echo "database passwords must not contain a single quote" >&2
  exit 1
fi

# Capture the rollback target before touching anything.
PREV_TAG=""
if [[ -f "$ROOT/image.env" ]]; then
  PREV_TAG="$(sed -n 's/^API_TAG=//p' "$ROOT/image.env" | head -n 1)"
fi
PREV_RELEASE=""
if [[ -L "$RELEASES/current" ]]; then
  PREV_RELEASE="$(readlink "$RELEASES/current")"
fi
# compose needs API_TAG to parse the file even for postgres/caddy.
if [[ ! -f "$ROOT/image.env" ]]; then
  echo "API_TAG=$SHA" > "$ROOT/image.env"
fi

log "starting postgres and caddy"
# Caddyfile lives in its own directory (mounted as a directory) so an upload by rename is always visible.
mkdir -p "$ROOT/caddy"
[[ -f "$ROOT/caddy/Caddyfile" ]] || { echo "$ROOT/caddy/Caddyfile is missing" >&2; exit 1; }
compose up -d --wait postgres caddy
log "reloading the Caddyfile"
# An invalid Caddyfile makes this fail, which aborts the deploy before anything is changed.
compose exec -T caddy caddy reload --config /etc/caddy/Caddyfile

log "applying roles.sql"
# Passwords travel on stdin, never on a command line (they would show up in the process list).
{
  printf '\\set migrator_password '"'"'%s'"'"'\n' "$DB_MIGRATOR_PASSWORD"
  printf '\\set app_password '"'"'%s'"'"'\n' "$DB_APP_PASSWORD"
  cat "$RELEASE/roles.sql"
} | compose exec -T postgres psql -U postgres -d gameya -v ON_ERROR_STOP=1 -q

log "running migrations"
chmod +x "$RELEASE/efbundle"
# The bundle's design-time factory reads ConnectionStrings__Migrator; `-e NAME` passes it without putting the value
# on the command line.
ConnectionStrings__Migrator="Host=postgres;Port=5432;Database=gameya;Username=gameya_migrator;Password=$DB_MIGRATOR_PASSWORD" \
  docker run --rm --network "${PROJECT}_default" \
    -e ConnectionStrings__Migrator -e DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    -v "$RELEASE/efbundle:/efbundle:ro" \
    mcr.microsoft.com/dotnet/runtime-deps:10.0 /efbundle

switch_current() {
  ln -sfn "$1" "$RELEASES/current.tmp"
  mv -T "$RELEASES/current.tmp" "$RELEASES/current"
}

rollback() {
  log "FAILED: rolling back"
  if [[ -n "$PREV_TAG" ]]; then
    echo "API_TAG=$PREV_TAG" > "$ROOT/image.env"
    [[ -n "$PREV_RELEASE" ]] && switch_current "$PREV_RELEASE"
    compose up -d api || true
    log "restored API_TAG=$PREV_TAG, current -> ${PREV_RELEASE:-unchanged}"
  else
    log "no previous release to roll back to; stopping api"
    compose stop api || true
  fi
}

# From here until the health check passes, any failure or termination signal (SSH drop, job timeout) rolls back.
on_failure() {
  trap - ERR HUP INT TERM
  set +e
  rollback
  exit 1
}
trap on_failure ERR HUP INT TERM

log "switching api to $SHA"
echo "API_TAG=$SHA" > "$ROOT/image.env"
compose up -d api
switch_current "$SHA"

curl_args=(-fsS --max-time 5 --resolve "$SITE_ADDRESS:443:127.0.0.1")
[[ "${GAMEYA_INSECURE_CURL:-0}" == "1" ]] && curl_args+=(-k)

log "waiting for /health (up to ${HEALTH_TIMEOUT_SECONDS}s)"
deadline=$((SECONDS + HEALTH_TIMEOUT_SECONDS))
healthy=0
while ((SECONDS < deadline)); do
  if body="$(curl "${curl_args[@]}" "https://$SITE_ADDRESS/health" 2> /dev/null)" && grep -q 'Healthy' <<< "$body"; then
    healthy=1
    break
  fi
  sleep 3
done

if ((healthy == 0)); then
  compose logs --tail 40 api >&2 || true
  on_failure
fi
trap - ERR HUP INT TERM
log "healthy"

# Keep the newest 3 releases (and the live and previous ones) and the newest 3 images.
mapfile -t old_releases < <(find "$RELEASES" -mindepth 1 -maxdepth 1 -type d -printf '%T@ %f\n' | sort -rn | awk 'NR>3 {print $2}')
for old in "${old_releases[@]}"; do
  [[ "$old" == "$SHA" || "$old" == "$PREV_RELEASE" ]] && continue
  rm -rf "${RELEASES:?}/$old"
done
# Only release images (tagged with a 40-character git SHA) are touched, never dev/latest tags.
mapfile -t old_images < <(docker image ls gameya-api --format '{{.Tag}}' | grep -E '^[0-9a-f]{40}$' | awk 'NR>3' || true)
for tag in "${old_images[@]}"; do
  [[ "$tag" == "$SHA" || "$tag" == "$PREV_TAG" ]] && continue
  docker image rm "gameya-api:$tag" > /dev/null 2>&1 || true
done
docker image prune -f > /dev/null
log "done"
