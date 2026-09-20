using System;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Why an explicitly ordered pick was refused before anything was
/// attempted. Zero means it was not.</summary>
internal enum CollectionOrderRefusal
{
    /// <summary>Nothing refused.</summary>
    None = 0,

    /// <summary>The player has not opted in. The first thing asked, so the answer
    /// a player who never turned this on gets is the one that says so.</summary>
    FeatureOff = 1,

    /// <summary>The work-authority rule refused: no world, not the host, a
    /// dedicated server, or somebody else connected.</summary>
    WorkRefused = 2,

    /// <summary>The start-up probe could not verify every game member the pickup
    /// binds, so none of them is called.</summary>
    SeamUnavailable = 3,

    /// <summary>No worker body to pick with, or it is not alive.</summary>
    NoWorker = 4,

    /// <summary>A pick is already in flight. One body picks one thing.</summary>
    AlreadyWorking = 5,

    /// <summary>The player is not pointing at anything that can be picked.
    /// </summary>
    NothingPointedAt = 6,

    /// <summary>It is not one of the two kinds the owner's decision covers - a
    /// loose stone or a fallen branch.</summary>
    NotOneHeCollects = 7,

    /// <summary>What it would give is not what the game gives for one of these,
    /// or it would give more than one. Fails closed: a source whose yield has
    /// been changed is not picked at all.</summary>
    YieldIsNotVanilla = 8,

    /// <summary>He is not standing next to it. There is no reaching across a
    /// clearing and nothing here moves him.</summary>
    OutOfReach = 9,

    /// <summary>Something could not be read - an unusable reach, a distance that
    /// is not a number. Unknown refuses.</summary>
    Unreadable = 10,

    /// <summary>No world is loaded as far as the runtime's own lifecycle is
    /// concerned - including the window where the game is shutting down and its
    /// singletons are still up.
    ///
    /// <b>Separate from the authority rule on purpose.</b> That rule reads the
    /// game's singletons, which are still answering during a shutdown; the
    /// lifecycle has already reported the world gone and dropped the record of
    /// what was picked. An order accepted in that window would be an order with
    /// no record standing behind it, which is where a second yield comes
    /// from.</summary>
    WorldIsGoingAway = 11,

    /// <summary>He is standing right there, and his last change could not be
    /// written to his own network object - so what he is carrying and what is
    /// saved disagree, and he is handed nothing more to hold.
    ///
    /// <b>This refusal exists because the one it replaced was a lie.</b> A
    /// persistence failure makes the hauling runtime's <c>BoundBody</c> answer
    /// null, the runtime read that as "no worker", and the player was told
    /// <i>"Gunnar is not here. Bring him into the world first."</i> about a Gunnar
    /// standing in front of them. Nothing said a write had failed, nothing said
    /// he was holding something, and the only other true sentence reachable was
    /// <c>ct_haul retire</c>'s unreadable refusal - whose only escape is
    /// <c>retire force</c>, which destroys the stone. So the false sentence
    /// pointed at the destructive door. This one names the failure and points at
    /// the reload instead.</summary>
    WorkerRecordUnwritable = 12,

    /// <summary>He is standing right there, and his body has not been able to
    /// READ what it is carrying - a corrupt stored package, or simply the window
    /// before the record's <c>Start</c> has run. Such a body is inert by
    /// construction: it never saves, so picking into it would put a stone
    /// somewhere that never persists.
    ///
    /// <b>The sibling of <see cref="WorkerRecordUnwritable"/>, and it was missing
    /// for a round.</b> <c>WorkerInventoryRecord.Trust</c> refuses on three
    /// states, the runtime only told the player about one of them, and the other
    /// two fell through to <i>"Gunnar is not here"</i> - about a visible Gunnar,
    /// while <c>ct_haul status</c> said he was here and ready, because the census
    /// is unaffected. The comment excusing that said "the useful sentence there is
    /// a different one"; no different sentence existed. This is it.</summary>
    WorkerRecordUnreadable = 13,
}

