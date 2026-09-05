# Cartographer runtime read contract (CT-021)

This document is the written contract behind `Adapters/CartographerCapability`
and `Domain/Cartographer/*`: exactly what Concerned Teamster reads from a
running Concerned Cartographer, at which versions, and what happens when any
part of it cannot be proven. It exists because the two products **never
reference each other at compile time** — the integration is a runtime
capability probe over reflective member names, so the names below are the
entire agreement.

## Identity and version gate

| Item | Value |
|---|---|
| Detection key | BepInEx GUID `com.theconcernedcat.valheim.concernedcartographer` in `Chainloader.PluginInfos` |
| Version source | The registered `BepInPlugin` metadata version |
| **Version floor** | **0.10.0** |
| Ceiling | None by number; every contract member must verify at runtime |

**Why 0.10.0.** It is the first Cartographer build ever distributed publicly
(the Thunderstore public beta); earlier versions exist only as internal
release candidates on the dev machine, so no user can legitimately run one.
The floor was verified against the sources at commit `a23bef0` (the released
0.10.0 build): the five runtime/domain contract files
(`CartographerRuntime.cs`, `RouteStore.cs`, `AtlasRoute.cs`, `AtlasId.cs`,
`RoadPoint.cs`) are byte-identical to the current tree (0.10.1), and
`Plugin.cs` — owner of the `_runtime` member — differs only in its version
constant and one comment, with the `_runtime` declaration unchanged.

**Version policy.**

- Below the floor, or no version in the registry → **hidden**
  (`VersionTooLow`). The floor cannot be proven, so nothing is assumed.
- At or above the floor → the member probe decides. A future Cartographer
  that keeps every member's shape passes and the integration works; one that
  changes any member fails the probe and the integration hides itself. The
  probe, not the version number, is the forward-compatibility gate.

## Detection outcomes

Evaluated once per session, on the plugin's first `Update` tick (after every
plugin's `Awake`; probing from `Awake` could misread BepInEx's load order as
absence). Exactly one INFO line is logged in every state; nothing ever
errors, and no integration UI exists in any hidden state.

| State | Meaning | Effect |
|---|---|---|
| `Available` | Installed, floor met, all 12 members verified | Route features may appear |
| `Absent` | GUID not registered | Hidden; Teamster fully standalone |
| `VersionTooLow` | Version below 0.10.0 or unprovable | Hidden; line asks to update Cartographer |
| `ProbeFailed` | Lookup threw, instance missing, or members changed | Hidden; line names each failing member |

## The member chain

Every member Teamster relies on, root to leaf. Resolution never names a
Cartographer type: each hop's type is discovered from the previous hop's
field/property metadata, and values are read by these names on every access.

| # | Owner (label) | Member | Kind | Shape relied on |
|---|---|---|---|---|
| 1 | `CartographerPlugin` | `_runtime` | private instance field | holds the runtime object; null before build/after dispose |
| 2 | `CartographerRuntime` | `_routeStore` | private instance field | holds the live route table |
| 3 | `RouteStore` | `Living` | public instance property | `IEnumerable<AtlasRoute>`; excludes tombstoned (deleted) routes |
| 4 | `RouteStore` | `ChangeStamp` | public instance property | `long`; monotonic, bumped on every published change |
| 5 | `AtlasRoute` | `Id` | public instance property | `AtlasId`; durable identity |
| 6 | `AtlasRoute` | `Name` | public instance property | `string` display name |
| 7 | `AtlasRoute` | `Archived` | public instance property | `bool`; selection UIs exclude archived routes |
| 8 | `AtlasRoute` | `Points` | public instance property | `List<RoadPoint>` polyline in order |
| 9 | `AtlasId` | `Value` | public instance property | `Guid`; stable across edits/sessions/sync |
| 10 | `RoadPoint` | `X` | public instance property | `float` world X |
| 11 | `RoadPoint` | `Y` | public instance property | `float` world height |
| 12 | `RoadPoint` | `Z` | public instance property | `float` world Z |

Leaf value types (`string`, `bool`, `long`, `Guid`, `float`) are
shape-checked by the probe; mid-chain types are whatever the metadata says,
which is what lets a Cartographer refactor that preserves shapes keep
working.

## Semantics and hard rules

- **Never cache past the plugin instance.** Cartographer replaces
  `_routeStore` on world enter (`CartographerRuntime` loads the new world's
  routes), and `_runtime` itself is torn down on plugin destroy. The reader
  walks the chain fresh on every call; a null anywhere is a normal lifecycle
  state ("nothing to read now"), not an error.
- **Read-only, always.** Every access is a reflective read into an immutable
  Teamster-owned snapshot (`CartographerRouteSnapshot`). No setter, method
  call, or collection mutation is ever invoked on Cartographer objects — the
  no-atlas-mutation promise is structural.
- **Fail closed.** A structural surprise while reading returns "not
  readable" with an empty list; a malformed individual route row is skipped
  so the rest stay usable; a hole in a polyline drops that whole route
  (truncated geometry must never present as complete).
- **Cost posture.** Members are resolved per call (a few dozen reflection
  lookups); callers are expected to poll `ChangeStamp` and re-copy routes
  only on change. Nothing here runs in a per-frame path in CT-021.
- **No config surface.** Integration visibility is purely capability-driven;
  the global `Enabled` switch (off = no Teamster features at all) also skips
  the probe.

## Enforcement

- `tools/validate_repo.py` **cross-product audit**: fails on any
  `ProjectReference`/`Reference`/`PackageReference`, `using` directive, or
  `InternalsVisibleTo` coupling either product (or its test project) to the
  other, in both directions.
- `tools/validate_repo.py` **contract tripwire**: statically verifies all 12
  member declarations still appear in the Cartographer sources. A
  Cartographer rename fails validation with instructions to update this
  document, `Domain/Cartographer/CartographerContract.cs`, and the floor
  decision together.
- `ConcernedTeamster.Tests` covers the four detection paths
  (present/absent/mismatch/probe-failure) and the reader's copy/skip/fail
  behavior over fake object graphs.

## Changing this contract

1. A Cartographer change that touches any member above lands only with a
   coordinated update: contract class + this document + tripwire patterns,
   and a floor bump when the old shape no longer exists.
2. Additional members (for later leaves) are added to the contract class,
   this table, and the tripwire in the same change.
3. If Cartographer ever offers a public, versioned integration surface (a
   Cartographer-side issue), this contract migrates to it and the floor
   moves to that release; the adapter seam (`CartographerCapability`) is the
   only code that would change.
