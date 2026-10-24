# Claude Code prompt — local integration, build and playable candidate

Paste this complete prompt into Claude Code on BLD. Read it through before taking
actions. It continues the supplied source candidate; it is not a request to start
a second ladder implementation.

---

You are the local integration lead for **Concerned Ladders**, an independent
Valheim mod in `Weakened/ConcernedCatMods`. The owner wants real vertical ladders
placed with the hammer like walls, in wood, copper and iron, with a genuine
climb loop if practical and regular walking animation as an accepted fallback.
They want a dedicated Thunderstore profile with dependencies installed and the
candidate ready to run. They specifically want detailed execution and testing
that avoids repeatedly handing obvious failures back to them.

## Authority and operating rules

The owner has authorized implementation, a new isolated development profile,
dependency installation into that profile, and preparation/testing of the game
there. Proceed through those reversible steps autonomously. Use only a newly
created disposable character and world. Do not operate on real saves or change
existing profiles. No Thunderstore publication, public release, tags, billing,
global game reinstall, or broad cleanup is authorized by this task.

Read `AGENTS.md`, `CLAUDE.md`, `docs/NAMING_CONVENTIONS.md`, then every document in
`docs/mods/concerned-ladders/`. The current brief supersedes the older Foreman
ladder scope: this is a new product with custom wood/copper/iron pieces. It does
not authorize rebuilding NPC/settlement systems or silently changing shipped
product identities. Preserve `Prepare-TCC-Screenshot-Profile.ps1` exactly.

You may use subagents. Give each a finite task, separate worktree and file
allowlist. Only you integrate, run product builds, deploy or operate Valheim.
Never let two agents write the same tree. Keep a visible named console or
readable progress log for this run, and update `STATUS.md` after each meaningful
checkpoint. A PID is not proof of progress or a working game.

## Phase 0 — recover exactly what is already done

1. Canonical checkout is `C:\code\ConcernedCatMods`. Inspect repo root, current
   branch, dirty/untracked files, remote, recent commits and worktrees. Preserve
   owner edits and other running jobs. Do not reset, clean, overwrite, stash-pop,
   delete worktrees, or merge unrelated PRs.
2. The cloud source baseline was
   `89ce8b68e05c4494e5d14165482eca8d2486c786`. The supplied source candidate has
   original custom-piece registration, new traversal adapters, pure tests,
   package metadata, CI wiring, profile generator and deployment script. Read
   the supplied status/manifest and import this work before writing replacements.
3. If the feature branch is accessible on origin, fetch and inspect it. Otherwise
   import the supplied Git bundle into a uniquely named local branch, or apply
   the supplied patch after `git apply --check` in an isolated worktree. The
   source ZIP is a review copy, not a reason to overwrite the canonical checkout.
   If main advanced, reconcile only the feature's files in your worktree.
4. CL-001, CL-002 and CL-003 are local work-item keys. The cloud GitHub connector
   refused issue and branch creation with HTTP 403; they are not real issue numbers. If
   your authorized local GitHub connection can write, create the small issue set
   from `PROJECT.md`; otherwise keep accurate local tracking and continue.
5. Current device status must be observed locally. The previous ChatGPT session
   could read GitHub but Desktop Commander was paused by its monthly usage
   allowance. Do not retry a known service limit, reconnect, change billing or
   bypass it. A local CLI run uses its normal authorized local access.

Deliver this phase: source head and dirty status, active worker roles, actual
Valheim install/version/build, manager/version/data path, and a short task graph.
Do not ask the owner for paths that can be discovered read-only.

## Phase 1 — verify APIs and get an honest product build

1. Inspect the installed licensed Valheim assemblies and current native method
   signatures. Check Character.UpdateMotion(float), Character.Jump(bool),
   Player.TakeInput(), m_moveDir, m_maxAirAltitude, ZNetView ownership, collider
   shape, Rigidbody.linearVelocity, ZSyncAnimation floats, and every Piece/
   WearNTear field used by the new registration. Keep private API access behind
   `Runtime/LadderMotor.cs` or another narrow existing adapter.
2. New product dependencies are **BepInExPack Valheim 5.4.2351** and **Jotunn
   2.30.2**. These were verified as publisher releases, not tested against your
   installation. Jotunn 2.30.x contains Valheim 1.0 menu changes; the old repo's
   2.29.2 pin is not evidence that it suits this product. Preserve other products'
   package pins. If current installed APIs require a change, document the exact
   evidence and update this product's NuGet/package/profile pins together.
3. Complete Phase 2 steps 1–2 first when the new dependency profile is needed
   for the build. Verify its installed versions before choosing references.
   Use the current private `Environment.props` according to the repo's workflow;
   copy it into the feature worktree without committing it. Bind the worktree's
   required references to the intended test setup. Never retarget another
   product's deployment path or expose local account paths/tokens in a PR.
4. Product builds must use the existing machine-wide build lock described in
   `docs/settlement/cart-and-collection/TASKS.md`. Inspect that lock's path and
   implementation; do not create competing publicizer jobs. Pure tests may run
   independently. Run the real `scripts/verify.ps1`, which builds all product
   assemblies, tests those outputs, and validates the repo.