/// <summary>What the runtime read at the moment a pick was ordered. Every field
/// is something the game was asked this frame.
///
/// <b>A defaulted instance is refused.</b> <c>default</c> is "not opted in, no
/// authority, no seam, no worker, nothing pointed at", which fails the first
/// clause - so a caller that forgets to fill a field gets a refusal, never an
/// admission.</summary>
internal readonly struct CollectionOrderRequest
{
    public CollectionOrderRequest(
        bool featureEnabled,
        bool worldIsUp,
        WorkAuthorityVerdict authority,
        bool seamAvailable,
        bool workerPresent,
        bool workerRecordUnwritable,
        bool workerRecordUnreadable,
        bool pickInFlight,
        bool pointedAtASource,
        string? sourceObjectName,
        string? yieldItemPrefabName,
        int yieldUnits,
        float reachMetres,
        float distanceMetres)
    {
        FeatureEnabled = featureEnabled;
        WorldIsUp = worldIsUp;
        Authority = authority;
        SeamAvailable = seamAvailable;
        WorkerPresent = workerPresent;
        WorkerRecordUnwritable = workerRecordUnwritable;
        WorkerRecordUnreadable = workerRecordUnreadable;
        PickInFlight = pickInFlight;
        PointedAtASource = pointedAtASource;
        SourceObjectName = sourceObjectName ?? string.Empty;
        YieldItemPrefabName = yieldItemPrefabName ?? string.Empty;
        YieldUnits = yieldUnits;
        ReachMetres = reachMetres;
        DistanceMetres = distanceMetres;
    }

    /// <summary>Teamster's master switch and the collection switch, both on.
    /// </summary>
    public bool FeatureEnabled { get; }

    /// <summary>Whether the runtime's own lifecycle currently has a world. False
    /// during a shutdown even while the game's singletons still answer, which is
    /// the window the authority rule cannot see.</summary>
    public bool WorldIsUp { get; }

    /// <summary>What the shared work-authority rule said, asked now rather than
    /// when the runtime started.</summary>
    public WorkAuthorityVerdict Authority { get; }

    public bool SeamAvailable { get; }

    public bool WorkerPresent { get; }

    /// <summary>Whether a bound, living body's last inventory change failed to
    /// reach its own network object. A body in that state answers null from
    /// <c>BoundBody</c>, so <see cref="WorkerPresent"/> is false as well - this
    /// field is what tells the two apart, and it is asked FIRST so the player is
    /// told which one it is.
    ///
    /// <b>False is the safe default here, unusually.</b> A caller that forgets it
    /// cannot admit a pick: the body is already absent as far as
    /// <see cref="WorkerPresent"/> is concerned, so the order still refuses. What
    /// forgetting loses is the true sentence, not the refusal.</summary>
    public bool WorkerRecordUnwritable { get; }

    /// <summary>Whether a bound, living body could not READ what it carries -
    /// inert, the other state that makes <c>BoundBody</c> answer null. Same
    /// reasoning as <see cref="WorkerRecordUnwritable"/>: it changes no decision,
    /// only which true sentence the player gets.</summary>
    public bool WorkerRecordUnreadable { get; }

    public bool PickInFlight { get; }

    public bool PointedAtASource { get; }

    /// <summary>The scene object's name, clone suffix and all. The gate strips
    /// it; a name decorated any other way simply is not on the allowlist, which
    /// is the refusing direction.</summary>
    public string SourceObjectName { get; }

    /// <summary>The prefab name of the item one pick would give.</summary>
    public string YieldItemPrefabName { get; }

    /// <summary>How many units one pick would give.</summary>
    public int YieldUnits { get; }

    /// <summary>How close he has to be, from this product's own limits.</summary>
    public float ReachMetres { get; }

    /// <summary>How far he actually is, horizontally.</summary>
    public float DistanceMetres { get; }
}

