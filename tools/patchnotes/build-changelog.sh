#!/usr/bin/env bash
#
# Regenerate the public /patchnotes source from the actual commit log.
#
# The page is a changelog, not a blog: every line under a date is a real commit
# that is in the history, so "what the page says shipped" and "what shipped" are
# the same thing and neither can drift from the other by being written up.
#
# WHY THIS IS GENERATED INTO A COMMITTED FILE rather than read from git at
# request time: production runs a published, self-contained binary out of
# /opt/wareborn/WorldsAdriftServer-linux and there is no repository on that box
# to run `git log` against. The generated source is embedded as a web asset like
# every other page fragment, so the page has no runtime dependency on git at all.
#
# Run it before cutting a release, and commit the result:
#
#   tools/patchnotes/build-changelog.sh
#   git add WorldsAdriftServer/Web/Assets/patch-notes.md
#
# MERGE COMMITS ARE EXCLUDED. A merge's subject is "Merge branch 'feat/x'",
# which tells a reader nothing the branch's own commits do not already say, and
# the branch's commits are in the history either way - so including merges would
# list the same work twice, once uselessly.
#
# WHERE THE LOG STARTS. This repository carries the ORIGINAL WorldsAdriftReborn
# project's history from 2021 - 135 commits by killzoms, sp00ktober, mmjr-x, Cat
# and others - and Wareborn's own work begins at b7f7329 on 2026-08-07. This
# page is Wareborn's changelog, so it starts at that exact moment and credits
# the foundation in a line at the end rather than listing other people's
# commits as if they were ours. Change SINCE only if that boundary is wrong.
#
# THE BOOKKEEPING COMMITS ARE EXCLUDED, and that is not cosmetic - it is what
# makes this converge. Regenerating and committing the result is itself a
# commit, so the file could never contain the commit that records it: each run
# would report one more than the last, forever. Filtering the regeneration
# commit is the fixed point. It also happens to be the right editorial call -
# a changelog listing "Regenerate the patch notes" fifty times tells a reader
# nothing about the game.
set -euo pipefail

# Git's approxidate parser treats a bare YYYY-MM-DD as that date at the current
# clock time. There are inherited and Wareborn commits on the same day, so pin
# the precise instant immediately before b7f7329. The explicit offset also
# makes the result independent of the machine's current time zone.
since="2026-08-07 21:05:54 +0200"

# Subjects that are bookkeeping about this page rather than work on the server.
exclude_grep=(--invert-grep --grep='^Regenerate the patch notes')

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="$repo_root/WorldsAdriftServer/Web/Assets/patch-notes.md"

cd "$repo_root"

if ! git rev-parse --git-dir >/dev/null 2>&1; then
  echo "not a git repository: $repo_root" >&2
  exit 1
fi

total="$(git log --no-merges "${exclude_grep[@]}" --since="$since" --oneline | wc -l | tr -d ' ')"
inherited="$(git log --no-merges --until="$since" --oneline | wc -l | tr -d ' ')"
inherited_first="$(git log --no-merges --until="$since" --pretty='%ad' --date=short | tail -1)"
# tail, not `--reverse | head`: git log streams newest-first, so the oldest date
# is the last line. Piping a long log into `head` closes the pipe early, git
# takes SIGPIPE, and `set -o pipefail` turns that into a failed build.
first_day="$(git log --no-merges "${exclude_grep[@]}" --since="$since" --pretty='%ad' --date=short | tail -1)"

# ONE PASS OVER THE LOG, grouped here rather than re-queried per day.
#
# The previous shape asked git again for each day with
# --since="$day 00:00:00" --until="$day 23:59:59", and a commit on a day
# boundary matched TWO of those windows and was written twice; an earlier
# variant with --until "23:59" dropped one instead. Both bugs come from the
# same mistake - re-deriving membership from a timestamp range after already
# knowing which day the commit belongs to. Streaming the log once and grouping
# on the printed date makes each commit appear exactly once by construction,
# and there is no window left to be wrong about.
#
# Written to a TEMPORARY file and moved into place only after the self-check
# passes, because the check used to run on a file it had already written - so
# "REFUSING: do not ship this file" left the bad file sitting in the tree,
# where it was committed.
tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT

{
  cat <<EOF
Worlds Adrift shut down in 2019. Wareborn is a fan-run server that puts it back online.

Every commit, newest first. ${total} of them since ${first_day}. Merges are left out - they only repeat what the commits under them already say.
EOF

  # %ad with --date=short is the AUTHOR date - the day the work was written.
  # %cd would move a whole day of history the first time anything is rebased.
  # SORTED BY DATE before grouping. git streams in COMMIT order while we group
  # by AUTHOR date, so without this a day whose commits are not contiguous in
  # the stream gets two headers - which is how 14 days first rendered as 18.
  # -s keeps each day's commits in the order git listed them; -r puts the
  # newest day first, which ISO dates sort correctly under.
  git log --no-merges "${exclude_grep[@]}" --since="$since" \
      --pretty=$'%ad\t%h\t%s' --date=short \
    | sort -s -r -k1,1 \
    | awk -F'\t' '
        { line[NR] = $0; count[$1]++ }
        END {
          for (i = 1; i <= NR; i++) {
            split(line[i], f, "\t")
            if (f[1] != cur) {
              cur = f[1]
              printf "\n## %s | %d %s\n\n", cur, count[cur], (count[cur] == 1 ? "commit" : "commits")
            }
            printf "* %s %s\n", f[2], f[3]
          }
        }' 

  # The inherited history, credited rather than absorbed. It carries a real
  # date - the day that history starts - rather than a wordy "Before this",
  # because every other entry on the page is dated and the newest-first
  # ordering is checked by a test.
  cat <<EOF

## ${inherited_first} | Built on WorldsAdriftReborn

Wareborn is not a from-scratch server. It stands on the original WorldsAdriftReborn project, which worked out how to talk to the client at all - ${inherited} commits by killzoms, sp00ktober, mmjr-x, Cat and others, from 2021 onwards. That history is in this repository and is not listed above, because it is theirs and not ours.
EOF
} > "$tmp"

# SELF-CHECK. The per-day loop re-queries git with a time window, so it is
# possible for a commit to exist in the total and fall outside every day's
# window - which is exactly what happened with `--until "$day 23:59"`, a bound
# that means 23:59:00 and silently dropped a commit made at 23:59:52. A
# changelog that quietly loses commits is worse than one that fails to build,
# so this refuses to write a file it cannot account for.
written="$(grep -c '^\* ' "$tmp" || true)"
if [ "$written" != "$total" ]; then
  echo "REFUSING: git reports $total commits since $since but only $written rows were written." >&2
  echo "The grouping and the count disagree. Nothing was written." >&2
  exit 1
fi

# Only now does it become the real file.
mv "$tmp" "$out"

echo "wrote $out"
echo "  $written commits across $(grep -c '^## ' "$out") dated sections (one is the inherited-history note)"
