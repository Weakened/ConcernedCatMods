using System;
using System.Globalization;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedForeman.Domain.Construction;

/// <summary>What the desk needs to know about the world to answer a command.
/// Read fresh each time, never remembered: a marker proposed where the player
/// was standing ten minutes ago is not a marker where the player is standing.
/// </summary>
internal readonly struct BuildOrderContext
{
    internal BuildOrderContext(
        bool mayWork, SitePoint here, float facing, IPieceRecipes? recipes, string refusal = "")
    {
        MayWork = mayWork;
        Here = here;
        Facing = facing;
        Recipes = recipes;
        Refusal = refusal ?? string.Empty;
    }

    /// <summary>Whether this runtime may change the world at all: opted in,
    /// host, not dedicated, nobody else connected.</summary>
    internal bool MayWork { get; }

    /// <summary>Where the player is standing.</summary>
    internal SitePoint Here { get; }

    /// <summary>Which way they are facing, in world degrees.</summary>
    internal float Facing { get; }

    /// <summary>Where real costs come from. Null when there is no world to ask.
    /// </summary>
    internal IPieceRecipes? Recipes { get; }

    /// <summary>Why the runtime may not work, in the player's words. Empty when
    /// it may.</summary>
    internal string Refusal { get; }
}

/// <summary>The state of the one build order a player may have going.</summary>
internal enum BuildOrderStatus
{
    /// <summary>Nothing is marked.</summary>
    None = 0,

    /// <summary>A marker is placed and nobody has agreed to it.</summary>
    Proposed = 1,

    /// <summary>The player confirmed it. Thorstein may build.</summary>
    Confirmed = 2,
}

/// <summary>The Build Orders menu, as a set of commands rather than a set of
/// buttons.
///
/// <b>Why the menu is this and the panel is a shell over it.</b> Every decision
/// a player makes about a build order - where it goes, which way it faces, what
/// it will cost, whether they agree to it - is made here, where it can be proved
/// with no game running. The Jotunn panel and the console command are two ways
/// of typing the same five words, and the repository already works this way:
/// Thorstein's collection panel calls the collection runtime's own command
/// surface, the same one the console uses.
///
/// <b>The five words.</b> <c>status</c>, <c>here</c>, <c>turn</c>,
/// <c>preview</c>, <c>confirm</c>, <c>cancel</c>. <c>here</c> puts a marker
/// where the player is standing, facing the way they are facing, which is the
/// smallest thing that can be called aiming; <c>turn</c> adjusts the facing
/// without moving it; <c>preview</c> prices it from the real recipes;
/// <c>confirm</c> is the authorisation and nothing is placed without it.
///
/// <b>It holds the order and nothing else.</b> No plan is remembered - a plan is
/// priced from the world each time it is asked for, because a recipe read at the
/// main menu and a recipe read in a world are not guaranteed to be the same
/// number and a remembered one would be the wrong one, silently.</summary>
internal sealed class BuildOrderDesk
{
    private BuildOrderMarker _marker;

    /// <summary>The marker, confirmed or not.</summary>
    internal BuildOrderMarker Marker => _marker;

    /// <summary>Where the order stands.</summary>
    internal BuildOrderStatus Status => _marker.Kind == BuildOrderKind.None
        ? BuildOrderStatus.None
        : _marker.IsConfirmed ? BuildOrderStatus.Confirmed : BuildOrderStatus.Proposed;

    /// <summary>Whether a player has authorised building. <b>The one question
    /// the placement gate asks about authority</b>, and the reason the
    /// confirmation is a state rather than a moment.</summary>
    internal bool IsAuthorised => _marker.IsConfirmed;

    /// <summary>The order priced against the world as it is now, or a refusal.
    /// </summary>
    internal ShelterPlan PlanNow(in BuildOrderContext context) =>
        ShelterPlan.For(_marker, context.Recipes);

    /// <summary>Forgets the order entirely. A world unload: the marker names a
    /// place in a world that is going away, and an order carried into the next
    /// one would authorise building somewhere nobody agreed to.</summary>
    internal void Forget() => _marker = default;

    /// <summary>Runs one command and says what to tell the player.</summary>
    internal string Execute(string[]? words, in BuildOrderContext context)
    {
        string word = words != null && words.Length > 0
            ? words[0].ToLowerInvariant()
            : "status";

        switch (word)
        {
            case "status":
                return Describe(context);
            case "here":
                return Propose(context);
            case "turn":
                return Turn(words, context);
            case "preview":
                return Preview(context);
            case "confirm":
                return Confirm(context);
            case "cancel":
                return Cancel();
            default:
                return "Build orders: say status, here, turn <degrees>, preview, confirm or cancel.";
        }
    }

