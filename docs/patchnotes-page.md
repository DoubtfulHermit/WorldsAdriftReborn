# The public patch notes at /patchnotes

**Nothing in this document has been applied to production.** The route ships in
the login server binary; the one hand-applied step is the Caddy block below.

The in-game **PATCH NOTES** button on the landing screen is pointed at this page
(that redirect is a client-mod change and lives elsewhere). For a lot of people
this will be the first thing they read about the server.

## The route

One handler, `WorldsAdriftServer/Handlers/PatchNotes/PatchNotesHandler.cs`, owns
the whole `/patchnotes` namespace. Unauthenticated, no cookies read, no session
issued, nothing that can reach the admin command bridge.

| Route | What it is | Cache-Control |
| --- | --- | --- |
| `GET /patchnotes` | the page | `no-cache` |
| `GET /patchnotes/source` | the same notes as plain text | `no-cache` |
| anything else under `/patchnotes/` | 404 | `no-store` |

It claims the prefix and answers **every** URL beneath it. That is not
tidiness: this server sends no response at all for a path no handler claims, so
a mistyped URL under a route we advertise would leave the socket hanging instead
of 404ing.

`/patchnotesomething` only shares a leading string with the prefix and is
correctly not ours. `/patch`, `/patch/manifest.json` and `/patch/files/*` belong
to the patcher and are untouched.

Neither route is cached, because the point of the operator override below is
that a correction is live on the next load. The page is a few kilobytes of
already-composed string and there is no script on it at all.

## Where the notes live

**The file is the record.** `WorldsAdriftServer/Web/Assets/patch-notes.md`,
embedded in the binary like every other web asset. Patch notes describe a build,
and a build is a commit; keeping them in the repository is what makes "what the
page says shipped" and "what shipped" the same object, reviewable in one diff.

**The row is the correction.** `server_config['patch_notes']`. When that row
exists and is non-blank it replaces the file entirely. The admin panel has a
textarea for it under **System**, beside the server name, prefilled from
`/patchnotes/source` so the operator edits the exact text a visitor is reading.
Clearing it deletes the row and the committed notes take over again.

**No migration.** `server_config` has existed since schema v3 and was built as a
key-value table for exactly this - its own comment says a key-value shape lets
the next setting "be an INSERT rather than a migration". Production runs the
game server and the login server against one shared database, and a migration
shipped in one binary alone turns persistence off for the other.

A database that cannot be read reads as "no override", not as an error: the page
falls back to the file, which is in the process's own memory and cannot fail.

## The source format

```
lines before the first release         the page's opening paragraphs
## 2026-08-18 | Title | badge          starts a release; badge is optional
### Heading                            a heading inside it
- item                                 a bullet; a run of them is one list
anything else                          prose; a run of lines is one paragraph
```

Inline: `**bold**`, `` `code` ``, and `[label](/path)`. Everything is
HTML-escaped first and those three markers are the only things that become a
tag, so nothing an operator can store reaches a browser as markup. A link target
with a colon or a leading `//` in it is refused and the label is printed as plain
text - which is what keeps "this page reaches for nothing off this host" true by
construction rather than by review.

Releases are shown in **file order**. Nothing sorts them; a test asserts the
file is newest-first.

An empty or missing source renders the page with one card saying so. It is not
an error state.

## The Caddy block to add

Inside the existing `wareborn.ratlabs.cc` site block:

```caddy
handle /patchnotes* {
    reverse_proxy host.docker.internal:8085
}
```

Use whatever upstream address the existing `/patch*` and `/map*` handles in that
site block use - it is the same login-server upstream, and on this stack that
has been the host gateway IP rather than `host.docker.internal`. Copy the
working one.

The trailing `*` matters. Without it `/patchnotes/source` would not be proxied.

## Verified

Against a local login server on port 18099, headless Chromium at 1440 and 390
CSS px:

- 4 releases, 15 sections, 72 bullets rendered at both widths.
- Exactly **one** network request for the whole page (the page), zero console
  entries, zero external references.
- No horizontal overflow at either width.
- `/patchnotes/nope` returns 404; `POST /patchnotes` returns 404.
- Storing a `server_config` row through `/admin/patch-notes` changes the page;
  clearing it restores the committed notes.
- With the committed file emptied, the page renders its empty state.
