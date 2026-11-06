using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>Uses the UI module's native Cancel event, rather than assuming a
/// particular controller button or bypassing its current input mapping.</summary>
internal sealed class StoryCancelHandler : MonoBehaviour, ICancelHandler
{
    internal Action? Cancel;

    public void OnCancel(BaseEventData eventData)
    {
        Cancel?.Invoke();
        eventData.Use();
    }
}
