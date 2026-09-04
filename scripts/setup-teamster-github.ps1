[CmdletBinding()]
param(
    [string]$Repository = "Weakened/ConcernedCatMods",
    [int]$ThrottleSeconds = 2,
    [switch]$DryRun
)

# Idempotent Concerned Teamster label and issue generator (CT-OPS-001, #107).
# Creates/updates Teamster labels, ten sprint controllers, and leaf issues
# CT-001..CT-050. Existing issues are matched by exact title and never
# duplicated; controller bodies are refreshed only when content changed.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) is required. Install it and run 'gh auth login'."
}
gh auth status | Out-Host
if ($LASTEXITCODE -ne 0) { throw "GitHub CLI is not authenticated." }

function Invoke-Throttle {
    if ($script:ThrottleSeconds -gt 0) { Start-Sleep -Seconds $script:ThrottleSeconds }
}

# ---------------------------------------------------------------------------
# Labels
# ---------------------------------------------------------------------------

$labels = @(
    @{ Name = "mod:teamster"; Color = "2F81F7"; Description = "Concerned Teamster" },
    @{ Name = "area:carts"; Color = "A371F7"; Description = "Cart telemetry, load physics, safety, and diagnostics" }
)
foreach ($v in "0.1", "0.2", "0.3", "0.4", "0.5", "0.6", "0.7", "0.8", "0.9") {
    $labels += @{ Name = "sprint:teamster-v$v"; Color = "C2E0C6"; Description = "Concerned Teamster v$v release sprint" }
}
$labels += @{ Name = "sprint:teamster-v1.0"; Color = "FFD33D"; Description = "Concerned Teamster v1.0 release sprint" }

foreach ($label in $labels) {
    if ($DryRun) { Write-Host "[dry-run] label: $($label.Name)"; continue }
    gh label create $label.Name --repo $Repository --color $label.Color --description $label.Description --force | Out-Null
    Write-Host "Label ready: $($label.Name)"
}

# ---------------------------------------------------------------------------
# Shared body fragments
# ---------------------------------------------------------------------------

$contractLine = "#107 (CT-OPS-001) and ``docs/mods/concerned-teamster/AUTONOMOUS_EXECUTION.md``"

$autonomyRule = @(
    "Claude Code owns implementation, tests, documentation, defect filing and repair, integration, and release-candidate preparation for this issue.",
    "Select work per AUTONOMOUS_EXECUTION.md: lowest-numbered open unblocked CT leaf; open Cartographer public-beta P0/P1 regressions preempt Teamster work; active-sprint defects come before new leaves.",
    "Branch from current main, implement only this issue, run every automatable check, open a PR, pass a focused independent review, merge, comment exact evidence, close, and continue immediately.",
    "Record non-blocking uncertainty with its safe reversible default in ``docs/mods/concerned-teamster/HUMAN_ATTENTION.md`` and keep going. Stop only for the hard-stop conditions listed in #107.",
    "Never fabricate PASS. Research uncertain Valheim or third-party mod APIs against the current game build instead of inventing them."
) -join " "

$standardDoD = @(
    "- [ ] Acceptance criteria pass with recorded evidence.",
    "- [ ] ``python tools/validate_repo.py`` passes; solution build and ``ConcernedTeamster.Tests`` pass where runnable on this machine.",
    "- [ ] Documentation and automated tests are updated for the change.",
    "- [ ] Discovered defects are filed against the active sprint before it closes.",
    "- [ ] No open in-scope P0/P1 defect remains.",
    "- [ ] Work is merged to main through a reviewed PR and this issue is closed with exact evidence.",
    "- [ ] Manual-only in-game claims are recorded as pending for the owner smoke checklist, never marked PASS."
) -join "`n"

# ---------------------------------------------------------------------------
# Sprint and leaf definitions
# ---------------------------------------------------------------------------

