using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>What a lifecycle event did to the pick record. Zero is "nothing",
/// so an event nobody routed never drops a record.</summary>
internal enum PickForget
{
    /// <summary>Nothing was forgotten.</summary>
    Nothing = 0,

    /// <summary>A job ended. The pick in flight was released; <b>every
    /// unconfirmed source was kept</b>.</summary>
    Job = 1,

    /// <summary>A world went away. The pick in flight and every unconfirmed
    /// source went with it.</summary>
    World = 2,
}

/// <summary>The two lifecycle verbs of a pick, as the thing that routes them
/// sees them. The port implements this; nothing else does.
///
/// <b>They are not interchangeable, and that is the whole reason this interface
/// exists.</b> Vanilla's pick raises two routed messages - one drops the items,
/// a second marks the source picked - and between them the source still reports
/// that it can be picked. The record of what was just picked is the only thing
/// standing between one source and two yields, so which verb an event routes to
/// decides whether material can be minted.</summary>
internal interface IPickLifecycle
{
    /// <summary>A <b>job</b> ended: cancelled, abandoned, refused mid-flight.
    /// Releases the pick in flight and <b>keeps</b> the unconfirmed-source
    /// record, because those sources still exist and may still be
    /// mid-settle.</summary>
    void Forget();

    /// <summary>The <b>world</b> went away: unloaded, disconnected, shut down.
    /// The only verb that may drop the unconfirmed-source record, because only
    /// then do the sources it names genuinely no longer exist.</summary>
    void ForgetWorld();
}

/// <summary>Routes a runtime's lifecycle events onto the pick's two verbs
/// (#381), game-free so the routing itself can be proved.
///
/// <b>Why the routing lives here rather than at each call site.</b> Getting
/// these two backwards is the defect that re-opens the mint: answering a
/// cancelled job with the world-scoped verb wipes the record, and
/// <c>begin - pick - forget - begin</c> on the same source then yields a second
/// full load out of nothing. That was a real defect in the port's first version.
/// A runtime that spelled the choice at every call site would be a runtime whose
/// choices no test in this repository can reach, because every runtime here
/// binds Unity. So the runtime reports <i>what happened</i> and this decides
/// <i>which verb that is</i>, over values, in one place.
///
/// <b>Only a world going down drops the record.</b> A world coming <i>up</i>
/// forgets nothing, deliberately: dropping the record is the minting direction,
/// and it is never done on the strength of an edge a flicker in the game's
/// singletons could also produce. Nothing is lost by that asymmetry - a world
/// that came up has had nothing picked in it yet, and a stale key inherited from
/// a previous world can only <i>refuse</i> a pick, which is the safe direction
/// and is bounded anyway by the settle horizon.</summary>
internal sealed class CollectionLifecycle
{
    private readonly IPickLifecycle _pick;
    private bool _worldUp;

    public CollectionLifecycle(IPickLifecycle pick)
    {
        _pick = pick ?? throw new ArgumentNullException(nameof(pick));
    }

    /// <summary>Whether the last observation saw a world.</summary>
    public bool WorldIsUp => _worldUp;

    /// <summary>Reports whether a world is loaded, once per frame. Only the
    /// down-edge forgets anything, and what it forgets is the world.</summary>
    public PickForget ObserveWorld(bool worldIsUp)
    {
        bool wasUp = _worldUp;
        _worldUp = worldIsUp;
        if (wasUp && !worldIsUp)
        {
            _pick.ForgetWorld();
            return PickForget.World;
        }

        return PickForget.Nothing;
    }

    /// <summary>Reports that an order ended for a reason that is not the world
    /// going away: the player cancelled it, authority was withdrawn, the body
    /// went away, the runtime faulted. A job, so the record stays.</summary>
    public PickForget OrderEnded()
    {
        _pick.Forget();
        return PickForget.Job;
    }

    /// <summary>Reports that this process is tearing down - the game is shutting
    /// down, or the plugin is being removed. The world is going with it, so this
    /// is the world verb even if no world was ever observed: nothing can pick
    /// afterwards, so dropping the record cannot mint.</summary>
    public PickForget Shutdown()
    {
        _worldUp = false;
        _pick.ForgetWorld();
        return PickForget.World;
    }
}
