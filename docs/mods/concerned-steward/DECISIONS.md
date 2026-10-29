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
  one, every body removal, the guard above the removal, that the removal acts on her own body's view (declared from
  `body.View`, and spelled nowhere but that declaration, its validity check and the one `view.Destroy();`, in a method
  with no lambda, local function, delegate or deconstruction; the `body` parameter never reassigned or passed by
  reference), and that the port
  is named once, by `StewardRuntime.Retire` (a call or a method group), with no file importing it through `using`.
  It is a text audit: it establishes where the refusal is written, not that control flow obeys it.
- `src/ConcernedSteward.Tests/StewardRetirementTests.cs` — control flow over the stubbed game.
- `tools/tests/test_steward_retire_audit.py` — twenty plants, each a way a body could leave the world outside the
  verb, each verified to pass the validator without this rule (main `d72b397`) and to be refused with it; five of them
  (a reassigned view, a swapped body, a `using static`, a `using` alias, a method group) also got past the first
  version of the rule, and four more (a local function or lambda whose own parameter is `view`, in two shapes, and a
  deconstruction into `view`) got past the second; each was added with the fix that closes it.
- The port reads her pack from the body itself; a pack the caller states in the ledger can only add a reason to
  refuse (more held, or unreadable), never remove one.
- `StewardBody.FindLive` skips a body whose network object is no longer valid, so for the rest of the frame in which
  she is retired nothing can adopt the body that is going.

**Still owner go-around pending:** the retire and re-recruit round trip in a running game.
