#!/usr/bin/env bash
#
# Recreate and push the per-episode start-epNN / end-epNN tags from the linear
# `teaching` branch.
#
# Why this script exists: the tags were built and verified in an automated
# environment whose git proxy blocks pushing `refs/tags/*` (branch pushes are
# allowed, tag pushes return 403). The `teaching` branch — the 16 ordered
# snapshot commits the tags point at — is already on the remote, so this script
# rebuilds the tags from it and pushes them from a machine where tag pushes are
# permitted.
#
# Usage:
#   ./playlist/create-tags.sh [remote] [ref]
#   # defaults: remote=origin  ref=origin/teaching
#
# Safe to re-run (force-updates the tags).

set -euo pipefail

REMOTE="${1:-origin}"
REF="${2:-$REMOTE/teaching}"

git fetch "$REMOTE" teaching

mapfile -t C < <(git rev-list --reverse "$REF")
if [ "${#C[@]}" -ne 16 ]; then
  echo "Expected 16 commits on $REF (start-ep01 .. end-ep15), found ${#C[@]}." >&2
  echo "Aborting so we don't mis-tag." >&2
  exit 1
fi

# Commit 1 is the empty skeleton -> start-ep01.
git tag -f "start-ep01" "${C[0]}"

# Commits 2..16 are end-ep01 .. end-ep15. Each end-ep(n) is also start-ep(n+1)
# (same commit, two names). The final commit additionally carries end-ep16.
for n in $(seq 1 15); do
  commit="${C[$n]}"
  printf -v end "end-ep%02d" "$n"
  git tag -f "$end" "$commit"
  if [ "$n" -lt 15 ]; then
    printf -v start "start-ep%02d" "$((n + 1))"
    git tag -f "$start" "$commit"
  else
    git tag -f "end-ep16" "$commit"
  fi
done

tags=$(git tag | grep -E '^(start|end)-ep[0-9]+$' | sort -V)
echo "Tags ready locally:"
echo "$tags" | sed 's/^/  /'

# shellcheck disable=SC2086
git push "$REMOTE" $tags
echo "Pushed all start-epNN / end-epNN tags to $REMOTE."