/// <summary>Whether an explicitly ordered pick may start (#381). The whole
/// decision, over values, so every refusal can be exercised without the game
/// running - which matters more here than usual, because the runtime that asks
/// binds Unity and no test in this repository can load it.
///
/// <b>What this is, and what it is not.</b> It is the gate on <i>one pick a
/// player ordered by pointing at the thing</i>: the player's own explicit order
/// is the provenance, the way pointing at a cart is what authorizes assigning
/// it. It is <b>not</b> a replacement for <see cref="GunnarTargetPredicate"/>,
/// which judges candidates <i>nobody pointed at</i> and asks about wards,
/// locations, creators and work areas as well - all questions that only matter
/// once something other than a person is choosing. That predicate is still
/// unwired; when the automatic survey lands it is what decides, and this gate
/// stays what it is.
///
/// <b>Identity is still the allowlist.</b> Both share
/// <see cref="GunnarCollectionAllowlist"/>, so "which things may Gunnar pick up"
/// has one answer in this product and widening it is one visible edit.</summary>
internal static class CollectionOrderGate
{
    /// <summary>Decides. The first failure is the refusal, and the order is the
    /// order a player would fix them in: their own setting, then the room, then
    /// this build, then Gunnar, then the thing itself.</summary>
    public static CollectionOrderRefusal Evaluate(in CollectionOrderRequest request)
    {
        if (!request.FeatureEnabled)
        {
            return CollectionOrderRefusal.FeatureOff;
        }

        if (!request.WorldIsUp)
        {
            return CollectionOrderRefusal.WorldIsGoingAway;
        }

        if (request.Authority != WorkAuthorityVerdict.Granted)
        {
            return CollectionOrderRefusal.WorkRefused;
        }

        if (!request.SeamAvailable)
        {
            return CollectionOrderRefusal.SeamUnavailable;
        }

        // Before "no worker", because a body whose record could not be written
        // IS absent as far as BoundBody is concerned - and answering "Gunnar is
        // not here" about a Gunnar standing in front of the player was the
        // falsehood this clause exists to stop.
        if (request.WorkerRecordUnwritable)
        {
            return CollectionOrderRefusal.WorkerRecordUnwritable;
        }

        if (request.WorkerRecordUnreadable)
        {
            return CollectionOrderRefusal.WorkerRecordUnreadable;
        }

        if (!request.WorkerPresent)
        {
            return CollectionOrderRefusal.NoWorker;
        }

        if (request.PickInFlight)
        {
            return CollectionOrderRefusal.AlreadyWorking;
        }

        if (!request.PointedAtASource)
        {
            return CollectionOrderRefusal.NothingPointedAt;
        }

        if (!GunnarCollectionAllowlist.TryKindOfPrefab(
                PrefabNameOf(request.SourceObjectName), out CollectableKind kind))
        {
            return CollectionOrderRefusal.NotOneHeCollects;
        }

        // The yield, exactly as the shipped predicate already pins it: the item
        // the game gives for one of these, and exactly one of it. A source whose
        // yield has been changed is not picked, rather than picked and counted
        // wrong.
        if (request.YieldUnits != 1 ||
            !string.Equals(
                PrefabNameOf(request.YieldItemPrefabName),
                GunnarCollectionAllowlist.ExpectedYieldOf(kind),
                StringComparison.Ordinal))
        {
            return CollectionOrderRefusal.YieldIsNotVanilla;
        }

        if (request.ReachMetres <= 0f || float.IsNaN(request.ReachMetres) ||
            float.IsNaN(request.DistanceMetres) || float.IsInfinity(request.DistanceMetres))
        {
            return CollectionOrderRefusal.Unreadable;
        }

        if (request.DistanceMetres > request.ReachMetres)
        {
            // Nothing in this slice walks him anywhere. An order he cannot
            // reach from where he stands is refused rather than turned into
            // movement nobody authorized.
            return CollectionOrderRefusal.OutOfReach;
        }

        return CollectionOrderRefusal.None;
    }

