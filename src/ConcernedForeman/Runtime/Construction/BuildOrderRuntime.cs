using System;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.Settlement.Worker;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Construction;

/// <summary>Build orders, against the installed game: the menu's commands, the
/// world facts they need, and what is standing at the site.
///
/// <b>It decides nothing.</b> Every answer comes from
/// <see cref="BuildOrderDesk"/>, <see cref="ShelterPlan"/> or
/// <see cref="ConstructionProgress"/>, all of which are proved with no game.
/// What is here is the three readings only the game can make - where the player
/// is standing, which way they are facing, and whether this runtime may work at
/// all - and the wiring that hands them over.
///
/// <b>The order does not survive a world load, deliberately.</b>
/// <see cref="Forget"/> is called when a world goes away: a marker names a place
/// in a world that no longer exists, and an order carried into the next one
/// would authorise building somewhere nobody agreed to. The cost is real and is
/// on the owner go-around list rather than buried here - <b>mark an order,
/// confirm it, reload, and the order is gone</b>. Making it survive is a durable
/// format this leaf deliberately does not add; what it would take is its own
/// issue.</summary>
internal sealed class BuildOrderRuntime
{
    private readonly BuildOrderDesk _desk = new BuildOrderDesk();
    private readonly WorldPieceCatalogue _catalogue;
    private readonly Func<bool> _mayWork;
    private readonly Func<string> _whyNot;

    internal BuildOrderRuntime(Func<bool> mayWork, Func<string> whyNot, Action<string> log)
    {
        _mayWork = mayWork ?? throw new ArgumentNullException(nameof(mayWork));
        _whyNot = whyNot ?? throw new ArgumentNullException(nameof(whyNot));
        _catalogue = new WorldPieceCatalogue(log ?? throw new ArgumentNullException(nameof(log)));
    }

    /// <summary>The order, confirmed or not.</summary>
    internal BuildOrderMarker Marker => _desk.Marker;

    /// <summary>Whether a player has authorised building. What the placement
    /// gate asks about authority, and the only thing that answers it.</summary>
    internal bool IsAuthorised => _desk.IsAuthorised;

    /// <summary>Where the order stands.</summary>
    internal BuildOrderStatus Status => _desk.Status;

    /// <summary>What a player is reading in the panel's status line.</summary>
    internal string Status_() => _desk.Execute(new[] { "status" }, Context());

    /// <summary>The order priced against the world as it is now.</summary>
    internal ShelterPlan Plan() => _desk.PlanNow(Context());

    /// <summary>How far along the shelter is, read from the pieces standing at
    /// the site rather than from anything remembered.</summary>
    internal ConstructionProgress Progress() => ConstructionProgress.Read(Plan(), _catalogue);

    /// <summary>A world has gone away.</summary>
    internal void Forget()
    {
        _desk.Forget();
        _catalogue.Forget();
    }

    /// <summary>One command from the console or the panel.</summary>
    internal string Execute(string[]? args)
    {
        try
        {
            return _desk.Execute(args, Context());
        }
        catch (Exception exception)
        {
            return "The build order command failed: " + exception.Message;
        }
    }

    /// <summary>What the world says right now. Read fresh for every command: a
    /// marker proposed where the player was standing a minute ago is not a
    /// marker where the player is standing.</summary>
    private BuildOrderContext Context()
    {
        Player player = Player.m_localPlayer;
        if (player == null)
        {
            // Not the default point: the default point is the world origin, a
            // perfectly finite coordinate, and a marker made from it is a
            // cottage proposed at the middle of the map by a question nobody
            // answered.
            var nowhere = new SitePoint(float.NaN, float.NaN, float.NaN);
            return new BuildOrderContext(
                false, nowhere, 0f, _catalogue, "there is nobody in the world to give an order.");
        }

        Transform where = player.transform;
        var here = new SitePoint(where.position.x, where.position.y, where.position.z);
        float facing = where.rotation.eulerAngles.y;

        bool may;
        string why;
        try
        {
            may = _mayWork();
            why = may ? string.Empty : _whyNot();
        }
        catch (Exception)
        {
            // An authority answer that could not be established is not a yes.
            may = false;
            why = "whether this world may be worked in could not be established.";
        }

        return new BuildOrderContext(may, here, facing, _catalogue, why);
    }
}
