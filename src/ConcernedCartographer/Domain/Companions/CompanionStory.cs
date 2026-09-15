using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Companions;

/// <summary>One page of the introduction: who is speaking, and the
/// localization key for what they say. Nothing here is a literal string, so
/// the whole introduction translates through the existing Cartographer
/// localization path.</summary>
internal readonly struct StoryPage
{
    public StoryPage(string speakerKey, string bodyKey)
    {
        SpeakerKey = speakerKey;
        BodyKey = bodyKey;
    }

    /// <summary>Localization key for the page's heading. The first page is the
    /// object itself rather than a person, which is why this is a key and not
    /// a companion id.</summary>
    public string SpeakerKey { get; }

    public string BodyKey { get; }
}

/// <summary>Hulgi's introduction, and the paging model the UI drives.
///
/// The content and the reader are separate on purpose: the pages are authored
/// data that a translator replaces wholesale, while the reader is the part
/// that has to behave under a player mashing buttons — so the reader is what
/// the tests exercise, and it has no idea what any page says.</summary>
internal static class CompanionStory
{
    /// <summary>The authored introduction. Four short pages: the object, the
    /// loss, the cat, the offer. Deliberately readable inside a minute.</summary>
    public static readonly IReadOnlyList<StoryPage> Pages = new[]
    {
        new StoryPage("companion.compass.name", "story.hulgi.page1"),
        new StoryPage("companion.hulgi.name", "story.hulgi.page2"),
        new StoryPage("companion.hulgi.name", "story.hulgi.page3"),
        new StoryPage("companion.hulgi.name", "story.hulgi.page4"),
    };

    public static int PageCount => Pages.Count;
}

/// <summary>Where the player is in the introduction, and the only type allowed
/// to decide what the two end-of-story buttons mean.
///
/// Every movement is clamped, so no amount of duplicate input — a held key, a
/// double click, a controller repeating — can walk off either end. Reaching
/// the last page is <i>not</i> completion: completion is an explicit choice on
/// that page, which is what lets a player close the panel half way through and
/// come back to it later with nothing decided in their absence.</summary>
internal sealed class StoryReader
{
    private readonly int _pageCount;
    private int _pageIndex;

    public StoryReader(int pageCount = 0)
    {
        _pageCount = pageCount > 0 ? pageCount : CompanionStory.PageCount;
        if (_pageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageCount), "A story needs at least one page.");
        }
    }

    public int PageCount => _pageCount;

    /// <summary>Zero-based; always inside [0, PageCount - 1].</summary>
    public int PageIndex => _pageIndex;

    /// <summary>One-based, for display.</summary>
    public int PageNumber => _pageIndex + 1;

    public bool IsFirstPage => _pageIndex == 0;

    public bool IsLastPage => _pageIndex >= _pageCount - 1;

    /// <summary>True while the story is still being read — i.e. the choice
    /// buttons are not yet the thing in front of the player.</summary>
    public bool HasMoreToRead => !IsLastPage;

    /// <summary>Advances one page. Returns false when already at the end, so
    /// the caller can tell "turned a page" from "the player is asking to
    /// finish".</summary>
    public bool Next()
    {
        if (IsLastPage)
        {
            return false;
        }

        _pageIndex++;
        return true;
    }

    public bool Back()
    {
        if (IsFirstPage)
        {
            return false;
        }

        _pageIndex--;
        return true;
    }

    public void Rewind()
    {
        _pageIndex = 0;
    }

    /// <summary>Resumes where a reader left off, clamped. Used when the panel
    /// is reopened inside one session; across sessions the story restarts,
    /// which is cheap for four pages and avoids putting a UI cursor into the
    /// save file.</summary>
    public void Resume(int pageIndex)
    {
        if (pageIndex < 0)
        {
            _pageIndex = 0;
            return;
        }

        _pageIndex = pageIndex > _pageCount - 1 ? _pageCount - 1 : pageIndex;
    }
}
