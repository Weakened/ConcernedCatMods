using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheConcernedCat.ConcernedTeamster.Domain.Compatibility;

namespace ConcernedTeamster.Tests;

/// <summary>CT-036: the compatibility registry detects fake mods and applies
/// each policy outcome correctly, the status surface (log banner and panel
/// share one composition) reflects exactly the applied policies, and
/// unknown/not-found mods produce no lines — silence is the default. A
/// separate audit proves feature code never branches on a specific mod's
/// GUID; policies live only in the registry.</summary>
public class CompatibilityFrameworkTests
{
    private static readonly KnownModProbe CoexistProbe =
        new("com.example.coexist", "Coexist Mod", CompatibilityPolicy.Coexist,
            CompatibilityAffectedAspect.None, "no changes needed");

    private static readonly KnownModProbe AdaptProbe =
        new("com.example.adapt", "Adapt Mod", CompatibilityPolicy.Adapt,
            CompatibilityAffectedAspect.None, "readings adjust automatically");

    private static readonly KnownModProbe WarnProbe =
        new("com.example.warn", "Warn Mod", CompatibilityPolicy.Warn,
            CompatibilityAffectedAspect.None, "some readings may be inaccurate");

    private static readonly KnownModProbe[] FakeRegistry = { CoexistProbe, AdaptProbe, WarnProbe };

    [Fact]
    public void Evaluate_DetectsEachFakeMod_AndAppliesItsOwnPolicy()
    {
        var installed = new Dictionary<string, string?>
        {
            [CoexistProbe.Guid] = "1.0.0",
            [WarnProbe.Guid] = "2.3.4",
            // AdaptProbe is registered but not "installed" in this fake world.
        };

        IReadOnlyList<ModDetectionResult> results = CompatibilityRegistry.Evaluate(
            FakeRegistry, guid => installed.TryGetValue(guid, out string? version) ? (true, version) : (false, null));

        Assert.Equal(3, results.Count);

        ModDetectionResult coexist = results.Single(r => r.Probe == CoexistProbe);
        Assert.True(coexist.Found);
        Assert.Equal("1.0.0", coexist.DetectedVersion);
        Assert.Equal(CompatibilityPolicy.Coexist, coexist.Probe.Policy);

        ModDetectionResult warn = results.Single(r => r.Probe == WarnProbe);
        Assert.True(warn.Found);
        Assert.Equal("2.3.4", warn.DetectedVersion);
        Assert.Equal(CompatibilityPolicy.Warn, warn.Probe.Policy);

        ModDetectionResult adapt = results.Single(r => r.Probe == AdaptProbe);
        Assert.False(adapt.Found);
        Assert.Null(adapt.DetectedVersion);
    }

    [Fact]
    public void Evaluate_NeverQueriesAnythingOutsideTheRegistry()
    {
        var queriedGuids = new List<string>();
        CompatibilityRegistry.Evaluate(FakeRegistry, guid =>
        {
            queriedGuids.Add(guid);
            return (false, null);
        });

        Assert.Equal(FakeRegistry.Select(p => p.Guid).OrderBy(g => g), queriedGuids.OrderBy(g => g));
    }

    [Fact]
    public void Evaluate_EmptyRegistry_ProducesNoResultsAndNeverCallsLookup()
    {
        bool lookupCalled = false;
        IReadOnlyList<ModDetectionResult> results = CompatibilityRegistry.Evaluate(
            System.Array.Empty<KnownModProbe>(), _ => { lookupCalled = true; return (false, null); });

        Assert.Empty(results);
        Assert.False(lookupCalled);
    }

    [Fact]
    public void StatusSurface_ReflectsExactlyTheAppliedPolicies()
    {
        var installed = new Dictionary<string, string?>
        {
            [CoexistProbe.Guid] = "1.0.0",
            [AdaptProbe.Guid] = "5.0.0",
        };

        IReadOnlyList<ModDetectionResult> results = CompatibilityRegistry.Evaluate(
            FakeRegistry, guid => installed.TryGetValue(guid, out string? version) ? (true, version) : (false, null));

        IReadOnlyList<string> lines = CompatibilityStatusPresenter.ComposeDetectedLines(results);

        // Exactly the two detected mods, silent on the not-found WarnProbe.
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, line => line.Contains(CoexistProbe.DisplayName) && line.Contains(CoexistProbe.Description));
        Assert.Contains(lines, line => line.Contains(AdaptProbe.DisplayName) && line.Contains(AdaptProbe.Description));
        Assert.DoesNotContain(lines, line => line.Contains(WarnProbe.DisplayName));
    }

    [Fact]
    public void StatusSurface_NothingDetected_ComposesTheNoneDetectedLine()
    {
        IReadOnlyList<ModDetectionResult> results = CompatibilityRegistry.Evaluate(
            FakeRegistry, _ => (false, null));

        Assert.Empty(CompatibilityStatusPresenter.ComposeDetectedLines(results));
        Assert.NotEmpty(CompatibilityStatusPresenter.ComposeNoneDetectedLine());
    }

    [Fact]
    public void StatusSurface_EveryPolicyProducesDistinctNonEmptyWording()
    {
        // A copy-paste bug making two policies resolve to the same wording
        // (e.g. Adapt and Warn both rendering as "coexisting") would pass a
        // per-policy non-empty check but must fail this pairwise comparison.
        string[] lines = System.Enum.GetValues(typeof(CompatibilityPolicy))
            .Cast<CompatibilityPolicy>()
            .Select(policy =>
            {
                var probe = new KnownModProbe(
                    "com.example.policy", "Policy Mod", policy, CompatibilityAffectedAspect.None, "description");
                IReadOnlyList<ModDetectionResult> results = CompatibilityRegistry.Evaluate(
                    new[] { probe }, _ => (true, "1.0.0"));
                return Assert.Single(CompatibilityStatusPresenter.ComposeDetectedLines(results));
            })
            .ToArray();

        Assert.All(lines, Assert.NotEmpty);
        Assert.Equal(lines.Length, lines.Distinct().Count());
    }

    // ---- Audit: feature code never branches on a specific mod's GUID -------

    [Fact]
    public void ShippedRegistry_GuidsNeverAppearOutsideTheCompatibilityDomain()
    {
        // The shipped registry is non-empty since CT-037; this is the real
        // enforcement mechanism for "adding a mod policy must not require
        // touching feature code." The early-return only guards a future
        // state where the registry is temporarily empty again.
        if (CompatibilityKnownMods.Registry.Count == 0)
        {
            return;
        }

        string shippedRoot = Path.Combine(RepoRoot, "src", "ConcernedTeamster");
        // Trailing separator so this is a folder-membership check, not a
        // string-prefix match — otherwise a future sibling directory like
        // Domain/CompatibilityV2 would wrongly be treated as exempt too.
        string compatibilityDirPrefix =
            Path.Combine(shippedRoot, "Domain", "Compatibility") + Path.DirectorySeparatorChar;

        foreach (string file in Directory.EnumerateFiles(shippedRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.StartsWith(compatibilityDirPrefix, System.StringComparison.OrdinalIgnoreCase) ||
                file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            foreach (KnownModProbe probe in CompatibilityKnownMods.Registry)
            {
                Assert.False(text.Contains(probe.Guid),
                    $"{file} references known-mod GUID '{probe.Guid}' outside Domain/Compatibility.");
            }
        }
    }

    private static readonly System.Lazy<string> _repoRoot = new(() =>
    {
        var directory = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new System.InvalidOperationException("ConcernedCatMods.sln not found above test output.");
    });

    private static string RepoRoot => _repoRoot.Value;
}
