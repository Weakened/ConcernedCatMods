using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.Settlement.Designations;

/// <summary>Whether the player may build on a piece of ground, as far as the
/// game could tell us.
///
/// Three values, not two, and <see cref="Unavailable"/> is <b>zero</b>. A
/// caller that forgot to answer, an adapter that threw, an area half outside
/// loaded ground — all of them produce the default, and the default refuses.
/// The alternative arrangement, where the safe answer is the one you have to
/// remember to write, is how a check gets silently skipped.
///
/// This is the same three-valued shape CF-SET-003 uses for placement, arrived
/// at for the same reason. The two are deliberately not sharing a type yet:
/// CF-SET-003 is unmerged and its gate covers ten checks to this one's one, and
/// coupling a merged leaf to an unmerged branch would be worse than a small
/// duplication that can be unified once both exist.</summary>
internal enum AreaAccess
{
    /// <summary>We could not establish the answer. Refuse.</summary>
    Unavailable = 0,

    /// <summary>Somebody else's ward covers this ground.</summary>
    Denied = 1,

    /// <summary>No ward objects.</summary>
    Granted = 2,
}

/// <summary>The one question this layer has to ask the game.
///
/// The seam is narrow on purpose. Everything else about a designation — its
/// bounds, its uniqueness, whether removing it cancels anything — is decided
/// without the game, so it can be exercised exhaustively off-game. What cannot
/// be decided here is whether a piece of ground belongs to somebody, and that
/// is the whole of this interface.</summary>
internal interface IDesignationSite
{
    /// <summary>Answers whether the area centred on <paramref name="centre"/>
    /// is free of wards the player does not own.
    ///
    /// <paramref name="radius"/> is zero for a single point. An implementation
    /// that cannot see all of the area — because part of it is outside loaded
    /// ground, or because the world is not up — must answer
    /// <see cref="AreaAccess.Unavailable"/>, not
    /// <see cref="AreaAccess.Granted"/>.</summary>
    AreaAccess CheckAccess(SitePoint centre, float radius);
}

/// <summary>A site that refuses everything.
///
/// The fallback whenever a runtime has not been given a real one. A settlement
/// that designates nothing is visibly inert; one that designates against an
/// unchecked world is not.</summary>
internal sealed class RefusingDesignationSite : IDesignationSite
{
    internal static readonly RefusingDesignationSite Instance = new();

    public AreaAccess CheckAccess(SitePoint centre, float radius) => AreaAccess.Unavailable;
}