    private string Describe(in BuildOrderContext context)
    {
        switch (Status)
        {
            case BuildOrderStatus.None:
                return "No build order. Stand where the middle of the shelter should go, face the " +
                    "way the door should face, and say here.";
            case BuildOrderStatus.Proposed:
                return "A shelter is marked at " + _marker.At + " facing " +
                    _marker.Yaw.ToString("0", CultureInfo.InvariantCulture) +
                    " degrees, and nobody has authorised it. Say preview to see what it costs, " +
                    "confirm to authorise it, or here to move it.";
            default:
                return "A shelter is authorised at " + _marker.At + " facing " +
                    _marker.Yaw.ToString("0", CultureInfo.InvariantCulture) +
                    " degrees. Say cancel to withdraw it." +
                    (context.MayWork
                        ? string.Empty
                        : " Nothing will be built meanwhile: " + Why(context));
        }
    }

    private string Propose(in BuildOrderContext context)
    {
        if (Status == BuildOrderStatus.Confirmed)
        {
            // Moving a confirmed order would move the authority with it, to a
            // place the player agreed to nothing about.
            return "That shelter is already authorised. Cancel it first if it should go somewhere " +
                "else; an order is not moved out from under the work.";
        }

        _marker = BuildOrderMarker.Proposed(BuildOrderKind.Shelter, context.Here, context.Facing);
        if (Status == BuildOrderStatus.None)
        {
            return "That is not a place a shelter can go.";
        }

        return "A shelter is marked at " + _marker.At + " facing " +
            _marker.Yaw.ToString("0", CultureInfo.InvariantCulture) +
            " degrees. It is not authorised yet. Say preview, then confirm.";
    }

    private string Turn(string[]? words, in BuildOrderContext context)
    {
        if (Status == BuildOrderStatus.None)
        {
            return "There is nothing marked to turn.";
        }

        if (Status == BuildOrderStatus.Confirmed)
        {
            return "That shelter is already authorised. Cancel it first if it should face another way.";
        }

        if (words == null || words.Length < 2 ||
            !float.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float by))
        {
            return "Say turn and how many degrees, such as turn 90.";
        }

        _marker = BuildOrderMarker.Proposed(
            BuildOrderKind.Shelter, _marker.At, _marker.Yaw + by);
        return "The shelter now faces " + _marker.Yaw.ToString("0", CultureInfo.InvariantCulture) +
            " degrees. It is still not authorised.";
    }

    private string Preview(in BuildOrderContext context)
    {
        if (Status == BuildOrderStatus.None)
        {
            return "There is nothing marked to price.";
        }

        // A proposal is priced by asking the same question of a confirmed copy:
        // pricing is not an authorisation, and doing it this way means the
        // number a player agrees to is the number the build will use.
        ShelterPlan plan = ShelterPlan.For(_marker.Confirm(), context.Recipes);
        return ConstructionSentences.Offer(plan);
    }

    private string Confirm(in BuildOrderContext context)
    {
        if (Status == BuildOrderStatus.None)
        {
            return "There is nothing marked to confirm.";
        }

        if (Status == BuildOrderStatus.Confirmed)
        {
            return "That shelter is already authorised.";
        }

        // Priced before it is authorised, every time. An order that cannot be
        // priced must never become one that is allowed to open a chest.
        ShelterPlan plan = ShelterPlan.For(_marker.Confirm(), context.Recipes);
        if (!plan.IsPlanned)
        {
            return "That build order was not authorised: " + plan.Refusal + ".";
        }

        _marker = _marker.Confirm();
        string authorised = "Authorised: a shelter at " + _marker.At + " facing " +
            _marker.Yaw.ToString("0", CultureInfo.InvariantCulture) + " degrees, " +
            plan.Pieces.Count + " pieces, " + plan.Total.Describe() +
            ". Material comes out of the containers you have enabled, and nothing else.";

        return context.MayWork
            ? authorised
            : authorised + " Nothing will be built yet: " + Why(context);
    }

    private string Cancel()
    {
        if (Status == BuildOrderStatus.None)
        {
            return "There is no build order to cancel.";
        }

        // Withdrawn rather than forgotten: the marker stays visible where it
        // was, and it authorises nothing. What happens to material already
        // reserved or carried is custody's, and it is reported there.
        _marker = _marker.Withdraw();
        return "The build order is withdrawn. Nothing more will be placed, and anything already " +
            "taken out of a container is accounted for rather than dropped.";
    }

    private static string Why(in BuildOrderContext context) =>
        context.Refusal.Length != 0 ? context.Refusal : "the settlement runtime is not working here.";
}
