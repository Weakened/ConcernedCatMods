# Concerned Steward — recorded owner decisions

Each entry is an authority this product was granted, with its date, its scope, what it does not grant, and where it is
enforced. Nothing here widens any other product's authority.

## S1. Sunniva's own worker body may leave the world through one guarded retire verb

Status: **accepted**. Owner decision **2026-10-28**, given directly to the completion lead and relayed to the Steward lane
(#382, home #340).

**Why it was asked.** The owner wants Sunniva to rest as a player-style presentation figure and work as her worker
body, never both at once (Concerned NPC's `ForWorkerWithPresentation` seam, #443). Handing back from the worker body to
the figure means her worker body has to leave the world. Until this decision Concerned Steward could not remove a body
at all.

**What is granted.** `cs_steward retire` may remove **Sunniva's own worker body** — the network object of a
`CS_Steward` body this game owns — through vanilla's own `ZNetView.Destroy()`, **only** when:

- her pack, read from the body's own record, holds nothing, and that record is readable (loaded, not inert, and its
  last change reached her own object — unknown is never empty);
- the fuel ledger says she carries nothing;
- no maintenance round is open: the loop is not mid-round and not stopped for attention, the durable maintenance plan
  has no unsettled run, and the record has no unreceipted step;
- no fuel she took is unaccounted for.

Any one of those witnesses refuses on its own. Her employment, her record, her quest and her designations are
untouched; `cs_steward recruit` brings her body back.

**What is not granted.**

- no forcing word: unlike Gunnar she can always be emptied (every round returns its remainder to the marked chest,
  and a stopped round is resolved by `cs_steward resolve`), so no carried fuel is ever destroyed by this verb;
- no removal of any other object — never a vanilla object, never another product's body, never one looked up rather
  than handed in from her own census;
- no ownership takeover; a body another peer owns is refused;
- no item drop or item spawning; death keeps its existing path and is not touched by this decision;
- no change to her identity (`steward/steward`), prefab name (`CS_Steward`) or saved keys (`tcc.steward.*`).

**Where it is enforced.**

- `src/ConcernedSteward/Domain/Retirement/StewardRetirement.cs` — the decision (`Decide`, `Allows`).
- `src/ConcernedSteward/Runtime/StewardRetirementPort.cs` — the one removal, `view.Destroy()` on `body.View`, below
  `StewardRetirement.Decide(` and a refusing `if (!StewardRetirement.Allows(`.
- `tools/validate_repo.py` "[steward] S1 retire audit" — pins every destruction in the product by file, every routed
  one and every body removal; refuses, in the raw text of every product source, any line whose first non-blank character
  is `#` (every directive, `#region` and `#pragma` included, whatever whitespace or line ending C# accepts in front of
  it), any `Conditional` attribute, any `\u`/`\U` escape (C# reads one inside an identifier as the letter it
  names), and any U+0085, U+2028 or U+2029 (C# ends a `//` comment there and the audit's
  comment stripping does not), since the plugin (net48) and the tests (net10.0) compile the same source - this
  covers `src/ConcernedSteward` only; the Concerned NPC library and shared sources it builds on are not scanned; pins the port's `Retire(...)` as a method whose braces balance, with no lambda, local
  function, delegate, interpolated string, `goto` or deconstruction, that says Retired once, as its last statement, where each of these appears once, whole, as its own statement at the
  method's top level (no braceless `if`, `else`, `while` or `for` in front): the decision on `facts`, the refusal on
  `verdict` (`verdict` spelled nowhere else but the sentence), `ZNetView? view = body.View;`, and the validity check -
  each refusing block exactly one `answer = ...;` and its `return` (Refused, NotOurs), nothing else; the removal is
  the one `view.Destroy();`, directly in the top-level `try`, and `view` is spelled nowhere else; the `body` parameter is never reassigned or passed by reference; and the port is
  named once, by `StewardRuntime.Retire` (a call or a method group), with no file importing it through `using`. It
  is a text audit: it establishes where the refusal is written, not that control flow obeys it (and the tests run on
  net10.0 only, so code that branches on the runtime it runs on is a reviewer's job); it does not follow
  data flow, so the values the facts are built from (her pack count, its readability, the ledger) are proved by the
  port tests, one witness at a time, not by the audit; and it cannot see a destruction reached through a delegate, a
  method group or reflection.
- `src/ConcernedSteward.Tests/StewardRetirementTests.cs` — control flow over the stubbed game.
- `tools/tests/test_steward_retire_audit.py` — forty plants, each a way a body could leave the world outside
  the verb, each verified to pass the validator without this rule (main `d72b397`; main `0171cf6` for the twelve after it,
  main `7900862` for the last) and to be
  refused with it. Twenty-nine
  of them were found by successive independent reviews, each getting past the version of the rule it was found against
  and added with the fix that closes it: a reassigned view, a swapped body, a `using static`, a `using` alias and a
  method group; a local function or lambda whose own parameter is `view` (two shapes) and a deconstruction into
  `view`; a misindented brace ending the audited text, a `goto` past the guard and a guard in a braced condition; a
  braceless `if` or an `else` in front of the refusal, a braceless `if` in front of the ownership check and an
  overwritten verdict; a conditional `return` inside the refusal and inside the ownership check;
  `#if` around the guard and inside the decision, an interpolation hiding a verdict overwrite, and an early
  Retired while her body still stands; a directive hidden behind `#region x /*`, one after a form feed, and a
  `[Conditional]` attribute; code after a U+2028 inside a `//` comment; a `List<int?>` in an
  interpolation that made the shared comment stripper read the format colon as code (fixed in the stripper, which
  the Teamster audits share; a Teamster plant was added too); a verbatim string opening with an escaped quote that
  the same stripper read as a raw string (likewise fixed there, with a Teamster plant); a
  `\u` escape spelling `Destroy` inside an identifier.
- The port reads her pack from the body itself; a pack the caller states in the ledger can only add a reason to
  refuse (more held, or unreadable), never remove one.
- `StewardBody.FindLive` skips a body whose network object is no longer valid, so for the rest of the frame in which
  she is retired nothing can adopt the body that is going.

**Known limits of this text audit.** Ten successive independent reviews each found another spelling of the same
destruction that a text audit could not see, and each was closed with a refusal and a plant. That does not converge,
so - as for Teamster's audits in `CLAUDE.md` - the remaining limits are stated rather than chased:

- it establishes where the refusal is written, not that control flow obeys it; that is `StewardRetirementTests`, which
  run on net10.0 only, so code that branches on the runtime it runs on is a reviewer's job;
- it does not follow data flow: the values the facts are built from are proved by the port tests, one witness at a time;
- it cannot see a destruction reached through a delegate, a method group or reflection;
- it reads names as text, so a Unicode formatting character (category Cf, e.g. a soft hyphen U+00AD or U+200B) written
  raw inside an identifier - which C# ignores when comparing names - would spell `Destroy` or a second `view` that no
  pattern here matches (found by the final review of `15eda77`; not refused, recorded here);
- its scope is `src/ConcernedSteward`; the Concerned NPC library and shared sources it builds on are not scanned.

**Still owner go-around pending:** the retire and re-recruit round trip in a running game.

## S2. Sunniva rests as a player-style figure and works as her worker body, never both

Status: **accepted**. Owner decision **2026-10-27**, given to the completion lead (#382).

**What is granted.** While Sunniva has no worker body in the world and nothing is open, she is shown at her home post
(the supply chest, else the settlement centre) as a player-style figure built from the game's own character model by
Concerned NPC (`NpcPresentationBody`, dressed by `NpcFigureDresser`): female, Matronly hair, fair, light blonde, in
leather. Her role's contract is Concerned NPC's `ForWorkerWithPresentation`, so the library's arbiter refuses the
figure while her worker lease is held and the worker lease while the figure's is held.

**What is not granted.** No live `Player` is instantiated and stripped; the figure has no network view, character, AI
or collider, is seen only on the host's machine, and changes nothing in the world. Identity `steward/steward`, prefab
`CS_Steward` and keys `tcc.steward.` are unchanged.

**Where it is enforced.** `Domain/Npc/SunnivaBodyPlan.cs` (a saved worker body anywhere, an unidentified one or a
failed count shows nothing), `Domain/Npc/SunnivaFigureLease.cs`, `Runtime/SunnivaPresentation.cs` (`YieldToWorker`
before every worker claim and spawn, on world unload and while the runtime is off), `[Appearance] ShowAtHome`;
`SunnivaFigureTests`. **Owner go-around pending:** smoke test section 3b.

## S3. Her own hair on her worker body

Status: **accepted**. Owner decision **2026-10-29** (working bodies try player hair/beard pieces on the existing
Dverger worker body behind a setting, plain Dverger as the fallback; no prefab rebase or migration).

**What is granted.** While she works, her Matronly hair is put on her own saved `CS_Steward` worker body, locally, by
Concerned NPC's `NpcWorkerDresser`, which refuses any other body; a piece that will not attach or fit is taken off.

**What is not granted.** The dresser adds no beard and no garments (her leather is the existing `VisEquipment`
look), and changes no prefab, saved data, network object or world state.

**Where it is enforced.** `Domain/Appearance/SunnivaLookPlan.cs` (`WorkerSpec`), `Runtime/SunnivaPresentation.cs`
(`OnWorkerBodyLoaded` / `OnWorkerBodyGone`), `Runtime/StewardRuntime.cs` (`BindBody`, `RefreshCensus`),
`[Appearance] WorkerLook`; `SunnivaFigureTests`. **Owner go-around pending:** smoke test 3b, item 5.
