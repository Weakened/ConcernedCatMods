# Concerned Ladders — revision architecture and implementation boundaries

This is the target design for [PROJECT.md](PROJECT.md), not proof that the
current source implements it. See [STATUS.md](STATUS.md). The previous
supported-only mount and fixed walk-speed adapter require revision.

## Product and source boundaries

| Area | Responsibility |
|---|---|
| `Plugin.cs` | Config, early native skill registration, piece/runtime lifecycle, startup build identity |
| `Pieces/` | Inactive custom clones, original geometry, wear-state references, recipes, readable names/snaps |
| `Domain/` | Game-free reach/face/mode/progression policy and bounded movement decisions |
| `Runtime/LadderSegment.cs` | Custom marker, native hover/interaction and loaded segment registry |
| `Runtime/` | Narrow native input/motor/skill/collision/presentation adapters; local ownership |
| `src/Shared/Ladders/` | Existing BCL-only primitives; preserve other consumers |
| `src/ConcernedLadders.Tests/` | Source-linked production policy tests; no invented game stubs |
| `tools/prepare_ladders_profile.py` | Dependency-only profile archive, not the mod ZIP |
| `scripts/deploy-ladders-profile.ps1` | Optional development DLL deployment; not final package acceptance |
| `tools/verify_ladders_candidate.py` | Additional read-only artifact identity check; never gameplay proof |

The local lead chooses small adapter names after inspecting actual native
signatures. This map does not assert new classes already exist. Products do not
reference one another at compile time. Foreman's old motor remains untouched;
its tests cannot prove this product. The explicit conflict refusal remains.

## Prefab and skill lifecycle

Wait for `PrefabManager.OnVanillaPrefabsAvailable`. Clone `woodwall` and
`woodiron_pole` under Jotunn's inactive parent. Keep required root components,
native effects and support; replace clone-only geometry/colliders/LODs/snaps
and rebind descendant wear references. Collision remains independent of visual
state groups. Preserve saved prefab IDs and coordinate layout. Copy loaded
materials before changing them; never mutate shared donors.

Register three pieces with supported Jotunn Usage tags and the existing recipes.
A partial failure is terminal for that registration attempt and reported;
do not retry an already-reserved prefab ID. Keep pieces registered with climbing
disabled. Title Case names apply to hammer, hover and native repair surfaces.
Correct native snap labels without changing snap coordinates or replacing the
whole hammer hint panel. Inspect current formatter expectations first.

Register the permanent skill once in plugin Awake, before player Skills.Awake,
independent of the climb toggle. `AddSkill(SkillConfig)` returns the skill enum;
`SkillManager.GetSkill` returns its definition. Player progress is native.
Jotunn derives the saved UID from the identifier, so never rename it. There is
no separate skill save or a custom death-loss exemption in this design.

## Admission, selection and feedback

Process an explicit Use edge once. Native interaction takes priority; bounded
ladder-only assistance cannot steal another interactable. Use one shared
eligibility result for hints and admission, with a final recheck before
committing state. Return distinct reasons for reach, unavailable control,
geometry/ownership failure, obstruction and lack of a landing.

Compute chest-to-rung reach from the actual capsule and authored rung span.
Airborne/rising/falling is permitted. Validate line of sight, full capsule sweep,
destination room and bounded horizontal/vertical alignment before touching
physics, fall tracking, animation, stamina or skill. Failed admission is inert.
Successful catch commits the arrest of that fall once; later release uses the
actual release position. Reachable low-rung hanging has explicit geometric
bounds and must not become an endpoint-clamp hoist.

Select a physical face from the approach and lock it in the session. Derive
stand-off independently from both collider surfaces. The original body box
occupies local z=0.08..0.24; it is not centred at zero. A rear approach must
remain on its rear lane. The audited runtime instead routed both approach
signs to its front line, so the rear sweep crossed the ladder box. Correct the
lane; do not suppress the error or generally ignore ladder collision.

Obstruction classification is separate from clearance. A building-specific
error requires the hit collider's actual blocking `Piece`, excluding the
selected ladder and connected ladder run. A Piece behind the usable face,
range failure, no floor, terrain or actor does not satisfy that predicate.
Terrain/actors still participate in collision and get truthful feedback.
Unknown classification or full query buffers fail safely with a neutral reason.
Log query stage and bounded blocker identity on events, not every frame.

## Sessions, geometry and exits

One locally owned player owns one temporary session; the shared ladder marker
never owns mutable player anchors. The endpoint graph spans the whole loaded
run under fixed registry/chain limits. Aligned mixed materials connect; missing,
overlapping, parallel or physically incompatible lanes do not. Camera motion
cannot change the chosen face. Keep collision enabled throughout traversal.

Use the actual scaled capsule, centre and skin in movement and destination
checks. Exclude only appropriate self-body contacts; preserve actual ladder,
building, terrain and actor barriers. Do not ignore an entire building or all
ladder colliders as a workaround. Any narrowly justified contact filtering
needs a source explanation and both clear/blocked regression cases.

Top exit planning begins before the head meets a lip. Search a small bounded
set of plausible floor heights and destinations. Require full standing room,
supported footprint and an inboard endpoint, then sweep the complete clearance,
rise, cross and settle route. Revalidate every multi-frame leg and landing.
Closed roofs/hatches remain blocked. A safe intermediate Use exit cannot switch
shafts. No cached removed platform may support the player.

