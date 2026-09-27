#!/usr/bin/env bash
# Dong bo danh sach thanh vien trong team.json vao quyen collaborator (push)
# cho tung repo liet ke trong team.json. Idempotent: chi cap/giu quyen,
# khong xoa collaborator nao khong co trong danh sach.
#
# Cach dung: them username moi vao mang "members" trong team.json roi chay lai file nay.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEAM_FILE="$SCRIPT_DIR/team.json"

repos=$(jq -r '.repos[]' "$TEAM_FILE")
members=$(jq -r '.members[]' "$TEAM_FILE")

while IFS= read -r repo; do
  while IFS= read -r user; do
    echo "==> cap quyen push: $repo <- $user"
    gh api "repos/$repo/collaborators/$user" -X PUT -f permission=push
  done <<< "$members"
done <<< "$repos"
