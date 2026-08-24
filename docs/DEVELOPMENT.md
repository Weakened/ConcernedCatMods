# Development conventions

## Branches

```text
feat/cc-###-short-description
fix/cc-###-short-description
chore/cc-###-short-description
```

## Commits

Use Conventional Commit-style messages with the mod scope:

```text
feat(cartographer): add dirt road overlay
fix(cartographer): isolate road data by world UID
chore(repo): add package validation
```

## Pull requests

Every PR must identify:

- the issue and acceptance criteria addressed;
- tests and commands run;
- manual in-game evidence;
- performance observations;
- compatibility implications;
- known limitations.

## Versioning

Each package uses independent `Major.Minor.Patch` versions. The monorepo tag contains the package slug:

```text
concerned-cartographer/v0.1.0
```

## Dependency policy

- Pin Thunderstore dependencies in `thunderstore.toml`.
- Pin the Jötunn NuGet package in the C# project.
- Update deliberately in a dedicated issue after checking Jötunn/Valheim release notes.
- Never commit game or loader DLLs.

## AI-assisted development

Agents may write or review code, but a human remains responsible for:

- approving scope and architecture;
- reading every diff;
- running the mod in Valheim;
- validating multiplayer/compatibility claims;
- deciding whether and when to publish;
- accurately disclosing AI assistance on Thunderstore.
