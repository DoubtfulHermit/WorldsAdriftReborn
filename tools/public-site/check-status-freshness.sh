#!/usr/bin/env bash
#
# Fail when gameplay/domain truth changed after the public homepage was last
# reviewed. This cannot decide what prose a feature deserves; it makes that
# human review an explicit release gate instead of a memory exercise.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

page="WorldsAdriftServer/Web/Assets/home-body.html"
review="$(git log -1 --format=%H -- "$page")"
if [ -z "$review" ]; then
  echo "REFUSING: cannot find a committed homepage status review." >&2
  exit 1
fi

# These are the implementation surfaces behind the homepage's gameplay,
# topology, domain and infrastructure claims. Styling and private operator UI
# are deliberately absent: they cannot change what the public roadmap says is
# live. Reverting a source change back to the reviewed tree is also safe because
# git diff compares content, not timestamps.
truth_paths=(
  WorldsAdriftRebornGameServer/Game
  WorldsAdriftRebornGameServer/WorldsAdriftRebornGameServer.cs
  WorldsAdriftRebornGameServer/ComponentsSerializer.cs
  WorldsAdriftRebornGameServer/data
  docs/architecture/elastic-runtime-phases.md
  docs/architecture/retail-flight-program-board.md
)

if git diff --quiet "$review" -- "${truth_paths[@]}"; then
  echo "    homepage status reviewed at $(git rev-parse --short "$review")"
  exit 0
fi

echo "REFUSING: gameplay/domain truth changed after the homepage's last review." >&2
echo "Review $page, update its milestone/status claims (even if the truthful" >&2
echo "change is only the reviewed build), and commit that review before deploy." >&2
echo >&2
git log --oneline "$review"..HEAD -- "${truth_paths[@]}" >&2 || true
if ! git diff --quiet HEAD -- "${truth_paths[@]}"; then
  echo "There are also uncommitted truth-surface changes." >&2
fi
exit 1
