# Concerned Cartographer — AI-Assisted Development and Provenance

## Summary

Concerned Cartographer is a human-directed project with material assistance from AI coding agents.

**Project creator / product owner / maintainer:** Eren Cansunar, publishing as **The Concerned Cat**.

The original product concept, feature selection, acceptance decisions, testing direction, release decisions, repository organization and ongoing maintenance are human-directed. AI coding agents have materially assisted implementation, refactoring, tests, research and documentation.

## Why this disclosure exists

This document:

1. transparently discloses AI assistance;
2. documents how generated changes are reviewed;
3. prevents future maintainers from assuming every implementation detail was personally hand-written;
4. preserves a provenance trail for release/legal/compliance discussions.

## AI is not the authority

The repository, issue history, human decisions, tests and release evidence are authoritative.

An AI agent may propose architecture, write/refactor code, write tests, investigate errors, prepare docs, and review diffs.

An AI agent does **not** establish that something works merely by saying it works. For Valheim-specific behavior, actual game evidence wins.

## Required review standard

Material AI-generated changes should not merge solely because they compile.

Review must consider:

- does the code implement the approved issue?
- are game APIs/method signatures real?
- are reflection/Harmony targets validated against the tested Valheim version?
- is deterministic logic covered by tests?
- can failure corrupt atlas/world data?
- can it reveal unexplored information?
- can stale multiplayer state regress newer state?
- are loops/allocations bounded?
- is logging rate-limited?
- are migrations backed up/recoverable?
- was third-party code/assets copied without compatible licensing?
- did the agent modify unrelated files?

## Preferred architecture for AI-assisted work

```text
Valheim state
   ↓
narrow adapter
   ↓
simple domain value
   ↓
pure deterministic logic + tests
   ↓
render/persistence/network adapter
```

This limits the surface on which a generated change can invent or misuse a game API.

## Public release disclosure

Thunderstore releases should keep the appropriate **AI Generated** disclosure/category when materially AI-assisted.

Suggested wording:

> Concerned Cartographer is created and maintained by Eren Cansunar / The Concerned Cat. AI coding agents materially assisted implementation, tests, research and documentation. Releases are reviewed and validated through the project's test/release process.

## Copyright note

Copyright law around AI-assisted works is fact-specific and varies by jurisdiction.

In the United States, copyright protection requires human authorship; purely AI-generated expression is not protected merely because a person supplied a prompt. Human-authored code, selection, arrangement, revisions, integration, documentation and other sufficiently creative human contributions may still be protected.

Do not make false authorship statements in a registration or legal filing. If registering a work containing material AI-generated content, review current U.S. Copyright Office guidance or consult an attorney about what human-authored material to claim and what AI-generated material should be disclaimed.

This file is documentation, not legal advice.

## Provenance evidence

The strongest practical provenance trail is:

- Git commit history;
- dated GitHub issues/PRs;
- release tags;
- GitHub Releases;
- changelog;
- package hashes;
- `LICENSE`;
- `NOTICE.md`;
- archived release artifacts;
- human smoke-test evidence.

Do not rewrite public history or force-push release tags/branches.

## Contributor AI policy

Contributors using AI should:

1. disclose material AI assistance in the PR;
2. remain responsible for submitted code;
3. review generated output before submitting;
4. provide tests/evidence;
5. never paste secrets/private data into prompts;
6. verify third-party licensing;
7. avoid copying/generating protected assets without permission.

A contribution is evaluated on correctness, safety and provenance, not on whether AI was used.
