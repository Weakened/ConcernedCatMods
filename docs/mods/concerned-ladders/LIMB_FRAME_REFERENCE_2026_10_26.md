# Original ladder motion — limb and body frame reference

**Provisional authored reference, not native measurements or a tested animation.**
Complete VANILLA_ANIMATION_BASELINE_2026_10_26.md before selecting final gait,
easing, export sampling or blend durations and before implementing replacement
motion. This written plan and the GIF references explain the intended movement.
No more owner reference material is required to start the native analysis.

## 1. What a numbered frame means

Frames 0–59 are sixty evenly spaced samples of normalized cycle progress.
Frame 60 is a closure check, not an extra held playback frame. These numbers
are not gameplay FPS, recording FPS, a required export rate or a one-second clip.
Interpolate continuously. Choose actual asset sampling from the native baseline.

One reference cycle corresponds to two rung spacings of actual completed travel.
Faster real travel crosses the same cycle sooner. Stopping or being blocked
freezes traversal progress. Consume each accepted motor sample once. Keep the
unwrapped cycle count so a loop boundary advances contact indices by two rungs
instead of snapping hands/feet backward to the previous cycle.

Body displacement in the tables explains the movement already made by Claude's
motor. Never apply it again as root translation, velocity or a physical target.

## 2. Coordinates, contact points and fit

Left/right always mean anatomical left/right, independent of camera and ladder
face. Use a ladder-face basis: X toward the character's right, Y upward, Z from
the rung surface outward toward the character. Rotate the whole basis for the
other face; do not mirror bone scale.

- r = observed rung pitch; k = lower-foot reference rung.
- H = fitted integer gap between k and the lower-hand rung. Choose it from the
  actual character's reach, never from the reference mannequin's proportions.
- LH/RH = left/right palm grip contacts, not necessarily wrist bone pivots.
- LF/RF = left/right supported sole/toe contacts, not ankle pivots.
- Fit hand/foot X spacing, contact offsets around each real rung, outward
  clearance, pelvis height and torso distance to the inspected character.

| Limb | Initial target | At the end of an upward cycle | At the end of a downward cycle |
|---|---|---|---|
| LH | k + H + 1 | k + H + 3 | k + H - 1 |
| RH | k + H | k + H + 2 | k + H - 2 |
| LF | k | k + 2 | k - 2 |
| RF | k + 1 | k + 3 | k - 1 |

A planted contact stays at the same world rung while the body moves. Its target
relative to the shoulder/hip therefore changes; do not keep the limb fixed in
body coordinates and let it slide through the ladder. Resolve contacts against
real visible rungs, including their centre offset, rather than assuming rung 0
is at the prefab origin. Revalidate geometry across seams and scaffold extensions.

## 3. Paired ascent candidate — main full-speed comparison

RH and opposite LF advance together while LH/RF stay planted, then LH and RF
advance while RH/LF stay planted. Two diagonal contacts support each swing.
This is a proposed game-animation gait, not a real-world ladder safety tutorial.

| Frames | Hands and arms | Feet and legs | Body movement |
|---|---|---|---|
| 0 | Both grips established; elbows softly bent | LF low, RF one rung higher; knees unlocked | Stable initial stance |
| 1–7 | RH releases, clears the rung and starts reaching up; LH remains fixed | LF unloads; knee bends and lifts the foot outward clear of the rung; RF supports | Pelvis rises with motor and shifts slightly toward supporting RF if reach allows; no extra bob |
| 8–15 | RH reaches higher with shoulder lift and controlled elbow opening; wrist faces its target | LF knee rises; toe clears the intervening rung; supporting right knee begins to extend | Torso follows upward travel while staying close enough for the planted LH grip |
| 16–23 | RH approaches new grip; elbow remains soft, no snap or hyperextension | LF approaches the higher foothold; foot orientation prepares a supported plant | Continue the measured rise; retain stable RH/LF approach and LH/RF contact |
| 24–30 | RH approaches rung k+H+2; grip completes at 30 | LF approaches k+2; supported plant completes at 30 with a bent knee | Weight transfers onto new LF at contact; at 30 body has risen one rung |
| 31–37 | LH releases and begins reaching up; RH stays planted | RF unloads and bends/lifts; LF supports | Pelvis continues rising, shifting slightly toward supporting LF |
| 38–45 | LH reaches higher; RH elbow adjusts as the body approaches its grip | RF knee lifts and clears the rung; left supporting knee extends | Torso remains aligned with the physical body, without twisting abruptly |
| 46–53 | LH approaches its new grip with a controlled wrist/elbow | RF approaches its higher foothold | Continue accepted rise and preserve RH/LF planted contacts |
| 54–60 | LH approaches k+H+3; grip completes at 60 | RF approaches k+3; plant completes at 60 | Weight settles at contact; same relative pose as frame 0, two rungs higher |

