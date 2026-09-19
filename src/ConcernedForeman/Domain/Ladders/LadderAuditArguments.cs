using System.Globalization;

namespace TheConcernedCat.ConcernedForeman.Domain.Ladders;

/// <summary>Where `cf_ladders` reads its subcommand and its radius from
/// (CF-LAD-004, `docs/mods/concerned-foreman/LADDERS.md` §3 L3, which is the
/// procedure of record for observing whether vanilla ladders stack).
///
/// Two positional reads, and the shipped command had both of them one place
/// too far along: it took the subcommand from `args[1]` and the radius from
/// `args[2]`, as though its own name were still at the front of the array. It
/// is not. The vanilla registrar strips it before calling `Run`
/// (`VanillaConsoleCommands.Skip`), Jötunn's fallback path strips it too, and
/// every other console command in this repository reads its subcommand from
/// `args[0]`.
///
/// The cost was not a visible error, which is why it survived. `cf_ladders
/// snaps` found nothing at index 1, fell back to `list`, and printed a
/// credible prefab table with no snap-point section at all - which reads
/// exactly like "this piece has no snap points", the confidently wrong answer
/// to the one question that decides whether the feature needs a custom ladder
/// piece. `cf_ladders here 24` answered "Unknown subcommand".
///
/// So the two reads live here, with no Unity or Jötunn type anywhere near
/// them, where a test can hold them to their positions. The audit itself needs
/// a loaded world and cannot be tested; deciding which argument is which does
/// not, and never did.</summary>
internal static class LadderAuditArguments
{
    /// <summary>Every loaded prefab carrying a <c>Ladder</c>.</summary>
    internal const string List = "list";

    /// <summary>The ladders standing around the player.</summary>
    internal const string Here = "here";

    /// <summary>The same, plus the snap points that decide stacking.</summary>
    internal const string Snaps = "snaps";

    /// <summary>What the audit does when nobody said. `list` needs no world
    /// position and no ladder nearby, so it is the one that always has
    /// something to say.</summary>
    internal const string Default = List;

    /// <summary>The radius `here` and `snaps` measure to when none is given.
    /// Twelve metres is a building's width: far enough to catch the run you
    /// are standing at, close enough that the report stays readable.</summary>
    internal const float DefaultRadius = 12f;

    /// <summary>The largest radius accepted. Past this the audit is describing
    /// every ladder in two zones and nobody reads the result.</summary>
    internal const float MaximumRadius = 64f;

    /// <summary>The subcommand, which is the first argument.
    ///
    /// A word nobody recognises comes back as it stands rather than corrected
    /// to <see cref="Default"/>. That is the whole point: the caller refuses
    /// it by name, and a refusal a person can read beats an audit that quietly
    /// answers a different question.</summary>
    internal static string Subcommand(string[]? args) =>
        args != null && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
            ? args[0].ToLowerInvariant()
            : Default;

    /// <summary>Whether the audit has a subcommand by that name. The three the
    /// <c>Help</c> text advertises, and nothing else.</summary>
    internal static bool IsKnown(string? subcommand) =>
        subcommand == List || subcommand == Here || subcommand == Snaps;

    /// <summary>The radius, which is the second argument.
    ///
    /// Missing, unparseable, zero, negative or past <see cref="MaximumRadius"/>
    /// all fall back to <see cref="DefaultRadius"/>. This is a read-only audit
    /// run by hand at a console, so refusing to measure anything because
    /// somebody typed `twelve` would help nobody; the number they meant is
    /// obvious from the report's own first line, which states the radius it
    /// used.</summary>
    internal static float Radius(string[]? args)
    {
        if (args != null && args.Length > 1 &&
            float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float radius) &&
            radius > 0f && radius <= MaximumRadius)
        {
            return radius;
        }

        return DefaultRadius;
    }
}