$sprints = @(
    @{
        Version = "0.1"; Name = "Cart Truth"
        Promise = "The cart's real mass, cargo weight, terrain grade, and pull state become visible through a discoverable Cart Status panel, with vanilla cart physics untouched."
        Gate = @(
            "Independent plugin/package/lifecycle proven: own DLL, GUID, package skeleton, validator coverage.",
            "Cart internals adapter documented from the current game build with fail-closed capability probes.",
            "Telemetry, grade math, and the Cart Status panel pass their automated tests.",
            "v0.1 release candidate sealed: synchronized versions, validator pass, package ZIP hash recorded."
        )
        Leaves = @(
            @{
                Key = "CT-001"; Title = "Bootstrap the independent Teamster plugin, package, and lifecycle"
                Type = "technical"; Priority = "critical"; Areas = @("carts", "release"); Deps = @()
                Goal = "Create Concerned Teamster as a fully independent product in the monorepo: its own project, tests, plugin identity, package skeleton, and validation, without touching Concerned Cartographer."
                Scope = @(
                    "Add ``src/ConcernedTeamster/ConcernedTeamster.csproj`` (net48) with root namespace/assembly ``TheConcernedCat.ConcernedTeamster``, version 0.1.0, wired into ``ConcernedCatMods.sln``.",
                    "Add ``Plugin.cs`` with GUID ``com.theconcernedcat.valheim.concernedteamster``, name ``Concerned Teamster``, an environment banner log line, and config binding — no gameplay behavior yet.",
                    "Add ``src/ConcernedTeamster.Tests/ConcernedTeamster.Tests.csproj`` runnable without Valheim assemblies, with at least one real domain test compiled from source-linked ``Domain/**``.",
                    "Add ``src/ConcernedTeamster/Package/`` skeleton: ``thunderstore.toml`` (namespace TheConcernedCat, name ConcernedTeamster, pinned denikson-BepInExPack_Valheim 5.4.2333 and ValheimModding-Jotunn 2.29.2), 256x256 ``icon.png``, ``README.md``, ``CHANGELOG.md``.",
                    "Extend ``tools/validate_repo.py`` to validate both products (required files, icon size, version sync, dependency pins, no foreign DLLs) without weakening any Cartographer check.",
                    "Extend ``scripts/build.ps1``, ``scripts/deploy.ps1``, and ``scripts/package.ps1`` to accept the Teamster project while defaulting to existing behavior."
                )
                Ac = @(
                    "``dotnet test`` (or the solution test run) passes for ``ConcernedTeamster.Tests`` on a machine without Valheim.",
                    "``python tools/validate_repo.py`` validates both Cartographer and Teamster and passes.",
                    "Building with local game references produces ``TheConcernedCat.ConcernedTeamster.dll`` only; no second DLL ships in the package copy list.",
                    "Deploying to the TCT-Dev profile and launching modded shows the Teamster banner in the BepInEx log, or that launch claim is recorded as pending manual evidence.",
                    "No Cartographer file's behavior changes except the shared scripts' additive parameters."
                )
                Ev = @(
                    "Validator and test command outputs.",
                    "Package copy-list excerpt showing only the Teamster DLL target.",
                    "BepInEx log excerpt with the banner, or an explicit pending-manual note."
                )
            },
            @{
                Key = "CT-002"; Title = "Spike current Valheim cart (Vagon) internals behind a narrow adapter"
                Type = "technical"; Priority = "high"; Areas = @("carts"); Deps = @("CT-001")
                Goal = "Verify, against the current game build, exactly which cart internals exist (component type, mass fields, container linkage, attach/detach state) and wrap them in a fail-closed CartAdapter; document findings instead of inventing APIs."
                Scope = @(
                    "Inspect the current cart implementation (expected: the ``Vagon`` component on the Cart prefab) using the locally generated publicized assemblies; record the exact verified member names, types, and semantics in ``docs/mods/concerned-teamster/CART_INTERNALS.md``.",
                    "Implement ``Adapters/CartAdapter`` exposing an immutable snapshot (cart id, base mass, total mass, cargo weight, attachment state, local pull state) with every game access behind a startup capability probe.",
                    "Fail closed: a missing/renamed member disables the capability with one actionable WARN line; the plugin still loads.",
                    "No behavior mutation of the cart in this issue; read-only access only.",
                    "Unit-test the snapshot mapping and capability-disable paths through a fake game surface."
                )
                Ac = @(
                    "CART_INTERNALS.md lists each required member with its verified signature and the game version/build it was checked against; no undocumented member is referenced in code.",
                    "CartAdapter compiles and its capability probe reports verified/missing members once at startup.",
                    "Simulated missing-member scenarios in tests disable the capability without exceptions.",
                    "No Valheim type name appears outside ``Adapters/``."
                )
                Ev = @(
                    "CART_INTERNALS.md committed with verification notes.",
                    "Test output for capability-disable scenarios.",
                    "Startup log excerpt showing the probe result (or pending manual note)."
                )
            },
            @{
                Key = "CT-003"; Title = "Read cart mass, velocity, attachment, cargo, and pull-state telemetry"
                Type = "feature"; Priority = "high"; Areas = @("carts"); Deps = @("CT-002")
                Goal = "Turn CartAdapter snapshots into a bounded, allocation-conscious telemetry stream the rest of the mod consumes."
                Scope = @(
                    "Implement a telemetry sampler with configurable interval, bounded nearby-cart search radius, and a hard per-tick budget; defaults documented and safe.",
                    "Define the immutable ``CartTelemetry`` domain record: mass breakdown, cargo weight, velocity, attachment/pull state, timestamp.",
                    "Reset all telemetry state on logout, world switch, and cart destruction; never show another world's or a destroyed cart's data.",
                    "No logging in the sample path; a gated debug config may log summaries at most once per several seconds.",
                    "Unit tests for sampler scheduling, budget accounting, and state reset."
                )
                Ac = @(
                    "Telemetry for a nearby cart populates every ``CartTelemetry`` field the adapter can verify; unobtainable fields are explicitly marked unavailable rather than defaulted.",
                    "Sampler respects its interval and budget in tests; steady-state sampling allocates no per-tick garbage in the domain path.",
                    "World-switch and cart-destruction reset paths are covered by tests.",
                    "In-game spot check of displayed-vs-expected cargo weight is recorded, or noted pending."
                )
                Ev = @(
                    "Test output for sampler and reset behavior.",
                    "Config listing with defaults and bounds.",
                    "In-game telemetry log excerpt or pending note."
                )
            },
            @{
                Key = "CT-004"; Title = "Measure terrain grade and surface with deterministic grade math"
                Type = "feature"; Priority = "high"; Areas = @("carts"); Deps = @("CT-003")
                Goal = "Compute the grade (slope) and surface kind under the cart deterministically, with pure math that is fully unit-tested against synthetic terrain."
                Scope = @(
                    "Implement ``Adapters/TerrainAdapter`` sampling ground height/normals near the cart through supported surfaces verified the same way as CT-002 (documented in CART_INTERNALS.md or a sibling note); read-only.",
                    "Implement pure ``Domain/GradeMath``: grade percentage and direction (climbing/descending/level relative to cart heading) from position samples, with smoothing that tolerates noisy terrain.",
                    "Classify surface kind where obtainable (untouched/dirt-paint/paved-paint), mirroring the terrain-paint kinds Cartographer proved readable; mark unavailable when not verifiable.",
                    "Extend ``CartTelemetry`` with grade and surface fields.",
                    "Fixture-based unit tests: flat, uniform slopes up/down, crest, dip, noisy samples."
                )
                Ac = @(
                    "GradeMath returns correct sign and magnitude within documented tolerance on every fixture.",
                    "Grade output is stable (no oscillation) on the noisy fixture per the smoothing spec.",
                    "Surface classification never guesses: unverifiable ground reports unavailable.",
                    "No terrain write path exists in TerrainAdapter.",
                    "In-game spot check on a built dirt slope vs flat ground is recorded, or noted pending."
                )
                Ev = @(
                    "GradeMath test output including fixture table.",
                    "CART_INTERNALS.md (or sibling) update for the verified terrain surface.",
                    "In-game grade screenshot/log or pending note."
                )
            },
            @{
                Key = "CT-005"; Title = "Deliver the discoverable Cart Status panel and seal the v0.1 release candidate"
                Type = "feature"; Priority = "critical"; Areas = @("carts", "ux", "release"); Deps = @("CT-004")
                Goal = "Ship the first user-visible slice — a Cart Status panel opened by a visible button showing mass, cargo, grade, and pull state — and seal the internal v0.1 RC."
                Scope = @(
                    "Add a visible, discoverable button (no hidden hotkey-only path) that opens the Cart Status panel; an optional rebindable shortcut may exist as an accelerator.",
                    "Panel shows: total mass, base vs cargo breakdown, grade with direction, attachment/pull state, and freshness (stale data is visibly stale, not silently frozen).",
                    "Drive the panel through a presenter over ``CartTelemetry`` snapshots; headless presenter tests cover every displayed field and the no-cart/stale states.",
                    "Run the v0.1 sprint campaign from TEST_PLAN.md (vanilla truth baseline, cart lifecycle, world lifecycle, uninstall safety); file defects for findings.",
                    "Seal the RC: synchronize 0.1.0 across csproj/Plugin.cs/thunderstore.toml/CHANGELOG, run validator and packaging, record the ZIP hash, and append pending manual claims to the owner smoke checklist."
                )
                Ac = @(
                    "Panel opens from the visible button; every number shown traces to a telemetry field covered by presenter tests.",
                    "No-cart, cart-destroyed, and stale-data states render explicitly instead of showing wrong numbers.",
                    "Version 0.1.0 is synchronized and ``python tools/validate_repo.py`` plus packaging pass; the RC ZIP contains only the Teamster DLL, package metadata, license, changelog, icon.",
                    "Sprint campaign executed; automatable results recorded, manual-only items listed as pending; no open P0/P1.",
                    "Sprint controller gate checklist is satisfied and the controller closes after this issue."
                )
                Ev = @(
                    "Presenter test output.",
                    "Validator/package output with the RC ZIP SHA-256.",
                    "Campaign result table with pending-manual list."
                )
            }
        )
    },
    @{
        Version = "0.2"; Name = "Cargo and Load Planning"
        Promise = "Haulers see exactly what the cart carries and how close it is to safe limits, with live warnings before the hill wins."
        Gate = @(
            "Immutable cargo manifest and weight summaries proven by tests.",
            "Manifest UI is sortable/filterable and button-discoverable.",
            "Recommended-load and climbability model calibrated from recorded vanilla runs, with uncertainty documented.",
            "Live warnings are bounded, hysteretic, and actionable; no spam.",
            "v0.2 release candidate sealed with recorded hashes."
        )
        Leaves = @(
            @{
                Key = "CT-006"; Title = "Build the immutable cargo manifest and weight summaries"
                Type = "feature"; Priority = "high"; Areas = @("carts"); Deps = @("CT-005")
                Goal = "Snapshot the cart container into an immutable manifest — items, counts, unit and total weights — without ever mutating inventory."
                Scope = @(
                    "Extend CartAdapter to read the cart container contents through verified members (documented in CART_INTERNALS.md); read-only.",
                    "Define ``Domain/CargoManifest``: immutable list of entries (item id, display name, count, unit weight, line weight) plus totals and a capture timestamp.",
                    "Refresh the manifest on container-change signals or bounded polling — no per-frame container scans.",
                    "Handle empty carts, unreadable items, and modded items with missing data by explicit unavailable markers.",
                    "Unit tests over fake containers: totals, ordering stability, immutability, unavailable-item handling."
                )
                Ac = @(
                    "Manifest totals equal the sum of line weights in every test case.",
                    "No code path writes to the container or item stacks.",
                    "Modded/unknown items appear with name fallback and explicit unknown weight rather than silently skewing totals.",
                    "Manifest refresh cost is bounded and measured in tests (call-count assertions)."
                )
                Ev = @(
                    "Manifest test output.",
                    "CART_INTERNALS.md update for container members.",
                    "In-game manifest-vs-container screenshot or pending note."
                )
            },
            @{
                Key = "CT-007"; Title = "Deliver the sortable, filterable cargo manifest UI"
                Type = "feature"; Priority = "high"; Areas = @("carts", "ux"); Deps = @("CT-006")
                Goal = "Present the cargo manifest in a panel players can sort and filter, discoverable from the Cart Status surface."
                Scope = @(
                    "Add a Manifest view reachable by button from the Cart Status panel; no hotkey-only access.",
                    "Columns: item, count, unit weight, line weight; sortable by each column with stable secondary order; text filter over item names.",
                    "Presenter-level sorting/filtering over the immutable manifest — the UI never re-reads game state directly.",
                    "Empty/filter-no-match/stale states render explicitly.",
                    "Headless presenter tests for sorting, filtering, and state rendering."
                )
                Ac = @(
                    "Every column sorts ascending/descending deterministically in tests.",
                    "Filtering matches case-insensitively and clears cleanly.",
                    "Panel remains responsive with a full cart (worst-case realistic stack count) without per-frame allocation spikes.",
                    "Discoverability: a player can reach the manifest using only visible buttons."
                )
                Ev = @(
                    "Presenter test output for sort/filter matrices.",
                    "Screenshot of the manifest panel or pending note.",
                    "Allocation/responsiveness measurement note."
                )
            },
            @{
                Key = "CT-008"; Title = "Model and calibrate recommended load and climbability"
                Type = "technical"; Priority = "high"; Areas = @("carts"); Deps = @("CT-007")
                Goal = "Turn recorded vanilla behavior into a calibrated, documented model answering: how much load is safe, and can this cart climb that grade?"
                Scope = @(
                    "Define a written calibration protocol: fixed cargo sets pulled on measured grades in TCT-Clean/TCT-Dev, recording speed/stall outcomes.",
                    "Store calibration results as versioned data (not code constants) under the Teamster source tree with provenance notes (game version, date, protocol).",
                    "Implement ``Domain/LoadModel``: recommended-load estimate for a given grade and climbability verdict (yes/marginal/no) with stated confidence and the calibration rows it derives from.",
                    "The model must be honest about extrapolation: outside calibrated ranges it reports uncertainty, never fake precision.",
                    "Unit tests: interpolation correctness, monotonicity (more grade or mass never improves the verdict), out-of-range handling."
                )
                Ac = @(
                    "Calibration data file exists with protocol, provenance, and at least the flat/moderate/steep grade rows the protocol defines.",
                    "LoadModel verdicts are monotonic and reproducible from the data file alone.",
                    "Out-of-calibration queries return explicit uncertainty.",
                    "Documentation explains what the model does and does not know."
                )
                Ev = @(
                    "Calibration data file and protocol doc.",
                    "LoadModel test output.",
                    "Recorded calibration-run notes (or pending items for runs requiring manual play)."
                )
            },
            @{
                Key = "CT-009"; Title = "Add live load/grade warnings with bounded sampling"
                Type = "feature"; Priority = "high"; Areas = @("carts", "ux"); Deps = @("CT-008")
                Goal = "Warn the hauler before the hill wins: live, actionable, non-spammy warnings when load or upcoming grade approaches unsafe territory."
                Scope = @(
                    "Evaluate warnings only on new telemetry snapshots (bounded by the sampler; no extra polling loops).",
                    "Warning levels with hysteresis (enter/exit thresholds differ) so boundary riding cannot flicker or spam.",
                    "Warning text states the situation and the action (for example lighten the load or pick a shallower path) — not just color.",
                    "Warnings surface in the Cart Status panel and an optional HUD hint; both configurable, HUD off-by-default choices documented.",
                    "Unit tests for threshold hysteresis, level transitions, and message selection."
                )
                Ac = @(
                    "Hysteresis verified: oscillating inputs across a threshold produce one transition pair, not a stream.",
                    "Every warning has actionable text and a non-color cue (icon/text), not color alone.",
                    "No warning evaluation happens outside snapshot updates (test-asserted).",
                    "Config can disable HUD hints independently of panel warnings."
                )
                Ev = @(
                    "Hysteresis/transition test output.",
                    "Screenshot or transcript of warning states, or pending note.",
                    "Config listing for warning options."
                )
            },
            @{
                Key = "CT-010"; Title = "Validate and package the v0.2 release candidate"
                Type = "release"; Priority = "critical"; Areas = @("release", "carts"); Deps = @("CT-009")
                Goal = "Integrate the cargo/load sprint, run the campaign, burn down defects, and seal the internal v0.2 RC."
                Scope = @(
                    "Run the v0.2 campaign: manifest correctness against real carts, load model spot checks against the calibration protocol, warning behavior on a test slope, plus the standard lifecycle/uninstall suite.",
                    "File and fix in-scope defects; regress fixes.",
                    "Synchronize 0.2.0 versions, update CHANGELOG, run validator and packaging, record the RC ZIP hash.",
                    "Append new pending manual claims to the owner smoke checklist.",
                    "Close the sprint controller when the gate checklist is green."
                )
                Ac = @(
                    "All automatable campaign rows PASS with recorded output; manual-only rows are listed pending.",
                    "No open P0/P1 defects labeled sprint:teamster-v0.2.",
                    "Versions synchronized at 0.2.0; validator and package build pass; ZIP hash recorded.",
                    "Sprint controller gate checklist fully satisfied."
                )
                Ev = @(
                    "Campaign result table.",
                    "Validator/package output with ZIP SHA-256.",
                    "Defect list with resolutions."
                )
            }
        )
    },
    @{
        Version = "0.3"; Name = "Descent Safety and Recovery Guidance"
        Promise = "Descent risk is predicted before the slope, a deliberate reversible parking brake holds the cart, and stuck carts explain themselves — with no teleports or force cheats."
        Gate = @(
            "Descent/runaway risk model calibrated and monotonic under test.",
            "Parking brake is explicit, reversible, owner-authorized, fail-closed, and leaves no trace in saves.",
            "Stuck/grounding diagnostics identify the real obstruction class in the test scenarios.",
            "Recovery guidance never moves the cart; it only explains.",
            "v0.3 release candidate sealed with recorded hashes."
        )
        Leaves = @(
            @{
                Key = "CT-011"; Title = "Build the descent and runaway risk model"
                Type = "technical"; Priority = "high"; Areas = @("carts"); Deps = @("CT-010")
                Goal = "Predict descent danger — will this grade, mass, and speed end in a runaway — as a calibrated, testable domain model."
                Scope = @(
                    "Extend the calibration protocol with descent runs (controlled release on measured downgrades with fixed cargo sets), stored as versioned data with provenance.",
                    "Implement ``Domain/RiskModel``: risk level (safe/caution/danger) from grade, total mass, and current speed, with hysteresis-friendly outputs.",
                    "Distinguish current-position risk from lookahead risk along the recent heading using existing terrain sampling; lookahead is bounded and configurable.",
                    "Document model limits (what was calibrated, what is extrapolated).",
                    "Unit tests: monotonicity in each input, calibration reproduction, lookahead bounds."
                )
                Ac = @(
                    "Risk never decreases when grade, mass, or speed increases (test matrix).",
                    "Lookahead sampling stays within its configured budget.",
                    "Out-of-calibration inputs yield explicit uncertainty, not confident nonsense.",
                    "Model documentation and calibration data are committed together."
                )
                Ev = @(
                    "RiskModel test output.",
                    "Calibration data delta with provenance.",
                    "Bounded-lookahead measurement."
                )
            },
            @{
                Key = "CT-012"; Title = "Implement the explicit, reversible parking brake with safe authority and lifecycle"
                Type = "feature"; Priority = "critical"; Areas = @("carts"); Deps = @("CT-011")
                Goal = "Deliver Teamster's first behavior-mutating feature under the strictest rules: a parking brake the player explicitly sets, that is always reversible, never persists into saves, and fails closed."
                Scope = @(
                    "Brake engages only through an explicit visible control on a cart the local player may control under vanilla rules; never automatically.",
                    "Implementation is runtime-only (constraining the cart's physics while engaged); document the exact mechanism after verifying it against the current build — no invented physics members.",
                    "Release paths: explicit release button, player detach beyond a bounded distance, world exit, plugin shutdown, and any adapter capability failure — every path releases the brake.",
                    "Nothing brake-related is written to world saves or Teamster sidecars; a reloaded world starts brake-free by construction.",
                    "Multiplayer posture: local-authority carts only in this version; foreign carts show no brake control.",
                    "Unit tests over a fake physics seam for every engage/release path and the fail-closed cases."
                )
                Ac = @(
                    "Every release path is test-covered; no reachable state leaves the brake engaged without its lifecycle owner.",
                    "Save/sidecar audit shows zero brake persistence.",
                    "Brake control is hidden when authority or capability is absent (fail closed, with one log line).",
                    "In-game slope hold/release behavior is recorded, or listed pending.",
                    "Uninstalling the mod restores fully vanilla cart behavior."
                )
                Ev = @(
                    "Lifecycle test matrix output.",
                    "Persistence audit note.",
                    "In-game brake demonstration or pending note."
                )
            },
            @{
                Key = "CT-013"; Title = "Add stuck, grounding, and obstruction diagnostics"
                Type = "feature"; Priority = "high"; Areas = @("carts"); Deps = @("CT-012")
                Goal = "When a cart will not move, tell the player why: grounded chassis, blocked wheel, impossible grade, or overload — from observed state, not guesses."
                Scope = @(
                    "Define diagnostic classes with observable signatures (for example pulling with near-zero velocity plus contact patterns) using only verified read-only surfaces.",
                    "Implement bounded detection that runs only while a stuck signature is active; idle carts cost nothing.",
                    "Report the most probable cause with its evidence, and say 'unclear' when signatures conflict rather than picking one.",
                    "Surface diagnostics in the Cart Status panel.",
                    "Unit tests per diagnostic class over synthetic telemetry traces."
                )
                Ac = @(
                    "Each diagnostic class triggers on its synthetic trace and not on the others (confusion matrix in tests).",
                    "Unclear/conflicting evidence yields the honest unclear verdict.",
                    "Detection work is zero for parked, unattended carts.",
                    "Staged in-game stuck scenarios are recorded, or listed pending."
                )
                Ev = @(
                    "Diagnostic confusion-matrix test output.",
                    "Panel screenshot with a diagnosis or pending note."
                )
            },
            @{
                Key = "CT-014"; Title = "Deliver recovery guidance UI without teleports or force cheats"
                Type = "feature"; Priority = "high"; Areas = @("carts", "ux"); Deps = @("CT-013")
                Goal = "Turn diagnostics into guidance: concrete, vanilla-legal steps to free the cart — never a button that moves it."
                Scope = @(
                    "Map each diagnostic class to guidance content (for example unload N weight to climb this grade, based on LoadModel; approach angle suggestions for lips).",
                    "Guidance panel reachable by button from the diagnostics surface; content is text plus non-color cues.",
                    "Guidance is advisory only: audit that no guidance path mutates cart transform, physics, or inventory.",
                    "Include the parking brake where genuinely relevant (hold on slope while unloading).",
                    "Presenter tests: correct guidance per diagnostic input, including the unclear case."
                )
                Ac = @(
                    "Every diagnostic class has guidance, and the unclear case offers safe generic steps.",
                    "Mutation audit passes: the guidance layer holds no reference to any mutating surface.",
                    "Quantitative guidance (unload amounts) traces to LoadModel outputs.",
                    "In-game guidance walkthrough recorded or pending."
                )
                Ev = @(
                    "Presenter test output.",
                    "Mutation-audit note.",
                    "Guidance screenshot or pending note."
                )
            },
            @{
                Key = "CT-015"; Title = "Validate and package the v0.3 release candidate"
                Type = "release"; Priority = "critical"; Areas = @("release", "carts"); Deps = @("CT-014")
                Goal = "Integrate the descent-safety sprint, burn down defects, and seal the internal v0.3 RC with the brake's safety evidence front and center."
                Scope = @(
                    "Run the v0.3 campaign: risk warnings on descents, full brake lifecycle including forced-failure releases, staged stuck scenarios, guidance correctness, standard lifecycle/uninstall suite.",
                    "Re-verify brake non-persistence on a fresh world copy.",
                    "File/fix/regress in-scope defects.",
                    "Synchronize 0.3.0, validator, package, hash, changelog; append pending manual claims to the smoke checklist.",
                    "Close the controller on a green gate."
                )
                Ac = @(
                    "Automatable campaign rows PASS with output; manual rows pending-listed.",
                    "Brake persistence re-audit is clean.",
                    "No open P0/P1 for sprint:teamster-v0.3.",
                    "0.3.0 RC sealed with recorded hash."
                )
                Ev = @(
                    "Campaign table, defect list.",
                    "Brake audit output.",
                    "Validator/package output with ZIP SHA-256."
                )
            }
        )
    },
    @{
        Version = "0.4"; Name = "Road Quality and Trip Profiles"
        Promise = "Recorded trips grade the roads themselves — roughness, drag, grade, and bottlenecks — so haulers improve routes with evidence instead of vibes."
        Gate = @(
            "Trip sampling is bounded, opt-in aware, and isolated per world in Teamster's own sidecar.",
            "Road-quality scoring is deterministic and documented.",
            "Trip history and comparison UI pass presenter tests.",
            "Bottleneck detection points at real recorded segments.",
            "v0.4 release candidate sealed with recorded hashes."
        )
        Leaves = @(
            @{
                Key = "CT-016"; Title = "Record bounded per-world trip samples in a Teamster sidecar"
                Type = "technical"; Priority = "high"; Areas = @("carts"); Deps = @("CT-015")
                Goal = "Persist trip telemetry (position, grade, speed, load) per world in Teamster's own versioned sidecar, with the same durability rules Cartographer proved."
                Scope = @(
                    "Implement ``Persistence/`` with per-world files under the BepInEx config path keyed by world UID with a ``teamster`` infix; atomic temp-file writes; versioned header; malformed-row skip; backup before format migration.",
                    "Record trips as bounded sample sequences (configurable rate and per-trip caps) started/ended by attach/detach with debounce.",
                    "Hard caps on file size/trip count with oldest-trip pruning and a visible retention setting.",
                    "Cross-world isolation by construction (world UID in filename and header).",
                    "Unit tests: round-trip, migration stub, malformed-row skip, cap/pruning, isolation."
                )
                Ac = @(
                    "Kill-during-write leaves the previous file intact (atomic write test).",
                    "World A trips never load into world B (isolation test).",
                    "Caps and pruning enforce the configured bounds.",
                    "Sidecar filenames cannot collide with Cartographer sidecars.",
                    "No write to any Valheim save file."
                )
                Ev = @(
                    "Persistence test output.",
                    "Sidecar sample file with header.",
                    "Retention config listing."
                )
            },
            @{
                Key = "CT-017"; Title = "Score roughness, grade, drag, and road quality from trips"
                Type = "technical"; Priority = "high"; Areas = @("carts"); Deps = @("CT-016")
                Goal = "Convert raw trip samples into deterministic per-segment quality scores a hauler can trust."
                Scope = @(
                    "Implement ``Domain/RoadQuality``: segmentation of trips into stable spatial segments, per-segment roughness (vertical noise), mean/max grade, and empirical drag proxy (speed vs expected for load/grade).",
                    "Scores are deterministic for identical inputs and documented with their formulas and limits.",
                    "Incremental scoring: new trips update affected segments only; no full recomputation per trip.",
                    "Score storage rides the CT-016 sidecar with versioned schema.",
                    "Unit tests: synthetic trips with known roughness/grade produce expected scores; incremental equals batch."
                )
                Ac = @(
                    "Deterministic: same trips, same scores, byte-identical persisted output.",
                    "Incremental-vs-batch equivalence test passes.",
                    "Formulas and limitations documented in ARCHITECTURE.md or DATA notes.",
                    "Score computation cost is bounded and measured."
                )
                Ev = @(
                    "Scoring test output.",
                    "Incremental-equivalence proof output.",
                    "Documentation delta."
                )
            },
            @{
                Key = "CT-018"; Title = "Deliver trip history and profile comparison UI"
                Type = "feature"; Priority = "high"; Areas = @("carts", "ux"); Deps = @("CT-017")
                Goal = "Let haulers browse recorded trips and compare route profiles side by side."
                Scope = @(
                    "Trip history panel (button-discoverable): list of trips with date, duration, distance, load summary, worst grade, quality summary; sortable.",
                    "Comparison view: two selected trips/profiles side by side on shared axes (distance-normalized grade and quality).",
                    "All rendering via presenters over persisted domain data; stale/empty states explicit.",
                    "Deletion of individual trips (their data only) with confirmation.",
                    "Presenter tests: listing, sorting, comparison alignment, deletion."
                )
                Ac = @(
                    "History reflects exactly the persisted trips; deletion removes only the chosen trip.",
                    "Comparison aligns by normalized distance and labels both series clearly with non-color distinction.",
                    "Empty/no-selection states are explicit.",
                    "Panel cost bounded with the maximum retained trip count."
                )
                Ev = @(
                    "Presenter test output.",
                    "Screenshot of history and comparison or pending note."
                )
            },
            @{
                Key = "CT-019"; Title = "Surface route-grade and load bottlenecks from recorded trips"
                Type = "feature"; Priority = "high"; Areas = @("carts"); Deps = @("CT-018")
                Goal = "Answer the planning question directly: where does this route defeat this load, and what is the binding constraint?"
                Scope = @(
                    "Implement bottleneck detection over scored segments: worst-grade segment, worst-quality segment, and the segment that binds the LoadModel for a chosen cargo mass.",
                    "Present bottlenecks in the trip/profile UI with location context (distance along route) and the constraint explanation.",
                    "For a hypothetical load (user-entered mass), recompute binding segments without new sampling.",
                    "Unit tests: crafted profiles with known bottlenecks are found; hypothetical-load math matches LoadModel."
                )
                Ac = @(
                    "Bottleneck detection finds the planted worst segments in test profiles.",
                    "Hypothetical-load analysis is pure domain math (no game access) and test-covered.",
                    "Explanations name the constraint (grade vs quality vs load) rather than a bare marker.",
                    "In-game bottleneck view recorded or pending."
                )
                Ev = @(
                    "Bottleneck test output.",
                    "UI screenshot or pending note."
                )
            },
            @{
                Key = "CT-020"; Title = "Validate and package the v0.4 release candidate"
                Type = "release"; Priority = "critical"; Areas = @("release", "carts"); Deps = @("CT-019")
                Goal = "Integrate the road-quality sprint, prove sidecar durability, and seal the internal v0.4 RC."
                Scope = @(
                    "Run the v0.4 campaign: record real trips, verify history/comparison/bottlenecks against them, sidecar durability spot checks (kill-during-write, world isolation), standard suite.",
                    "File/fix/regress defects; verify retention behavior over many trips.",
                    "Synchronize 0.4.0, validator, package, hash, changelog; append pending manual claims.",
                    "Close the controller on a green gate."
                )
                Ac = @(
                    "Automatable campaign rows PASS; manual rows pending-listed.",
                    "Durability spot checks pass on the dev machine.",
                    "No open P0/P1 for sprint:teamster-v0.4.",
                    "0.4.0 RC sealed with recorded hash."
                )
                Ev = @(
                    "Campaign table, defect list.",
                    "Durability check output.",
                    "Validator/package output with ZIP SHA-256."
                )
            }
        )
    },
    @{
        Version = "0.5"; Name = "Optional Cartographer Integration"
        Promise = "When Concerned Cartographer is present, its routes can be profiled for cart safety — distance, surfaces, grades, and safe-load bottlenecks — with zero hard dependency in either direction."
        Gate = @(
            "Capability adapter proves presence/version detection with graceful absence.",
            "Route selection, profiling, and recommendations work with Cartographer installed and vanish cleanly without it.",
            "No atlas mutation and no compile-time reference in either direction.",
            "Coexistence validated in the compat profile.",
            "v0.5 release candidate sealed with recorded hashes."
        )
        Leaves = @(
            @{
                Key = "CT-021"; Title = "Build the Cartographer capability and version adapter (no hard dependency)"
                Type = "technical"; Priority = "high"; Areas = @("interop", "carts"); Deps = @("CT-020")
                Goal = "Detect Concerned Cartographer at runtime, negotiate a compatible read surface, and degrade to hidden features when absent or incompatible."
                Scope = @(
                    "Implement ``Adapters/CartographerCapability``: detect by plugin GUID and version via BepInEx plugin infos; no compile-time reference from Teamster to Cartographer (enforced by an automated dependency audit in tests or validator).",
                    "Define the minimal read contract Teamster needs (route list with geometry) and access it reflectively or through a documented stable surface agreed in this issue; the contract and its version gates are written down.",
                    "Absence, version mismatch, or probe failure hides integration features with one INFO line; nothing errors.",
                    "Decide and document the compatibility floor (minimum Cartographer version).",
                    "Unit tests over fake plugin registries: present/absent/mismatch/probe-failure paths."
                )
                Ac = @(
                    "Dependency audit proves no Teamster->Cartographer compile-time reference (and none is added to Cartographer).",
                    "All four detection paths behave per spec in tests.",
                    "The read contract documentation names its Cartographer version floor and every member it relies on.",
                    "With Cartographer absent, Teamster's UI shows no integration stubs."
                )
                Ev = @(
                    "Dependency-audit output.",
                    "Capability test output.",
                    "Contract documentation."
                )
            },
            @{
                Key = "CT-022"; Title = "Select eligible Cartographer routes from the Teamster UI"
                Type = "feature"; Priority = "high"; Areas = @("interop", "ux"); Deps = @("CT-021")
                Goal = "List the current world's Cartographer routes inside Teamster and let the hauler pick one for profiling — read-only."
                Scope = @(
                    "Route picker panel (button-discoverable, only when the capability is live): route name, length, and basic metadata from the read contract.",
                    "Eligibility rules: routes with usable geometry in the current world; ineligible routes are shown with the reason or filtered per documented choice.",
                    "Selection state lives in Teamster only; nothing is written toward Cartographer.",
                    "Graceful mid-session changes: route deleted/renamed in Cartographer refreshes or invalidates the selection explicitly.",
                    "Presenter tests over fake route catalogs including mid-session mutation."
                )
                Ac = @(
                    "Picker lists exactly the eligible routes from the fake catalog; reasons/filters match the documented rule.",
                    "Deleting the selected route in the source invalidates the selection with an explicit state, not a crash or stale ghost.",
                    "Zero writes through the integration surface (audited).",
                    "With Cartographer absent the picker is absent."
                )
                Ev = @(
                    "Presenter/mutation test output.",
                    "Read-only audit note.",
                    "Screenshot with Cartographer installed or pending note."
                )
            },
            @{
                Key = "CT-023"; Title = "Profile selected routes: distance, surface, grades, safe-load bottleneck"
                Type = "feature"; Priority = "high"; Areas = @("carts", "interop"); Deps = @("CT-022")
                Goal = "Run Teamster's terrain and load math along a selected Cartographer route to produce a cart-safety profile."
                Scope = @(
                    "Sample terrain along route geometry with bounded, budgeted work (chunked over frames; cancellable; only loaded terrain — unloaded stretches are reported as unsampled, never guessed).",
                    "Produce a route profile: total distance, surface composition where known, grade histogram, worst segments, and the safe-load bottleneck for a chosen cargo mass via LoadModel.",
                    "Cache profiles keyed by route identity/revision; invalidate on route change signals from CT-022.",
                    "Unit tests: profile math over synthetic geometry, unsampled-gap honesty, cache invalidation."
                )
                Ac = @(
                    "Profiling work respects its per-frame budget and is cancellable (test-asserted bookkeeping).",
                    "Unsampled segments are explicitly reported; totals never silently include guessed data.",
                    "Bottleneck output matches LoadModel for the given mass.",
                    "Profile cache invalidates on route revision change."
                )
                Ev = @(
                    "Profile math test output.",
                    "Budget measurement.",
                    "In-game profile of a real route or pending note."
                )
            },
            @{
                Key = "CT-024"; Title = "Show problem sections and recommendations without atlas mutation"
                Type = "feature"; Priority = "high"; Areas = @("interop", "ux"); Deps = @("CT-023")
                Goal = "Present route profiling as actionable advice — worst sections, load limits, detour-worthiness — while writing nothing into Cartographer's atlas."
                Scope = @(
                    "Results panel: profile summary, ranked problem sections with distances and reasons, recommended maximum load, and honest unsampled-gap disclosure.",
                    "Recommendations reuse guidance language rules from CT-014 (actionable, non-color-cued).",
                    "Strict no-mutation audit of the whole integration path (CT-021..CT-024).",
                    "Presenter tests over fixed profiles, including profiles with gaps and with no problems."
                )
                Ac = @(
                    "Panel renders every profile fixture correctly, including gap and all-clear cases.",
                    "Mutation audit across the integration path passes.",
                    "Recommendations trace to model outputs (no free-floating advice).",
                    "In-game demonstration recorded or pending."
                )
                Ev = @(
                    "Presenter test output.",
                    "Integration mutation-audit note.",
                    "Screenshot or pending note."
                )
            },
            @{
                Key = "CT-025"; Title = "Validate coexistence and package the v0.5 release candidate"
                Type = "release"; Priority = "critical"; Areas = @("release", "interop"); Deps = @("CT-024")
                Goal = "Prove Teamster+Cartographer coexistence in both directions and seal the internal v0.5 RC."
                Scope = @(
                    "Coexistence matrix in TCT-Compat: both mods loaded (integration on), Teamster alone (integration hidden), Cartographer alone (unaffected), version-mismatch simulation (floor gate).",
                    "Regression: Cartographer's own smoke rows still pass with Teamster present (no patch conflicts, no UI collisions, no log exceptions).",
                    "File/fix/regress defects; standard suite.",
                    "Synchronize 0.5.0, validator, package, hash, changelog; append pending manual claims.",
                    "Close the controller on a green gate."
                )
                Ac = @(
                    "All four matrix rows behave per spec with recorded evidence.",
                    "No new exceptions in either mod's logs during coexistence runs.",
                    "No open P0/P1 for sprint:teamster-v0.5.",
                    "0.5.0 RC sealed with recorded hash."
                )
                Ev = @(
                    "Coexistence matrix with logs.",
                    "Campaign table, defect list.",
                    "Validator/package output with ZIP SHA-256."
                )
            }
        )
    },
    @{
        Version = "0.6"; Name = "Multiplayer Trust and Authority"
        Promise = "Cart truth stays true in multiplayer: explicit ownership and observation rules, validated player-hosted and dedicated behavior, cooperative diagnostics — and no granted force, ever."
        Gate = @(
            "Ownership/control/observation policy written, implemented, and fail-closed.",
            "Player-hosted and dedicated authority validated with evidence.",
            "Cooperative push/pull diagnostics grant no force and leak no private data.",
            "Malformed/stale network input hardening passes adversarial tests.",
            "v0.6 release candidate sealed with recorded hashes."
        )
        Leaves = @(
            @{
                Key = "CT-026"; Title = "Define and implement the cart ownership, control, and observation policy"
                Type = "technical"; Priority = "critical"; Areas = @("sync", "carts"); Deps = @("CT-025")
                Goal = "Write down and enforce who may read, who may act, and who merely observes for every Teamster feature in multiplayer."
                Scope = @(
                    "Policy document: local player, cart's vanilla authority owner, other modded peers, unmodded peers — per feature (telemetry, manifest, warnings, brake, diagnostics, trips).",
                    "Implement policy enforcement at the adapter layer: mutating features (brake) restricted to vanilla-authoritative control; observation features clearly labeled when data is remote/stale.",
                    "Unmodded-peer coexistence: Teamster must not alter what vanilla peers experience.",
                    "Fail closed on any authority ambiguity.",
                    "Unit tests: policy matrix enforcement over fake authority states."
                )
                Ac = @(
                    "The policy matrix document covers every shipped feature and matches the implementation (test-asserted mapping).",
                    "Brake control is provably unreachable without vanilla authority.",
                    "Ambiguous authority states disable mutation with one log line.",
                    "No network message alters an unmodded peer's behavior."
                )
                Ev = @(
                    "Policy document.",
                    "Policy enforcement test output.",
                    "Design note on authority detection surfaces (verified, not invented)."
                )
            },
            @{
                Key = "CT-027"; Title = "Validate player-hosted and dedicated-server authority behavior"
                Type = "test"; Priority = "high"; Areas = @("sync"); Deps = @("CT-026")
                Goal = "Prove the policy on real topologies: player-hosted worlds and a dedicated server, including authority handoff."
                Scope = @(
                    "Scenario scripts: two clients on a player-hosted world; two clients on a dedicated server (TCT-Dedicated profile); cart authority handoff between players mid-haul.",
                    "Verify telemetry accuracy, brake policy enforcement, and observation labeling per topology.",
                    "Automate what is automatable (log assertions, state dumps); record the rest as structured manual evidence or pending items.",
                    "Document topology-specific caveats discovered."
                )
                Ac = @(
                    "Every scenario row has PASS-with-evidence, or an explicit pending-manual entry.",
                    "No scenario shows a mutating action executing without authority.",
                    "Authority handoff never leaves a stale brake or stale panel state.",
                    "Caveats are documented in TEST_PLAN.md or the policy doc."
                )
                Ev = @(
                    "Scenario result table with logs/dumps.",
                    "Pending-manual list updates."
                )
            },
            @{
                Key = "CT-028"; Title = "Add cooperative push/pull diagnostics without granting force"
                Type = "feature"; Priority = "high"; Areas = @("sync", "carts"); Deps = @("CT-027")
                Goal = "Help crews haul together — show who is effectively helping and why the cart still will not move — without adding a newton of modded force."
                Scope = @(
                    "Detect multi-player interaction with the same cart from observed state (attachment, contact, motion correlation) through verified read-only surfaces.",
                    "Extend diagnostics: cooperative context (helping/hindering/idle) and combined-effort explanations for stuck verdicts.",
                    "Zero force injection: audit that no physics forces/impulses are applied anywhere.",
                    "Privacy: show only in-game player names already visible to the player; nothing else leaves the machine.",
                    "Unit tests over synthetic multi-actor traces."
                )
                Ac = @(
                    "Cooperative traces classify correctly in the test matrix.",
                    "Force-injection audit passes.",
                    "Privacy review: no new data leaves the local client.",
                    "Staged co-op scenario recorded or pending."
                )
                Ev = @(
                    "Trace classification test output.",
                    "Force/privacy audit notes.",
                    "Co-op evidence or pending note."
                )
            },
            @{
                Key = "CT-029"; Title = "Harden malformed, stale, and lifecycle network input; complete the privacy review"
                Type = "technical"; Priority = "critical"; Areas = @("sync"); Deps = @("CT-028")
                Goal = "Treat every network-derived input as hostile: bound it, validate it, drop it safely — and prove Teamster leaks nothing private."
                Scope = @(
                    "Inventory every network-derived input Teamster consumes (authority states, remote cart state, player presence) and wrap each in bounds/validity checks with single-shot logging.",
                    "Stale-data policy: age thresholds after which remote-derived displays mark themselves stale.",
                    "Lifecycle hardening: joins, leaves, disconnects mid-haul, world switches — no exceptions, no stale UI, no leaked state.",
                    "Privacy review: enumerate all data Teamster stores/displays; confirm none leaves the machine and nothing sensitive is logged.",
                    "Adversarial unit tests: out-of-range, NaN, oversized, and contradictory inputs."
                )
                Ac = @(
                    "Adversarial input matrix passes: every bad input is dropped/bounded with at most one log line.",
                    "Lifecycle scenarios produce no exceptions and no stale mutating state.",
                    "Privacy inventory is committed and clean.",
                    "Fuzz-style randomized input test runs clean for its documented iteration count."
                )
                Ev = @(
                    "Adversarial/fuzz test output.",
                    "Privacy inventory document.",
                    "Lifecycle scenario logs."
                )
            },
            @{
                Key = "CT-030"; Title = "Validate and package the v0.6 release candidate"
                Type = "release"; Priority = "critical"; Areas = @("release", "sync"); Deps = @("CT-029")
                Goal = "Integrate the multiplayer sprint, rerun both topologies end to end, and seal the internal v0.6 RC."
                Scope = @(
                    "Full multiplayer campaign rerun (CT-027 scenarios plus co-op diagnostics and hardening spot checks) on both topologies.",
                    "Standard suite, defect burn-down, regression.",
                    "Synchronize 0.6.0, validator, package, hash, changelog; append pending manual claims.",
                    "Close the controller on a green gate."
                )
                Ac = @(
                    "Campaign rows PASS or pending-listed; both topologies covered.",
                    "No open P0/P1 for sprint:teamster-v0.6.",
                    "0.6.0 RC sealed with recorded hash."
                )
                Ev = @(
                    "Campaign table across topologies.",
                    "Defect list.",
                    "Validator/package output with ZIP SHA-256."
                )
            }
        )
    },
    @{
        Version = "0.7"; Name = "UX, Controller, Accessibility, Localization"
        Promise = "Every Teamster feature is reachable by button and controller, readable at any UI scale, distinguishable without color, and translatable from a complete English catalog."
        Gate = @(
            "Full controller navigation with rebindable accelerators and conflict handling.",
            "Localization framework with complete English catalog and translator template.",
            "UI scale, contrast, and non-color cues meet the documented accessibility bar.",
            "Onboarding and safe config profiles make buttons-first discovery real.",
            "v0.7 release candidate sealed with recorded hashes."
        )
        Leaves = @(
            @{
                Key = "CT-031"; Title = "Complete controller navigation and rebindable accelerators"
                Type = "feature"; Priority = "high"; Areas = @("ux"); Deps = @("CT-030")
                Goal = "Make every panel and control fully operable by controller, with keyboard/mouse accelerators rebindable and conflict-checked."
                Scope = @(
                    "Controller focus/navigation across all Teamster panels (status, manifest, history, comparison, guidance, route picker) with visible focus indication.",
                    "Rebindable accelerator bindings via config with conflict detection against vanilla and known-mod defaults; conflicts warn, never silently override.",
                    "No feature is accelerator-only; audit against the buttons-first rule.",
                    "Presenter/navigation-model tests for focus order and binding conflict logic."
                )
                Ac = @(
                    "Every interactive element is reachable in a deterministic focus order (test-asserted navigation model).",
                    "Binding conflicts are detected and reported per spec.",
                    "Buttons-first audit passes for every feature.",
                    "In-game controller walkthrough recorded or pending."
                )
                Ev = @(
                    "Navigation/binding test output.",
                    "Buttons-first audit note.",
                    "Controller evidence or pending note."
                )
            },
            @{
                Key = "CT-032"; Title = "Add the localization framework, English catalog, and translator template"
                Type = "technical"; Priority = "high"; Areas = @("ux"); Deps = @("CT-031")
                Goal = "Externalize every user-facing string into a localization catalog with English complete and a documented path for community translations."
                Scope = @(
                    "String externalization across all UI/warnings/guidance; a hardcoded-string audit (test) keeps them out.",
                    "English catalog complete; keys stable and documented; formatting placeholders validated.",
                    "Translator template + contribution doc (file format, where to submit, how placeholders work).",
                    "Runtime language selection following the game/BepInEx convention used by the localization surface chosen (verified, not invented).",
                    "Tests: catalog completeness (every referenced key exists), placeholder validity, fallback to English."
                )
                Ac = @(
                    "Hardcoded-string audit passes.",
                    "Catalog completeness and placeholder tests pass.",
                    "Missing-key fallback shows English plus a logged once-only warning.",
                    "Translator documentation committed."
                )
                Ev = @(
                    "Audit/test output.",
                    "Catalog and template files.",
                    "Translator doc."
                )
            },
            @{
                Key = "CT-033"; Title = "Deliver UI scale, contrast, non-color cues, and readable warnings"
                Type = "feature"; Priority = "high"; Areas = @("ux"); Deps = @("CT-032")
                Goal = "Make Teamster readable: scalable UI, sufficient contrast, and every state distinguishable without color."
                Scope = @(
                    "UI scale setting applied across all panels; layout survives the documented scale range without clipping.",
                    "Contrast pass over panel text/backgrounds against the documented target; adjustments recorded.",
                    "Non-color cues (icons/text/patterns) for every colored state — warnings, risk levels, comparison series, diagnostics.",
                    "Warning readability review: concise wording, consistent terminology from the localization catalog.",
                    "Presenter/layout tests where automatable; visual claims recorded as evidence or pending."
                )
                Ac = @(
                    "Scale range renders without clipping in layout tests or recorded checks.",
                    "Every colored state has a documented non-color cue (audit table).",
                    "Contrast review results recorded with any fixes applied.",
                    "No warning relies on color alone (test over warning definitions)."
                )
                Ev = @(
                    "Cue audit table.",
                    "Layout/contrast evidence or pending notes.",
                    "Test output for warning definitions."
                )
            },
            @{
                Key = "CT-034"; Title = "Add onboarding, safe config profiles, and buttons-first discoverability polish"
                Type = "feature"; Priority = "high"; Areas = @("ux"); Deps = @("CT-033")
                Goal = "A new player discovers Teamster by playing: gentle onboarding, safe defaults, and preset config profiles that never surprise."
                Scope = @(
                    "First-run onboarding: a short, dismissable pointer to the Cart Status button when near a cart; never modal, never repeated after dismissal.",
                    "Config profiles (for example Minimal / Standard / Everything-observational) as documented presets; switching is explicit and reversible; brake remains opt-in in every profile.",
                    "Discoverability polish pass: consistent button placement/labels across panels.",
                    "Config migration safety: unknown/old keys preserved or migrated per documented policy.",
                    "Tests: onboarding state machine, profile application idempotence, config migration."
                )
                Ac = @(
                    "Onboarding shows once, dismisses forever, and never blocks input (state-machine tests).",
                    "Applying a profile twice equals applying it once; every profile keeps mutating features opt-in.",
                    "Config migration tests pass for old-version fixtures.",
                    "Discoverability audit updated."
                )
                Ev = @(
                    "State-machine/profile/migration test output.",
                    "Onboarding capture or pending note.",
                    "Updated audit."
                )
            },
            @{
                Key = "CT-035"; Title = "Validate and package the v0.7 release candidate"
                Type = "release"; Priority = "critical"; Areas = @("release", "ux"); Deps = @("CT-034")
                Goal = "Integrate the UX sprint, run the accessibility/localization campaign, and seal the internal v0.7 RC."
                Scope = @(
                    "Campaign: controller-only full walkthrough, localization fallback checks, scale/contrast spot checks, onboarding fresh-profile run, standard suite.",
                    "Defect burn-down and regression.",
                    "Synchronize 0.7.0, validator, package, hash, changelog; append pending manual claims.",
                    "Close the controller on a green gate."
                )
                Ac = @(
                    "Campaign rows PASS or pending-listed.",
                    "No open P0/P1 for sprint:teamster-v0.7.",
                    "0.7.0 RC sealed with recorded hash."
                )
                Ev = @(
                    "Campaign table, defect list.",
                    "Validator/package output with ZIP SHA-256."
                )
            }
        )
    },
    @{
        Version = "0.8"; Name = "Compatibility, Recovery, Scale"
        Promise = "Teamster coexists with the real, researched cart-mod ecosystem, survives bad data and big worlds, and gives users backups and a sanitized support bundle when things go wrong."
        Gate = @(
            "Runtime compatibility framework detects and adapts to researched mods.",
            "Better Carts coexistence/precedence behaves per documented policy.",
            "Researched compatibility matrix complete with exact names/versions — nothing invented.",
            "Migration, backup/recovery, and support bundle proven.",
            "v0.8 release candidate sealed with recorded hashes."
        )
        Leaves = @(
            @{
                Key = "CT-036"; Title = "Build the runtime compatibility and capability framework"
                Type = "technical"; Priority = "high"; Areas = @("interop"); Deps = @("CT-035")
                Goal = "Generalize capability detection into a framework: detect relevant mods at runtime, adapt or disable features per documented policy, and report status honestly."
                Scope = @(
                    "Registry of known-mod probes (GUID/version based) with per-mod policy hooks (coexist, adapt, warn); built on the CT-021 pattern.",
                    "A compatibility status surface (log banner + panel section) stating what was detected and which Teamster behaviors adapted.",
                    "Policy outcomes are data-driven and documented; adding a mod policy must not require touching feature code.",
                    "Unit tests over fake plugin registries for each policy outcome."
                )
                Ac = @(
                    "Framework detects fake mods and applies each policy outcome correctly in tests.",
                    "Status surface reflects exactly the applied policies.",
                    "Feature code contains no mod-specific branches (audit); policies live in the registry.",
                    "Unknown mods produce no warnings (silence is the default)."
                )
                Ev = @(
                    "Framework test output.",
                    "Status surface capture or log excerpt.",
                    "Registry/policy documentation."
                )
            },
            @{
                Key = "CT-037"; Title = "Validate Better Carts coexistence and precedence"
                Type = "test"; Priority = "high"; Areas = @("interop", "carts"); Deps = @("CT-036")
                Goal = "With Better Carts (the known physics-altering cart mod) installed, Teamster must either measure the modified reality accurately or clearly mark readings unavailable — never present vanilla numbers as truth."
                Scope = @(
                    "Install the current Better Carts release in TCT-Compat; record its exact name/version and the behaviors it changes.",
                    "Define and implement the precedence policy through CT-036: which Teamster readouts remain valid, which adapt, which disable with a visible 'altered physics' notice.",
                    "Test the matrix: telemetry, manifest, load model, risk model, brake, diagnostics under Better Carts.",
                    "Document the policy in the compatibility section of TEST_PLAN.md and the package README."
                )
                Ac = @(
                    "Every readout under Better Carts is accurate, adapted, or visibly unavailable — no silently wrong numbers (matrix evidence).",
                    "Brake policy under altered physics is explicit and fail-closed.",
                    "Exact mod name/version recorded; policy documented in both places.",
                    "No exceptions in either mod's logs during the matrix run."
                )
                Ev = @(
                    "Coexistence matrix with logs.",
                    "Policy documentation deltas.",
                    "Screenshots of altered-physics notices or pending notes."
                )
            },
            @{
                Key = "CT-038"; Title = "Research and validate current major cart/physics/inventory mod combinations"
                Type = "test"; Priority = "high"; Areas = @("interop"); Deps = @("CT-037")
                Goal = "Establish the real, current compatibility surface: research which cart/physics/inventory mods players actually run today, then validate the significant combinations — inventing names is prohibited."
                Scope = @(
                    "Research pass over current Thunderstore Valheim listings for maintained mods that alter carts, cart physics, item weights, or container behavior; record exact names, versions, and download-order significance. If research access is unavailable, record that as a hard fact and scope to locally known mods rather than guessing.",
                    "Select the significant combinations (documented selection rationale) and run the coexistence matrix in TCT-Compat for each.",
                    "Feed each finding into the CT-036 registry as a policy or a documented limitation.",
                    "Update package README compatibility section."
                )
                Ac = @(
                    "The researched list contains only verified names/versions with retrieval evidence (listing citations or local install proof).",
                    "Every selected combination has a completed matrix row: works / adapted / limited, with logs.",
                    "Registry/limitation updates merged for each finding.",
                    "README compatibility section matches the evidence."
                )
                Ev = @(
                    "Research notes with citations.",
                    "Combination matrix with logs.",
                    "Registry and README deltas."
                )
            },
            @{
                Key = "CT-039"; Title = "Deliver config/data migration, backup/recovery, and the sanitized support bundle"
                Type = "feature"; Priority = "high"; Areas = @("interop", "carts"); Deps = @("CT-038")
                Goal = "When versions change or data breaks, users keep their history and can hand over a privacy-safe diagnostic bundle."
                Scope = @(
                    "Versioned migration for config and sidecars with backup-before-migration and documented rollback.",
                    "Corruption recovery: detect broken sidecars, quarantine (never delete), continue with valid data, tell the user what happened.",
                    "Support bundle export (button-discoverable): versions, config, compatibility status, recent Teamster log lines, sidecar summaries — sanitized (no world names beyond hashes unless user opts in, no player names, no paths beyond profile-relative).",
                    "Tests: migration fixtures across versions, corruption injection, bundle sanitization audit."
                )
                Ac = @(
                    "Old-version fixtures migrate with backups; rollback procedure verified.",
                    "Injected corruption quarantines the bad file and preserves valid data.",
                    "Bundle sanitization test proves the exclusion list.",
                    "Recovery events surface to the user in plain language."
                )
                Ev = @(
                    "Migration/corruption test output.",
                    "Sample sanitized bundle.",
                    "Sanitization audit output."
                )
            },
            @{
                Key = "CT-040"; Title = "Run scale, corruption, and compatibility validation; package the v0.8 release candidate"
                Type = "release"; Priority = "critical"; Areas = @("release", "interop"); Deps = @("CT-039")
                Goal = "Prove Teamster at scale and under abuse, rerun the compatibility surface, and seal the internal v0.8 RC."
                Scope = @(
                    "Scale runs: maximum retained trips, worst-case manifest sizes, long-session sampling; record timings/allocations against budgets.",
                    "Corruption/recovery campaign rerun on fresh fixtures.",
                    "Compatibility matrix rerun for the researched set.",
                    "Standard suite, defect burn-down, regression.",
                    "Synchronize 0.8.0, validator, package, hash, changelog; append pending manual claims; close the controller on green."
                )
                Ac = @(
                    "Scale metrics within documented budgets.",
                    "Recovery and compatibility reruns green.",
                    "No open P0/P1 for sprint:teamster-v0.8.",
                    "0.8.0 RC sealed with recorded hash."
                )
                Ev = @(
                    "Scale measurement table.",
                    "Rerun campaign tables.",
                    "Validator/package output with ZIP SHA-256."
                )
            }
        )
    },
    @{
        Version = "0.9"; Name = "Public Beta Hardening"
        Promise = "A frozen, audited, rehearsed public beta: privacy-safe feedback, complete public docs and media, automated profiles, burned-down defects, and a sealed RC only the owner may publish."
        GateNote = "This sprint ends with a sealed public-beta RC and an owner packet. Thunderstore publication of the beta is Eren-only and happens outside the conveyor; conveyor work continues into v1.0 unless a hard stop applies."
        Gate = @(
            "Feature/default freeze recorded; only defect fixes land afterward inside this sprint.",
            "Privacy-safe feedback path documented and audited.",
            "Public docs/media/package/license/privacy/security audit complete.",
            "Install/upgrade/uninstall rehearsal green on automated profiles.",
            "Sealed v0.9 public-beta RC with recorded hashes and the owner approval packet."
        )
        Leaves = @(
            @{
                Key = "CT-041"; Title = "Freeze features and defaults; establish the privacy-safe feedback path"
                Type = "technical"; Priority = "critical"; Areas = @("release"); Deps = @("CT-040")
                Goal = "Declare the beta surface: freeze features and default config, and give beta users a feedback path that respects their privacy."
                Scope = @(
                    "Freeze record: the shipped feature list and every default value, committed; post-freeze changes inside v0.9 are defect fixes only.",
                    "Default audit: every default is safe, observational, and documented; mutating features are opt-in.",
                    "Feedback path: where beta users report (GitHub issues link in README and a visible About/Feedback button), what to include (the CT-039 support bundle), and what never to include; no telemetry, no phoning home.",
                    "Tests: default-config snapshot test to catch accidental default drift after the freeze."
                )
                Ac = @(
                    "Freeze document committed and referenced by the sprint controller.",
                    "Default snapshot test locks the frozen defaults.",
                    "Feedback button and README both route to the documented path.",
                    "Privacy audit confirms no automatic data egress exists."
                )
                Ev = @(
                    "Freeze document.",
                    "Snapshot test output.",
                    "Privacy audit note."
                )
            },
            @{
                Key = "CT-042"; Title = "Complete the public docs, media, package pages, license, privacy, and security audit"
                Type = "release"; Priority = "high"; Areas = @("release"); Deps = @("CT-041")
                Goal = "Make the public face true and complete: package README, changelog, media, license, privacy notes, and a security self-audit of everything shipped."
                Scope = @(
                    "Package README: accurate feature list with limitations, compatibility section from CT-038 evidence, install/uninstall, feedback path, AI-assisted disclosure per repository policy.",
                    "Media: current screenshots (and GIF where useful) produced from real gameplay; icon final at 256x256.",
                    "CHANGELOG complete from v0.1; LICENSE correct; privacy statement (what is stored locally, what never leaves).",
                    "Security self-audit: dependency pins, no secrets in repo/package, package copy-list minimal, adapter fail-closed review; findings fixed or filed.",
                    "Thunderstore categories set per repository convention including the AI Generated category."
                )
                Ac = @(
                    "Every README claim traces to shipped behavior or is listed as limitation.",
                    "Media reflects the current build (no mockups).",
                    "Audit checklist committed with all findings resolved or filed as defects.",
                    "Validator passes with the final metadata."
                )
                Ev = @(
                    "README/CHANGELOG/media deltas.",
                    "Audit checklist with outcomes.",
                    "Validator output."
                )
            },
            @{
                Key = "CT-043"; Title = "Automate clean/dev/compat/dedicated profiles and rehearse install/upgrade/uninstall"
                Type = "technical"; Priority = "high"; Areas = @("release"); Deps = @("CT-042")
                Goal = "Script the TCT profile family and rehearse the full user journey: fresh install, upgrade from every prior RC, and clean uninstall."
                Scope = @(
                    "Idempotent profile automation for TCT-Clean/TCT-Dev/TCT-Compat/TCT-Dedicated following the repository's existing profile tooling patterns; never touching real user profiles beyond the TCT family.",
                    "Rehearsal scripts/checklists: fresh install from the RC ZIP, upgrade v0.1->v0.9 chain (config/sidecar migrations exercised), uninstall leaving vanilla behavior and no orphan errors.",
                    "Each rehearsal records exact evidence; failures become defects.",
                    "Documentation: how the owner reruns everything."
                )
                Ac = @(
                    "Profile scripts are idempotent (second run changes nothing) and scoped to TCT profiles.",
                    "Fresh/upgrade/uninstall rehearsals each have recorded evidence; migrations ran where expected.",
                    "No rehearsal step requires undocumented manual fiddling.",
                    "Owner-facing rehearsal doc committed."
                )
                Ev = @(
                    "Profile script output (twice, proving idempotence).",
                    "Rehearsal evidence per journey.",
                    "Owner doc."
                )
            },
            @{
                Key = "CT-044"; Title = "Burn down beta defects and finalize the exact owner smoke checklist"
                Type = "test"; Priority = "critical"; Areas = @("release"); Deps = @("CT-043")
                Goal = "Close out every known in-scope defect and turn the accumulated pending-manual ledger into the exact, ordered checklist the owner will run."
                Scope = @(
                    "Defect sweep: triage every open Teamster defect; fix P0-P2 in scope, document deferred P3s with rationale.",
                    "Regression after the final fix batch.",
                    "Owner smoke checklist: compile every pending manual claim from v0.1-v0.9 into an ordered, timed, step-by-step in-game checklist with expected results per step (PRE_RELEASE_SMOKE_TEST.md pattern).",
                    "Dry-run the checklist's automatable preambles (profile setup, package install) to guarantee the owner starts from green."
                )
                Ac = @(
                    "Zero open P0/P1/P2 Teamster defects in scope; deferred P3 list documented.",
                    "The smoke checklist covers every pending manual claim (cross-referenced) with expected results.",
                    "Checklist preambles verified runnable.",
                    "Regression suite green after final fixes."
                )
                Ev = @(
                    "Defect closure table.",
                    "Committed smoke checklist with cross-reference table.",
                    "Regression output."
                )
            },
            @{
                Key = "CT-045"; Title = "Seal the v0.9 public-beta release candidate and owner packet"
                Type = "release"; Priority = "critical"; Areas = @("release"); Deps = @("CT-044"); ExtraLabels = @("gate:human-preview")
                Goal = "Produce the sealed public-beta RC and the complete owner packet. Publication is Eren-only; the conveyor does not publish."
                Scope = @(
                    "Synchronize 0.9.0 everywhere; validator; package build; record ZIP and DLL hashes; seal the RC (no further changes without a new RC).",
                    "Owner packet: RC identity (commit, hashes), campaign/defect summary, freeze record, smoke checklist, exact owner-only publish steps, and rollback/reapproval guidance following the Cartographer dossier pattern.",
                    "Verify the sealed ZIP installs into a fresh profile from scratch.",
                    "Hand off: the packet is committed and referenced from the sprint controller; conveyor proceeds to v1.0 work without publishing."
                )
                Ac = @(
                    "RC sealed with recorded hashes; fresh-profile install verified.",
                    "Owner packet complete per the dossier pattern.",
                    "No publish action occurred or is scheduled by automation.",
                    "Sprint controller gate green and closed."
                )
                Ev = @(
                    "Hash record and install evidence.",
                    "Committed owner packet.",
                    "Controller closure comment."
                )
            }
        )
    },
    @{
        Version = "1.0"; Name = "Stable Teamster"
        Promise = "Stable Teamster: the golden path proven end to end, budgets met, docs and accessibility signed off, and the sealed v1.0 RC waiting only for the owner's smoke test and publish."
        GateNote = "This sprint ends the conveyor: the sealed v1.0 RC and owner smoke packet are the final deliverable. The in-game smoke test and Thunderstore publication are Eren-only."
        Gate = @(
            "Golden path and v1.0 Definition of Done matrix complete and green where automatable.",
            "Full regression green across the standard profiles.",
            "Performance/memory/network/long-run budgets met with measurements.",
            "Final docs/localization/controller/accessibility/migration/compat sign-off recorded.",
            "Sealed v1.0 RC and owner packet delivered; publication owner-only."
        )
        Leaves = @(
            @{
                Key = "CT-046"; Title = "Audit the golden path and complete the v1.0 Definition of Done matrix"
                Type = "technical"; Priority = "critical"; Areas = @("release"); Deps = @("CT-045")
                Goal = "Define and audit the golden path — the end-to-end journey every stable user takes — and expand it into the v1.0 Definition-of-Done matrix."
                Scope = @(
                    "Golden path document: install, first cart, status/manifest, a planned haul with warnings, a descent with risk/brake, a recorded trip with quality/bottleneck, recovery guidance, uninstall.",
                    "V1_DEFINITION_OF_DONE matrix: every product promise mapped to its evidence source (test, campaign row, or smoke item) following the Cartographer pattern.",
                    "Audit current state against the matrix; every gap becomes a defect or a documented limitation.",
                    "No new features: this issue only proves or files."
                )
                Ac = @(
                    "Golden path and DoD matrix committed.",
                    "Every matrix row has evidence, a filed defect, or a documented limitation — no blanks.",
                    "Gap defects are labeled into this sprint."
                )
                Ev = @(
                    "Committed documents.",
                    "Gap/defect table."
                )
            },
            @{
                Key = "CT-047"; Title = "Run the full regression across every standard profile"
                Type = "test"; Priority = "critical"; Areas = @("release"); Deps = @("CT-046")
                Goal = "Rerun everything that can be rerun: unit suites, validators, campaigns, and compatibility matrices across the TCT profile family."
                Scope = @(
                    "Full automated suite (tests + validator + package) from a clean checkout.",
                    "Campaign reruns per sprint checklist on TCT-Dev; compatibility rerun on TCT-Compat; multiplayer rerun per CT-027/CT-030 scope; dedicated rerun on TCT-Dedicated.",
                    "Every failure is a defect fixed and re-regressed inside this sprint.",
                    "Results recorded in a single regression report keyed to the DoD matrix."
                )
                Ac = @(
                    "Automated suite green from clean checkout (output attached).",
                    "Campaign/compat/multiplayer reruns green or pending-listed with reasons.",
                    "Regression report committed and referenced by the DoD matrix.",
                    "Zero open P0/P1."
                )
                Ev = @(
                    "Suite output.",
                    "Regression report.",
                    "Defect closure table."
                )
            },
            @{
                Key = "CT-048"; Title = "Prove performance, memory, network, and long-run stability budgets"
                Type = "test"; Priority = "critical"; Areas = @("release"); Deps = @("CT-047")
                Goal = "Commit to numbers: define the v1.0 budgets and prove Teamster meets them in sustained play."
                Scope = @(
                    "Budget definitions: sampler cost per tick, steady-state allocation, panel update cost, sidecar IO latency, network-derived processing bounds, long-run (multi-hour) stability including log volume.",
                    "Measurement harness: automated where possible (domain benchmarks), structured manual capture for in-game (profiler/log-based), following the Cartographer budget-evidence pattern.",
                    "Long-run session: extended haul session with panels open; record frame-time observations, memory growth, log size.",
                    "Failures become defects; budgets or code adjust with documented rationale."
                )
                Ac = @(
                    "Every budget has a number and a measurement against it.",
                    "Long-run session evidence shows no unbounded growth or spam.",
                    "Domain benchmarks run repeatably on any machine.",
                    "Misses are fixed or explicitly re-budgeted with rationale."
                )
                Ev = @(
                    "Budget table with measurements.",
                    "Long-run session notes/log stats.",
                    "Benchmark output."
                )
            },
            @{
                Key = "CT-049"; Title = "Complete final docs, localization, controller, accessibility, migration, and compatibility sign-off"
                Type = "test"; Priority = "critical"; Areas = @("release", "ux"); Deps = @("CT-048")
                Goal = "Sign off every non-code surface for v1.0: documentation truth, catalog completeness, controller and accessibility coverage, migration chain, and the final compatibility statement."
                Scope = @(
                    "Docs truth pass: README, in-repo docs, and package pages against shipped behavior.",
                    "Localization completeness rerun; translator docs current.",
                    "Controller and accessibility audits rerun against the v0.7 bars.",
                    "Migration chain rerun v0.1->v1.0 on fixtures.",
                    "Compatibility statement finalized from CT-038/CT-040 evidence with versions rechecked.",
                    "Each area gets an explicit recorded sign-off line with evidence link."
                )
                Ac = @(
                    "Six sign-off lines recorded, each with evidence.",
                    "No doc claim without shipped behavior.",
                    "Migration chain green on fixtures.",
                    "Compatibility statement current."
                )
                Ev = @(
                    "Sign-off table.",
                    "Rerun outputs.",
                    "Doc deltas."
                )
            },
            @{
                Key = "CT-050"; Title = "Seal the v1.0 release candidate and deliver the owner smoke/publish packet"
                Type = "release"; Priority = "critical"; Areas = @("release"); Deps = @("CT-049"); ExtraLabels = @("gate:human-preview")
                Goal = "Produce the final sealed v1.0 RC and the complete owner packet, then stop: the in-game smoke test and Thunderstore publication are Eren-only."
                Scope = @(
                    "Synchronize 1.0.0 everywhere; validator; package; record commit, ZIP, and DLL hashes; seal the RC.",
                    "Owner packet: RC identity, DoD matrix state, regression/budget reports, the final smoke checklist (CT-044 base updated through v1.0), exact publish and post-publish steps (tagging concerned-teamster/v1.0.0, Thunderstore upload), and rollback guidance.",
                    "Fresh-profile install verification of the sealed ZIP.",
                    "End-of-conveyor report: everything delivered, everything pending the owner."
                )
                Ac = @(
                    "RC sealed with recorded hashes; fresh-profile install verified.",
                    "Owner packet complete; smoke checklist current through v1.0.",
                    "No publish/tag-of-stable action performed by automation.",
                    "Conveyor closes with the end report; only owner actions remain."
                )
                Ev = @(
                    "Hash record and install evidence.",
                    "Committed owner packet.",
                    "End-of-conveyor report."
                )
            }
        )
    }
)