The moving foot clears outward before passing a rung. Its knee bends toward the
character's front, which is toward the ladder, only as far as geometry permits;
fit knee position and foot-clearance arc so neither penetrates the rungs. Do not
substitute a guessed Euler-angle rotation. Supporting knees extend through the
rise; supporting elbows generally flex as the torso rises toward their grips.
Actual joint bend direction comes from the real rig, not a hardcoded bone axis.

## 4. Paired descent candidate — deliberate lowering

The higher RF and higher LH move down first while LF/RH stay planted, followed
by LF/RH. This starts with the higher foot and avoids demanding a long reach
from the already-low LF before the pelvis has lowered enough.

| Frames | Hands and arms | Feet and legs | Body movement |
|---|---|---|---|
| 0 | Both grips established | Initial staggered stance | Stable starting height |
| 1–7 | LH releases and clears its rung; RH stays fixed | RF unloads and clears the rung; LF supports | Body lowers with motor; left supporting knee flexes to control the descent |
| 8–15 | LH reaches down with a controlled shoulder/elbow; RH arm lengthens as body lowers | RF travels downward toward k-1 with toe clearance | Pelvis stays close to support; modest downward look may follow the next rung without forcing camera aim |
| 16–23 | LH approaches its lower grip | RF approaches the lower foothold, preparing a supported plant | Continue actual lowering without dropping visually ahead of the capsule |
| 24–30 | LH approaches k+H-1; grip completes at 30 | RF approaches k-1; plant completes at 30 | Weight settles onto RF at contact; body has descended one rung |
| 31–37 | RH releases and clears the rung; LH remains fixed | LF unloads and clears its rung; RF supports | Right supporting knee flexes as the body continues downward |
| 38–45 | RH reaches down; planted LH arm lengthens without locking | LF lowers toward k-2 with clearance | Controlled descent, no sudden waist fold or stretched leg |
| 46–53 | RH approaches its lower grip | LF approaches its new foothold | Preserve LH/RF planted contacts and physical alignment |
| 54–60 | RH approaches k+H-2; grip completes at 60 | LF approaches k-2; plant completes at 60 | Return to initial relative stance at contact, two rungs lower |

Descent is inspected as its own animation. A negative playback multiplier on an
upward clip is not sufficient evidence. Existing contacts must remain continuous
when changing direction; do not restart frame 0 or move the physical player to
make the reference pattern fit.

## 5. Every intervening frame: interpolation and CSV meaning

For a moving limb, let u be its progress through its assigned swing interval.
The provisional easing is E(u) = 10u^3 - 15u^4 + 6u^5. Its vertical target is
start + (end - start) * E(u). Outward clearance fraction is sin(pi*u)^2.
Multiply that fraction by the separately fitted hand or foot clearance distance.
It is not a metre value. The easing and arc have zero endpoint velocity.

For the paired plan, first swings use frames 0–30 and second swings use 30–60.
For the optional three-contact plan, swings use successive 15-frame intervals.
Other limbs stay planted. Native baseline findings may justify another easing;
update both the written reference and generated samples if the plan changes.

The complete CSVs contain every frame, all four limb ordinates, clearance
fractions and planted/swing labels, for both directions:

- animation-reference/paired_cadence_candidate_frames_000_060.csv
- animation-reference/three_contact_reference_frames_000_060.csv

Hand offsets in those files add to k+H. Foot offsets add to k. Multiply the
resulting rung coordinate by r and add the measured rung reference origin and
contact offset. The body_dy_r column is +2*phase uphill or -2*phase downhill and
is explanatory only. X spacing, joint angles and torso offsets are rig-specific;
these files do not pretend to contain measured Valheim bone transforms.

## 6. Body, holds and timing checks

Begin with no gratuitous vertical pelvis bob. The body follows the existing
motor, knees provide compression and extension, shoulders follow hand reaches,
and small lateral shifts can follow the supporting foot. Fit any lean/roll to
native style and limb reach. Do not double the ascent through animated root or
hip translation. The visible body must remain aligned with the collision body.

