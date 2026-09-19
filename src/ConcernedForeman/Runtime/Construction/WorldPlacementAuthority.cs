using System;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Construction;

/// <summary>The seven world questions of CF-SET-003 (#280), asked by name.
///
/// <b>What this reimplements, and what it does not - said here rather than
/// discovered.</b> <c>Player.TryPlacePiece</c> switches over fourteen
/// <c>PlacementStatus</c> values before it delegates, and it is bound to a
/// <c>Player</c> and its placement ghost, which an NPC cannot drive.
/// <c>Player.PlacePiece</c> performs none of them. So the checks are made here,
/// explicitly:
///
/// <list type="bullet">
/// <item>the ward, through <c>PrivateArea.CheckAccess(point, radius, flash: false, wardCheck: true)</c>;</item>
/// <item>the station, through <c>CraftingStation.HaveBuildStationInRange(name, point)</c>;</item>
/// <item>the ground, through the same <c>ZoneSystem</c> queries the worker's own
/// site policy already uses and that this repository has verified against the
/// installed binary;</item>
/// <item>the space, through <c>Piece.GetAllPiecesInRadius</c>;</item>
/// <item>the world being loaded, and the runtime being allowed to act at all.</item>
/// </list>
///
/// <b>The residual, named.</b> Vanilla's ghost additionally judges biome,
/// cultivated ground, tilting surfaces, ceiling-only and floor-only pieces,
/// dungeons, deep snow and teleport areas. Those are not reimplemented here, and
/// pretending otherwise would be the silent skip the authority ADR forbids. The
/// containment is that the shelter blueprint uses four plain wood pieces that
/// declare none of those constraints - which is a property of the blueprint, not
/// of this code, and therefore belongs on the owner go-around list rather than
/// in a comment claiming it is checked. Closing it properly is the rest of #280:
/// read each constraint off the <c>Piece</c> and refuse any piece that declares
/// one this adapter does not implement.
///
/// <b>Every answer may be <see cref="ProbeAnswer.CouldNotTell"/>, and several
/// routinely are.</b> No zone system, no net, no prefab table: each of those is
/// a world that is not there, and the gate turns every one of them into a
/// refusal that names the check.</summary>
internal sealed class WorldPlacementProbe : IPlacementProbe
{
    /// <summary>How far out to look for something already occupying a planned
    /// spot. A wood panel is two metres across, so a metre is inside the piece
    /// rather than beside it: anything this close is in the way.</summary>
    private const float ClearanceMetres = 1.0f;

    /// <summary>The radius the ward question is asked with. Zero asks "is this
    /// exact point inside somebody's ward", which is the question a single
    /// placement has; the designation check widens it because it is asking about
    /// an area.</summary>
    private const float WardRadiusMetres = 0f;

    private readonly Func<bool> _mayWork;
    private readonly Func<string, GameObject?> _prefabs;

    /// <param name="mayWork">The work-authority answer (D3): opted in, host, not
    /// dedicated, nobody else connected. Injected rather than recomputed, so
    /// there is one such policy in this product and not two.</param>
    /// <param name="prefabs">How a prefab is found by name.</param>
    internal WorldPlacementProbe(Func<bool> mayWork, Func<string, GameObject?>? prefabs = null)
    {
        _mayWork = mayWork ?? throw new ArgumentNullException(nameof(mayWork));
        _prefabs = prefabs ?? FindPrefab;
    }

    /// <inheritdoc />
    public ProbeAnswer MayActAsHost()
    {
        try
        {
            return _mayWork() ? ProbeAnswer.Yes : ProbeAnswer.No;
        }
        catch (Exception)
        {
            return ProbeAnswer.CouldNotTell;
        }
    }

    /// <inheritdoc />
    public ProbeAnswer PieceExists(string prefab)
    {
        try
        {
            GameObject? found = _prefabs(prefab);
            if (found == null)
            {
                // "There is no world yet" and "there is no such piece" both land
                // here, and the difference does not matter: neither is a yes.
                return ZNetScene.instance == null ? ProbeAnswer.CouldNotTell : ProbeAnswer.No;
            }

            return found.GetComponent<Piece>() == null ? ProbeAnswer.No : ProbeAnswer.Yes;
        }
        catch (Exception)
        {
            return ProbeAnswer.CouldNotTell;
        }
    }