# ---------------------------------------------------------------------------
# Existing-issue map (idempotency)
# ---------------------------------------------------------------------------

Write-Host "Loading existing issues from $Repository ..."
$existing = @(gh issue list --repo $Repository --state all --limit 1000 --json "number,title" | ConvertFrom-Json)
$titleToNumber = @{}
foreach ($item in $existing) { $titleToNumber[$item.title] = $item.number }

$keyToNumber = @{}
foreach ($item in $existing) {
    if ($item.title -match '^(CT-\d{3}):') { $keyToNumber[$Matches[1]] = $item.number }
    if ($item.title -match '^SPRINT Teamster v(\d+\.\d+):') { $keyToNumber["SPRINT-$($Matches[1])"] = $item.number }
}

function Format-Dep {
    param([string]$DepKey)
    if ($DepKey -eq "") { return "#107 (CT-OPS-001)" }
    $number = $script:keyToNumber[$DepKey]
    if ($null -ne $number) { return "#$number ($DepKey)" }
    return $DepKey
}

function New-TrackedIssue {
    param([string]$Title, [string]$Body, [string[]]$IssueLabels)
    if ($script:titleToNumber.ContainsKey($Title)) {
        Write-Host "Exists: $Title (#$($script:titleToNumber[$Title]))"
        return [int]$script:titleToNumber[$Title]
    }
    if ($script:DryRun) {
        Write-Host "[dry-run] create: $Title [$($IssueLabels -join ', ')]"
        return 0
    }
    $bodyFile = New-TemporaryFile
    try {
        Set-Content -Path $bodyFile -Value $Body -Encoding utf8 -NoNewline
        $ghArgs = @("issue", "create", "--repo", $script:Repository, "--title", $Title, "--body-file", $bodyFile.FullName)
        foreach ($label in $IssueLabels) { $ghArgs += @("--label", $label) }
        $url = @(gh @ghArgs | Where-Object { $_ }) | Select-Object -Last 1
        if ($LASTEXITCODE -ne 0 -or [string]$url -notmatch '/issues/(\d+)\s*$') { throw "Failed to create issue: $Title ($url)" }
        $number = [int]$Matches[1]
        $script:titleToNumber[$Title] = $number
        Write-Host "Created: #$number $Title"
        Invoke-Throttle
        return $number
    }
    finally { Remove-Item $bodyFile -ErrorAction SilentlyContinue }
}

