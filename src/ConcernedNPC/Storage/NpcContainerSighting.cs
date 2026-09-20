namespace TheConcernedCat.ConcernedNPC.Storage;

/// <summary>What a role's adapter saw when it looked at one container, just now.
///
/// <b>Why the facts are gathered outside and judged inside.</b> Every one of
/// these is a question only the game can answer, and none of them is a decision.
/// Splitting them this way is what lets the gate - the part that has to be right
/// - be exercised exhaustively on a machine with no game installed, which is not
/// a luxury here: the products reference licensed assemblies no test runner has,
/// so anything left on the game's side of this line is provable only by a person
/// standing in front of a chest.
///
/// <b>The defaulted value is a total refusal.</b> <see cref="Exists"/> is false,
/// so a sighting nobody filled in is a container that is not there. Every other
/// permissive fact is also false by default - not owned here, ward denies,
/// privacy denies - so a half-filled sighting refuses rather than passes. The
/// one exception is spelled out below and is deliberate.
///
/// <b>Which shipped gate this is.</b> Two exist and they disagree. Foreman's
/// asks six questions: is this the container we designated, do we own it, is
/// anyone in it, does a ward allow it, does its privacy setting allow it, can
/// the worker reach it. The Steward's asks two: do we own it and is anyone in
/// it. <b>Foreman's is kept</b>, because the Steward's has a real hole - a depot
/// inside somebody else's ward, or set to Private and built by a different
/// character, is written to today by a Steward that should be refusing. Closing
/// it is a behaviour change that costs an existing player a working depot until
/// they fix the ward or the privacy setting, which is why the refusal names
/// which of the two it was, and why switching the Steward over is its own issue
/// with its own proof rather than something that rides along inside a
/// refactor.</summary>
internal readonly struct NpcContainerSighting
{
    internal NpcContainerSighting(
        bool exists,
        bool identityMatches,
        bool ownedHere,
        bool inUse,
        bool wardAllows,
        bool privacyAllows,
        bool reachAsked,
        bool withinReach)
    {
        Exists = exists;
        IdentityMatches = identityMatches;
        OwnedHere = ownedHere;
        InUse = inUse;
        WardAllows = wardAllows;
        PrivacyAllows = privacyAllows;
        ReachAsked = reachAsked;
        WithinReach = withinReach;
    }

    /// <summary>The container is there and its inventory could be read. False
    /// also when the question could not be asked at all.</summary>
    internal bool Exists { get; }

    /// <summary>What answered is the container that was meant. False after a
    /// world load has handed the remembered id to something else - the ordinary
    /// case rather than the exotic one.</summary>
    internal bool IdentityMatches { get; }

    /// <summary>This process owns it. A write by a non-owner is discarded
    /// without telling anybody, which is the quietest way to lose a player's
    /// materials there is.</summary>
    internal bool OwnedHere { get; }

    /// <summary>Somebody has it open, or it rides a cart that is in use.
    /// Whatever is written now is overwritten when their window closes.</summary>
    internal bool InUse { get; }

    /// <summary>A guard stone permits it. The player's fix is the ward.</summary>
    internal bool WardAllows { get; }

    /// <summary>Its own privacy setting permits it. The player's fix is the
    /// chest.</summary>
    internal bool PrivacyAllows { get; }

    /// <summary>Whether reach was part of this question at all.
    ///
    /// <b>The one fact that is permissive when absent, carried from the shipped
    /// gate unchanged.</b> Foreman's reach check answers yes when no worker
    /// position was supplied, because reach is a property of where somebody is
    /// standing and a caller with nobody standing anywhere is not asking. Reach
    /// is also the one refusal that is not a fault - walk closer and ask again -
    /// so treating "not asked" as "too far" would stall a job over a question
    /// nobody put.</summary>
    internal bool ReachAsked { get; }

    /// <summary>Close enough to reach from where the NPC is standing. Only
    /// consulted when <see cref="ReachAsked"/>.</summary>
    internal bool WithinReach { get; }

    /// <summary>A container that is not there, or that could not be looked at.
    /// The same value as <c>default</c>, named so a call site reads as a
    /// decision rather than an omission.</summary>
    internal static NpcContainerSighting Gone() => default;
}
