using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>Every console instruction the product tells a player to type is a
/// verb the console actually accepts. The Engaged beat once told players to type
/// "cs_steward tend on", a verb that no longer existed, as the first thing a new
/// player read.</summary>
public sealed class ConsoleTextTests
{
    [Fact]
    public void Every_cs_steward_verb_the_product_mentions_is_one_the_console_accepts()
    {
        string command = File.ReadAllText(Path.Combine(ProductSources.Product, "Runtime", "StewardCommand.cs"));
        var accepted = Regex.Matches(command, "case \"([a-z]+)\":")
            .Cast<Match>()
            .Select(match => match.Groups[1].Value)
            .ToHashSet();
        Assert.Contains("retire", accepted);

        foreach (string path in Directory.EnumerateFiles(ProductSources.Product, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                         && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
        {
            foreach (Match mention in Regex.Matches(File.ReadAllText(path), "cs_steward ([a-z]+)"))
            {
                Assert.True(
                    accepted.Contains(mention.Groups[1].Value),
                    Path.GetFileName(path) + " tells a player to type \"cs_steward " + mention.Groups[1].Value +
                    "\", which the console does not accept");
            }
        }
    }

    [Fact]
    public void Tab_completion_offers_every_verb_the_help_names()
    {
        string command = File.ReadAllText(Path.Combine(ProductSources.Product, "Runtime", "StewardCommand.cs"));
        foreach (string verb in new[] { "area", "depot", "clear", "runes", "recruit", "dismiss", "resolve", "retire", "status" })
        {
            Assert.Contains("\"" + verb + "\",", command);
        }
    }

    [Fact]
    public void The_engaged_beat_points_at_the_switch_that_starts_her()
    {
        string intro = File.ReadAllText(
            Path.Combine(ProductSources.Product, "Domain", "Recruitment", "StewardIntroduction.cs"));
        Assert.Contains("[Steward] TendFiresEnabled = true", intro);
        Assert.DoesNotContain("cs_steward tend", intro);
    }
}
