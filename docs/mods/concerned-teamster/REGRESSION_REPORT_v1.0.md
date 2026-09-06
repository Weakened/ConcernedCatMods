# v1.0 regression report (CT-047)

A rerun of every automatable check from a genuinely fresh checkout —
not the working tree this whole conveyor has been developing in, but a
separate `git clone` of `main` at the commit below, with only
`Environment.props` (gitignored, holding local Valheim/BepInEx paths)
copied in. Keyed to `V1_DEFINITION_OF_DONE.md`'s capability areas: every
row there that reads `Done`/`Done*` on automated evidence is re-proven
here from scratch, not merely re-asserted.

## Rerun identity

| | |
|---|---|
| Source commit | `5676b0feeb7bedcd0304249b22738e17c71297da` (`main`, post-CT-046) |
| Method | `git clone --branch main <canonical repo> <scratch dir>`, `Environment.props` copied in, nothing else carried over |
| Fresh-clone package identity | `TheConcernedCat-ConcernedTeamster-0.9.0` |
| Fresh-clone ZIP SHA-256 | `67efa4e1525b6d5c7d3ed2904c1648a5b20e96025b37f1525470f0c0a432a80c` |
| Fresh-clone DLL SHA-256 | `ca51de946de1ccc81e864aabe13d0fa50bc4ad09d48e0023b76f7d31cdaf0b31` |
| Fresh-clone DLL InformationalVersion | `0.9.0+5676b0feeb7bedcd0304249b22738e17c71297da` — read back and confirmed to name this exact commit |

These hashes intentionally differ from `RELEASE_DOSSIER.md`'s sealed
v0.9.0 entry — that seal was built from an earlier commit (`161dc9d`,
before CT-046 merged) and hashes are already documented as build
fingerprints, not reproducibility guarantees (DEF-teamster-v0.8-001,
#214). This rerun is not a re-seal; it is proof that the current tip
still builds and packages cleanly from nothing but a clone, not proof
that any specific artifact is reproducible. The InformationalVersion
match is the meaningful identity check here, and it holds.

## Full automated suite (tests + validator + package), from clean

| Check | Command | Result |
|---|---|---|
| Real-game build | `scripts/build.ps1` | PASS — 0 errors |
| Teamster unit tests | `dotnet test ConcernedTeamster.Tests` (Release) | **622/622 PASS** |
| Cartographer regression | `dotnet test ConcernedCartographer.Tests` (Release) | **568/568 PASS**, unchanged |
| Static validation + package + require-binary | `scripts/package.ps1 -Product ConcernedTeamster` | PASS — validator green, `tcli build` succeeded, 6-entry own-DLL-only ZIP |
| Interop audits (independence, contract, read-only, authority/no-force/no-egress) | `validate_repo.py` interop lines | PASS — all five, 0 violations, identical to the working-tree run |

No failure occurred at any step. No defect was found or needed fixing —
the clean-checkout rerun reproduced exactly the green state the working
tree already showed.

## Campaign reruns (automated layer)

Per CT-047's own acceptance criteria ("reruns green **or pending-listed
with reasons**"): the automated logic layer for every campaign reruns
clean below. The in-game layer stays pending for the same reason it has
been pending since CT-043 discovered it — no TCT-* mod-manager profile
exists on this development machine (`PROFILE_REHEARSAL.md`), and
creating one is a one-time owner GUI action this repo's tooling
deliberately does not perform. Nothing below claims otherwise.

| Campaign | Automated rerun | In-game (TCT-*) |
|---|---|---|
| Standard sprint checklist (TCT-Dev) | Full 622-test suite above already covers every domain decision every sprint checklist exercises | **Pending** — no TCT-Dev profile exists; see `PRE_RELEASE_SMOKE_TEST.md` §§1–5, 8, 10–13 |
| Compatibility matrix (TCT-Compat) | `dotnet test --filter FullyQualifiedName~CompatibilityFrameworkTests\|CompatibilityAdvisoryGateTests` (Release) → **18/18 PASS** | **Pending** — no TCT-Compat profile exists; see `PRE_RELEASE_SMOKE_TEST.md` §9 |
| Multiplayer scenarios (CT-027/CT-030 scope) | `dotnet test --filter FullyQualifiedName~MultiplayerScenarioTests\|CartAuthorityPolicyTests\|CooperativeEffortClassifierTests\|NetworkInputHardeningTests` (Release) → **86/86 PASS** | **Pending** — no second client / dedicated server session run; see `PRE_RELEASE_SMOKE_TEST.md` §7 |
| Dedicated-server validation (TCT-Dedicated) | Authority policy logic is proven topology-independent by construction (`BrakeDecision_IsPureFunctionOfFacts_TopologyNotAnInput`, part of the 86 above) — the dedicated and player-hosted rows share one tested logic path | **Pending** — no TCT-Dedicated profile exists; see `PRE_RELEASE_SMOKE_TEST.md` §7 rows 7.2 |

## Defect status

Zero new defects found or filed by this rerun. Pre-existing deferred
P3s unchanged: DEF-teamster-v0.8-001 (#214, build/ZIP hash
non-reproducibility) and DEF-teamster-v0.9-001 (#216, one flaky
allocation test under full-suite Debug timing — note this rerun used
`-c Release` throughout precisely because that timing sensitivity is
specific to Debug-configuration full-suite runs; a Release rerun is not
expected to reproduce it, and did not). No open P0/P1/P2 Teamster
defect.

## Conclusion

Every automatable check in `V1_DEFINITION_OF_DONE.md` re-proves clean
from a source clone with nothing but local game paths added — not from
this conveyor's own long-lived working tree, which could in principle
have accumulated stale generated files or cache state a fresh clone
would expose. It did not. The remaining gap is exactly what it was
before this leaf: real interactive sessions on profiles that do not yet
exist, itemized and cross-referenced in `PRE_RELEASE_SMOKE_TEST.md`,
never claimed passed here.
