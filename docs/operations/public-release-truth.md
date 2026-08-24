# Public release truth gate

The homepage and `/patchnotes` are release artefacts, not optional editorial
follow-up.

`/patchnotes` is generated from Git by
`tools/patchnotes/build-changelog.sh`. Both deploy scripts regenerate it and
refuse to continue if the committed asset differs.

The homepage is semantic and cannot be generated safely. Instead,
`tools/public-site/check-status-freshness.sh` compares the last commit that
reviewed `home-body.html` with the current gameplay, topology and domain source
tree. A difference blocks deployment until a person reviews and commits the
homepage. This deliberately avoids volatile counts such as the current number
of ships or owned entities; those belong on the live map and inspector, not in
a durable milestone.

## Normal release

1. Make and verify the gameplay/domain change.
2. Review `WorldsAdriftServer/Web/Assets/home-body.html`. Keep these boundaries
   explicit: production authority, live pure-shadow observation, compiled but
   default-off policy, and future remote-worker design.
3. Commit the implementation and homepage review together, or commit the
   homepage review immediately afterward.
4. Run `tools/patchnotes/build-changelog.sh` and commit the generated notes.
5. Run `tools/deploy-game.sh --dry-run`, then `tools/deploy-game.sh`.

The game deploy publishes the login/web service after the game service passes
its restore/health checks. This makes the homepage and build log part of the
same normal release. `WAREBORN_SKIP_PUBLIC_SITE_SYNC=1` exists only for an
explicit emergency in which restarting the public service is riskier than a
temporarily stale page; its warning must be recorded and followed by
`tools/deploy-login.sh` as soon as the emergency ends.

For a web-only change, `tools/deploy-login.sh --dry-run` and
`tools/deploy-login.sh` apply the same homepage and patch-note gates.

## What the gate does not prove

It proves that the source changed after the last review and forces a new human
decision. It cannot prove the wording is correct. Tests pin the highest-risk
claims and roadmap arithmetic, while screenshots remain required for layout.
