# Concerned Cartographer Documentation

Concerned Cartographer is a Valheim living-atlas mod created and maintained by **Eren Cansunar / The Concerned Cat**, with material assistance from AI coding agents.

## Start here

### Players / server owners

- [`PROJECT.md`](PROJECT.md) — product promise, scope and roadmap
- [`TROUBLESHOOTING.md`](TROUBLESHOOTING.md) — logs, recovery and common failures
- [`PRE_RELEASE_SMOKE_TEST.md`](PRE_RELEASE_SMOKE_TEST.md) — release-quality manual test matrix

### Developers / contributors

- [`DEVELOPER_GUIDE.md`](DEVELOPER_GUIDE.md) — clean-machine setup, build, test and deploy
- [`CODEBASE_GUIDE.md`](CODEBASE_GUIDE.md) — subsystem and class-by-class source map
- [`ARCHITECTURE.md`](ARCHITECTURE.md) — architectural contracts and deeper design decisions
- [`DATA_FORMATS.md`](DATA_FORMATS.md) — sidecars, schemas, migrations and recovery rules
- [`PIN_WORKBENCH_DESIGN.md`](PIN_WORKBENCH_DESIGN.md) — pin ownership/editing UX contract
- [`TEST_PLAN.md`](TEST_PLAN.md) — integration and release test plan

### Maintainers / release

- [`AI_DEVELOPMENT.md`](AI_DEVELOPMENT.md) — AI provenance and review policy
- [`V1_RELEASE_PREP.md`](V1_RELEASE_PREP.md) — v1 provenance, authorship and release checklist
- [`RELEASE_DOSSIER.md`](RELEASE_DOSSIER.md) — the v1.0 release-candidate handoff (identity, hashes, evidence, post-smoke commands)
- [`V1_DEFINITION_OF_DONE.md`](V1_DEFINITION_OF_DONE.md) — the v1 capability matrix
- [`HUMAN_ATTENTION.md`](HUMAN_ATTENTION.md) — decisions deferred by autonomous development
- root [`NOTICE.md`](../../../NOTICE.md) and [`AUTHORS.md`](../../../AUTHORS.md) — attribution
- root [`CONTRIBUTING.md`](../../../CONTRIBUTING.md) and [`SECURITY.md`](../../../SECURITY.md)

## Documentation rule

Documentation is part of the product.

Any PR that changes persistent data, ownership/synchronization semantics, public commands, configuration, compatibility requirements, class responsibilities or release behavior should update the corresponding documentation in the same change.

Before a stable release, compare the final source tree against `CODEBASE_GUIDE.md`; undocumented architecture is a release defect.
