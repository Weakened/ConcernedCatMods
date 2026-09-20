using System;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Construction;

/// <summary>How one attempt to place one piece ended.</summary>
internal enum PlacementResult
{
    /// <summary>Nothing was attempted. The default, and never a success.
    /// </summary>
    Unspecified = 0,

    /// <summary>The piece is standing.</summary>
    Placed = 1,

    /// <summary>A gate refused it. Nothing was spent.</summary>
    Refused = 2,

    /// <summary>Every gate allowed it and the placement itself did not happen.
    /// <b>Distinct from a refusal on purpose</b>: a refusal is the system
    /// working, and this is the system failing, and a caller that treated them
    /// alike would retry the one that will never succeed and give up on the one
    /// that would.</summary>
    Failed = 3,
}

/// <summary>What one placement attempt produced.</summary>
internal readonly struct PlacementReport
{
    internal PlacementReport(PlacementResult result, PlacementRefusal refusal, string reason)
    {
        Result = result;
        Refusal = refusal;
        Reason = reason ?? string.Empty;
    }

    internal PlacementResult Result { get; }

    internal PlacementRefusal Refusal { get; }

    internal string Reason { get; }

    internal bool Placed => Result == PlacementResult.Placed;

    public override string ToString() => Placed ? "placed" : Result + ": " + Reason;
}

/// <summary>The one thing that actually brings a vanilla piece into the world.
///
/// <b>It is a seam, and the seam is deliberately narrow.</b> Everything that
/// decides <i>whether</i> a piece may be placed is behind
/// <see cref="PlacementGate"/> and provable with no game. This is the single
/// call that changes the world, so it is one method with one responsibility, and
/// the placer below never calls it without the gate having said yes.</summary>
internal interface IPieceInstaller
{
    /// <summary>Brings the piece into the world at the placement's own position
    /// and rotation, as a real vanilla piece created by the host player.
    /// </summary>
    /// <returns>Whether it is standing, and why not when it is not.</returns>
    bool Install(in PiecePlacement placement, out string failure);
}

/// <summary>The gate, then the installer, and nothing in between.
///
/// <b>Why this type exists rather than a call at the end of the gate.</b> The
/// order "check, then place, and never place without checking" is the whole of
/// CF-SET-003, and an order is a thing that can be got wrong later. Putting it
/// in one place with one caller makes it a property of the code rather than a
/// convention, and lets a test prove that a refusal never reaches the
/// installer.</summary>
internal sealed class WorldPiecePlacer
{
    private readonly IPlacementProbe _probe;
    private readonly IPieceInstaller _installer;
    private readonly Action<string> _log;

    internal WorldPiecePlacer(IPlacementProbe probe, IPieceInstaller installer, Action<string> log)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>How many pieces this placer has actually brought into the world.
    /// </summary>
    internal int Placed { get; private set; }

    /// <summary>How many it has refused.</summary>
    internal int Refused { get; private set; }

    /// <summary>Tries to place one piece.</summary>
    /// <param name="piece">What, where, and what it costs.</param>
    /// <param name="reserved">What is in hand for it, out of custody. The cost
    /// is checked against this and never against a nearby chest.</param>
    /// <param name="authorised">Whether a player confirmed the order.</param>
    internal PlacementReport Place(CostedPiece piece, MaterialTally? reserved, bool authorised)
    {
        PlacementVerdict verdict =
            PlacementGate.May(piece.Placement, piece.Recipe, _probe, reserved, authorised);
        if (!verdict.MayPlace)
        {
            Refused++;
            _log(ConstructionSentences.Refused(piece.Placement, verdict.Check));
            return new PlacementReport(PlacementResult.Refused, verdict.Refusal, verdict.Check);
        }

        PiecePlacement placement = piece.Placement;
        bool installed;
        string failure;
        try
        {
            installed = _installer.Install(in placement, out failure);
        }
        catch (Exception exception)
        {
            installed = false;
            failure = exception.Message;
        }

        if (!installed)
        {
            // Not a refusal: every gate said yes. Saying so plainly is what
            // stops a caller retrying it forever as though the world had
            // changed.
            return new PlacementReport(
                PlacementResult.Failed,
                PlacementRefusal.None,
                string.IsNullOrEmpty(failure) ? "the piece was not created" : failure);
        }

        Placed++;
        return new PlacementReport(PlacementResult.Placed, PlacementRefusal.None, string.Empty);
    }
}

