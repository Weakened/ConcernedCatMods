# Concerned Teamster profile family and lifecycle rehearsal

CT-043. How the TCT profile family is set up, what is scripted, what
stays a one-time manual step, and how to rerun everything.

## The profile family

| Profile | Purpose | Teamster installed? |
|---|---|---|
| **TCT-Clean** | Vanilla-truth baseline. Compare Teamster's displayed numbers (cargo weight, grade, etc.) against what vanilla Valheim actually shows, with nothing to explain the difference away. | **No** — deliberately absent. Nothing ever deploys here. |
| **TCT-Dev** | Day-to-day development target. Every `deploy.ps1 -Product ConcernedTeamster` (default `-Profile Dev`) call lands here. | Yes, the current dev build. |
| **TCT-Compat** | Live compatibility-matrix verification: BetterCarts, ItemStacks, and ValheimPlus installed alongside Teamster to confirm the Compat panel and load-advice gate behave as `COMPATIBILITY.md` documents. | Yes, plus the three researched mods. |
| **TCT-Dedicated** | Dedicated-server authority scenarios (CT-027): does the brake stay owner-gated, do unmodded/unrelated peers see pure vanilla. | Yes, on a dedicated-server BepInEx install. |

## What is scripted vs. what is a one-time owner step

Creating a mod-manager profile and installing third-party mods into it are
GUI actions specific to whichever mod manager you use (Thunderstore Mod
Manager, r2modman, or similar) — this repo's tooling does not attempt to
drive that GUI or reverse-engineer its profile-registration format, for
the same reason it treats Valheim internals as unstable: safer to stay
behind a narrow, well-understood surface than to invent one.

**One-time, per profile, owner-driven:**

1. In your mod manager: New Profile → name it exactly `TCT-Clean`,
   `TCT-Dev`, `TCT-Compat`, or `TCT-Dedicated` → Install BepInEx (or, for
   TCT-Dedicated, whatever your manager's dedicated-server support
   provides, or a manual BepInEx server install).
2. For **TCT-Compat** only: also install BetterCarts, ItemStacks, and
   ValheimPlus from Thunderstore's own browser inside your mod manager.
   This script never downloads or installs third-party mods itself.
3. Copy `Environment.props.example`'s `TEAMSTER_DEPLOYPATH` /
   `TEAMSTER_COMPAT_DEPLOYPATH` / `TEAMSTER_DEDICATED_DEPLOYPATH` block
   into your real `Environment.props` and point each at that profile's
   `BepInEx/plugins` folder. TCT-Clean has no entry — nothing ever
   deploys there by design.

**Scripted, repeatable, idempotent:**

- `scripts/deploy.ps1 -Product ConcernedTeamster -Profile Dev|Compat|Dedicated`
  copies the current build's DLL into the configured profile's plugins
  folder. Running it twice in a row copies the same bytes both times —
  proven by `rehearse-teamster-lifecycle.ps1`'s idempotence check (§5
  below), which exercises the identical `Copy-Item -Force` primitive.
- `scripts/rehearse-teamster-lifecycle.ps1` (below) — the full file-level
  lifecycle rehearsal, entirely inside a scratch directory, never
  touching a real profile.

## Running the lifecycle rehearsal

```powershell
pwsh ./scripts/rehearse-teamster-lifecycle.ps1
```

Requires the same tools `package.ps1` already needs (`python`, `tcli`, a
configured `Environment.props` with real Valheim/BepInEx references) plus
`git`. It performs, entirely under `artifacts/teamster-rehearsal/` and
your system temp directory:

1. **Chain integrity** — every `concerned-teamster/vX.Y.Z` tag's `csproj`
   `<Version>` matches its tag name, and versions strictly increase.
   Cheap (no rebuild); automatically covers a new tag the moment CT-045
   creates `v0.9.0`, with no script change needed.
2. **Fresh install** — builds and packages the current source for real,
   extracts the real ZIP into a scratch `BepInEx/plugins/` layout, and
   confirms exactly the one expected DLL is present.
3. **Upgrade** — checks out `concerned-teamster/v0.7.0` (the last release
   before CT-039 introduced config schema versioning) into a temporary
   `git worktree`, builds and packages *that* real historical source,
   installs it into the same scratch folder, then overlays the current
   build on top — confirming the DLL actually changed and no stale file
   was left behind. The worktree is always removed afterward, even on
   failure (`finally` block).
4. **Uninstall** — deletes the plugin file and confirms zero remaining
   references to Teamster anywhere under the scratch BepInEx tree.
5. **Deploy-copy idempotence** — runs the exact `Copy-Item -Force`
   primitive `deploy.ps1` uses, twice, and confirms byte-identical output
   both times.

Pass `-KeepScratch` to inspect `artifacts/teamster-rehearsal/` afterward
instead of having it cleaned up automatically.

### What this does and does not prove

It proves the **file-level** mechanics — the exact bytes a real install,
upgrade, or uninstall produces — using real, actually-built packages, not
synthetic stand-ins. It does **not** and cannot prove that BepInEx
actually invokes `Plugin.Awake`'s config/sidecar migration correctly
during a real Valheim launch, or that a real mod-manager profile behaves
identically to this scratch simulation — those need a live game session
and stay itemized pending in `HUMAN_ATTENTION.md`, never claimed done by
this script. The migration *logic* itself
(`ConfigSchemaMigration.Decide`, `TripPersistPlan.Decide`) is already
exhaustively unit-tested against realistic fixtures in
`ConcernedTeamster.Tests` — this rehearsal is not a substitute for that
coverage, it proves the file-handling built around it.

## Owner smoke checklist additions from this rehearsal

Once the four profiles exist for real (per the one-time steps above),
confirm in-game:

1. **TCT-Clean vs TCT-Dev**: load the same cart/cargo in both; Teamster's
   displayed mass/cargo in TCT-Dev matches what TCT-Clean's vanilla UI
   shows.
2. **TCT-Compat**: with BetterCarts/ItemStacks/ValheimPlus installed,
   confirm the Compat panel detects each one and the load-advice gate
   behaves per `COMPATIBILITY.md`'s documented policy.
3. **TCT-Dedicated**: confirm brake authority and cooperative diagnostics
   behave per `AUTHORITY_POLICY.md` against a real dedicated server.
4. **Upgrade-in-game**: install the sealed v0.7.0 ZIP into a scratch
   profile for real, play a session (generate a config file and a trip
   sidecar), then upgrade to the current build and confirm the startup
   log shows the expected one-line schema-migration message.
