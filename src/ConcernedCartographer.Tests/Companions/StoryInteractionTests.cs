using TheConcernedCat.ConcernedCartographer.Companions;

namespace TheConcernedCat.ConcernedCartographer.Tests.Companions;

public sealed class StoryInteractionTests
{
    [Theory]
    [InlineData(StoryAction.Next)]
    [InlineData(StoryAction.Welcome)]
    [InlineData(StoryAction.ToolsOnly)]
    [InlineData(StoryAction.Close)]
    internal void OpeningInputCannotActOnThePanelItJustOpened(StoryAction button)
    {
        var story = new StoryInteraction();
        story.Open(0, true, 100);
        Assert.Equal(StoryAction.None, story.Apply(button, StoryInput.Forward, 100));
        Assert.Equal(0, story.PageIndex);
        Assert.True(story.IsOpen);
    }

    [Theory]
    [InlineData(StoryAction.Back, true)]
    [InlineData(StoryAction.Back, false)]
    [InlineData(StoryAction.Next, true)]
    [InlineData(StoryAction.Next, false)]
    [InlineData(StoryAction.Close, true)]
    [InlineData(StoryAction.Close, false)]
    internal void KeyboardAndFocusedSubmitAdvanceExactlyOnceInEitherOrder(StoryAction focused, bool keyboardFirst)
    {
        var story = new StoryInteraction();
        story.Open(1, true, 10);
        StoryAction first = keyboardFirst ? StoryAction.None : focused;
        StoryAction second = keyboardFirst ? focused : StoryAction.None;

        Assert.Equal(StoryAction.Next, story.Apply(first, StoryInput.Forward, 11));
        Assert.Equal(StoryAction.None, story.Apply(second, StoryInput.Forward, 11));
        Assert.Equal(2, story.PageIndex);
        Assert.True(story.IsOpen);
    }

    [Theory]
    [InlineData(StoryAction.Welcome, true)]
    [InlineData(StoryAction.Welcome, false)]
    [InlineData(StoryAction.ToolsOnly, true)]
    [InlineData(StoryAction.ToolsOnly, false)]
    internal void RevealingTheFinalPageCannotAlsoSelectItsNewChoice(StoryAction choice, bool keyboardFirst)
    {
        var story = new StoryInteraction();
        story.Open(CompanionStory.PageCount - 2, true, 10);
        StoryAction first = keyboardFirst ? StoryAction.None : choice;
        StoryAction second = keyboardFirst ? choice : StoryAction.None;

        Assert.Equal(StoryAction.Next, story.Apply(first, StoryInput.Forward, 11));
        Assert.Equal(StoryAction.None, story.Apply(second, StoryInput.Forward, 11));
        Assert.True(story.CanChoose);
        Assert.True(story.IsOpen);
        // A later deliberate submit on the focused choice is accepted once.
        Assert.Equal(choice, story.Apply(choice, StoryInput.Forward, 12));
        Assert.False(story.IsOpen);
        Assert.Equal(StoryAction.None, story.Apply(choice, StoryInput.Forward, 12));
        Assert.Equal(StoryAction.None, story.Apply(choice, StoryInput.None, 13));
    }

    [Fact]
    public void PointerAndControllerButtonPathsAlsoShareTheFrameGuard()
    {
        var story = new StoryInteraction();
        story.Open(0, true, 1);
        Assert.Equal(StoryAction.Next, story.Apply(StoryAction.Next, StoryInput.None, 2));
        Assert.Equal(StoryAction.None, story.Apply(StoryAction.Next, StoryInput.None, 2));
        Assert.Equal(1, story.PageIndex);
        Assert.Equal(StoryAction.Back, story.Apply(StoryAction.Back, StoryInput.None, 3));
        Assert.Equal(0, story.PageIndex);
    }

    [Fact]
    public void LastPageKeyboardPollingDoesNotConsumeTheFocusedChoice()
    {
        var story = new StoryInteraction();
        story.Open(CompanionStory.PageCount - 1, true, 1);
        Assert.Equal(StoryAction.None, story.Apply(StoryAction.None, StoryInput.Forward, 2));
        Assert.Equal(StoryAction.ToolsOnly, story.Apply(StoryAction.ToolsOnly, StoryInput.Forward, 2));
    }

    [Fact]
    public void ReplayCanReadEveryPageButCannotWelcomeOrSkip()
    {
        var story = new StoryInteraction();
        story.Open(0, false, 0);
        for (int page = 1; page < CompanionStory.PageCount; page++)
        {
            Assert.Equal(StoryAction.Next, story.Apply(StoryAction.Next, StoryInput.None, page));
        }

        Assert.True(story.IsLastPage);
        Assert.False(story.CanChoose);
        Assert.Equal(StoryAction.None, story.Apply(StoryAction.Welcome, StoryInput.None, 10));
        Assert.Equal(StoryAction.None, story.Apply(StoryAction.ToolsOnly, StoryInput.Forward, 11));
        Assert.True(story.IsOpen);
        Assert.Equal(StoryAction.Close, story.Apply(StoryAction.Close, StoryInput.None, 12));
    }

    [Fact]
    public void ChoiceCallbacksAreRejectedUntilTheLastPage()
    {
        var story = new StoryInteraction();
        story.Open(0, true, 1);
        Assert.Equal(StoryAction.None, story.Apply(StoryAction.Welcome, StoryInput.None, 2));
        Assert.Equal(StoryAction.None, story.Apply(StoryAction.ToolsOnly, StoryInput.None, 3));
        Assert.True(story.IsOpen);
        Assert.Equal(0, story.PageIndex);
    }

    [Fact]
    public void EscapeWinsOverSubmitAndCanCloseAfterAnotherActionInTheSameFrame()
    {
        var story = new StoryInteraction();
        story.Open(CompanionStory.PageCount - 1, true, 1);
        Assert.Equal(StoryAction.Close, story.Apply(StoryAction.Welcome, StoryInput.Close | StoryInput.Forward, 2));
        Assert.Equal(StoryAction.None, story.Apply(StoryAction.Welcome, StoryInput.None, 2));

        story.Open(0, true, 10);
        Assert.Equal(StoryAction.Next, story.Apply(StoryAction.Next, StoryInput.None, 11));
        Assert.Equal(StoryAction.Close, story.Apply(StoryAction.None, StoryInput.Close, 11));
        Assert.False(story.IsOpen);
    }

    [Fact]
    public void ForcedCloseAndReopenRejectStaleCallbacksAndTheOpeningSubmit()
    {
        var story = new StoryInteraction();
        story.Open(CompanionStory.PageCount - 1, true, 10);
        story.Close(); // The panel's safety, failure and scene-reset paths.
        Assert.Equal(StoryAction.None, story.Apply(StoryAction.Welcome, StoryInput.None, 11));

        story.Open(0, true, 12);
        Assert.Equal(StoryAction.None, story.Apply(StoryAction.Next, StoryInput.Forward, 12));
        Assert.Equal(StoryAction.Next, story.Apply(StoryAction.Next, StoryInput.Forward, 13));
        Assert.Equal(1, story.PageIndex);
    }

    [Fact]
    public void BoundaryPagingDoesNotChangeThePageOrFinishTheStory()
    {
        var story = new StoryInteraction();
        story.Open(-100, true, 0);
        Assert.Equal(StoryAction.None, story.Apply(StoryAction.Back, StoryInput.None, 1));
        story.Open(100, true, 2);
        Assert.Equal(StoryAction.None, story.Apply(StoryAction.Next, StoryInput.None, 3));
        Assert.True(story.IsOpen);
        Assert.True(story.CanChoose);
    }
}