    /// <summary>The prefab name behind a scene object's name, with the clone
    /// suffix the host adds removed - the same reading the port already does for
    /// a dropped item.
    ///
    /// <b>This is the WEAKER of the two readings in this product, and it is
    /// weaker on purpose only here.</b> <see cref="CollectableFacts"/>
    /// .<c>NetworkPrefabName</c> is the strong one: it is the name the game's own
    /// prefab table gives the object's <i>network prefab hash</i>, which no
    /// decoration, rename or re-parenting can shift. This reads the scene
    /// object's name instead, which a world location's embedded copy decorates
    /// with a numbered suffix.
    ///
    /// <b>Why that is acceptable here, and nowhere else.</b> A decorated name
    /// simply is not on the allowlist, so the source is refused - the weakness
    /// runs in the refusing direction, and on an order where the player is
    /// pointing at the object, their pointing is the provenance. That argument
    /// does <b>not</b> transfer. In a survey-driven path nothing is pointing at
    /// anything, provenance is exactly what has to be established, and a refusal
    /// that depends on a decoration is a refusal a renamed prefab removes.
    /// <b>So: do not copy this into the automatic path.</b> That path already has
    /// <see cref="GunnarTargetPredicate.Classify"/> and the network-prefab
    /// reading it takes, and those are what it must use.</summary>
    public static string PrefabNameOf(string? sceneObjectName)
    {
        string name = sceneObjectName ?? string.Empty;
        int clone = name.IndexOf("(Clone)", StringComparison.Ordinal);
        return clone < 0 ? name : name.Substring(0, clone);
    }

    /// <summary>One sentence a player can act on.</summary>
    public static string Describe(CollectionOrderRefusal refusal)
    {
        switch (refusal)
        {
            case CollectionOrderRefusal.None:
                return "Nothing stops him.";
            case CollectionOrderRefusal.FeatureOff:
                return "Gunnar's collection is off. Turn on Workers/GunnarCollectionEnabled " +
                    "(and Teamster's General/Enabled) if you want him picking things up.";
            case CollectionOrderRefusal.WorkRefused:
                // The verdict's own sentence is the useful one; the overload
                // below says which part of the rule refused.
                return "Work is not allowed here right now.";
            case CollectionOrderRefusal.SeamUnavailable:
                return "This game build does not match what Gunnar's pickup needs, so he will not " +
                    "pick anything up. Everything else Teamster does keeps working.";
            case CollectionOrderRefusal.NoWorker:
                return "Gunnar is not here. Bring him into the world first.";
            case CollectionOrderRefusal.WorkerRecordUnwritable:
                return "Gunnar is here, and he is still holding what he picked up - but the game "
                    + "could not write down what he is carrying, so what he holds and what is "
                    + "saved no longer agree. He will not be handed anything more until a change "
                    + "does get written. Reload the world, or log out and back in, and he comes "
                    + "back with what was last saved; whatever the failed write was carrying is "
                    + "gone. The log says why the write failed.";
            case CollectionOrderRefusal.WorkerRecordUnreadable:
                return "Gunnar is here, but his body has not been able to read what it is "
                    + "carrying, so he will not pick anything up and will not write an empty "
                    + "inventory over what he holds. If he has only just appeared, try again in a "
                    + "moment; otherwise the log says what could not be read.";
            case CollectionOrderRefusal.AlreadyWorking:
                return "He is already picking something up.";
            case CollectionOrderRefusal.NothingPointedAt:
                return "Point at the thing you want him to pick up.";
            case CollectionOrderRefusal.NotOneHeCollects:
                return "That is not a loose stone or a fallen branch, and those are the only two " +
                    "things he is allowed to pick up.";
            case CollectionOrderRefusal.YieldIsNotVanilla:
                return "What that would give is not what the game gives for one of these, so he " +
                    "leaves it alone.";
            case CollectionOrderRefusal.OutOfReach:
                return "He is not standing next to it, and nothing here walks him over.";
            case CollectionOrderRefusal.Unreadable:
                return "Something about that could not be read, so he does nothing.";
            case CollectionOrderRefusal.WorldIsGoingAway:
                return "There is no world for him to work in right now.";
            default:
                return "Refused for a reason nobody recorded; that is a bug.";
        }
    }

    /// <summary>The same sentence, except that a work refusal is reported in the
    /// authority rule's own words - so a player is told <i>which</i> part of the
    /// rule refused rather than just that it did.</summary>
    public static string Describe(CollectionOrderRefusal refusal, WorkAuthorityVerdict authority) =>
        refusal == CollectionOrderRefusal.WorkRefused
            ? WorkAuthorityPolicy.Describe(authority)
            : Describe(refusal);
}
