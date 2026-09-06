# Concerned Teamster v0.9.0 — owner packet

CT-045. Everything needed to decide whether, and how, to publish the
first-ever public release of Concerned Teamster. This packet does not
publish anything — publication is Eren-only, always, per every operating
document this project has. Nothing here is legal advice.

## One-paragraph summary

Concerned Teamster's v0.1 through v0.8 internal RCs delivered cart truth,
load planning, descent safety, trip recording, optional Cartographer
integration, multiplayer authority, accessibility, and compatibility
awareness — nine sprints, every acceptance criterion proven off-game and
recorded. v0.9 adds no new gameplay feature; it is entirely
release-engineering: a frozen feature/default surface, a privacy-safe
feedback path, complete public docs, a security self-audit, scripted
deploy tooling, a defect burn-down to zero P0/P1/P2, and — for the first
time — a single compiled pre-release smoke checklist covering every
pending manual claim accumulated since v0.1. **Every automatable gate is
green. The entire remaining gate is the human smoke checklist itself,
which has never been run.**

## RC identity

Full detail in `RELEASE_DOSSIER.md`'s v0.9.0 entry. Summary:

| | |
|---|---|
| Version | 0.9.0 |
| Source commit | `161dc9d951f42ef345da88cb2e6f962e33d9d3b4` |
| ZIP | `TheConcernedCat-ConcernedTeamster-0.9.0.zip` |
| ZIP SHA-256 | `07ac86f6c9e0af216aaad239945cc5927c8c02ff1822d0634d3a705ab51807fd` (144,126 B) |
| DLL SHA-256 | `6d0133f363c0fd99a7a1b844ddd53d7d9070b248b719b91b0f77059b96638cbf` (247,808 B) |
| Built against | Valheim 0.221.12 (buildid 21981559), BepInExPack 5.4.2333, Jötunn 2.29.2 |