# ---------------------------------------------------------------------------
# Body builders
# ---------------------------------------------------------------------------

function Build-ControllerBody {
    param([hashtable]$Sprint, [bool]$WithChildNumbers)
    $v = $Sprint.Version
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("## Release promise")
    $lines.Add("")
    $lines.Add($Sprint.Promise)
    $lines.Add("")
    $lines.Add("## Ownership and execution")
    $lines.Add("")
    $lines.Add("**Owner:** Claude Code")
    $lines.Add("**Operating contract:** $script:contractLine")
    $lines.Add("**Integration branch:** ``sprint/concerned-teamster-v$v`` — created on demand for multi-issue integration or RC sealing; routine leaves branch from ``main`` and merge to ``main`` after focused review.")
    $priorNumber = $script:keyToNumber["SPRINT-$($Sprint['Prior'])"]
    if ($null -ne $Sprint['Prior'] -and $null -ne $priorNumber) {
        $lines.Add("**Prior sprint:** #$priorNumber (SPRINT Teamster v$($Sprint['Prior'])) must pass its gate before this sprint's leaves begin.")
    }
    elseif ($null -ne $Sprint['Prior']) {
        $lines.Add("**Prior sprint:** SPRINT Teamster v$($Sprint['Prior']) must pass its gate before this sprint's leaves begin.")
    }
    else {
        $lines.Add("**Prior work:** kickoff #107 (CT-OPS-001) must be complete.")
    }
    $lines.Add("")
    $lines.Add("This sprint's gate is an **internal quality gate**: complete every leaf in dependency order, run every automatable check, file and fix in-scope defects, seal the sprint release candidate, close this controller, and continue immediately. Record non-blocking uncertainty in ``docs/mods/concerned-teamster/HUMAN_ATTENTION.md``. Work selection follows ``docs/mods/concerned-teamster/AUTONOMOUS_EXECUTION.md`` (lowest-numbered unblocked CT leaf; Cartographer public-beta P0/P1 regressions preempt).")
    if ($null -ne $Sprint['GateNote']) {
        $lines.Add("")
        $lines.Add($Sprint.GateNote)
    }
    $lines.Add("")
    $lines.Add("## Ordered leaves")
    $lines.Add("")
    $index = 1
    foreach ($leaf in $Sprint.Leaves) {
        $leafNumber = $script:keyToNumber[$leaf.Key]
        if ($WithChildNumbers -and $null -ne $leafNumber) {
            $lines.Add("$index. #$leafNumber — $($leaf.Key): $($leaf.Title)")
        }
        else {
            $lines.Add("$index. $($leaf.Key): $($leaf.Title)")
        }
        $index++
    }
    $lines.Add("")
    $lines.Add("## Sprint gate")
    $lines.Add("")
    $lines.Add("- [ ] All five leaves closed with recorded evidence.")
    foreach ($gateItem in $Sprint.Gate) { $lines.Add("- [ ] $gateItem") }
    $lines.Add("- [ ] No open P0/P1 defect labeled ``sprint:teamster-v$v``.")
    $lines.Add("")
    $lines.Add("## Defects")
    $lines.Add("")
    $lines.Add("File sprint defects as ``DEF-teamster-v$v-NNN`` with ``mod:teamster``, ``bug``, a ``severity:P0..P3`` label, and ``sprint:teamster-v$v``. Fix P0-P2 in scope before the gate; document deferred P3s.")
    return ($lines -join "`n")
}

