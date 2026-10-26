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

The native Crouch press is a separate **Drop** action in every attached mode.
Resolve eligible ladder actions Jump > Drop > Use > travel once; native hammer
actions own any shared binding before ladder Use-to-dismount. Consume losing
release edges too, so Jump+Crouch cannot toggle sneak, and Drop+Use cannot
interact after detachment in the same frame. Inspect the actual processed action
and controller mappings; do not infer a press from native sneak state or a
hardcoded key. A Crouch held before admission is not a fresh press. Consume only
the attached release press, preserving pre-existing and off-ladder native crouch.
Overlay input must not queue a release when input resumes.

Drop settles preceding observed climb accounting once, relinquishes the motor
and pose, and restores ordinary gravity at the current safe position. No jump
call/force, outward shove, teleport, landing check or stamina requirement may
gate it. Remove only ladder-commanded motion, including horizontal alignment/
exit motion; preserve independently applied motion instead of zeroing all
velocity. An external takeover or
lost ownership follows its existing cleanup path without overwriting velocity.
Normal fall behavior and collision remain. Give Drop the same 0.4 s plus fresh
Use remount guard across all ladders. Never let a second tick reattach or retain
climb animation. Zero stamina cannot prevent deliberate release.

## Input, motion and accounting

Building is an allowed stationary attached context, not a blanket busy-state
exemption. The audited Free() rejects InPlaceMode() in both update and motor
paths, and TryMount separately rejects a building tool. Revise both gates so
equipping a hammer or mounting with it is valid; keep death, ownership, stagger
and external-control protections. Inspect actual native placement/repair/remove
action states before narrowly allowing them through any InAttack guard.

Hammer equipment alone need not prevent traversal. Native build actions and
focused menus hold the current anchor, with no passive climb charge or XP.
Preserve native action/menu/selection/rotation/snap/repair/removal/hotbar control
and native placement rules. Resolve a shared native hammer Use binding before
the old no-Interactable fallback dismount path. Do not let held or queued UI
input move/drop the player on closing a menu. The hammer animation can use its
required visual layer without moving the physical anchor or cancelling attach.

TryRun previously ran only on mount, leaving the connected run cached. On
relevant registry changes or before crossing an endpoint, refresh the bounded
run with the player's current face/anchor preserved. Newly built valid sections
become traversable without remounting. No global per-frame scan, arbitrary
neighbor switch or invisible support after removal. Native building performs
the resource/world action once; the ladder mod supplies no synthetic placement,
refund, reach, collision or ownership bypass. Recheck active support afterward.

Bind current native input/control APIs once behind a narrow adapter. Read Run
intent with remaps, controller and native toggle behavior; a stale running-state
flag after motion suppression is not intent. Menus/chat/map/console/inventory
neutralize intent. Consume each action once and preserve idle native controls.

The selected normal/fast/slide speed must reach both planner and actual motor
bounds. Alignment/exit has separate ordinary safe speed. Stamina costs apply
to observed eligible displacement divided by full-input mode speed. Budget the
maximum effective cost before a paid step; allow one pending step, settle once
before the next paid step or voluntary Use/Jump/Drop, and before jump affordability.
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

Before replacement motion implementation, complete the native inspection gate in
VANILLA_ANIMATION_BASELINE_2026_10_26.md. Read-only probe tooling is permitted;
Claude alone operates the game/build. Compare metadata with real native playback
and distinguish authoring sampling from state speed and render/capture FPS.
Optional curve access is not a mandatory extraction dependency. Missing native
access or playback evidence is BLOCKED_NATIVE_ANIMATION_BASELINE.

LIMB_FRAME_REFERENCE_2026_10_26.md provides provisional original contacts and body
progression for every sample. Final cadence/easing follows the native analysis.
Interpolate continuously, retain unwrapped contact indices and never apply the
reference body's travel a second time. The numbered samples are not engine FPS.

The existing walking-parameter path is fallback. Inspect actual rig, controller,
other animation modifications and Unity version before authoring original clips
adapted for in-place presentation, or scoped original procedural limb movement.
The owner selected original motion using GIF references, with no Meshy purchase
or stock animation import. Prefer a scoped per-player override
or a proven controller/clip Playables blend. Actual traversal drives loop phase and
cadence; no visual root motion controls travel. Restore only resources/state
owned by this mod and respect later takeovers. Keep equipment intact.

Apply [ANIMATION_REVIEW_2026_10_26.md](ANIMATION_REVIEW_2026_10_26.md). Inspect
real bone mapping, rest pose, competing animation writers and imported body/root
curves before diagnosing the rejected limbs. A Unity Humanoid export does not
establish compatibility with the installed player Avatar. Grounded mount,
accepted midair catch and collision-checked supported top transfer are separate
transitions. Fit contacts to real rungs on both faces; preserve hammer arm/action
freedom and immediate gameplay-focused Jump/Drop. A mapping or blending defect
is not evidence that custom animation is impossible.

Codex owns original presentation in an isolated worktree; Claude owns motor and
lead integration. Agree a small read-only state from accepted gameplay: session
identity, mode/face/rung geometry, actual signed travel, validated transition
progress, native hammer arm ownership and cleanup. Consume each movement sample
once. Only traversal-loop phase follows distance; mount/catch/finish also follow
the accepted transition. Presentation never consumes input or writes physics,
stamina or XP. The author cannot be the sole independent animation reviewer.

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
Controlled build evidence and separate gameplay rows are required. Prefer original animation assets embedded in the DLL. Loose assets must
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
