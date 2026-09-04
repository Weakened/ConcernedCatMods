# Concerned Teamster autonomous execution order

This document is the operating contract for the Concerned Teamster conveyor.
Authority: kickoff issue **CT-OPS-001 (#107)**, which adopts the rev-2 conveyor
model proven on Concerned Cartographer (OPS-001, #15).

## Ownership

Claude Code owns issue generation, implementation, integration, testing, defect
filing and burn-down, documentation, and release-candidate preparation from
kickoff through the sealed Teamster v1.0 RC. Eren owns the final in-game v1.0
smoke test and every Thunderstore publication, including the v0.9 public beta.

## Issue graph

- Ten sprint controllers (`SPRINT Teamster vX.Y`, label `type:epic`) — one per
  version v0.1 through v1.0. Controllers are never worked directly.
- Fifty leaf issues `CT-001` through `CT-050`, five per sprint, each carrying
  `mod:teamster`, its `sprint:teamster-vX.Y` label, owner/type/area/priority
  labels, an explicit dependency list, Goal, Scope, Acceptance criteria, and
  Definition of Done.
- Defects discovered during a sprint are filed as `DEF-teamster-vX.Y-NNN`
  issues labeled `mod:teamster`, `bug`, a `severity:P0..P3` label, and the
  active sprint label. In-scope P0/P1/P2 defects are fixed inside the sprint.

## Selection rule (lowest-numbered unblocked leaf)

When choosing the next unit of work, in order:

1. **Preemption check.** If any open issue labeled `mod:cartographer` carries
   `severity:P0` or `severity:P1` and stems from the Cartographer public beta,
   it preempts Teamster work. Fix it under the Cartographer contract first,
   then return here.
2. **Active-sprint defects.** If the active Teamster sprint has open `bug`
   issues of severity P0-P2, take the lowest-severity-number, then
   lowest-issue-number one before any new leaf.
3. **Lowest-numbered unblocked leaf.** Among open issues titled `CT-NNN: ...`
   labeled `mod:teamster` (excluding `type:epic` controllers), select the one
   with the lowest `NNN` whose dependencies are all closed. Dependencies are
   the issues named in the leaf's `Dependencies:` line.
4. If every leaf is blocked, resolve the blocker named by the lowest-numbered
   blocked leaf; if the blocker requires the owner, record it and check for a
   genuine hard stop.

One writer at a time, in the canonical tree `C:\code\ConcernedCatMods` only.

## Per-issue workflow

1. `git switch main && git pull --ff-only`.
2. Branch: `feat/ct-NNN-slug`, `fix/ct-NNN-slug`, `chore/ct-NNN-slug`, or
   `docs/ct-NNN-slug`.
3. Implement only that issue's scope. Research uncertain Valheim/mod APIs
   instead of inventing them; record findings in the issue or the spike doc.
4. Run every automatable check: `python tools/validate_repo.py`, solution
   build and `ConcernedTeamster.Tests` (plus `scripts/build.ps1` when local
   game references exist).
5. Push, open a PR, run a focused independent review pass against the issue's
   acceptance criteria, and fix findings.
6. Merge only when acceptance criteria and Definition of Done demonstrably
   pass. Comment the exact evidence (commands, outputs, hashes, screenshots as
   applicable) on the issue, close it, and continue immediately.
7. Manual-only in-game claims are recorded as pending and appended to the
   owner smoke checklist — never marked PASS.

## Sprint gates (internal)

The last leaf of each sprint validates the integrated sprint and seals a
release candidate. Gates v0.1 through v0.8 are internal quality gates: when
green, close the sprint controller and continue immediately. The integration
branch `sprint/concerned-teamster-vX.Y` is created on demand when multi-issue
integration or RC sealing needs isolation; routine leaves branch from `main`
and merge to `main` after review.

The v0.9 controller ends with a sealed public-beta RC and an owner packet;
the v1.0 controller ends with the sealed v1.0 RC and the owner smoke packet.
Publication of either is owner-only. Conveyor work continues (for example
v1.0 leaves after the v0.9 seal) unless a hard stop applies.

## Non-blocking uncertainty

Questions that deserve owner awareness but have a safe reversible default are
recorded in `HUMAN_ATTENTION.md` with the chosen default, and work continues.

## Hard stops

Stop and report only for:

- credible world/save corruption risk;
- an uncontained P0;
- credentials, payment, account, or CAPTCHA actions;
- required destructive Git operations (force-push, history rewrite);
- unresolved licensing/legal problems;
- an unavailable dependency forcing a material scope change;
- an irreversible product-promise change with no safe backward-compatible
  default;
- the final v1.0 in-game smoke test and any Thunderstore publication
  (owner-only by design).

## Safety invariants

Never: publish to Thunderstore; commit game/Unity/BepInEx/Jötunn binaries,
publicized assemblies, `Environment.props`, saves, profiles, credentials, or
private logs; mutate Valheim world saves; weaken validation to make a task
look complete; fabricate PASS results; force-push or rewrite history; create
nested repositories; or let two agents edit the same tree simultaneously.
`main` stays buildable.
