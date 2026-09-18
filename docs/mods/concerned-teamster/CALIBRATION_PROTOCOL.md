# Cart load calibration protocol (CT-008)

This protocol turns vanilla cart behavior into versioned calibration data
for the LoadModel. **Only rows produced by this protocol may carry
`basis=Measured`.** Until measured rows exist, the data file ships priors
and verified-constant derivations, clearly labeled, and the model answers
"uncertain" in the uncalibrated middle instead of faking precision.

## Fixed cargo sets

Weights use the game's own item weights (stone 2.0, wood 2.0 — quality
scaling does not apply to materials). The installed Valheim 1.0.14
`Cart.prefab` serializes `m_baseMass = 50` and
`m_itemWeightMassFactor = 0.1` (bundle audit, Steam build 25364265;
see CART_INTERNALS.md). The C# field initializers are 20 and 1.0, but the
prefab overrides them before a live cart exists. Therefore **total cart
mass = 50 + cargo weight × 0.1** for the vanilla cart.

| Set | Contents | Cargo weight | Total cart mass |
|---|---|---|---|
| A (empty) | nothing | 0 | 50 |
| B (light) | 25 stone | 50 | 55 |
| C (working) | 50 stone + 50 wood | 200 | 70 |
| D (heavy) | 150 stone | 300 | 80 |
| E (max ore run) | 300 stone (6 full slots + 6 more) | 600 | 110 |

## Measured grades

Build three straight 25 m hoe-leveled dirt test ramps in a disposable
world (`TCT_Mod_Test`), verified with the Cart Status panel's grade
readout (CT-004) sampled at three points along each ramp:

| Ramp | Target grade | Acceptable band |
|---|---|---|
| Flat | 0% | -1% .. +1% |
| Moderate | 10% | 8% .. 12% |
| Steep | 25% | 22% .. 28% |

## Procedure (per set × ramp)

1. Profile: `TCT-Clean` first for feel baseline, then `TCT-Dev` for the
   recorded run (panel provides mass/grade/speed evidence).
2. Rested, fed character (document the three foods); full stamina.
3. Attach the cart at the ramp base, pull straight up at run speed until
   the top or a stall.
4. Record: sustained speed band from the panel (m/s), stall (yes/no and
   where), stamina at the top, any joint break, and the panel's grade
   reading.
5. Two repetitions minimum; a third if the first two disagree.
6. Append one row per outcome to `src/ConcernedTeamster/Data/
   CartLoadCalibration.txt` with `basis=Measured`, the date, and the game
   version; bump the file's `data-version`.

## Outcome vocabulary

- `Climbs` — sustained forward progress to the top without stalling.
- `Marginal` — reached the top but with stalls/creep or near-zero stamina.
- `Stalls` — could not sustain progress.
- `JointBreak` — the attach joint snapped (force exceeded `m_breakForce`).

## Descent runs (CT-011)

Descent calibration uses the same cargo sets and ramps, walked DOWN, plus
an entry-speed dimension. Rows go to
`src/ConcernedTeamster/Data/CartDescentCalibration.txt`.

Procedure (per set × ramp × entry speed):

1. Attach at the ramp top. Entry speeds: **stand** (0 m/s — controlled
   release from stillness), **walk**, **run** (record the panel's speed
   readout at the crest as the row's speed value).
2. Descend the full ramp trying to stay in control (no sprint, no jumping).
3. Record the outcome:
   - `Held` — controlled the whole way; stopping mid-slope was possible.
   - `Dragged` — the cart accelerated beyond control but the joint held
     and control returned at the bottom.
   - `Runaway` — detached/uncontrollable descent (joint broke from pull or
     the cart forced a detach), or the cart left on its own.
   - `JointBreak` — the attach joint snapped during the descent.
4. Two repetitions minimum; a third on disagreement. Append rows with
   `basis=Measured`, date, game version; bump `data-version`.

## What is calibrated today

No measured rows exist yet (in-game runs are owner/manual work; the TCT
profiles arrive with CT-043). The shipped file contains:

- `Prior` rows: flat-ground pullability for sets A-C — vanilla's design
  intent (carts are routinely pulled on flat ground); labeled as priors,
  awaiting protocol confirmation.
- `DerivedConstant` rows: physical impossibility bounds computed from
  installed-game constants. The current `Cart.prefab` serializes joint break
  force **100000**, base mass **50** and cargo factor **0.1**. The embedded
  bounds were revised with those values in data version 2. These are certain
  *upper* bounds, not playability claims.

Everything between the priors and the impossibility bounds is **unknown
by design** until measured. The LoadModel says so explicitly.
