# Release policy

## Release sequence

1. Complete and merge the release milestone.
2. Update version and changelog in one release PR.
3. Run `python tools/validate_repo.py`.
4. Run `pwsh scripts/package.ps1 -Configuration Release`.
5. Import the generated ZIP into a fresh mod-manager profile.
6. Complete the manual release checklist in `TEST_PLAN.md`.
7. Run `pwsh scripts/publish.ps1 -Version X.Y.Z`.
8. Create and push the namespaced Git tag.
9. Monitor Thunderstore comments/issues and BepInEx logs from early users.

## Rollback

Thunderstore versions are immutable. A bad release is handled by:

- immediately deprecating the package version/listing when appropriate;
- fixing forward with a higher patch version;
- documenting the failure and prevention step in the changelog/issue.

Never overwrite an existing ZIP and assume Thunderstore will update it.
