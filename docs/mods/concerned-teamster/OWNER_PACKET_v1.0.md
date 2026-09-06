# Concerned Teamster v1.0.0 — owner packet

CT-050. Everything needed to decide whether, and how, to publish
Concerned Teamster's first stable release. This packet does not publish
anything — publication is Eren-only, always, per every operating
document this project has. Nothing here is legal advice.

## One-paragraph summary

Concerned Teamster's v0.1 through v0.9 internal RCs delivered cart
truth, load planning, descent safety, trip recording, optional
Cartographer integration, multiplayer authority, accessibility,
compatibility awareness, and a public-beta release-engineering pass —
ten sprints, every acceptance criterion proven off-game and recorded.
v1.0 adds no new gameplay feature; it is a final audit (CT-046), a
clean-checkout regression proof (CT-047), formal performance/memory/
network budgets (CT-048), a final cross-area sign-off (CT-049), and this
seal (CT-050). **Every automatable gate is green. The entire remaining
gate is the human smoke checklist itself, which has never been run.**
This is the same shape of readiness v0.9.0 reached — v1.0.0 is a
superset, having additionally reproved everything from a fresh checkout
and formalized every performance claim.

## RC identity

Full detail in `RELEASE_DOSSIER.md`'s v1.0.0 entry. Summary:

| | |
|---|---|
| Version | 1.0.0 |
| Source commit | `0d204eb9737707044b6b6cb834ed19fad2f710a9` (`main`'s tip after the CT-050 merge) |
| ZIP | `TheConcernedCat-ConcernedTeamster-1.0.0.zip` — a persistent copy lives at `artifacts/thunderstore/` in the working repo on this machine |
| ZIP SHA-256 | `3a482abf8b0d7eea8336447c00cd949d1ce557067f0e0014f513b0660636f024` (144,734 B) |
| DLL SHA-256 | `4449fe3b69ee85917584e1e57c4f4778f695dc1c099ec64a47af0679cb218fd7` (247,808 B) |
| Built against | Valheim 0.221.12 (buildid 21981559), BepInExPack 5.4.2333, Jötunn 2.29.2 |
| Dependencies | `JotunnLib 2.29.2` only — zero transitive dependencies |

**These are corrected values** (reseal same day, hours after the
original CT-050 seal) — the artifact whose hash the seal first recorded
was only ever measured inside a scratch clone that was then deleted,
never copied anywhere durable. No source changed; see
`RELEASE_DOSSIER.md`'s "Reseal note" and `DEF-teamster-v1.0-001` for the
full account. If you already downloaded a ZIP from this repo's
`artifacts/thunderstore/` folder before this correction, re-fetch it —
the one there now is the one these hashes describe.

Both hashes are build fingerprints of the artifact this seal actually
tested — not a promise that rebuilding from the source commit reproduces
them byte-for-byte. This is a known, pre-existing, documented limitation
(DEF-teamster-v0.8-001, #214, deferred P3, no correctness/safety impact)
present since the v0.1 RC; do not treat a rebuild producing a different
hash as evidence the artifact was tampered with — check the DLL's
InformationalVersion string instead (`1.0.0+0d204eb9737707044b6b6cb834ed19fad2f710a9`),
which does correctly and reliably name its exact source commit.

**Do not rebuild this ZIP if you intend to publish the exact tested
artifact.** If you make any change, however small, before publishing,
that is a new RC — reseal it (a manual version-sync + package + hash
cycle following this same pattern) rather than publishing an unhashed
rebuild.

## What is proven vs. what is pending

**Proven (automated, exhaustive):** 629 Teamster unit tests + 568
Cartographer regression tests, all passing, reproduced from a genuinely
fresh `git clone` of the exact sealed commit (not just this conveyor's
own working tree); every domain decision (load/risk models, authority
policy, compatibility gate, config/sidecar migration, network-input
hardening) unit-tested against realistic fixtures; three CI-gated
interop audits (authority/no-network, no-force, no-internet-egress) at
zero violations; 14 formal performance/memory/network budgets, all Met
(`PERFORMANCE_BUDGETS.md`); a six-area final sign-off — docs truth,
localization, controller, accessibility, migration chain, compatibility
statement (`V1_SIGNOFF.md`); a file-level lifecycle rehearsal against
real, actually-built packages (fresh install, an upgrade from a real
historical v0.7.0 build, uninstall, deploy idempotence), run against
this exact sealed commit from a separate clone. Full detail: `TEST_PLAN.md`,
`RELEASE_DOSSIER.md`, `V1_DEFINITION_OF_DONE.md`.

**Pending (the actual gate):** every row of
`PRE_RELEASE_SMOKE_TEST.md` — now a 39-row cross-reference table over 17
numbered sections (§0–§16), extended at this leaf with a new §14
covering CT-048's real-session-only performance observations (frame-time,
BepInEx log volume, terrain-probe cost, process working set) and an
updated §9.1 noting BetterCarts' version bump (1.0.6→1.1.0, not
re-decompiled against the new build). **None of it has ever been run.**
This is not a formality; it is real, substantive verification this
conveyor cannot perform (no licensed, interactive Valheim session exists
in this environment, and no TCT-* mod-manager profile has been created
yet — see `PROFILE_REHEARSAL.md`). Do not skip to publication without
running it.

