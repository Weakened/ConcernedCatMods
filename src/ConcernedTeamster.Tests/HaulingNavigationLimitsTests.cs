using System.Globalization;
using System.Reflection;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;
using TheConcernedCat.Workers;
using static ConcernedTeamster.Tests.NavigationFixtures;

namespace ConcernedTeamster.Tests;

/// <summary>#314: every hauling bound is a <see cref="HaulLimits"/> value with a
/// recorded rationale (docs/mods/concerned-teamster/CART_ROUTES.md), and the
/// probe budget covers the leg it was sized for.</summary>
public class HaulingNavigationLimitsTests
{
    [Fact]
    public void EveryHaulLimitHasARationaleRowWithItsDefaultValue()
    {
        string document = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "mods", "concerned-teamster", "CART_ROUTES.md"));
        HaulLimits defaults = HaulLimits.Default.Validate();
        var missing = new List<string>();

        foreach (PropertyInfo property in typeof(HaulLimits).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            object? value = property.GetValue(defaults);
            string text = value switch
            {
                float single => single.ToString(CultureInfo.InvariantCulture),
                int whole => whole.ToString(CultureInfo.InvariantCulture),
                _ => throw new InvalidOperationException(property.Name + " has an unexpected type"),
            };

            string row = "| `" + property.Name + "` | " + text + " | ";
            if (!document.Contains(row, StringComparison.Ordinal))
            {
                missing.Add(row);
            }
        }

        Assert.True(missing.Count == 0, "CART_ROUTES.md lacks rationale rows: " + string.Join(", ", missing));
    }

    [Fact]
    public void AFullLengthLegWithTwoCornersAndRepairsFitsTheProbeBudget()
    {
        HaulLimits limits = HaulLimits.Default;
        var (planner, world, paths) = Planner(limits);
        paths.Corners = new List<WorkPoint> { P(0, 0), P(20, 0), P(20, 20), P(44, 20) };
        world.Obstacles.Add(FakeObstacle.Pillar("post inside the first turn", 18f, 2f, 0.3f));
        world.Obstacles.Add(FakeObstacle.Pillar("post inside the second turn", 22f, 18f, 0.3f));

        CartRoutePlan plan = planner.Plan(Request(P(0, 0), P(44, 20)), 0f);

        Assert.True(plan.IsSuitable, planner.LastAssessment!.Describe());
        Assert.True(plan.LengthMetres > limits.MaxLegMetres - VanillaCart.HitchLengthMetres - 1f);
        Assert.True(planner.LastAssessment.Costs.Repairs >= 1, planner.LastAssessment.Describe());
        Assert.InRange(planner.LastAssessment.Costs.ClearanceProbes, 1, limits.ClearanceProbesPerPlan);
    }

    private static string RepoRoot()
    {
        for (DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
