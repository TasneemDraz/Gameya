#!/usr/bin/env bash
# Nightly backup (systemd gameya-backup.timer): pg_dump + files volume -> Backblaze B2 via rclone.
# Env overrides: GAMEYA_ROOT (default /opt/gameya), GAMEYA_PROJECT (compose project, default gameya), RCLONE_REMOTE (default "b2:$B2_BUCKET"; any rclone path, so a
# local directory can stand in for B2 in the rehearsal).
set -euo pipefail

ROOT="${GAMEYA_ROOT:-/opt/gameya}"
PROJECT="${GAMEYA_PROJECT:-gameya}"
BACKUPS="$ROOT/backups"
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
  # rclone reads its remote from env, so there is no config file holding the key.
  export RCLONE_CONFIG_B2_TYPE=b2
  RCLONE_CONFIG_B2_ACCOUNT="$(env_get B2_KEY_ID)"
  RCLONE_CONFIG_B2_KEY="$(env_get B2_APP_KEY)"
  export RCLONE_CONFIG_B2_ACCOUNT RCLONE_CONFIG_B2_KEY
fi

ts="$(date -u +%Y%m%dT%H%M%SZ)"
dir="$BACKUPS/$ts"
mkdir -p "$dir"
trap 'rm -rf "$dir"' ERR

echo "[backup $ts] dumping database"
compose exec -T postgres pg_dump -U postgres -Fc gameya > "$dir/gameya-$ts.dump"

echo "[backup $ts] archiving files volume"
docker run --rm --user "$(id -u):$(id -g)" \
  -v "${PROJECT}_files":/data:ro -v "$dir:/out" postgres:17 \
  tar czf "/out/gameya-files-$ts.tar.gz" -C /data .

echo "[backup $ts] uploading to $RCLONE_REMOTE"
rclone copyto "$dir/gameya-$ts.dump" "$RCLONE_REMOTE/db/gameya-$ts.dump"
rclone copyto "$dir/gameya-files-$ts.tar.gz" "$RCLONE_REMOTE/files/gameya-files-$ts.tar.gz"
trap - ERR

# Keep 3 days locally; the bucket's lifecycle rule enforces 35 days remotely.
find "$BACKUPS" -mindepth 1 -maxdepth 1 -type d -mtime +2 -exec rm -rf {} +

ping_url="$(env_get BACKUP_PING_URL)"
if [[ -n "$ping_url" ]]; then
  curl -fsS --max-time 10 --retry 3 "$ping_url" > /dev/null || echo "backup ping failed" >&2
fi
echo "[backup $ts] done"
