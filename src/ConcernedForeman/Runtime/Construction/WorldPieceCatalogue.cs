using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using UnityEngine;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedForeman.Runtime.Construction;

/// <summary>The game side of a build order: what a vanilla piece really costs,
/// and what is already standing where one was planned.
///
/// <b>Why the prefab comes from <c>ZNetScene</c> rather than Jotunn's
/// <c>PrefabManager</c>.</b> Both can find a vanilla prefab by name. The net
/// scene's table is the one the host actually instantiates from, so a name that
/// is in it is a name that can be placed; and its absence means there is no
/// world, which is exactly the answer this adapter wants to give when it cannot
/// tell. Jotunn's manager answers at the main menu too, where "yes, that piece
/// exists" would be true and useless.
///
/// <b>Nothing here decides anything.</b> Every judgement - whether a cost is
/// usable, whether a phase may start, whether a piece may be placed - is in
/// <c>Domain/Construction</c>, where it can be proved with no game installed.
/// This class reads four fields and converts two coordinate systems.</summary>
internal sealed class WorldPieceCatalogue : IPieceRecipes, IPieceSight
{
    /// <summary>How close a standing piece has to be to a planned placement to
    /// be that placement, horizontally.
    ///
    /// These are the <b>door tolerances this repository already uses</b> for the
    /// same question about a different world object (<c>DoorPlace.MatchMetres</c>
    /// and <c>MatchHeightMetres</c>): is the thing on this socket the thing I
    /// remember being on this socket. Reusing them rather than inventing a pair
    /// keeps one answer to one question.</summary>
    internal const float MatchMetres = 0.35f;

    /// <summary>The same, vertically. Looser, because a floor and the floor
    /// above it are two metres apart and nothing legitimate is half a metre
    /// apart.</summary>
    internal const float MatchHeightMetres = 0.5f;

    /// <summary>How far out to sweep for a piece that might be the one. A little
    /// past the horizontal tolerance, because the vanilla sweep measures in
    /// three dimensions and this one has to catch a piece that is the right
    /// distance away horizontally and a legal distance up.</summary>
    private const float SweepMetres = 1.0f;

    private readonly Action<string> _log;
    private readonly Func<string, GameObject?> _prefabs;
    private readonly Dictionary<string, PieceRecipe> _priced =
        new Dictionary<string, PieceRecipe>(StringComparer.Ordinal);

    private bool _saidItFailed;

    internal WorldPieceCatalogue(Action<string> log, Func<string, GameObject?>? prefabs = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _prefabs = prefabs ?? FindPrefab;
    }

    /// <summary>Forgets what was priced. A world load can replace the prefab
    /// table, and a cost remembered across one would be a number from a
    /// different game.</summary>
    internal void Forget() => _priced.Clear();

    /// <summary>How many distinct pieces have been priced. For the handoff and
    /// for a test of the caching; nothing depends on it.</summary>
    internal int Priced => _priced.Count;

    /// <inheritdoc />
    public PieceRecipe Read(string prefab)
    {
        if (string.IsNullOrEmpty(prefab))
        {
            return PieceRecipe.Unknown(prefab, "no name was asked for");
        }

        if (_priced.TryGetValue(prefab, out PieceRecipe remembered))
        {
            return remembered;
        }

        PieceRecipe recipe = Price(prefab);
        _priced[prefab] = recipe;
        return recipe;
    }

    /// <inheritdoc />
    public PieceSighting Look(in PiecePlacement placement)
    {
        try
        {
            if (!placement.IsValid)
            {
                return PieceSighting.Unknown;
            }

            ZoneSystem zones = ZoneSystem.instance;
            var at = new Vector3(placement.At.X, placement.At.Y, placement.At.Z);
            if (zones == null || !zones.IsZoneLoaded(at))
            {
                // Ground nobody has loaded is ground nobody has looked at. This
                // is the one answer that must never be read as "empty": building
                // on it would put a second floor on top of the first.
                return PieceSighting.Unknown;
            }

            var found = new List<Piece>();
            Piece.GetAllPiecesInRadius(at, SweepMetres, found);

            bool somethingElse = false;
            foreach (Piece piece in found)
            {
                if (piece == null)
                {
                    continue;
                }

                Vector3 where = piece.transform.position;
                float horizontal = Horizontal(where, at);
                if (horizontal > MatchMetres || Math.Abs(where.y - at.y) > MatchHeightMetres)
                {
                    continue;
                }

                if (string.Equals(NameOf(piece), placement.Piece.Prefab, StringComparison.Ordinal))
                {
                    return PieceSighting.Standing;
                }

                somethingElse = true;
            }

            return somethingElse ? PieceSighting.Blocked : PieceSighting.Missing;
        }
        catch (Exception exception)
        {
            Complain("Build order: a planned spot could not be read (" + SafeFailure.Brief(exception) +
                "), so it is left for the next round.");
            return PieceSighting.Unknown;
        }
    }

    /// <summary>A piece's prefab name, with the engine's clone suffix taken off.
    /// The same read the game's own helper makes.</summary>
    internal static string NameOf(Piece piece)
    {
        string name = piece.gameObject.name ?? string.Empty;
        int clone = name.IndexOf("(Clone)", StringComparison.Ordinal);
        return clone < 0 ? name : name.Substring(0, clone);
    }

    private static float Horizontal(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return (float)Math.Sqrt((dx * dx) + (dz * dz));
    }

    private PieceRecipe Price(string prefab)
    {
        try
        {
            GameObject? found = _prefabs(prefab);
            if (found == null)
            {
                return PieceRecipe.Unknown(
                    prefab, "the world has no prefab by that name, or there is no world yet");
            }

            Piece? piece = found.GetComponent<Piece>();
            if (piece == null)
            {
                return PieceRecipe.Unknown(prefab, "that prefab is not a buildable piece");
            }

            Piece.Requirement[]? requirements = piece.m_resources;
            if (requirements == null)
            {
                // A piece with a null requirement array is not a free piece: it
                // is a piece whose recipe we could not read. Reading it as free
                // is how a player's chest pays nothing for a cottage.
                return PieceRecipe.Unknown(prefab, "its recipe could not be read");
            }

            var costs = new List<PieceCost>(requirements.Length);
            foreach (Piece.Requirement requirement in requirements)
            {
                if (requirement == null || requirement.m_amount <= 0)
                {
                    continue;
                }

                ItemDrop? item = requirement.m_resItem;
                if (item == null)
                {
                    return PieceRecipe.Unknown(
                        prefab, "one of its requirements names an item that is not there");
                }

                // The ITEM PREFAB NAME, which is what Foreman's custody ledger
                // already spells its material with - see MaterialItem.PrefabName
                // and EngineInventoryPorts' m_dropPrefab.name match. A display
                // name would be a second vocabulary that stops agreeing the
                // moment somebody plays in another language.
                costs.Add(new PieceCost(item.gameObject.name, requirement.m_amount));
            }

            return PieceRecipe.Known(prefab, costs);
        }
        catch (Exception exception)
        {
            return PieceRecipe.Unknown(prefab, "the game threw while it was asked (" +
                SafeFailure.Brief(exception) + ")");
        }
    }

    private static GameObject? FindPrefab(string name)
    {
        ZNetScene scene = ZNetScene.instance;
        return scene == null ? null : scene.GetPrefab(name);
    }

    private void Complain(string message)
    {
        if (_saidItFailed)
        {
            return;
        }

        // This runs once per planned piece per round, so an unreadable world
        // would otherwise write the same line into a player's log all evening.
        _saidItFailed = true;
        _log(message + " This is said once.");
    }
}