**Freeze record:** `FEATURE_FREEZE.md` documents the exact frozen
feature list and every config default, unchanged since v0.9.0 — v1.0
added no feature and changed no default. If the smoke checklist finds a
real defect, fixing it is in scope; adding a feature or changing a
default while you're in there is not.

## Defect status

Zero open P0/P1/P2. Two deferred P3s, both release-engineering concerns
with no correctness/gameplay/safety impact, unchanged since v0.9.0:
DEF-teamster-v0.8-001 (#214, build hashes aren't bit-reproducible across
environments — a traceability limitation, not a defect in shipped
behavior) and DEF-teamster-v0.9-001 (#216, one flaky allocation test
under full-suite Debug timing — a test-harness sensitivity). Neither
blocks publication.

One small, non-blocking re-verification gap worth your awareness:
BetterCarts released a real new version (1.0.6→1.1.0) since CT-037
decompiled its mass-reduction Harmony patch; this conveyor did not
re-decompile the new build (that requires downloading and inspecting
third-party compiled content, which this project treats as needing your
awareness first). The registry's GUID-based, presence-only detection is
unaffected either way, but if you want the exact "~20%" figure reconfirmed
for 1.1.0 specifically, that's a real-game check (see smoke §9.1) or a
manual decompile, not something automatable here.

## Publication steps (owner-only)

Following Concerned Cartographer's own `V1_RELEASE_PREP.md` in full —
this is a real v1.0 stable release, not a scoped-down beta, so the
heavier items skipped for v0.9.0 apply now.

1. **Freeze and verify the RC.**
   ```powershell
   git checkout main
   git pull --ff-only
   git status --short
   ```
   Status should be clean. The already-built, already-verified ZIP at
   `artifacts/thunderstore/TheConcernedCat-ConcernedTeamster-1.0.0.zip`
   is the one this packet's hashes describe — don't assume any commit
   hash cited in this doc still equals `main`'s exact current tip by the
   time you read this (further doc-only fixes can move `main` without
   changing the sealed artifact at all); the DLL's own InformationalVersion
   string is the reliable source of truth for which commit actually
   produced it. Confirm `dotnet test` and `python tools/validate_repo.py`
   both still pass on your machine — they did on this conveyor's, but
   your machine is the one whose word counts for the actual release.
2. **Run `PRE_RELEASE_SMOKE_TEST.md` in full.** Every **BLOCKS** row must
   pass before continuing. Do not proceed on a partial pass.
3. **Do not rebuild.** If the smoke test passes clean, the ZIP hashed
   above is the one to publish. If it finds a real defect, fix it,
   reseal (new commit, new hash, a fresh dossier entry), and re-run the
   smoke test's affected sections — never publish an unhashed rebuild.
4. **Make original authorship visible** (should already all be true;
   this is a final check, not new work): `LICENSE` names Eren Cansunar,
   `NOTICE.md` names The Concerned Cat, root and package READMEs name
   the project/maintainer, `SECURITY_AUDIT.md`/package README carry the
   AI-assistance disclosure, `CHANGELOG.md` has release history.
5. **Consider a signed tag.** A cryptographically signed annotated tag
   strengthens evidence that your key/account approved this release. If
   you want this, configure GPG/SSH/S/MIME signing *before* release day
   and test it first — do not improvise key setup while publishing.
   Either way (plain or signed):
   ```powershell
   git tag -a concerned-teamster/v1.0.0 -m "Concerned Teamster 1.0.0"
   # or, if signing is configured:
   # git tag -s concerned-teamster/v1.0.0 -m "Concerned Teamster 1.0.0"
   git push origin concerned-teamster/v1.0.0
   ```
   **This conveyor has not created this tag.** `concerned-teamster/v0.9.0`
   was backfilled at this leaf (it had been missed, the same gap CT-043
   fixed for v0.1–v0.8), but `v1.0.0` — the actual final stable tag — is
   yours to create.