Jump has priority in every attached mode. Settle pending climb payment, resolve
camera-relative movement direction (neutral outward), relinquish the motor,
then apply a verified native-equivalent upward/directional jump once. Inspect
forced/airborne native semantics; a normal Jump call after detachment may be
refused as airborne. Never fake global ground contact or have the next tick
zero the impulse. Insufficient stamina still allows ordinary deliberate release.
Enforce 0.4 s plus a fresh Use edge across every ladder, not just the old anchor.

## Input, motion and accounting

Bind current native input/control APIs once behind a narrow adapter. Read Run
intent with remaps, controller and native toggle behavior; a stale running-state
flag after motion suppression is not intent. Menus/chat/map/console/inventory
neutralize intent. Consume each action once and preserve idle native controls.

The selected normal/fast/slide speed must reach both planner and actual motor
bounds. Alignment/exit has separate ordinary safe speed. Stamina costs apply
to observed eligible displacement divided by full-input mode speed. Budget the
maximum effective cost before a paid step; allow one pending step, settle once
before the next paid step or voluntary Use/Jump, and before jump affordability.
Bound settlement to that planned movement so knockback/teleport cannot earn XP
or spend climb stamina. Native multipliers apply once. No eligible movement
means no cost or XP. Exhaustion chooses normal/hold, not detach.

Award native RaiseSkill only for completed normal/fast climb distance, on the
owner. Preserve sub-metre unawarded distance across mounts within the same
character/world session; clear on logout/change. Native learned level and
within-level XP use native persistence. Threshold reads learned level >=20,
not effective buffs or an unlock flag. Death can relock slide under native
settings; test exact installed behavior instead of inventing a percentage.

## Presentation and networking

The existing walking-parameter path is fallback. Inspect actual rig, controller,
other animation modifications and Unity version before attempting original
in-place clips or procedural limb movement. Prefer a scoped per-player override
or a proven controller/clip Playables blend. Actual movement drives phase and
cadence; no visual root motion controls travel. Restore only resources/state
owned by this mod and respect later takeovers. Keep equipment intact.

Local Unity playback does not establish observer behavior. Document how the
observer identifies mode/face and clears stale presentation. Prefer sufficient
verified native replicated state. Independently review any minimal new
presentation-only protocol before integration: owner validation, finite/bounded
values, expiry, late join and disconnect cleanup. No remote movement authority,
per-frame movement RPCs or persistent player-ZDO keys. Dedicated servers must
not require graphical asset operations. Observers never spend stamina or XP.

If no sound custom animation can be completed, document the concrete inspected
limitation/attempt or failed prototype, retain tested walking and use
`CUSTOM_ANIMATION_DEFERRED`. This is the sole optional requested feature.

## Restoration, build and evidence

Check ownership before every physics, pose, stamina and XP write. Death,
teleport, external control, destruction, movement/rotation, invalid view,
disabled climbing, unload, world switch and exceptions release safely. Restore
only modified state; do not replay old velocity or overwrite another controller.
Keep ordinary fall behavior, non-climbing play and other products unchanged.

Compile against actual licensed installed assemblies and pinned dependencies
under the machine-wide lock. Pure tests prove policy, not Unity execution.
Test the regression that fails under the old policy before accepting the new
one; avoid fake engine stubs and implementation-mirroring assertions.

Freeze source/version, package, run the final full verify, compare exact bytes,
manager-import the ZIP, cold-start and test it. The candidate verifier checks
package/verified DLL/active plugins/current log/source/version/Downloads copy;
`IDENTITY_MATCH_ONLY` explicitly has `gameplayVerified=false` and
`binaryProvenanceVerified=false`; the log SHA is a claim, not binary attestation.
Controlled build evidence and separate gameplay rows are required. Prefer owned assets embedded in the DLL. Loose assets must
be declared, packaged, hashed and verified in the installed profile too; extend
the current strict six-file checker first. Its restriction alone does not defer
the requested animation investigation.

See [TEST_PLAN.md](TEST_PLAN.md) for the final command and gates. No public release,
tag or main merge is authorized by this architecture document.

## Primary API references and limits

- [SkillConfig v2.30.2](https://github.com/Valheim-Modding/Jotunn/blob/v2.30.2/JotunnLib/Configs/SkillConfig.cs)
- [SkillManager v2.30.2](https://github.com/Valheim-Modding/Jotunn/blob/v2.30.2/JotunnLib/Managers/SkillManager.cs)
- [InputManager v2.30.2](https://github.com/Valheim-Modding/Jotunn/blob/v2.30.2/JotunnLib/Managers/InputManager.cs)
- [KeyHintManager v2.30.2](https://github.com/Valheim-Modding/Jotunn/blob/v2.30.2/JotunnLib/Managers/KeyHintManager.cs)
- [Generated English tokens](https://github.com/Valheim-Modding/Jotunn/blob/v2.30.2/JotunnLib/Documentation/data/localization/translations/English.md)
- [Unity AnimatorOverrideController](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimatorOverrideController.html)
- [Unity AnimationClip.SetCurve](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimationClip.SetCurve.html)

Generated Jotunn input/localization tables may describe older game versions.
Check native formatter/control signatures locally. Runtime SetCurve supports
legacy clips, not arbitrary new non-legacy Animator curves. These references
establish API/design constraints; they are not current Valheim rig or network
compatibility evidence.
