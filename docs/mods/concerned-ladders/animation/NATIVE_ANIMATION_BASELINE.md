# Native animation baseline — local analysis, 2026-10-26

**Status: BASELINE ACCEPTED FOR AUTHORING WITH DOCUMENTED LIMITATIONS.**
The owner explicitly authorized independent frame-and-timing review for this pre-authoring gate. The uninvolved reviewer accepted the corrected record on that basis. Normal-speed fluidity approval remains required for the exact integrated candidate. No continuous playback observation is claimed. This is Codex's analysis of Claude's actual capture set, not an adoption of every claim in the lead draft. See BASELINE_REVIEW_2026_10_26.md.

## Identity and evidence

Runtime build source: `441ea830785a27920f4bad11c01f877ed94b7d28`, Concerned Ladders 0.2.1.
Lead docs source: `ec72119ad52de343eccdc709e31ed984fdcd28c7`.
Lead receipt reports installed own DLL SHA256 `287b825ad7cc97f94bfe95c47feccc3096627d616a657c148c29591013110f19`
and package SHA256 `01e3a766a104e4a680182891a7cc20c52375e29f3f8fada689b208ff015e7837`.
These identify the native-analysis environment; they are not an original-animation candidate or a compiler attestation by this lane.

Game/engine observed in the retained dump: Valheim 1.0.20, Unity 6000.0.75f1.
Profile: TCC-Ladders-Dev; disposable LADDERTEST / TCCLadderTest, per lead.
Capture hardware/setup: RTX 4080, 2560x1440 display, 1600x904 game client, vSync1, targetFrameRate -1.

Primary local evidence directory:
`C:\code\cc-integration\cl-ladders-review-evidence\native-baseline`.
Evidence read for this report is identified in `native_baseline_evidence_manifest.csv`. It fingerprints local files at review time; later edits require a new review. Media remain local and are not committed.
Native dump read at SHA256 `5f8b70d2ea2c074706f6861282058098090d736f891fd2270072ed5f3477cc70`.
Swim trace read at SHA256 `5ac2bfafc884491be6b11ee33939e23095f272ab29d407a198b6a431ebf078a8`.

Claude's session2/session3 isolation receipts place removal of the rejected `.pose` owner before native captures; session3 explicitly removes it again after `ladder_now_ref`. The reference take is excluded from native measurements. Session-one swim has weaker retained harness/isolation provenance and is supplementary. An empty patch inventory before a later re-enabled hook would not suffice by itself.

Probe source is readable at `C:\code\cc-integration\cl-ladders-devtools\probe\Probe.cs`; the lead owns its builds and game operation. Its LateUpdate samples are current evaluated poses, not a guaranteed final post-every-writer pose. Raw curves/game clips/rig assets are not exported. Our optional read-only probe and analyzers live in `tools/animation`; they do not run in or ship with the product.

## Observed rig/controller metadata

| Item | Actual dump observation |
|---|---|
| Selected rig | One Animator on Visual; Player_animator, plain RuntimeAnimatorController |
| Avatar | player_maleAvatar, isHuman/isValid true; 53 mapped human bones; humanScale1.1954 |
| Evaluation | Fixed; CullUpdateTransforms; applyRootMotion false; hasRootMotion false |
| Layers | Base Layer and upperbody; both weights1 at dump |
| Physics | fixedDeltaTime about.02s, Rigidbody Interpolate, capsule height1.85/radius.49, capsule scale1 |
| Imported bone scale | Mapped bone lossyScale approximately95; preserve it, do not assume unit-scaled joints |
| Reference data | humanDescription reports53 human /69 skeleton entries. Imported reference transforms/body COM/sole-palm offsets are unavailable and requested for later binding; current localEuler values are evaluated pose, not rest/T-pose. |
| Controller inventory | 755 references,434 distinct clips; 423 have frameRate30,10 have24,1 has29.97 |
| Native clips | Non-legacy, humanMotion. No climbing clip was identified by the inspected names; names alone do not prove every asset's semantic content. |

Derived from evaluated swim pivots, upper/lower arm segments are about .318/.260 root-local units and leg segments .450/.446. These are bone-pivot distances, not palm/sole contact offsets, a T-pose, or an authored contact fit. Runtime binding must remeasure the current actor in world space and fail cleanly on missing/incompatible bones.

## Keep four rates separate

1. **Source sampling metadata:** representative active idle/locomotion/jump/swim/hammer clips have frameRate30. This does not establish30 actual keys/s, unique key counts or runtime FPS. length×frameRate is arithmetic, not a key count.
2. **Effective playback:** measured normalized-state change and blend participation. A1s Walk New asset appears in a state taking about1.42s per normalized cycle; a4.5333s Swimming asset participates in a much faster blend. State/clip names, lengths and rates are distinct.
3. **CPU render-update observations:** our first-to-last sample calculations yield119.69..120.03 callbacks/s for idle/walk/jog/run/swim, with no missing frame indices in those traces. Median dt.0083s. This is instrumented LateUpdate cadence, not independently measured GPU/display presentation.
4. **Recording:** ddagrab requested60FPS; encoded/decoded timestamps and ffmpeg dup/drop statistics belong to the recorder. CAPTURE_LOG reports achieved rates around58..59.7. Requested60 is not proof of60 unique presented frames.