When stopping mid-swing, freeze distance phase and keep planted contacts fixed.
A short presentation-only settle may place free limbs on reachable contacts
without moving the capsule. If necessary keep a credible three-contact hold
with the free limb naturally bent. Do not force a step completion, move the physical
player or anchor to another rung, award XP or charge stamina because the pose settled. Resume or reverse by
matching the current contacts and blending continuously, not by replaying mount.

The deliberate three-contact order is RH, LF, LH, RF going up; RF, LH, LF, RH
coming down. It is an optional controlled-speed reference, not a requirement at
full normal/fast speed. With cycle duration T=2r/abs(v), a sequential swing takes
T/4, while a paired swing takes T/2. Their average vertical swing speeds are
4*abs(v) and 2*abs(v); the provisional quintic peak speeds are 7.5*abs(v) and
3.75*abs(v), respectively. These are world contact-target rates, not measured
joint or muscle velocities.

The audited r=1/3 m and design normal/fast speeds 1.8/2.43 m/s imply cycles of
about 0.370/0.274 s. Recheck current geometry and speeds locally. Compare both
plans against native fluidity and actual full-speed footage; do not mandate a
frantic one-limb-at-a-time sequence or silently slow the motor. The selected gait,
easing and baseline findings must be recorded before implementation.

## 7. Mount and top-finish checkpoints

These numbered checkpoints use accepted transition progress, not traversal
cycle phase or a fixed duration. Intermediate poses must be authored after the
native baseline. Actual target contacts depend on the validated geometry.

| Transition sample | Ground mount | Supported top exit |
|---|---|---|
| 0 | Grounded stance; accepted mount exists | Stable upper-ladder stance; clear landing/path already validated |
| 5 | First reachable hand lifts toward grip; feet still supported | Leading knee folds to clear the edge; hands and trailing foot support |
| 10 | First hand grips; second hand reaches | Leading sole approaches the actual supported platform |
| 15 | Both grips establish; leading knee lifts toward admitted foothold | Leading foot plants; motor advances through the validated route |
| 20 | Leading foot plants; trailing foot follows | Weight moves over leading foot; trailing foot leaves rung; release hand influence as reach requires |
| 25 | Both feet settle into reachable ladder stance | Trailing foot reaches floor; hand grips release into balanced standing |
| 30 | Compatible hold or climb phase, no extra hoist | Native grounded motion resumes only after motor-confirmed completion |

Select leading side from actual pose/path rather than forcing a left or right
foot through geometry. A ground-mount hop adds no extra physical impulse beyond
the admitted motor transition. An airborne catch uses its own accepted-catch
blend; it never replays takeoff. A blocked exit remains hold/reverse/Jump/Drop
responsive. Do not use a timer or final animation sample to assert a landing.

Jump and Crouch/Drop interrupt immediately when gameplay has focus. Hammer work
keeps the anchor while releasing the required hand/upper-body influence to the
native action. Slide uses an intentional slide pose rather than a fast reversed
walking loop. Preserve all earlier gameplay, cleanup and observer requirements.

## 8. Frame comparison acceptance

For each implemented state, Codex supplies a reference-to-capture comparison:
reference frame/phase, observed body position, LH/RH/LF/RF contact or swing,
knee/elbow bend direction and any deviation. Compare normal playback first, then
slow frame stepping. A screenshot or a smooth-looking mannequin does not prove
in-game continuity. An uninvolved reviewer checks actual character views on both
ladder faces and source/installed-package identity.

The following tables list every sample of the paired plan for convenient written
cross-checking. P means planted; S means swing. Heights use the offset conventions
above. The CSVs preserve additional precision and clearance information.

## Appendix A — every ascent sample