5. Fix actual API/compile errors within this feature; do not fabricate game
   stubs, suppress compiler errors, skip product projects, change acceptance
   criteria, or quote a bare `dotnet test` as a successful plugin build. A
   pre-existing unrelated build failure must be isolated and reported honestly.

Deliver this phase: successful real Release verify summary or exact blocker,
API audit notes, source SHA and focused independent review of corrections.

## Phase 2 — install one separate profile

1. Run `python tools/prepare_ladders_profile.py`. Its `.r2z` contains dependency
   references only. Import through Thunderstore Mod Manager's supported **new
   profile** flow, selecting Valheim and `TCC-Ladders-Dev`. Inspect the imported
   profile name/path and installed package versions. Do not hand-author manager
   `mods.yml`, assume a cache exists, or import the unpublished ladder package
   from a nonexistent online listing.
2. Existing profiles must remain untouched. If this exact profile already
   exists, inspect whether it is this task's clean candidate; preserve unknown
   content and choose a clearly separate new test profile only with matching
   explicit tooling changes. Never delete a profile to force the script green.
3. The `.r2z` cannot carry this unpublished mod DLL: the supported importer
   rejects DLLs/scripts. Build/package the mod locally. Run
   `scripts/deploy-ladders-profile.ps1 -WhatIf` with the real profile path,
   successful verify log, full source commit and exact own-DLL SHA256. Resolve
   any duplicate/wrong-version/unrelated-plugin findings without mass deletion.
4. After preflight passes, deploy the same bytes. Preserve an existing candidate
   through the script's explicit replacement/backup route. Record its receipt.
   `DEPLOYED_NOT_LAUNCHED` means exactly that, not ready to run.
5. Configure the disposable save/test setup through supported manager options.
   A mod profile does not isolate Valheim saves. If using `-savedir`, verify
   current local/cloud save behavior; do not assume it hides real characters.
6. Launch with this profile's **Start modded** or its manager-generated shortcut.
   Do not invent a hardcoded Doorstop launch line; installed loader versions use
   different arguments. Confirm the live loader log includes the expected own
   assembly, version/commit and all three registered custom prefab IDs.

Deliver this phase: exact profile path/name, dependency inventory, candidate
hash, startup log and visible successful menu/world load. If launch is blocked,
report the exact last completed state, such as `DEPLOYED_NOT_LAUNCHED` when the
copy succeeded, and finish independent work. Do not claim launch or readiness.

## Phase 3 — repair through the acceptance matrix

Execute `TEST_PLAN.md`. You, not the owner, perform the initial regression pass.
Build the repeatable disposable tower course and record test IDs and evidence.

The must-work behaviors are: three material pieces; ordinary cost/station/ward/
support rules; 2 m wall placement; two/ten-section mixed stacks; deliberate
grounded mounting; up/down/neutral/reverse; top/bottom/intermediate landing entry
and exit; blocked ceilings/doors/floor lips; ordinary fall after Jump; remapped
keyboard and controller; menu/chat pause; destruction/teleport/death/reload
cleanup; normal stairs/combat after leaving; valid save/reload identities.

For each defect, give the review agent a concrete repro and bounded source
allowlist, integrate one correction, rerun the failed case and adjacent safety
cases. Never solve clipping by disabling all collision, teleporting through
walls, changing global player movement, or granting fall immunity. Never replace
all source with a new climbing framework because one hook needs correction.

Run host/client and dedicated tests with legitimate available endpoints, including
two simultaneous climbers, late join, destruction and mismatched versions. If a
second endpoint is unavailable, mark those rows BLOCKED; solo/replication-source
inspection is not a substitute. Do not publish with a fabricated network pass.

Foreman is deliberately absent from the clean profile. In a separate compatibility
profile, verify the explicit conflict refusal, then Foreman ladders disabled and
restart. Do not unpatch Foreman or silently rewrite its config.

## Phase 4 — presentation, freeze and handoff

Coordinate with Codex using `CODEX_PROMPT.md`. The accepted minimum is the walking
cycle: moves with actual climb, holds still, reverses coherently, restores after
exit, and is visible coherently to an observer. Original authored animation is
optional after movement works; it must not delay a reliable ladder candidate.
No ripped clip or broad animator replacement. Bones remain optional follow-up.

Freeze and commit the final source. Under the build lock, run
`scripts/package.ps1 -Product ConcernedLadders` first: that script rebuilds.
Then run the mandatory full `scripts/verify.ps1` and capture a fresh log.
Compare the package's own DLL SHA256 with the final verified DLL. If they differ,
package the verified bytes with the existing `tcli build` invocation and
`tools/reproducible_zip.py` without another product rebuild, then compare again.
Deploy exactly the verified DLL and complete the decisive smoke after deployment.
Do not rebuild silently after recording evidence. Keep public publication and
main merge separate from the candidate's test readiness.

Final response to the owner must contain: exact source/branch/PR if actually
created; what works with observed evidence; exact profile and how to start it;
DLL/package paths and hashes; complete checks and remaining unrun rows; and no
more than four short visual checks for the owner. Include video of mount →
climb → hold → reverse → safe exit and at least one blocked top. Never describe
an unfinished profile or source-only candidate as ready to run.

Continue autonomously through normal fixes and local testing. Ask a question
only for a real unresolved decision that cannot be safely inferred; explain the
exact blocker and the concrete finished work awaiting that decision.