function Build-LeafBody {
    param([hashtable]$Sprint, [hashtable]$Leaf, [int]$ControllerNumber)
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("## Sprint")
    $lines.Add("")
    $lines.Add("**Version:** v$($Sprint.Version) — $($Sprint.Name)")
    $lines.Add("**Owner:** Claude Code")
    if ($ControllerNumber -gt 0) { $lines.Add("**Sprint controller:** #$ControllerNumber") }
    else { $lines.Add("**Sprint controller:** SPRINT Teamster v$($Sprint.Version)") }
    $lines.Add("**Operating contract:** $script:contractLine")
    if ($Leaf.Deps.Count -eq 0) { $lines.Add("**Dependencies:** #107 (CT-OPS-001)") }
    else {
        $formatted = @($Leaf.Deps | ForEach-Object { Format-Dep $_ })
        $lines.Add("**Dependencies:** $($formatted -join ', ')")
    }
    $lines.Add("")
    $lines.Add("## Goal")
    $lines.Add("")
    $lines.Add($Leaf.Goal)
    $lines.Add("")
    $lines.Add("## Scope")
    $lines.Add("")
    foreach ($item in $Leaf.Scope) { $lines.Add("- $item") }
    $lines.Add("")
    $lines.Add("## Acceptance criteria")
    $lines.Add("")
    foreach ($item in $Leaf.Ac) { $lines.Add("- [ ] $item") }
    $lines.Add("")
    $lines.Add("## Required evidence")
    $lines.Add("")
    foreach ($item in $Leaf.Ev) { $lines.Add("- [ ] $item") }
    $lines.Add("")
    $lines.Add("## Autonomous execution rule")
    $lines.Add("")
    $lines.Add($script:autonomyRule)
    $lines.Add("")
    $lines.Add("## Definition of Done")
    $lines.Add("")
    $lines.Add($script:standardDoD)
    return ($lines -join "`n")
}