6. **Create a GitHub Release** titled `Concerned Teamster 1.0.0`,
   attaching the exact ZIP and a `SHA256SUMS.txt` with the hash above.
   Release notes should cover: headline features (see the package
   README / `CHANGELOG.md`'s `## 1.0.0` entry), supported Valheim/
   BepInEx/Jötunn versions, multiplayer/server requirements, known
   limitations (the freeze record + any smoke-test findings + the
   BetterCarts re-verification gap above), migration notes (none — same
   config/sidecar schema as v0.9.0), uninstall safety, the AI-assistance
   disclosure, source link, and this checksum.
7. **Thunderstore identity:** Team `TheConcernedCat`, package
   `ConcernedTeamster`, community Valheim, version `1.0.0`, categories
   `mods`/`client-side`/`utility`/`ai-generated` (already set in
   `thunderstore.toml` — verify at upload time, don't retype). Website/
   source: the canonical GitHub repository.
8. **Upload the exact tested ZIP** via the Thunderstore web UI, or
   `pwsh ./scripts/publish.ps1 -Product ConcernedTeamster` with a token
   passed via an environment variable — never committed, never stored in
   a file. **No automation in this repository ever does this step for
   you.**
9. **Post-publication smoke:** install the just-published package from
   Thunderstore into a genuinely fresh profile; confirm
   `PRE_RELEASE_SMOKE_TEST.md` §1 (fresh install/startup) passes from the
   real published artifact, not just the locally-built one.
10. **Optional: dependency/SBOM record.** Already generated for this
    seal (see RC identity above — one dependency, `JotunnLib 2.29.2`,
    zero transitive). Save alongside your release evidence if you keep
    one; regenerate with `dotnet list ConcernedTeamster.csproj package
    --include-transitive` if dependencies change before you publish.
11. **Optional: copyright registration.** U.S. copyright generally
    exists automatically once original work is fixed; registration is
    not required to have copyright, but can help enforcement. Because
    this repository contains AI-assisted code, current USCO guidance
    requires human authorship and appropriate disclosure of AI-generated
    material for registration purposes — review current guidance and
    consider legal advice before filing. This is your call entirely; no
    automation here takes a position on whether to register.
12. **Optional: a private release-evidence archive** outside Git (ZIP,
    SHA256SUMS.txt, smoke-test results, screenshots, sanitized logs,
    dependency list, release notes) if you want a durable local record
    beyond what's committed here.
13. **Optional: branch protection.** Consider disallowing force-pushes
    to `main`, requiring status checks, and restricting release
    publishing to yourself, if you haven't already.
14. **Announce**, if desired, once post-publication smoke passes.

## Final technical blockers (checked at seal time)

Per Cartographer's own v1 checklist — confirmed clear at this seal, not
just asserted:

- [x] No open P0/P1 defect (two deferred P3s only, neither correctness-
      or safety-impacting).
- [x] No world/save corruption risk — Teamster never writes to world
      saves; its own sidecar has rotating backups and refuses unknown
      future format versions rather than guessing.
- [x] No broken fresh-profile install — file-level rehearsal green
      against this exact sealed commit from a clean clone.
- [x] No migration without backup/recovery — `TripPersistPlanTests`
      mandates a backup before every migration.
- [x] No unsupported dependency represented as supported — one
      dependency (`JotunnLib 2.29.2`), pinned, matches what's shipped.
- [x] Package contents match the smoke-tested ZIP — same commit, same
      hash, this packet and `PRE_RELEASE_SMOKE_TEST.md` §0 both cite it.
- [x] README/category/install requirements consistent with final
      behavior — checked directly against source at CT-049 and again at
      this seal.
- [ ] **Not yet checked — genuinely requires you:** the in-game smoke
      checklist itself. Nothing above substitutes for it.

## Rollback and reapproval guidance

**If a real defect surfaces after publishing:** do not delete or
overwrite the published 1.0.0 version — Thunderstore versions are
effectively immutable once real users may have downloaded them, same
principle as Cartographer's own release discipline ("fix the bug and
publish 1.0.1", not "delete and reupload 1.0.0"). File the defect, fix
it, reseal as 1.0.1 through a fresh issue and PR, and publish the patch.
Communicate the issue in the GitHub issue tracker and, if severe, in the
release notes for the patch version.

**If Thunderstore's own moderation rejects or flags the upload:** this
has a documented precedent from Concerned Cartographer's own release
history — check the team-visible rejection reason on the package page
first; automated flags are often false positives with a manual-review
path, not necessarily a real problem with the package. Reapprove in
place rather than re-uploading changed bytes unless a moderator
specifically demands a change; a version number is consumed by an
upload attempt regardless of its approval outcome.

**If you decide NOT to publish 1.0.0 as-is:** nothing forces your hand.
This packet documents readiness; it does not compel a decision. Per this
issue's `gate:human-preview` label, the autonomous conveyor stops here —
this is the last leaf in the v1.0 sprint, and no further Teamster work
is scheduled to resume automatically after this seal.

## Sign-off checklist

- [ ] I have read this packet and `RELEASE_DOSSIER.md`'s v1.0.0 entry.
- [ ] I have run `PRE_RELEASE_SMOKE_TEST.md` in full (or explicitly
      accepted specific pending rows as out of scope, with my own
      written rationale).
- [ ] I have decided whether to re-verify BetterCarts 1.1.0's exact
      mass-reduction behavior (smoke §9.1) before relying on the
      specific "~20%" figure — the Adapt classification itself does not
      depend on the exact number.
- [ ] I approve publishing this exact ZIP (hash above), OR I have
      identified specific defects that need a resealed RC first.
- [ ] I understand publication, tagging `concerned-teamster/v1.0.0`, and
      every step above are mine to take — no automation in this
      repository will do them for me.