Both hashes are build fingerprints of the artifact this seal actually
tested — not a promise that rebuilding from the source commit reproduces
them byte-for-byte. This is a known, pre-existing, documented limitation
(DEF-teamster-v0.8-001, #214, deferred P3, no correctness/safety impact)
present since the v0.1 RC; do not treat a rebuild producing a different
hash as evidence the artifact was tampered with — check the DLL's
InformationalVersion string instead, which does correctly and reliably
name its exact source commit.

**Do not rebuild this ZIP if you intend to publish the exact tested
artifact.** If you make any change, however small, before publishing,
that is a new RC — reseal it (a new CT-04x-style leaf, or a manual
version-sync + package + hash cycle following this same pattern) rather
than publishing an unhashed rebuild.

## What is proven vs. what is pending

**Proven (automated, exhaustive):** 622 Teamster unit tests + 568
Cartographer regression tests, all passing; every domain decision
(load/risk models, authority policy, compatibility gate, config/sidecar
migration, network-input hardening) unit-tested against realistic
fixtures; three CI-gated interop audits (authority/no-network, no-force,
no-internet-egress) at zero violations; a file-level lifecycle rehearsal
against real, actually-built packages (fresh install, an upgrade from a
real historical v0.7.0 build, uninstall, deploy idempotence). Full
detail: `TEST_PLAN.md`, `RELEASE_DOSSIER.md`.

**Pending (the actual gate):** every row of
`PRE_RELEASE_SMOKE_TEST.md` — a 36-row cross-reference table over 16
numbered sections (§0–§15), covering every feature from the Cart Status
panel through the compatibility matrix, multiplayer authority, and the
upgrade chain. **None of it has ever been run.** This is not a
formality; it is real, substantive
verification this conveyor cannot perform (no licensed, interactive
Valheim session exists in this environment). Do not skip to publication
without running it.

**Freeze record:** `FEATURE_FREEZE.md` documents the exact frozen
feature list and every config default. No feature or default should
change between this seal and publication without a new, explicit issue —
that is what "frozen" means. If the smoke checklist finds a real defect,
fixing it is in scope; adding a feature or changing a default while
you're in there is not.

## Defect status

Zero open P0/P1/P2. Two deferred P3s, both release-engineering concerns
with no correctness/gameplay/safety impact: DEF-teamster-v0.8-001 (#214,
build hashes aren't bit-reproducible across environments — a
traceability limitation, not a defect in shipped behavior) and
DEF-teamster-v0.9-001 (#216, one flaky allocation test under full-suite
Debug timing — a test-harness sensitivity). Neither blocks publication.

## Publication steps (owner-only)

Adapted from Concerned Cartographer's own `V1_RELEASE_PREP.md`, scoped
to a beta rather than a v1.0 stable release (skip the heavier v1.0-only
items — signed tags, SBOM, copyright registration — those belong to
Teamster's own eventual v1.0 seal, not this beta).

1. **Run `PRE_RELEASE_SMOKE_TEST.md` in full.** Every **BLOCKS** row must
   pass before continuing. Do not proceed on a partial pass.
2. **Do not rebuild.** If the smoke test passes clean, the ZIP hashed
   above is the one to publish. If it finds a real defect, fix it,
   reseal (new commit, new hash, a fresh dossier entry), and re-run the
   smoke test's affected sections — never publish an unhashed rebuild.
3. **Tag the release** (plain or signed, your choice — Cartographer's
   precedent uses `git tag -s`):
   ```powershell
   git tag -a concerned-teamster/v0.9.0 -m "Concerned Teamster 0.9.0 Public Beta"
   git push origin concerned-teamster/v0.9.0
   ```
4. **Create a GitHub Release** titled `Concerned Teamster 0.9.0 (Public
   Beta)`, attaching the exact ZIP and a `SHA256SUMS.txt` with the hash
   above. Release notes should cover: headline features (see the package
   README), supported Valheim/BepInEx/Jötunn versions, multiplayer/server
   requirements, known limitations (the freeze record + any smoke-test
   findings), uninstall safety, the AI-assistance disclosure, and this
   checksum.
5. **Thunderstore identity:** Team `TheConcernedCat`, package
   `ConcernedTeamster`, community Valheim, version `0.9.0`, categories
   `mods`/`client-side`/`utility`/`ai-generated` (already set in
   `thunderstore.toml` — verify at upload time, don't retype). Website/
   source: the canonical GitHub repository.
6. **Upload the exact tested ZIP** via the Thunderstore web UI, or
   `pwsh ./scripts/publish.ps1` with a token passed via an environment
   variable — never committed, never stored in a file. **No automation in
   this repository ever does this step for you.**
7. **Post-publication smoke:** install the just-published package from
   Thunderstore into a genuinely fresh profile; confirm
   `PRE_RELEASE_SMOKE_TEST.md` §1 (fresh install/startup) passes from the
   real published artifact, not just the locally-built one.
8. **Announce**, if desired, once post-publication smoke passes.

## Rollback and reapproval guidance

**If a real defect surfaces after publishing:** do not delete or
overwrite the published 0.9.0 version — Thunderstore versions are
effectively immutable once real users may have downloaded them,
same principle as Cartographer's own release discipline ("fix the bug
and publish 1.0.1", not "delete and reupload 1.0.0"). File the defect,
fix it, reseal as 0.9.1 through the normal conveyor process, and publish
the patch. Communicate the issue in the GitHub issue tracker and, if
severe, in the release notes for the patch version.

**If Thunderstore's own moderation rejects or flags the upload:** this
has a documented precedent from Concerned Cartographer's own release
history — check the team-visible rejection reason on the package page
first; automated flags are often false positives with a manual-review
path, not necessarily a real problem with the package. Reapprove in
place rather than re-uploading changed bytes unless a moderator
specifically demands a change; a version number is consumed by an
upload attempt regardless of its approval outcome, so an unnecessary
re-upload just for the sake of retrying costs you a wasted version
number for no benefit.

**If you decide NOT to publish 0.9.0 as-is:** nothing forces your hand.
This packet documents readiness; it does not compel a decision. The
conveyor's own next planned work (CT-046 onward, the v1.0 stabilization
sprint) can proceed on internal RCs regardless of whether or when 0.9.0
specifically gets published — but per this issue's `gate:human-preview`
label, that further conveyor work does not resume automatically until
you've reviewed this packet.

## Sign-off checklist

- [ ] I have read this packet and `RELEASE_DOSSIER.md`'s v0.9.0 entry.
- [ ] I have run `PRE_RELEASE_SMOKE_TEST.md` in full (or explicitly
      accepted specific pending rows as out of scope for this beta, with
      my own written rationale).
- [ ] I approve publishing this exact ZIP (hash above), OR I have
      identified specific defects that need a resealed RC first.
- [ ] I understand publication is a step I take myself — no automation
      in this repository will do it for me.
