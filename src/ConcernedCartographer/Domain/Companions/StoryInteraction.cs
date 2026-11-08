using System;

namespace TheConcernedCat.ConcernedCartographer.Companions;

internal enum StoryAction
{
    None,
    Back,
    Next,
    Welcome,
    ToolsOnly,
    Close,
}

[Flags]
internal enum StoryInput
{
    None = 0,
    Back = 1,
    Forward = 2,
    Close = 4,
}

/// <summary>One story action per input frame, shared by keyboard paging and UI
/// button submission. The caller supplies the same keyboard snapshot to either
/// path, so a focused Back button cannot turn Enter's intended Next into Back.
/// No quest progress changes here: a completion result only authorizes the
/// panel's existing explicit-choice callback.</summary>
internal sealed class StoryInteraction
{
    private StoryReader _reader = new StoryReader();
    private int _lastActionFrame;
    private bool _choicesAllowed;

    public bool IsOpen { get; private set; }
    public int PageIndex => _reader.PageIndex;
    public int PageNumber => _reader.PageNumber;
    public int PageCount => _reader.PageCount;
    public bool IsFirstPage => _reader.IsFirstPage;
    public bool IsLastPage => _reader.IsLastPage;
    public bool CanChoose => IsOpen && _choicesAllowed && IsLastPage;

    public void Open(int resumeAtPage, bool allowChoices, int frame)
    {
        _reader = new StoryReader();
        _reader.Resume(resumeAtPage);
        _choicesAllowed = allowChoices;
        // The input that opened the panel must not also page or choose in it.
        _lastActionFrame = frame;
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    public StoryAction Apply(StoryAction requested, StoryInput keyboard, int frame)
    {
        if (!IsOpen) { return StoryAction.None; }
        // Cancellation always releases the panel, even after another action
        // this frame. Inspect it from button callbacks too, before any choice.
        if ((keyboard & StoryInput.Close) != 0)
        {
            Close();
            return StoryAction.Close;
        }

        if (frame <= _lastActionFrame) { return StoryAction.None; }
        if ((keyboard & StoryInput.Back) != 0) { requested = StoryAction.Back; }
        else if ((keyboard & StoryInput.Forward) != 0 && !IsLastPage)
        {
            requested = StoryAction.Next;
        }

        switch (requested)
        {
            case StoryAction.Back:
                if (!_reader.Back()) { return StoryAction.None; }
                break;
            case StoryAction.Next:
                if (!_reader.Next()) { return StoryAction.None; }
                break;
            case StoryAction.Welcome:
            case StoryAction.ToolsOnly:
                if (!CanChoose) { return StoryAction.None; }
                Close(); // Reject duplicate/reentrant choices before the callback runs.
                break;
            case StoryAction.Close:
                Close();
                break;
            default:
                return StoryAction.None;
        }

        _lastActionFrame = frame;
        return requested;
    }
}