# ---------------------------------------------------------------------------
# Pass 1: controllers, then leaves (dependency numbers resolve in order)
# ---------------------------------------------------------------------------

$prior = $null
foreach ($sprint in $sprints) {
    $sprint["Prior"] = $prior
    $prior = $sprint.Version
}

foreach ($sprint in $sprints) {
    $v = $sprint.Version
    $controllerTitle = "SPRINT Teamster v${v}: $($sprint.Name)"
    $controllerLabels = @("mod:teamster", "owner-claude", "type:epic", "priority:critical", "area:release", "sprint:teamster-v$v")
    if ($v -in @("0.9", "1.0")) { $controllerLabels += "gate:human-preview" }
    $controllerBody = Build-ControllerBody -Sprint $sprint -WithChildNumbers $false
    $controllerNumber = New-TrackedIssue -Title $controllerTitle -Body $controllerBody -IssueLabels $controllerLabels
    $keyToNumber["SPRINT-$v"] = $controllerNumber

    foreach ($leaf in $sprint.Leaves) {
        $leafTitle = "$($leaf.Key): $($leaf.Title)"
        $leafLabels = @(
            "mod:teamster", "owner-claude",
            "type:$($leaf.Type)", "priority:$($leaf.Priority)",
            "sprint:teamster-v$v"
        )
        foreach ($area in $leaf.Areas) { $leafLabels += "area:$area" }
        if ($null -ne $leaf['ExtraLabels']) { $leafLabels += $leaf.ExtraLabels }
        $leafBody = Build-LeafBody -Sprint $sprint -Leaf $leaf -ControllerNumber $controllerNumber
        $leafNumber = New-TrackedIssue -Title $leafTitle -Body $leafBody -IssueLabels $leafLabels
        $keyToNumber[$leaf.Key] = $leafNumber
    }
}