/// <summary>The one call that changes the world: the host player places a real
/// vanilla piece at a place an NPC chose.
///
/// <b>The signature, read off the installed binary rather than inferred.</b>
/// <c>void Player.PlacePiece(Piece piece, Vector3 pos, Quaternion rot, bool
/// doAttack, bool cheated)</c>. That was the open question when this file was first
/// written - the shipped build flow is <c>UpdatePlacementGhost</c> then
/// <c>TryPlacePiece</c>, and if the position had come from the player's own
/// placement ghost then an NPC could not have aimed it and a call here would
/// have built the wall wherever the player happened to be looking. It does not:
/// the position and the rotation are parameters. The authority ADR's choice
/// stands, and this is it.
///
/// <b>Both booleans are false, and <c>doAttack</c> is an authority boundary
/// rather than cosmetic hygiene.</b> Read out of the installed binary's IL:
/// <c>PlacePiece</c> contains <c>callvirt ZSyncAnimation::SetTrigger</c>, guarded
/// by a <c>brfalse.s</c> on that very argument. <c>ZSyncAnimation.SetTrigger</c>
/// <b>sends an RPC</b> - this repository's own compatibility audit records that
/// (<c>docs/mods/concerned-cartographer/COMPANION_COMPATIBILITY.md</c> section 3),
/// and a cosmetic hammer trigger was explicitly refused when the neighbouring
/// product asked for one. So <c>doAttack: false</c> is the thing that keeps a
/// forbidden RPC out of the placement path altogether, and it happens to also
/// stop the <i>player</i> swinging because an NPC put a wall up twenty metres
/// away. <b>Flipping it would widen an authority boundary, not add an
/// animation</b>, and it needs its own owner decision.
///
/// <c>cheated</c> marks the piece as having been conjured, and it is the one
/// flag this whole product exists not to set: the material comes out of a
/// player's chest through custody, so the piece is not cheated and must not say
/// it is.
///
/// <b>It does not pay for the piece.</b> Vanilla consumes a build cost in the
/// caller, not in <c>PlacePiece</c>, and in this product the paying is custody's
/// - reserved, moved once, committed once. An installer that also consumed would
/// be the second place material leaves a chest, which is how a conservation
/// invariant stops being one.
///
/// <b>It returns void, so there is no success to read.</b> This method does not
/// hand back whether the piece went up, so "true" here means the call was made
/// without throwing and nothing more. What actually decides whether a wall is
/// standing is the same thing that decides it for a wall the player built - the
/// next look at the site, through <see cref="IPieceSight"/>. That is not a
/// weakness of this seam, it is why progress is read from the world rather than
/// remembered from what we asked for.</summary>
internal sealed class HostPlayerPieceInstaller : IPieceInstaller
{
    private readonly Func<string, GameObject?> _prefabs;

    internal HostPlayerPieceInstaller(Func<string, GameObject?>? prefabs = null)
    {
        _prefabs = prefabs ?? FindPrefab;
    }

    /// <inheritdoc />
    public bool Install(in PiecePlacement placement, out string failure)
    {
        Player player = Player.m_localPlayer;
        if (player == null)
        {
            failure = "there is no host player to place it";
            return false;
        }

        GameObject? prefab = _prefabs(placement.Piece.Prefab);
        Piece? piece = prefab == null ? null : prefab.GetComponent<Piece>();
        if (piece == null)
        {
            failure = "the world has no piece called " + placement.Piece.Prefab;
            return false;
        }

        var at = new Vector3(placement.At.X, placement.At.Y, placement.At.Z);
        Quaternion facing = Quaternion.Euler(0f, placement.Yaw, 0f);

        // Void: the call reports nothing back. Whether the piece is standing is
        // answered by the next look at the site, not by this line.
        //
        // AND ONE THING THIS CALL DOES THAT D13 FORBIDS, recorded because it is
        // currently inert and will not stay that way by itself: vanilla wraps its
        // own Instantiate here in TerrainModifier.SetTriggerOnPlaced(true), so a
        // piece carrying a TerrainModifier WOULD level the ground under it. The
        // placement-clearance policy is a parked owner decision
        // (docs/settlement/cart-and-collection/DECISIONS.md D13), so that is a
        // blocker the moment a blueprint piece has one. The four pieces this
        // blueprint uses carry none - wood_floor, wood_wall, wood_door, bed - and
        // nothing here PINS that, which is why it is written down rather than
        // relied on. Its own issue.
        player.PlacePiece(piece, at, facing, doAttack: false, cheated: false);

        failure = string.Empty;
        return true;
    }

    private static GameObject? FindPrefab(string name)
    {
        ZNetScene scene = ZNetScene.instance;
        return scene == null ? null : scene.GetPrefab(name);
    }
}