Actual Fixed metadata plus repeated pose/state samples support roughly50Hz skeleton changes in these sessions over an interpolated root. They do not prove that all future pose writers/actions universally change only then. Trace values are rounded, and initial physics-step counters need care.

Our swim trace has961 rows/7.998s,120.0300 callbacks/s, and400 base-state-time changes (50.0125/s). Authored sampling30, pose-change cadence50 and recording request60 coexist; none substitutes for another.

## Effective playback and body observations

Clip names below identify observed participating clips, not readable controller-state names inferred from hashes. Rates are approximate, segment-specific observations. Claude's CSV uses dominant-clip intervals that can include blends, acceleration or an airborne edge; they are not all steady grounded experiments.

| Action | Observed clips / source duration / sampling | Playback and body observation |
|---|---|---|
| Idle | IdleTweaked1.9333s/30 | normalized state about.516 cycles/s; hipsY range.0008 in our trace, gentle hand/body movement |
| Walk | Walk New1s/30 plus native blend-tree participants | state cycle about1.42s in selected forward portions; alternating arms/legs, supporting leg extends and opposite knee recovers; hipsY range about.083 |
| Jog | Jog New/Jog New Fast(.8667s)/30 and blends | selected fast-jog state about.86..87s; stronger knee/heel recovery; hipsY range about.164 |
| Run | Run New.6667s/30 and blends | useful side segment about.664s/cycle; larger bent-arm and leg excursions, forward torso lean; a behind take includes an airborne edge and heading change |
| Crouch | Crouch Idle1.6667s; Sneak walk.903s/30 | actual lower pelvis and forward torso, flexed knees; total crouch/stand range includes about.36 of hipsY change, not periodic bob |
| Jump | **JumpTweaked.8667s/30** actually plays | reported state multiplier product.8 and about1.08..1.10s per normalized-time unit; selected state residence is about.85..87s before landing interrupts; asymmetric arms/legs during flight and standing recovery; inventory Jump Start/Loop/End names alone do not identify the played jump |
| Swim | Treading Water3s; Swimming4.5333s/30 | trace state rates differ with blending (~.332 normalized units/s treading, ~.547 over our broad swimming interval); bilateral reach/recovery and knee flexion visible through water |
| Hammer idle/movement | IdleTweaked and one-handed Sword Right variants | native equipment changes locomotion blend participation; do not replace or hide equipment |
| Hammer place/remove | hammer_Swing.5s/30, upperbody layer | state product1.5 and about.333..347s swing; near-static pelvis, substantial right arm plus meaningful left arm/chest movement |
| Hammer repair | Installed `Player.Repair` independently inspected; same equipped-tool attack trigger expression as place/remove | Captured hammer motion is the representative. Repair invocation itself is untested; source equivalence is not an in-game repair or control test. See REPAIR_NATIVE_PATH_INSPECTION.md. |

Numbers are root-relative evaluated-pivot observations. They are not contact dimensions, joint limits or precise sole slip. Native fore-aft foot motion is not a reliable touchdown/plant detector. Water/grass/camera obstruction further limit contact claims.

## Frame-to-observation ledger

Method: timestamped frame decoding/stepping plus traces. No claim of normal-speed perceptual fluidity review is made from a contact sheet alone. Original videos retain their timing; analysis crops do not replace them.

| Evidence / video time | LH/RH, LF/RF and body observation | Limit |
|---|---|---|
| side_walk2.00..3.25 | Opposed arm/leg alternation; supporting knee extends, swinging knee folds; pelvis remains upright with modest bob, torso follows stance | Some sole contact hidden by grass; not a no-slip proof |
| side_jog2.00..2.75 | Bent elbows, alternating leg recovery and stronger vertical compression | Terrain/grass hide precise soles |
| side_run2.00..2.36 | Larger leg excursion and forward torso, bent alternating arms | Tree/camera clips later2.36..2.60; exclude that interval for full-body assessment |
| side_crouch2.50..3.75 | Low pelvis/forward torso; knees stay bent; hands offset front/back while legs alternate | Sole visibility partial |
| side_jump_run2.10..2.40 | One knee lifts and opposing arm rises while trailing leg extends | Later camera clipping hides knees/feet; alternate jump_stand4.10..5.10 and jump_run2.70..3.70 are needed for recovery |
| swim3.00..3.60 | Broad arm/leg extension changes to bent-knee recovery; body leans into aquatic travel | Water distorts exact contacts; no grounded contact exists |
| hammer_place2.70..3.20 and8.55..9.05 | Hammer action visible while pelvis stays nearly still; left arm/chest cannot be treated as fixed | Building UI/grass obscure feet |
| hammer_remove1.60..2.10 | Native swing and removal visible | Native method invocation is animation evidence, not a keyboard binding test |
| idle1.00..5.50 | Stable standing silhouette and small arm/body changes | Subtle amplitude needs trace support |

