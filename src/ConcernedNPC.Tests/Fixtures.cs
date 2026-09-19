using TheConcernedCat.ConcernedNPC.Roles;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>A role, as a test writes one. Every field is settable so a test can
/// make exactly one thing wrong and leave the rest valid.</summary>
internal sealed class FakeRole : INpcRole
{
    internal FakeRole(NpcIdentity identity, NpcBodyContract body, INpcDataPaths? paths = null)
    {
        Identity = identity;
        Body = body;
        Paths = paths ?? FakePaths.Valid;
    }

    public NpcIdentity Identity { get; }

    public NpcBodyContract Body { get; }

    public INpcDataPaths Paths { get; }
}

/// <summary>A role whose own property getter throws, which is a broken role and
/// must not be a broken process.</summary>
internal sealed class ThrowingRole : INpcRole
{
    public NpcIdentity Identity => throw new InvalidOperationException("a role read its own identity badly");

    public NpcBodyContract Body => NpcBodyContract.ForPresentation();

    public INpcDataPaths Paths => FakePaths.Valid;
}

internal sealed class FakePaths : INpcDataPaths
{
    internal FakePaths(string root)
    {
        Root = root;
    }

    internal static FakePaths Valid { get; } =
        new FakePaths(OperatingSystem.IsWindows() ? @"C:\somewhere\a-role" : "/somewhere/a-role");

    public string Root { get; }

    public bool TryResolveFile(string purpose, out string absolutePath)
    {
        // No purposes exist yet; a role that keeps no file for one says so.
        absolutePath = string.Empty;
        return false;
    }
}

/// <summary>A role whose data-paths object throws when asked for its root.</summary>
internal sealed class ThrowingPaths : INpcDataPaths
{
    public string Root => throw new InvalidOperationException("the root could not be composed");

    public bool TryResolveFile(string purpose, out string absolutePath)
    {
        absolutePath = string.Empty;
        return false;
    }
}

internal static class Identities
{
    internal static NpcIdentity Thorstein => new NpcIdentity("foreman", "thorstein");

    internal static NpcIdentity Gunnar => new NpcIdentity("teamster", "gunnar");

    internal static NpcIdentity Steward => new NpcIdentity("steward", "steward");

    internal static NpcIdentity Hulgi => new NpcIdentity("cartographer", "hulgi");

    /// <summary>A registry with nothing in it. Never the shared one: a test that
    /// touched the process-wide registry would leak into every other test in the
    /// assembly, and the shared registry is the one thing in this package that
    /// is deliberately global.</summary>
    internal static NpcRoleRegistry EmptyRegistry() => new NpcRoleRegistry();
}
