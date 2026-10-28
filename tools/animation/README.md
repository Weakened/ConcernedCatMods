# Native baseline tooling and lead integration

These files are preparation for the mandatory native analysis, not climbing animation. Nothing auto-installs or runs on the player's machine.

Claude's existing dev harness can provide the baseline; it does not need to adopt the optional C# probe. Do not add this diagnostic to the release DLL or package. Licensed builds and all game/profile actions remain Claude's.

## Optional C# read-only probe

`tools/animation/NativeAnimationBaselineProbe.cs` is source-only dev tooling. It is deliberately outside the product's Compile glob. Integrate into a dev harness only if useful, with the actual game/Harmony references. It is not semantically compiled in this lane; no fake engine stubs are supplied.

- Select the real local owning Player and its actual Animator, cross-checking CharacterAnimEvent/ZSyncAnimation. It refuses known own .pose, .presentation and original-ik-cleanup hooks and stops if actor/controller/avatar/ownership changes.
- Begin requires action, game/source identity claims and a reference to actual native-isolation evidence. Caller claims never attest source provenance or installed hashes.
- Call SampleLateFrame once from an explicitly documented LateUpdate hook. It observes that callback's current pose, not a guaranteed final rendered pose.
- Optional SampleNativeIK calls occur only inside read-only native OnAnimatorIK prefix/postfix hooks. Humanoid bodyPosition/bodyRotation getters also belong only here. Do not call these or IK getters from arbitrary Update/LateUpdate. Label native pre/post order and list all remaining patches.
- ObserveNativeTrigger may receive an observed native tool trigger; it never sends one.
- Finish flushes buffered JSONL through the lead's local evidence sink. No file output occurs during measurement.
- Bounds: 20 seconds, 3600 frames, 16M UTF-16 characters, 16 layers, 512 distinct clips, 2048 clip references/overrides and 256 parameters. The inventory includes all mapped bones; per-frame pose includes the body/limbs through RightToes and UpperChest, excluding fingers/face to bound capture cost. Combined pose/IK captures can still hit the buffer first; inspect end.reason and duration. Source caps do not establish capture completeness.
- Read timing excludes serialization/GC and output. Record cadence as instrumented and compare uninstrumented footage where appropriate.

Never use Rebind, SampleAnimation, forced Animator state or bone/controller setters to manufacture native metadata. Normal playback is required.

## JSONL measurement analyzer

```powershell
python tools/animation/analyze_native_baseline.py <lead-probe.jsonl> --video-metadata <lead-ffprobe.json> --csv <derived-clips.csv>
python -m unittest discover -s tools/animation -p "test_*.py" -v
```

The video JSON, when used, comes from the lead's separate read-only ffprobe stream metadata. The analyzer keeps authored sampling, state normalized-time inference, CPU update observations and container frame-rate facts separate. It rejects duplicate/reversed frames, non-finite values, missing inventory identities and incomplete records. Its status is always measurements-only; it cannot complete the baseline or prove visual quality.

Synthetic tests exercise these interpretation/refusal boundaries. They are not game captures or engine tests. Claude's CSV trace format may be analyzed directly/adapted; do not force a new game build solely to match this optional schema.

After live evidence, complete the product-local animation baseline documents and have an uninvolved reviewer check them. Replacement authoring remains gated until then.

Claude CSV trace analysis:

```powershell
python tools/animation/analyze_harness_trace.py <animtrace_action.csv>
```

This reports name-based state-time inference and evaluated limb-pivot ranges. Duplicate clip names remain ambiguous; no source sampling rate, rest pose or world contact is manufactured from the trace.