# ---------------------------------------------------------------------------
# Pass 2: refresh controller bodies with child issue numbers
# ---------------------------------------------------------------------------

if (-not $DryRun) {
    foreach ($sprint in $sprints) {
        $v = $sprint.Version
        $controllerNumber = $keyToNumber["SPRINT-$v"]
        if ($null -eq $controllerNumber -or $controllerNumber -eq 0) { continue }
        $desired = Build-ControllerBody -Sprint $sprint -WithChildNumbers $true
        $current = (@(gh issue view $controllerNumber --repo $Repository --json body -q .body) -join "`n")
        if (($current -replace "`r`n", "`n").TrimEnd() -eq $desired.TrimEnd()) {
            Write-Host "Controller #$controllerNumber body current."
            continue
        }
        $bodyFile = New-TemporaryFile
        try {
            Set-Content -Path $bodyFile -Value $desired -Encoding utf8 -NoNewline
            gh issue edit $controllerNumber --repo $Repository --body-file $bodyFile.FullName | Out-Null
            Write-Host "Controller #$controllerNumber body refreshed with child numbers."
            Invoke-Throttle
        }
        finally { Remove-Item $bodyFile -ErrorAction SilentlyContinue }
    }
}

$leafCount = ($sprints | ForEach-Object { $_.Leaves.Count } | Measure-Object -Sum).Sum
Write-Host ""
Write-Host "Concerned Teamster GitHub setup complete: $($sprints.Count) sprint controllers, $leafCount leaves, $($labels.Count) labels ensured."
