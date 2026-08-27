using System;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Ownership state machine for one logical modal input block.
/// Jötunn's GUIManager.BlockInput is reference-counted: every true call
/// must be balanced by exactly one false call, or player/map input stays
/// trapped forever (DEF-v1.0-001: the adopt-prompt → managed-editor
/// transition showed the panel twice but hid it once). This class owns AT
/// MOST one outstanding request: Acquire and Release are idempotent, so
/// re-entry cannot double-acquire and releasing while not owning cannot
/// steal a block held by another mod.</summary>
internal sealed class ModalInputBlock
{
    private readonly Action<bool> _applyBlock;

    public ModalInputBlock(Action<bool> applyBlock)
    {
        _applyBlock = applyBlock ?? throw new ArgumentNullException(nameof(applyBlock));
    }

    /// <summary>True while this owner holds exactly one block request.</summary>
    public bool Owned { get; private set; }

    public void Acquire()
    {
        if (Owned)
        {
            return;
        }

        // Ownership flips before the callback so a throwing backend can
        // never be asked for a second (unbalanced) request on retry.
        Owned = true;
        _applyBlock(true);
    }

    public void Release()
    {
        if (!Owned)
        {
            return;
        }

        Owned = false;
        _applyBlock(false);
    }
}
