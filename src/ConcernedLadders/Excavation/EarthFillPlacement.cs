using System.Collections.Generic;
using TheConcernedCat.ConcernedLadders.Domain.Excavation;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Excavation;

/// <summary>Places earth blocks (#577): the one place the product creates a world object. Reached only from
/// <see cref="ExcavationRuntime"/>'s dig commit, after the game has placed the dig and its gates have passed (the ward
/// and no-build checks cover the ring the blocks stand in), and from its undo, to put back blocks a dig took out. Each
/// block is instantiated as the game places a building piece (<c>Player.PlaceObject</c> instantiates the prefab), scaled
/// to its height through the game's own scale sync, and given nothing else. No creator is recorded: the block is earth,
/// not a player's building, so monsters never target it (its piece says so too) and the hammer still removes it.
/// Removal is <see cref="ExcavationClearing"/>'s.</summary>
internal static class EarthFillPlacement
{
    /// <summary>Whether <paramref name="target"/> is an earth block.</summary>
    internal static bool IsFill(GameObject target) => target != null && Utils.GetPrefabName(target) == EarthFill.Prefab;

    /// <summary>An earth block as it stands: its cell, its floor and its top.</summary>
    internal static FillBlock Read(GameObject block)
    {
        Vector3 position = block.transform.position;
        return new FillBlock(EarthFill.CellAt(position.x, position.z), position.y, position.y + block.transform.localScale.y);
    }

    /// <summary>Places each of <paramref name="blocks"/>, adding what was placed to <paramref name="placed"/>. Returns how many.</summary>
    internal static int Place(GameObject prefab, IReadOnlyList<FillBlock> blocks, List<FillBlock> placed)
    {
        int count = 0;
        foreach (FillBlock block in blocks)
        {
            if (!(block.Height > 0f)) continue;
            (float x, float y, float z) = EarthFill.Origin(block);
            GameObject spawned = Object.Instantiate(prefab, new Vector3(x, y, z), Quaternion.identity);
            ZNetView view = spawned.GetComponent<ZNetView>();
            if (view != null) view.SetLocalScale(new Vector3(1f, block.Height, 1f));
            placed.Add(block);
            count++;
        }

        return count;
    }
}
