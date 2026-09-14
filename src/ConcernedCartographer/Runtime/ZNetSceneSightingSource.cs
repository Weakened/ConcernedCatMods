using System.Collections.Generic;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>The survey's original loaded-world surface: the networked
/// objects Valheim has instantiated from ZDOs (<c>ZNetScene.m_instances</c>).
/// Characters are skipped here because that exclusion is a property of
/// this surface, not of the sweep.</summary>
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

    public bool TryRead(int index, out SurveySighting sighting)
    {
        sighting = default;
        ZNetView view = _views[index];
        if (view == null || view.gameObject == null ||
            view.GetComponent<Character>() != null)
        {
            return false;
        }

        UnityEngine.Vector3 position = view.transform.position;
        sighting = new SurveySighting(
            view.gameObject.name,
            new RoadPoint(position.x, position.y, position.z));
        return true;
    }
}
