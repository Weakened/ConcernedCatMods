using System.Collections.Generic;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>The survey's original loaded-world surface: the networked
/// objects Valheim has instantiated from ZDOs (<c>ZNetScene.m_instances</c>).
/// Characters are skipped here because that exclusion is a property of
/// this surface, not of the sweep.</summary>
/// <remarks>The split read matters on this surface: it holds thousands of
/// entries, so the <c>GetComponent</c> call and the allocating
/// <c>gameObject.name</c> interop stay in <see cref="TryReadName"/>, which
/// the sweep only reaches for an entry already proven in range — exactly
/// the ordering the inline scanner loop had before issue #258.</remarks>
internal sealed class ZNetSceneSightingSource : ISurveySightingSource
{
    private readonly List<ZNetView> _views = new();

    public int Count => _views.Count;

    public void Clear()
    {
        _views.Clear();
    }

    public void Add(ZNetView view)
    {
        _views.Add(view);
    }

    public bool TryReadPlacement(int index, out RoadPoint position, out float footprintRadiusMeters)
    {
        position = default;
        footprintRadiusMeters = 0f;
        ZNetView view = _views[index];
        if (view == null || view.gameObject == null)
        {
            return false;
        }

        UnityEngine.Vector3 world = view.transform.position;
        position = new RoadPoint(world.x, world.y, world.z);
        return true;
    }

    public bool TryReadName(int index, out string name)
    {
        name = "";
        ZNetView view = _views[index];
        if (view == null || view.gameObject == null ||
            view.GetComponent<Character>() != null)
        {
            return false;
        }

        name = view.gameObject.name;
        return true;
    }
}
