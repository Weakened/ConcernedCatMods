[CmdletBinding()]
param(
    [string]$Repository = "Weakened/ConcernedCatMods"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) is required. Install it and run 'gh auth login'."
}

gh auth status | Out-Host
if ($LASTEXITCODE -ne 0) { throw "GitHub CLI is not authenticated." }

$labels = @(
    @{ Name = "owner-claude"; Color = "D4C5F9"; Description = "Primary implementation owner: Claude" },
    @{ Name = "owner-codex"; Color = "BFDADC"; Description = "Primary implementation/review owner: Codex" },
    @{ Name = "owner-shared"; Color = "F9D0C4"; Description = "Requires coordinated design or handoff" },
    @{ Name = "mod:cartographer"; Color = "8B6F47"; Description = "Concerned Cartographer" },
    @{ Name = "priority:high"; Color = "B60205"; Description = "Highest current priority" },
    @{ Name = "type:feature"; Color = "0E8A16"; Description = "User-facing feature" },
    @{ Name = "type:technical"; Color = "1D76DB"; Description = "Technical foundation or refactor" },
    @{ Name = "type:test"; Color = "FBCA04"; Description = "Testing, compatibility, or validation" }
)

foreach ($label in $labels) {
    gh label create $label.Name --repo $Repository --color $label.Color --description $label.Description --force | Out-Null
}

$issues = @(
    @{
        Title = "CC-001: Bootstrap and prove plugin/map lifecycle"
        Labels = "mod:cartographer,priority:high,type:technical,owner-claude"
        Body = @"
## Goal
Prove the plugin loads cleanly and survives map/world lifecycle events before adding more behavior.

## Acceptance criteria
- Debug build succeeds against the configured local Valheim/Jotunn environment.
- DLL deploys to the TCC-Dev profile.
- BepInEx log shows Concerned Cartographer 0.1.0 loaded.
- Enter world, open map, logout, re-enter without stale-overlay exceptions.
- Static repository validation passes.

## Definition of Done
PR includes exact commands, BepInEx log excerpt, game version, dependency versions, and manual lifecycle test evidence. No unrelated feature work.
"@
    },
    @{
        Title = "CC-002: Detect dirt Pathen and paved terrain beneath the player"
        Labels = "mod:cartographer,priority:high,type:feature,owner-claude"
        Body = @"
## Goal
Validate the terrain-paint adapter against the current Valheim build.

## Acceptance criteria
- Untouched and cultivated terrain are not classified as roads.
- Dirt Pathen classifies as Dirt.
- Paved terrain classifies as Paved.
- API failure logs once and disables probing for the session.
- No per-frame logging or whole-world scan.

## Definition of Done
Attach the completed terrain-classification matrix and relevant log evidence.
"@
    },
    @{
        Title = "CC-003: Render separate dirt and paved map overlays"
        Labels = "mod:cartographer,priority:high,type:feature,owner-codex"
        Body = @"
## Goal
Render recorded road segments through two Jotunn overlays.

## Acceptance criteria
- Dirt and paved strokes are visually distinct.
- Both render on full map and minimap.
- Layers can be toggled independently.
- Fog-of-war remains respected.
- Full texture rebuild occurs only on map/world initialization; new segments draw incrementally.

## Definition of Done
Provide screenshots/video, lifecycle evidence, and a short allocation/performance review.
"@
    },
    @{
        Title = "CC-004: Persist road atlas per world UID"
        Labels = "mod:cartographer,priority:high,type:technical,owner-codex"
        Body = @"
## Goal
Persist and restore road strokes without touching Valheim world saves.

## Acceptance criteria
- Sidecar file is under BepInEx config.
- Restart restores the atlas.
- World A data never appears in World B.
- Malformed rows are skipped without discarding valid rows.
- Writes use a temporary file and do not clear dirty state on failure.

## Definition of Done
Include restart, world-switch, malformed-row, and uninstall-safety evidence.
"@
    },
    @{
        Title = "CC-005: Capture successful Pathen and paving actions directly"
        Labels = "mod:cartographer,type:feature,owner-shared"
        Body = @"
## Goal
Record terrain-paint brush footprints when a player successfully creates or repaints a road, reducing the need to walk every point.

## Acceptance criteria
- Patch only a confirmed successful terrain-modification path.
- Failed/cancelled actions create no atlas data.
- Repainting/removal behavior is specified and tested.
- Multiplayer ownership and client/server behavior are documented before implementation.

## Definition of Done
Vertical-slice proof, compatibility review, and rollback strategy are recorded.
"@
    },
    @{
        Title = "CC-006: Backfill roads from loaded terrain chunks"
        Labels = "mod:cartographer,type:feature,owner-shared"
        Body = @"
## Goal
Recover pre-existing road candidates only from loaded heightmaps with bounded work.

## Acceptance criteria
- No world-file parsing and no global scan.
- Work is budgeted and cancellable.
- Unexplored map regions remain hidden.
- Broad cleared areas do not automatically become long roads without heuristics.
- Data is simplified/merged before persistence.

## Definition of Done
Profiling data, false-positive matrix, and old-world test evidence are attached.
"@
    },
    @{
        Title = "CC-007: Compatibility pass with Pinnacle and MapRoutes"
        Labels = "mod:cartographer,type:test,owner-codex"
        Body = @"
## Goal
Prove that Concerned Cartographer's road overlays coexist with established pin and route mods.

## Acceptance criteria
- Pinnacle editing/search/filter workflows still function.
- MapRoutes manual routes still render and persist.
- Concerned Cartographer overlay toggles remain usable.
- Logs contain no relevant patch/UI exceptions.
- Any limitation is documented in package README.

## Definition of Done
Complete compatibility matrix with exact versions, logs, and screenshots.
"@
    },
    @{
        Title = "CC-008: Design in-place marker editor and expanded legend"
        Labels = "mod:cartographer,type:feature,owner-shared"
        Body = @"
## Goal
Design the marker-management layer without duplicating or breaking established map mods.

## Acceptance criteria
- Competitive overlap with Pinnacle/PinAssistant is documented.
- Vanilla and foreign-mod pin ownership rules are defined.
- Interaction design covers edit name/icon/category/color/notes/status without delete/recreate.
- Controller and keyboard/mouse behavior are specified.
- Implementation is split into independently testable follow-up issues.

## Definition of Done
Approved UX/compatibility design document; no production UI code required in this issue.
"@
    }
)

$existing = @(gh issue list --repo $Repository --state all --limit 200 --json title | ConvertFrom-Json)
$existingTitles = @($existing | ForEach-Object { $_.title })

foreach ($issue in $issues) {
    if ($existingTitles -contains $issue.Title) {
        Write-Host "Skipping existing issue: $($issue.Title)"
        continue
    }

    gh issue create --repo $Repository --title $issue.Title --body $issue.Body --label $issue.Labels | Out-Host
}

Write-Host "GitHub labels and initial Concerned Cartographer backlog are ready."
