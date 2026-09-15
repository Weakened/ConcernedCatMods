# Concerned Cat mod portfolio

**Status of this document.** The ordering and the original one-line promises
below are a **recovery synopsis** of `docs/MOD_PORTFOLIO_2026.md` as it existed
inside `ConcernedCat-OvernightKit.zip` (saved 2026-08-27; the embedded document
is marked *Last reviewed: 2026-08-26*; archive SHA256
`6f925163ca5f1389f76e0142421903661c51460097d5b954c61d978ae9a7af61`). It was
recovered from that archive, **not** from this repository's history — no
matching portfolio content was found in the checkout or its tracked text
history. It is reproduced here because the order is a real prior decision worth
keeping, and it is labelled rather than presented as continuous history.

Everything under *Current status* is this repository as it stands today and is
maintained normally.

---

## Recovered priority order after Cartographer (2026-08-26)

| Rank | Product | Original direction |
|---|---|---|
| 1 | Concerned Teamster | Cart load, grade and route bottlenecks without rewriting vanilla physics |
| 2 | **Concerned Foreman** | Causal structural, shelter, comfort and dependency explanations |
| 3 | Concerned Steward | Base maintenance intelligence, real-resource depots, priorities and rules |
| 4 | Concerned Homesteader | Fields, crop state, seed reserves and harvest forecasts |
| 5 | Concerned Quartermaster | Expedition readiness, recursive requirements, staging and party preparation |
| 6 | Concerned Harbormaster | Harbour and fleet operations, repositioned to a lower priority |

Table order is the saved priority, not the order the ideas were written down.
Ranks 3–6 are **backlog concepts**, not commitments: no identity, GUID, issue
key or scope has been reserved for any of them, and none should be started from
this table alone.

---

## Current status

| Product | Version | State | Docs |
|---|---|---|---|
| Concerned Cartographer | 1.1.0 candidate (1.0.4 published) | Public beta; companion epic #264 implemented, live observation outstanding | [PROJECT](mods/concerned-cartographer/PROJECT.md) |
| Concerned Teamster | 1.0.4 | v1.0 conveyor complete and stopped; maintenance only | [PROJECT](mods/concerned-teamster/PROJECT.md) |
| **Concerned Foreman** | — | **Active target.** Building diagnostics plus the opt-in settlement runtime (#270, #273) | [PROJECT](mods/concerned-foreman/PROJECT.md) |

Shared infrastructure, owned by no single product:

- [`docs/COMPANIONS_ARCHITECTURE.md`](COMPANIONS_ARCHITECTURE.md) — the
  source-shared companion layer under `src/Shared/Companions`, adopted by one
  `<Compile Include>` line.
- [`COMPANION_INTEGRATION_RECIPE.md`](mods/concerned-cartographer/COMPANION_INTEGRATION_RECIPE.md)
  — how the next product adopts it.

Products never reference each other. `tools/validate_repo.py` enforces that on
every build.

---

## Original Foreman seeds, preserved

The recovered archive carried five seeds per product. Foreman's are reproduced
here because CF-001 is being worked from them:

1. **CF-001** — Validate the building-diagnostics gap and lock the product contract.
2. **CF-002** — Prove structural support-chain tracing from a selected piece to ground.
3. **CF-003** — Prototype structural heatmap, weakest-link and demolition dependency preview.
4. **CF-004** — Research and spike station shelter and comfort diagnostics.
5. **CF-005** — Create the v1 roadmap and test matrix.

**The archive's installer was deliberately not run.** It carries historical
`CT-001..CT-005` seeds whose identifiers now collide with completed Teamster
work, and bulk-importing all thirty seeds would manufacture five products that
do not exist. Foreman's five are transcribed by hand; the other twenty-five stay
in the archive.