The uninvolved reviewer independently decoded these views and returned open findings in the shared CODEX_NATIVE_BASELINE_REVIEW_OPEN record. The uninvolved reviewer accepted the corrected report/CSV and repair inspection under the owner-authorized frame-and-timing review. No continuous normal-speed observation is claimed.

## Blending / action ownership

Our transition-flag runs (first-to-last observed samples, so uncertain by a frame at each boundary):
- Jump entry about.104..115s; landing exit about.148s.
- Crouch entry about.208s; stand recovery about.249s.
- Hammer entry about.110..114s; exit about.190..198s.
These observed spans are not the controller's authored duration/unit. Continuous locomotion weight changes can occur within a blend tree with no IsInTransition flag.

Hammer is **not right-arm-only**. In place trace8.12..8.45, the uninvolved reviewer recomputed left-hand XYZ excursions.1720/.0665/.1038 and chestZ.0203. Remove trace1.24..1.57 has.1730/.0772/.1003 left-hand excursions. HipsY .0003/.0004 rounds to.000 but is not literal zero.

The proposed producer must release the native action's required arms/upper body; conservatively both arms and torso until the exact mask is validated. Keeping left grip pinned because the hammer is held in the right hand can conflict with native action. Legs/support and the physical anchor remain motor/presentation responsibilities, never action authority.

## Proposed authoring choice for baseline review — not implemented

- Select original procedural contact/body curves in the native humanoid IK path as the first available route. Actual valid Avatar/IK hooks are observed; no compatible local editor pipeline is established. This is an original-motion route, not walking or a claim that clips are impossible. Runtime non-legacy SetCurve remains unsuitable; no Meshy export/purchase or extracted game animation is used.
- Keep authored functions continuous in distance/accepted transition progress. Initial evaluation follows the native Fixed pass; no pose quantization to reference rows. If original clips are later baked,30Hz is an evidence-supported starting source sampling rate, not a mandatory key count or proof that denser authoring is useless.
- Begin with the paired two-rung reference as the initial contact-fit candidate (conservative only in reach/step size, not in cadence or fluidity). At1.8/2.43m/s it has .3704/.2743s cycles and .1852/.1372s paired swings. The deliberate four-beat candidate has only .0926/.0686s per swing and twice the provisional peak target rate. Compare both on the actual rig after the gate, rather than forcing a frantic four-beat sequence.
- The paired plan's5.4/7.29 pair-events/s is above native run's roughly3 steps/s. That is a **visual risk**, not a reason to silently slow the motor. A three-/four-rung stride cannot be justified solely by native forward foot excursion: held-hand reach is constrained by arm length, capsule lane distance and chest pose. Actual fit/prototype decides whether a longer stride is reachable without stretch/body drift.
- Initial quintic reference easing gives zero endpoint velocity/acceleration; keep it provisional until actual fast-climb footage. More samples cannot fix bad cadence or contacts. Maintain unwrapped rung indices and planted world contacts across stops/reversal/seams/extensions.
- Begin without added vertical pelvis bob; motor travel already provides vertical progress. Small fitted lateral/torso adjustments may support reach. Do not copy native walk/jog bob directly into another root rise.
- Use native-scale short entry/recovery blends (roughly.12..20s) as a starting presentation envelope, subordinate to accepted mount/catch/exit progress. No timer declares a landing. Jump/Drop handoff remains immediate and releases pose ownership synchronously; native hammer ownership wins immediately.

These choices are a proposal for independent baseline review, not a finished gait or an authored replacement. All first-loop and complete-motion visual fitting remains ahead.

## Remaining limitations / gate closure

1. The owner explicitly changed the pre-authoring section3 requirement to allow independent frame-and-timing review. That review passed. Available tools did not supply continuous perceptual observation, and no such observation is claimed. Normal-speed fluidity approval remains mandatory for the exact integrated candidate.
2. Corrected report/CSV/repair inspection and all66 fingerprinted inputs were independently checked with no mismatches. The review approves beginning original authoring, not a finished build.
3. Imported reference axes/body COM/native foot settings and fitted palm/sole offsets remain later binding/contact-fitting checks, not invented prerequisites to this timing baseline. Repair invocation remains a later actual-game test even when representative native motion is source-substantiated.
4. No raw curves/editor import settings inspected. This is acceptable if the remaining metadata/playback is sufficient and the limitation stays explicit.
5. Only male avatar observed; other avatars, equipment variations, lifecycle, scaffold and real observers remain later exact-candidate gates.

No original animation, observer transport or movement controller is implemented by this report.