| Frame | Body delta / r | LH | RH | LF | RF | Moving limbs |
|---:|---:|---:|---:|---:|---:|---|
| 0 | +0.000 | 1.000 P | 0.000 P | 0.000 P | 1.000 P | all planted |
| 1 | +0.033 | 1.000 P | 0.001 S | 0.001 S | 1.000 P | RH/LF |
| 2 | +0.067 | 1.000 P | 0.005 S | 0.005 S | 1.000 P | RH/LF |
| 3 | +0.100 | 1.000 P | 0.017 S | 0.017 S | 1.000 P | RH/LF |
| 4 | +0.133 | 1.000 P | 0.038 S | 0.038 S | 1.000 P | RH/LF |
| 5 | +0.167 | 1.000 P | 0.071 S | 0.071 S | 1.000 P | RH/LF |
| 6 | +0.200 | 1.000 P | 0.116 S | 0.116 S | 1.000 P | RH/LF |
| 7 | +0.233 | 1.000 P | 0.173 S | 0.173 S | 1.000 P | RH/LF |
| 8 | +0.267 | 1.000 P | 0.244 S | 0.244 S | 1.000 P | RH/LF |
| 9 | +0.300 | 1.000 P | 0.326 S | 0.326 S | 1.000 P | RH/LF |
| 10 | +0.333 | 1.000 P | 0.420 S | 0.420 S | 1.000 P | RH/LF |
| 11 | +0.367 | 1.000 P | 0.523 S | 0.523 S | 1.000 P | RH/LF |
| 12 | +0.400 | 1.000 P | 0.635 S | 0.635 S | 1.000 P | RH/LF |
| 13 | +0.433 | 1.000 P | 0.753 S | 0.753 S | 1.000 P | RH/LF |
| 14 | +0.467 | 1.000 P | 0.875 S | 0.875 S | 1.000 P | RH/LF |
| 15 | +0.500 | 1.000 P | 1.000 S | 1.000 S | 1.000 P | RH/LF |
| 16 | +0.533 | 1.000 P | 1.125 S | 1.125 S | 1.000 P | RH/LF |
| 17 | +0.567 | 1.000 P | 1.247 S | 1.247 S | 1.000 P | RH/LF |
| 18 | +0.600 | 1.000 P | 1.365 S | 1.365 S | 1.000 P | RH/LF |
| 19 | +0.633 | 1.000 P | 1.477 S | 1.477 S | 1.000 P | RH/LF |
| 20 | +0.667 | 1.000 P | 1.580 S | 1.580 S | 1.000 P | RH/LF |
| 21 | +0.700 | 1.000 P | 1.674 S | 1.674 S | 1.000 P | RH/LF |
| 22 | +0.733 | 1.000 P | 1.756 S | 1.756 S | 1.000 P | RH/LF |
| 23 | +0.767 | 1.000 P | 1.827 S | 1.827 S | 1.000 P | RH/LF |
| 24 | +0.800 | 1.000 P | 1.884 S | 1.884 S | 1.000 P | RH/LF |
| 25 | +0.833 | 1.000 P | 1.929 S | 1.929 S | 1.000 P | RH/LF |
| 26 | +0.867 | 1.000 P | 1.962 S | 1.962 S | 1.000 P | RH/LF |
| 27 | +0.900 | 1.000 P | 1.983 S | 1.983 S | 1.000 P | RH/LF |
| 28 | +0.933 | 1.000 P | 1.995 S | 1.995 S | 1.000 P | RH/LF |
| 29 | +0.967 | 1.000 P | 1.999 S | 1.999 S | 1.000 P | RH/LF |
| 30 | +1.000 | 1.000 P | 2.000 P | 2.000 P | 1.000 P | all planted |
| 31 | +1.033 | 1.001 S | 2.000 P | 2.000 P | 1.001 S | LH/RF |
| 32 | +1.067 | 1.005 S | 2.000 P | 2.000 P | 1.005 S | LH/RF |
| 33 | +1.100 | 1.017 S | 2.000 P | 2.000 P | 1.017 S | LH/RF |
| 34 | +1.133 | 1.038 S | 2.000 P | 2.000 P | 1.038 S | LH/RF |
| 35 | +1.167 | 1.071 S | 2.000 P | 2.000 P | 1.071 S | LH/RF |
| 36 | +1.200 | 1.116 S | 2.000 P | 2.000 P | 1.116 S | LH/RF |
| 37 | +1.233 | 1.173 S | 2.000 P | 2.000 P | 1.173 S | LH/RF |
| 38 | +1.267 | 1.244 S | 2.000 P | 2.000 P | 1.244 S | LH/RF |
| 39 | +1.300 | 1.326 S | 2.000 P | 2.000 P | 1.326 S | LH/RF |
| 40 | +1.333 | 1.420 S | 2.000 P | 2.000 P | 1.420 S | LH/RF |
| 41 | +1.367 | 1.523 S | 2.000 P | 2.000 P | 1.523 S | LH/RF |
| 42 | +1.400 | 1.635 S | 2.000 P | 2.000 P | 1.635 S | LH/RF |
| 43 | +1.433 | 1.753 S | 2.000 P | 2.000 P | 1.753 S | LH/RF |
| 44 | +1.467 | 1.875 S | 2.000 P | 2.000 P | 1.875 S | LH/RF |
| 45 | +1.500 | 2.000 S | 2.000 P | 2.000 P | 2.000 S | LH/RF |
| 46 | +1.533 | 2.125 S | 2.000 P | 2.000 P | 2.125 S | LH/RF |
| 47 | +1.567 | 2.247 S | 2.000 P | 2.000 P | 2.247 S | LH/RF |
| 48 | +1.600 | 2.365 S | 2.000 P | 2.000 P | 2.365 S | LH/RF |
| 49 | +1.633 | 2.477 S | 2.000 P | 2.000 P | 2.477 S | LH/RF |
| 50 | +1.667 | 2.580 S | 2.000 P | 2.000 P | 2.580 S | LH/RF |
| 51 | +1.700 | 2.674 S | 2.000 P | 2.000 P | 2.674 S | LH/RF |
| 52 | +1.733 | 2.756 S | 2.000 P | 2.000 P | 2.756 S | LH/RF |
| 53 | +1.767 | 2.827 S | 2.000 P | 2.000 P | 2.827 S | LH/RF |
| 54 | +1.800 | 2.884 S | 2.000 P | 2.000 P | 2.884 S | LH/RF |
| 55 | +1.833 | 2.929 S | 2.000 P | 2.000 P | 2.929 S | LH/RF |
| 56 | +1.867 | 2.962 S | 2.000 P | 2.000 P | 2.962 S | LH/RF |
| 57 | +1.900 | 2.983 S | 2.000 P | 2.000 P | 2.983 S | LH/RF |
| 58 | +1.933 | 2.995 S | 2.000 P | 2.000 P | 2.995 S | LH/RF |
| 59 | +1.967 | 2.999 S | 2.000 P | 2.000 P | 2.999 S | LH/RF |
| 60 | +2.000 | 3.000 P | 2.000 P | 2.000 P | 3.000 P | all planted |

