# Gunnar: A Fair Load — companion foundation (#297 / #456)

## Delivery and ownership

This branch implements #456's game-free record, its source includes, a scope/session
owner and a configuration/game-identity adapter. The adapter is now installed in
Plugin.Awake, refreshed in Update and forgotten on teardown. This isolated wiring
is outside the files in Claude's current PRs and active Gunnar hammer diff. It
changes no worker, shared NPC, validator, package, profile or game operation.
#456 remains pending integrated owner checks; config/lifecycle wiring is complete. All in-game rows are OWNER GO-AROUND PENDING.

Owned files: Domain/Companions/GunnarCompanion{Record,Session}.cs,
Adapters/Companions/GunnarCompanionAdapter.cs, the two Teamster project source
includes, Tests/Companions/GunnarCompanionRecordTests.cs, the four pinned Plugin
lifecycle sites and this document. TeamsterSettings.cs remains untouched.

## Identity and project

The stable CompanionId is `gunnar`; ProductId is `concerned-teamster`; QuestId is
`a-fair-load`. Display text never supplies a storage key. DisplayNameKey is
`companion.gunnar.name`; ResolveDisplayName accepts the later dialogue localizer,
then an editable configured name, then English `Gunnar`. No Cartographer assembly
reference is added. src/Shared/Companions compiles into Teamster and its tests as
source, alongside the existing ConcernedNPC assembly dependency.

## Persistence and quest

GunnarCompanionRecord.Open requires nonzero world UID and player-profile ID.
Its companion sidecar is beside TeamsterDataPaths' existing files, via
TripRecordingService.SidecarDirectory, at:

`<Teamster sidecar directory>/concerned-teamster.<world hex16>.<character hex16>.companions.tsv`

The shared schema-1 codec/store owns parsing, scope checks, atomic replacement
where supported and corruption quarantine. Its documented non-atomic fallback
on filesystems without replacement remains a limitation. No vanilla save entity,
ZDO, inventory, network message or world file is written by this feature.
Uninstall leaves only optional config/sidecar files on disk, nothing in the world.

QuestState is append-only: Unstarted=0, Discovered=1, Collected=2, IntroPending=3,
Recruited=4, Skipped=5. Skipped is a completed tools-only introduction, not a hire;
an explicit later accepted real-cart assignment can welcome Gunnar. The existing
shared transition machine supplies idempotence and monotonic progression.
Each transition writes a detached candidate before changing the visible session;
a failed write cannot expose recruitment or remove the memento. Session.Resolve
changes scope on character/world switches and drops it for unresolved IDs. Old
record handles are closed and refuse subsequent state changes or presentation.
The memento is eligible only at Unstarted/Discovered; saved Collected or later
never presents it again, including after relog.

Fresh installs, existing 1.0.x config/trips and a character who already owns a cart
all begin unrecruited. No legacy-access evidence is converted into recruitment.
Existing utilities retain their existing gates and do not consult this record.
Neither visibility nor a tools-only choice grants worker authority or custody.
TryRecruitAfterAcceptedCartAssignment is the integration seam for #460: only its
real-cart validation adapter may pass true. This leaf performs no cart lookup,
assignment or job start; those remain #460 and the existing opted-in worker gates.

Missing data starts Unstarted; corrupt, partially damaged, mismatched or unsupported
schema data refuses recruitment and progress for that session. A corrupt file is
quarantined by the shared store. A partially readable or foreign/newer file is
preserved, and requires explicit repair or moving it aside before a fresh session.
A damaged record may hide the figure but cannot remove access to existing tools.

## Lead integration instructions

Absolute worktree root:
`C:\Users\erenc\.codex\worktrees\ct-456-gunnar-record\ConcernedCatMods`.
The adapter is installed in this source revision; it has not been deployed to a live profile.

In the lead-owned Plugin.cs:

1. Hold a GunnarCompanionAdapter field and bind it once in Awake after ordinary
   config initialization: `GunnarCompanionAdapter.Bind(Config, Logger)`.
2. Ask Current() on each presentation tick/interaction. It reads only the native
   local player, world UID and player-profile ID. Never retain the returned record
   across scopes. Call Forget() on teardown. Opening reads but does not write.
3. Bind `Companions/Gunnar` (default true) and `Companions/GunnarDisplayName` through
   that adapter, independently of worker settings and the utility master switch.
4. #457's memento and #458's local figure must obey ShouldPresentMemento(enabled)
   and ShouldPresentFigure(enabled). Turning this opt-out off hides both; it
   never retires a worker body, clears a ledger, disables a tool or changes hire.
5. His name is `Companions/GunnarDisplayName`, the player's own setting (#459
   keeps it out of the catalog so a translator file never overrides it). #460 alone supplies accepted
   real-cart assignment to TryRecruitAfterAcceptedCartAssignment; a rejected cart,
   tools-only, prior config or a missing sidecar must never pass true.
6. Surface the record's Notice on the intro UI/log when present, including save
   failures; no success announcement or presentation removal after a false result.

Steps 1-3 are now wired by Plugin; steps 4-6 describe downstream presentation/recruitment integration.
Do not ship this foundation alone as a complete Gunnar introduction.

## Compatibility and tests

No existing utility, haul/collection gate, default, package identity or saved
worker record is modified. The source dependency audit scans existing Teamster
code outside the new Companions directories to refuse newly consulting the
companion record; its limit is textual names, not arbitrary reflective invocation.
The same test covers every actual TeamsterFeature/authority matrix entry.

Automated tests cover pinned stage assignments, stable identity, fresh/legacy
fixtures, no assignment acceptance, per-stage reopen, idempotent recruitment,
skip then explicit welcome, editable/localized name, opt-out, cross-character and
cross-world separation, logout/session switching, zero scope refusal, corruption,
a damaged row beside Recruited, future schema, copied foreign scope and failed-save
retry without an in-memory recruitment grant. File tests use disposable temp roots.
No production sidecar, profile, installed DLL or world is modified by the tests.

Run from the worktree root:

```powershell
dotnet test src/ConcernedTeamster.Tests/ConcernedTeamster.Tests.csproj -c Release --filter FullyQualifiedName~GunnarCompanionRecordTests
python tools/validate_repo.py
# The full game build/verify must use the shared build mutex; see handoff receipt.
```

## Owner checklist — all pending on an integrated candidate

| Row | Action and expected result |
|---|---|
| G456-01 | Fresh profile: correct name, unstarted intro, no recruited figure or worker permission. |
| G456-02 | Existing 1.0.x config/trips: every ordinary cart utility still works; Gunnar remains unrecruited. |
| G456-03 | Advanced character with cart: explicit valid assignment is still required; cart is never consumed or cloned. |
| G456-04 | Progress/relog at each stage, then world/character switches: only the correct scope resumes. |
| G456-05 | Opt out and re-enable: memento/figure visibility follows setting; tools, worker custody and saved recruitment remain intact. |
| G456-06 | Broken, missing, future or copied sidecar: no automatic recruitment; tools remain available and recovery notice is actionable. |
| G456-07 | Refuse a save: no joined announcement or memento removal; repair and retry survives relog. |
| G456-08 | Uninstall: no missing-object error or object left by this introduction. |

Memento (#457), figure (#458), dialogue (#459), assignment (#460), camp idle (#461)
and real-game evidence (#462) remain separate leaves; this record does not claim them.