    /// <inheritdoc />
    public ProbeAnswer IsLoaded(in PiecePlacement placement)
    {
        try
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null)
            {
                return ProbeAnswer.CouldNotTell;
            }

            return zones.IsZoneLoaded(At(placement)) ? ProbeAnswer.Yes : ProbeAnswer.No;
        }
        catch (Exception)
        {
            return ProbeAnswer.CouldNotTell;
        }
    }

    /// <inheritdoc />
    public ProbeAnswer WardAllows(in PiecePlacement placement)
    {
        try
        {
            // The ward list only holds wards whose objects are LOADED, so an
            // unloaded ward is invisible rather than absent. The gate asks
            // IsLoaded before this, which is what makes the answer mean
            // something - the same reasoning WorldDesignationSite records for
            // the same call.
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null || !zones.IsZoneLoaded(At(placement)))
            {
                return ProbeAnswer.CouldNotTell;
            }

            return PrivateArea.CheckAccess(At(placement), WardRadiusMetres, flash: false, wardCheck: true)
                ? ProbeAnswer.Yes
                : ProbeAnswer.No;
        }
        catch (Exception)
        {
            return ProbeAnswer.CouldNotTell;
        }
    }

    /// <inheritdoc />
    public ProbeAnswer StationInRange(in PiecePlacement placement)
    {
        try
        {
            GameObject? found = _prefabs(placement.Piece.Prefab);
            Piece? piece = found == null ? null : found.GetComponent<Piece>();
            if (piece == null)
            {
                return ProbeAnswer.CouldNotTell;
            }

            CraftingStation? station = piece.m_craftingStation;
            if (station == null)
            {
                // A piece that needs no station passes this gate. That is the
                // check answering, not the check being skipped.
                return ProbeAnswer.Yes;
            }

            return CraftingStation.HaveBuildStationInRange(station.m_name, At(placement))
                ? ProbeAnswer.Yes
                : ProbeAnswer.No;
        }
        catch (Exception)
        {
            return ProbeAnswer.CouldNotTell;
        }
    }

    /// <inheritdoc />
    public ProbeAnswer GroundAllows(in PiecePlacement placement)
    {
        try
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null)
            {
                return ProbeAnswer.CouldNotTell;
            }

            // Solid ground under the site, measured the way the worker's own
            // site policy measures it. Ground that cannot be measured is a
            // refusal, because "how high is this" has no safe default.
            var ground = new Vector3(placement.At.X, placement.At.Y, placement.At.Z);
            if (!zones.GetSolidHeight(ground, out float _))
            {
                return ProbeAnswer.CouldNotTell;
            }

            return ProbeAnswer.Yes;
        }
        catch (Exception)
        {
            return ProbeAnswer.CouldNotTell;
        }
    }

    /// <inheritdoc />
    public ProbeAnswer SpaceIsClear(in PiecePlacement placement)
    {
        try
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null || !zones.IsZoneLoaded(At(placement)))
            {
                return ProbeAnswer.CouldNotTell;
            }

            var found = new System.Collections.Generic.List<Piece>();
            Piece.GetAllPiecesInRadius(At(placement), ClearanceMetres, found);
            foreach (Piece piece in found)
            {
                if (piece != null)
                {
                    return ProbeAnswer.No;
                }
            }

            return ProbeAnswer.Yes;
        }
        catch (Exception)
        {
            return ProbeAnswer.CouldNotTell;
        }
    }

    private static Vector3 At(in PiecePlacement placement) =>
        new Vector3(placement.At.X, placement.At.Y, placement.At.Z);

    private static GameObject? FindPrefab(string name)
    {
        ZNetScene scene = ZNetScene.instance;
        return scene == null ? null : scene.GetPrefab(name);
    }
}
