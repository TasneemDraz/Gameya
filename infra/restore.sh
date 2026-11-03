#!/usr/bin/env bash
# Restores a backup from the off-site remote.
#   restore.sh [latest|<timestamp>] [--verify-only]
# --verify-only restores into a throwaway postgres:17 container and checks the data is readable; it never touches
# live data. Without it: stops api, pg_restore --clean --if-exists, restores the files volume, starts api.
# Env overrides: GAMEYA_ROOT (default /opt/gameya), GAMEYA_PROJECT (compose project, default gameya), RCLONE_REMOTE (default "b2:$B2_BUCKET"),
# CONFIRM_RESTORE=yes (skips the prompt for a destructive restore).
set -euo pipefail

WHEN="latest"
VERIFY_ONLY=0
for arg in "$@"; do
  case "$arg" in
    --verify-only) VERIFY_ONLY=1 ;;
    *) WHEN="$arg" ;;
  esac
done
[[ "$WHEN" =~ ^(latest|[0-9]{8}T[0-9]{6}Z)$ ]] || { echo "usage: restore.sh [latest|<timestamp>] [--verify-only]" >&2; exit 2; }

ROOT="${GAMEYA_ROOT:-/opt/gameya}"
PROJECT="${GAMEYA_PROJECT:-gameya}"
cd "$ROOT"

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

if [[ -z "${RCLONE_REMOTE:-}" ]]; then
  B2_BUCKET="$(env_get B2_BUCKET)"
  [[ -n "$B2_BUCKET" ]] || { echo "B2_BUCKET is not set in .env" >&2; exit 1; }
  RCLONE_REMOTE="b2:$B2_BUCKET"
  export RCLONE_CONFIG_B2_TYPE=b2
  RCLONE_CONFIG_B2_ACCOUNT="$(env_get B2_KEY_ID)"
  RCLONE_CONFIG_B2_KEY="$(env_get B2_APP_KEY)"
  export RCLONE_CONFIG_B2_ACCOUNT RCLONE_CONFIG_B2_KEY
fi

if [[ "$WHEN" == "latest" ]]; then
  newest="$(rclone lsf "$RCLONE_REMOTE/db/" | sort | tail -n 1)"
  [[ -n "$newest" ]] || { echo "no backups found in $RCLONE_REMOTE/db/" >&2; exit 1; }
  ts="${newest#gameya-}"
  ts="${ts%.dump}"
else
  ts="$WHEN"
fi

work="$(mktemp -d "$ROOT/restore-work.XXXXXX")"
verify_container="gameya-restore-verify-$$"
cleanup() {
  docker rm -fv "$verify_container" > /dev/null 2>&1 || true
  rm -rf "$work"
}
trap cleanup EXIT

echo "[restore] fetching backup $ts"
rclone copyto "$RCLONE_REMOTE/db/gameya-$ts.dump" "$work/db.dump"
rclone copyto "$RCLONE_REMOTE/files/gameya-files-$ts.tar.gz" "$work/files.tar.gz"

if ((VERIFY_ONLY == 1)); then
  echo "[restore] verifying in a throwaway postgres:17 container"
  docker run -d --name "$verify_container" -e POSTGRES_PASSWORD=verify postgres:17 > /dev/null
  # The image restarts postgres once while initialising, so poll for a real query from the final server.
  ready=0
  for _ in $(seq 1 90); do
    if docker exec "$verify_container" sh -c 'test -f /var/lib/postgresql/data/PG_VERSION && psql -U postgres -tAc "select 1"' > /dev/null 2>&1 \
      && docker logs "$verify_container" 2>&1 | grep -q 'init process complete'; then
      ready=1
      break
    fi
    sleep 1
  done
  ((ready == 1)) || { echo "throwaway postgres did not become ready" >&2; exit 1; }
  # Roles from the live server do not exist here, so ownership and grants are skipped.
  docker exec -i "$verify_container" pg_restore -U postgres -d postgres --no-owner --no-acl --exit-on-error < "$work/db.dump"
  migrations="$(docker exec "$verify_container" psql -U postgres -tAc 'select count(*) from "__EFMigrationsHistory"')"
  users="$(docker exec "$verify_container" psql -U postgres -tAc 'select count(*) from users')"
  tar tzf "$work/files.tar.gz" > /dev/null
  [[ "$migrations" -ge 1 ]] || { echo "VERIFY FAILED: __EFMigrationsHistory is empty" >&2; exit 1; }
  echo "[restore] VERIFY OK: backup $ts has $migrations migrations and $users users; files archive is readable"
  exit 0
fi

if [[ "${CONFIRM_RESTORE:-}" != "yes" ]]; then
  [[ -t 0 ]] || { echo "refusing to restore non-interactively without CONFIRM_RESTORE=yes" >&2; exit 1; }
  read -r -p "This REPLACES the live database and files with backup $ts. Type 'restore' to continue: " answer
  [[ "$answer" == "restore" ]] || { echo "aborted"; exit 1; }
fi

echo "[restore] stopping api"
compose stop api
compose up -d --wait postgres
echo "[restore] restoring database"
compose exec -T postgres pg_restore -U postgres -d gameya --clean --if-exists --exit-on-error < "$work/db.dump"
echo "[restore] restoring files"
docker run --rm -v "${PROJECT}_files":/data -v "$work:/in:ro" postgres:17 \
  sh -c 'find /data -mindepth 1 -delete && tar xzf /in/files.tar.gz -C /data'
echo "[restore] starting api"
compose up -d api
echo "[restore] done"