## Appendix B — every descent sample

| Frame | Body delta / r | LH | RH | LF | RF | Moving limbs |
|---:|---:|---:|---:|---:|---:|---|
| 0 | +0.000 | 1.000 P | 0.000 P | 0.000 P | 1.000 P | all planted |
| 1 | -0.033 | 0.999 S | 0.000 P | 0.000 P | 0.999 S | LH/RF |
| 2 | -0.067 | 0.995 S | 0.000 P | 0.000 P | 0.995 S | LH/RF |
| 3 | -0.100 | 0.983 S | 0.000 P | 0.000 P | 0.983 S | LH/RF |
| 4 | -0.133 | 0.962 S | 0.000 P | 0.000 P | 0.962 S | LH/RF |
| 5 | -0.167 | 0.929 S | 0.000 P | 0.000 P | 0.929 S | LH/RF |
| 6 | -0.200 | 0.884 S | 0.000 P | 0.000 P | 0.884 S | LH/RF |
| 7 | -0.233 | 0.827 S | 0.000 P | 0.000 P | 0.827 S | LH/RF |
| 8 | -0.267 | 0.756 S | 0.000 P | 0.000 P | 0.756 S | LH/RF |
| 9 | -0.300 | 0.674 S | 0.000 P | 0.000 P | 0.674 S | LH/RF |
| 10 | -0.333 | 0.580 S | 0.000 P | 0.000 P | 0.580 S | LH/RF |
| 11 | -0.367 | 0.477 S | 0.000 P | 0.000 P | 0.477 S | LH/RF |
| 12 | -0.400 | 0.365 S | 0.000 P | 0.000 P | 0.365 S | LH/RF |
| 13 | -0.433 | 0.247 S | 0.000 P | 0.000 P | 0.247 S | LH/RF |
| 14 | -0.467 | 0.125 S | 0.000 P | 0.000 P | 0.125 S | LH/RF |
| 15 | -0.500 | 0.000 S | 0.000 P | 0.000 P | 0.000 S | LH/RF |
| 16 | -0.533 | -0.125 S | 0.000 P | 0.000 P | -0.125 S | LH/RF |
| 17 | -0.567 | -0.247 S | 0.000 P | 0.000 P | -0.247 S | LH/RF |
| 18 | -0.600 | -0.365 S | 0.000 P | 0.000 P | -0.365 S | LH/RF |
| 19 | -0.633 | -0.477 S | 0.000 P | 0.000 P | -0.477 S | LH/RF |
| 20 | -0.667 | -0.580 S | 0.000 P | 0.000 P | -0.580 S | LH/RF |
| 21 | -0.700 | -0.674 S | 0.000 P | 0.000 P | -0.674 S | LH/RF |
| 22 | -0.733 | -0.756 S | 0.000 P | 0.000 P | -0.756 S | LH/RF |
| 23 | -0.767 | -0.827 S | 0.000 P | 0.000 P | -0.827 S | LH/RF |
| 24 | -0.800 | -0.884 S | 0.000 P | 0.000 P | -0.884 S | LH/RF |
| 25 | -0.833 | -0.929 S | 0.000 P | 0.000 P | -0.929 S | LH/RF |
| 26 | -0.867 | -0.962 S | 0.000 P | 0.000 P | -0.962 S | LH/RF |
| 27 | -0.900 | -0.983 S | 0.000 P | 0.000 P | -0.983 S | LH/RF |
| 28 | -0.933 | -0.995 S | 0.000 P | 0.000 P | -0.995 S | LH/RF |
| 29 | -0.967 | -0.999 S | 0.000 P | 0.000 P | -0.999 S | LH/RF |
| 30 | -1.000 | -1.000 P | 0.000 P | 0.000 P | -1.000 P | all planted |
| 31 | -1.033 | -1.000 P | -0.001 S | -0.001 S | -1.000 P | RH/LF |
| 32 | -1.067 | -1.000 P | -0.005 S | -0.005 S | -1.000 P | RH/LF |
| 33 | -1.100 | -1.000 P | -0.017 S | -0.017 S | -1.000 P | RH/LF |
| 34 | -1.133 | -1.000 P | -0.038 S | -0.038 S | -1.000 P | RH/LF |
| 35 | -1.167 | -1.000 P | -0.071 S | -0.071 S | -1.000 P | RH/LF |
| 36 | -1.200 | -1.000 P | -0.116 S | -0.116 S | -1.000 P | RH/LF |
| 37 | -1.233 | -1.000 P | -0.173 S | -0.173 S | -1.000 P | RH/LF |
| 38 | -1.267 | -1.000 P | -0.244 S | -0.244 S | -1.000 P | RH/LF |
| 39 | -1.300 | -1.000 P | -0.326 S | -0.326 S | -1.000 P | RH/LF |
| 40 | -1.333 | -1.000 P | -0.420 S | -0.420 S | -1.000 P | RH/LF |
| 41 | -1.367 | -1.000 P | -0.523 S | -0.523 S | -1.000 P | RH/LF |
| 42 | -1.400 | -1.000 P | -0.635 S | -0.635 S | -1.000 P | RH/LF |
| 43 | -1.433 | -1.000 P | -0.753 S | -0.753 S | -1.000 P | RH/LF |
| 44 | -1.467 | -1.000 P | -0.875 S | -0.875 S | -1.000 P | RH/LF |
| 45 | -1.500 | -1.000 P | -1.000 S | -1.000 S | -1.000 P | RH/LF |
| 46 | -1.533 | -1.000 P | -1.125 S | -1.125 S | -1.000 P | RH/LF |
| 47 | -1.567 | -1.000 P | -1.247 S | -1.247 S | -1.000 P | RH/LF |
| 48 | -1.600 | -1.000 P | -1.365 S | -1.365 S | -1.000 P | RH/LF |
| 49 | -1.633 | -1.000 P | -1.477 S | -1.477 S | -1.000 P | RH/LF |
| 50 | -1.667 | -1.000 P | -1.580 S | -1.580 S | -1.000 P | RH/LF |
| 51 | -1.700 | -1.000 P | -1.674 S | -1.674 S | -1.000 P | RH/LF |
| 52 | -1.733 | -1.000 P | -1.756 S | -1.756 S | -1.000 P | RH/LF |
| 53 | -1.767 | -1.000 P | -1.827 S | -1.827 S | -1.000 P | RH/LF |
| 54 | -1.800 | -1.000 P | -1.884 S | -1.884 S | -1.000 P | RH/LF |
| 55 | -1.833 | -1.000 P | -1.929 S | -1.929 S | -1.000 P | RH/LF |
| 56 | -1.867 | -1.000 P | -1.962 S | -1.962 S | -1.000 P | RH/LF |
| 57 | -1.900 | -1.000 P | -1.983 S | -1.983 S | -1.000 P | RH/LF |
| 58 | -1.933 | -1.000 P | -1.995 S | -1.995 S | -1.000 P | RH/LF |
| 59 | -1.967 | -1.000 P | -1.999 S | -1.999 S | -1.000 P | RH/LF |
| 60 | -2.000 | -1.000 P | -2.000 P | -2.000 P | -1.000 P | all planted |
