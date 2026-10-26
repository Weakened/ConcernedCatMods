# Native animation measurements and visual analysis

Status: PENDING_LEAD_CAPTURE. See [mandatory baseline gate](VANILLA_ANIMATION_BASELINE_2026_10_26.md).

No numeric native clip sampling rate, live rig dimension, rest pose, contact cadence or blend duration has been invented. This file is completed from the lead-delivered immutable dump, traces and actual gameplay footage; API availability and reference formulas are recorded separately in the gate document.

## Evidence ledger

| Item | Path / hash / source | State |
|---|---|---|
| Native isolation and plugin/patch inventory | Lead native-baseline capture directory | PENDING |
| Actual rig/controller/clip metadata | Lead animdump.txt | PENDING |
| Native per-frame traces | Lead animtrace_<action>.csv | PENDING |
| Native footage and recorder timing | Lead <action>.mp4 / ffmpeg log / CAPTURE_LOG.md | PENDING |
| Final approach and uninvolved review | After evidence analysis | PENDING |

## Rate terminology

1. Authored/imported sampling: AnimationClip.frameRate metadata, not exact key count.
2. Effective playback: actual state/clip change over recorded time, accounting for Animator and state multipliers/blends.
3. Render observation: CPU frame/update cadence from trace; presented/GPU frame rate is a separate fact if available.
4. Capture rate: recorder request and encoded/decoded timestamps, including reported drops/duplicates; neither proves authored sampling or game rendering.

## Required action observations

The CSV starts BLOCKED for each action. For each delivered capture, record names, measured rates, contacts, body movement, blend/interrupt behavior and source/trace/video timestamps. Inspect normal-speed motion before frame stepping. Current evaluated bone positions are not a rest/T-pose. A clip name does not prove a readable controller-state name.

## Authoring choice

NOT FINALIZED: sampling/export density, gait, easing, limb offsets, body amplitude and blends await the mandatory observations and independent check. Numbered reference rows are normalized distance phases, never a required FPS. No replacement implementation has started.
