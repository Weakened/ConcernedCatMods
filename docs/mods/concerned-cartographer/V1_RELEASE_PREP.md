# Concerned Cartographer — v1 Release Preparation and Authorship Checklist

This checklist has two goals:

1. prepare a technically safe v1 release;
2. preserve a strong public provenance/attribution record showing who created and released the project.

It is not legal advice.

## 1. Freeze the release candidate

Do not tag a dirty or ambiguous tree.

```powershell
git checkout main
git pull --ff-only
git status --short
```

The status should be clean.

Run the complete v1 gate, including `PRE_RELEASE_SMOKE_TEST.md`.

Record:

- exact commit SHA;
- exact version;
- exact package filename;
- SHA-256.

## 2. Make original authorship visible

Before v1 verify:

- `LICENSE` names Eren Cansunar in the copyright notice;
- `NOTICE.md` names the original project creator/maintainer;
- root `README.md` names The Concerned Cat and links to attribution;
- package README names the original project/maintainer;
- `AI_DEVELOPMENT.md` transparently explains AI assistance;
- `CHANGELOG.md` has release history;
- Git history remains intact.

The repository currently uses MIT.

Under MIT, other people may fork, modify, distribute and sell the code, but copies/substantial portions must preserve the copyright and permission notice.

**MIT does not prevent forks or rebranding.** It is intentionally permissive.

If you want stronger restrictions in the future, treat that as a deliberate license-policy decision. A later license change does not revoke MIT rights already granted for versions that were distributed under MIT.

Do not casually swap licenses on release night.

## 3. Separate source license from brand identity

### Source code

Governed by `LICENSE`.

### Official project identity

Official packages should remain under:

- canonical repository: `Weakened/ConcernedCatMods`;
- public creator: The Concerned Cat;
- Thunderstore team: `TheConcernedCat`;
- stable plugin GUID: `com.theconcernedcat.valheim.concernedcartographer`.

A canonical namespace, repository and release history make official provenance obvious even when forks exist.

## 4. Consider signed commits/tags

A cryptographically signed release tag strengthens evidence that a particular key/account approved a release.

GitHub supports verified signatures through supported GPG/SSH/S/MIME workflows.

Typical signed annotated tag:

```powershell
git tag -s concerned-cartographer/v1.0.0 -m "Concerned Cartographer 1.0.0"
git push origin concerned-cartographer/v1.0.0
```

Configure signing **before** release day and test it first. Do not improvise key setup while publishing.

Without signing, an annotated tag plus GitHub Release still provides useful timestamped provenance; cryptographic verification is simply stronger.

## 5. Create immutable artifact evidence

Hash the exact final ZIP:

```powershell
Get-FileHash .\artifacts\thunderstore\TheConcernedCat-ConcernedCartographer-1.0.0.zip -Algorithm SHA256
```

Create `SHA256SUMS.txt` containing the exact filename and hash and attach it to the GitHub Release.

Keep:

- release ZIP;
- checksum;
- release notes;
- source commit;
- tag;
- test summary.

Do not rebuild the ZIP after the human smoke test if you intend to upload the exact tested artifact.

## 6. Create the GitHub Release

Recommended title:

`Concerned Cartographer 1.0.0`

Release notes should include:

- headline features;
- supported Valheim/BepInEx/Jötunn versions;
- multiplayer/server requirements;
- known limitations;
- migration notes;
- uninstall safety;
- AI disclosure;
- source link;
- checksum.

Attach the exact ZIP and `SHA256SUMS.txt`.

## 7. Thunderstore official identity

For the first stable public v1 package:

- Team: `TheConcernedCat`
- Package: `ConcernedCartographer`
- Community: Valheim
- website/source: canonical GitHub repository
- version: exact semantic version
- AI Generated disclosure: enabled when applicable
- dependencies/categories: exactly match final tested behavior

The public namespace is another provenance signal.

## 8. Assembly/package metadata

Before v1, consider adding/confirming generated assembly metadata:

- Authors: Eren Cansunar
- Company/publisher: The Concerned Cat
- Copyright: Copyright © 2026 Eren Cansunar
- Repository URL
- Product name/version

Do this through a reviewed issue because it changes built binary metadata.

Never reuse the plugin GUID for another product.

## 9. Optional dependency/SBOM record

At minimum:

```powershell
dotnet list .\src\ConcernedCartographer\ConcernedCartographer.csproj package --include-transitive
```

Save dependency output in release evidence or generate an SBOM through an approved tool.

This helps prove what was shipped and diagnose future dependency problems.

## 10. Optional copyright registration

In the U.S., copyright generally exists automatically for copyrightable human-authored material once fixed; registration is not required simply to have copyright.

Registration can provide important enforcement benefits.

Because this repository contains material AI-assisted/generated code, registration requires care. Current U.S. Copyright Office policy requires human authorship and appropriate disclosure/disclaimer of AI-generated material.

If you want formal registration, review current USCO AI guidance and consider legal advice before filing. Do not claim AI-generated expression as human-authored if it was not.

## 11. Preserve a private release-evidence archive

Example outside Git:

```text
v1.0.0/
├─ release-zip/
├─ SHA256SUMS.txt
├─ final-smoke-results/
├─ screenshots/
├─ logs-sanitized/
├─ dependency-list/
└─ release-notes/
```

Do not include credentials or unrelated private information.

## 12. Branch protection and release discipline

Consider protecting `main`:

- disallow force pushes;
- require status checks where practical;
- keep release tags immutable;
- restrict release publishing to the owner/maintainer.

Do not delete/recreate a public v1 tag because of a bug. Fix the bug and publish `1.0.1`.

## 13. If somebody republishes it and claims it as theirs

Practical first steps:

1. Preserve URLs, screenshots, dates and package hashes.
2. Compare their files/source to the canonical tagged release.
3. Point to the canonical repository, copyright notice, Git history and release tag.
4. If they removed the MIT copyright/license from a copy/substantial portion, that may violate the license.
5. If they impersonate official The Concerned Cat branding, preserve evidence separately.
6. Use the platform's reporting/copyright process where appropriate.
7. For meaningful commercial/reputational harm, consult an IP attorney rather than relying on public arguments.

A README sentence alone is weak evidence. The complete provenance trail is much stronger.

## 14. Final technical blockers

Do not release with:

- P0/P1 defect;
- world/save corruption risk;
- cross-world atlas leakage;
- shared-deletion resurrection;
- unexplained recurring error;
- broken fresh-profile install;
- migration without backup/recovery;
- unsupported dependency represented as supported;
- package contents different from smoke-tested ZIP;
- README/category/install requirements inconsistent with final behavior.

## 15. Recommended final sequence

```text
freeze scope
  ↓
clean main / final RC commit
  ↓
automated tests + compatibility + migrations
  ↓
build exact ZIP
  ↓
record SHA-256
  ↓
human PRE_RELEASE_SMOKE_TEST
  ↓
NO REBUILD
  ↓
signed/annotated release tag
  ↓
GitHub Release + exact ZIP + SHA256SUMS
  ↓
Thunderstore upload of exact tested ZIP
  ↓
fresh profile install FROM THUNDERSTORE
  ↓
post-publication smoke
  ↓
announce
```

This produces both a safer release and a clear public provenance chain.
