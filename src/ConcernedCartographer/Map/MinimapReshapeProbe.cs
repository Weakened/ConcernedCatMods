using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>#419: is the small minimap's image currently being reshaped by
/// Round Minimap? That mod turns the minimap into a rotating disc by
/// adding one mesh effect, named CircleMeshEffect, to the minimap image
/// itself (verified in its MIT source, RoundMinimap/src/MinimapShape.cs and
/// CircleMeshEffect.cs, v1.4.1), and removes it again when its round shape
/// is switched off. Reading which components sit on a vanilla object is
/// public Unity state: nothing of the other mod is called, patched or
/// reflected into, and only the component's type NAME is compared. If a
/// later version renames it, this reports "not reshaped", which is the
/// pre-#419 behaviour (ink drawn) rather than a failure. Checked at most
/// once a second, without allocating.</summary>
internal sealed class MinimapReshapeProbe
{
    private const string ReshapeEffectTypeName = "CircleMeshEffect";
    private const float RecheckSeconds = 1f;

    private readonly List<BaseMeshEffect> _effects = new();
    private float _nextCheck = float.NegativeInfinity;
    private bool _reshaped;

    public bool IsReshaped(Minimap? minimap)
    {
        float now = Time.unscaledTime;
        if (now < _nextCheck)
        {
            return _reshaped;
        }

        _nextCheck = now + RecheckSeconds;
        _reshaped = false;
        try
        {
            RawImage? image = minimap != null ? minimap.m_mapImageSmall : null;
            if (image == null)
            {
                return false;
            }

            image.GetComponents(_effects);
            foreach (BaseMeshEffect effect in _effects)
            {
                if (effect != null && effect.isActiveAndEnabled &&
                    effect.GetType().Name == ReshapeEffectTypeName)
                {
                    _reshaped = true;
                    break;
                }
            }
        }
        catch
        {
            // A probe that cannot look reports "not reshaped": the ink is
            // drawn, as it was before #419.
            _reshaped = false;
        }
        finally
        {
            _effects.Clear();
        }

        return _reshaped;
    }

    public void Reset()
    {
        _nextCheck = float.NegativeInfinity;
        _reshaped = false;
    }
}
