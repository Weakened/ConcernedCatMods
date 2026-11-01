using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.Companions.Dialogue;
using TheConcernedCat.ConcernedForeman.Domain.Npc;

namespace ConcernedForeman.Tests;

/// <summary>#295 S3: what Thorstein says when talked to at home. Before he is
/// taken on he answers about his tools, from the real ledger; after, he rotates
/// through his house-building remarks without repeating one while others are
/// unsaid.</summary>
public sealed class ThorsteinTalkTests
{
    [Fact]
    public void Before_the_square_he_says_nothing()
    {
        Assert.Null(ThorsteinTalk.Line(ThorsteinQuestStage.NotMet, true, true, ThorsteinTalk.NewRotation()));
    }

    [Fact]
    public void While_waiting_he_answers_about_the_tool_he_still_needs()
    {
        DialogueRotation rotation = ThorsteinTalk.NewRotation();

        Assert.Equal(ThorsteinTalk.BothTools,
            ThorsteinTalk.Line(ThorsteinQuestStage.WaitingForTools, false, false, rotation));
        Assert.Equal(ThorsteinTalk.NeedsHammer,
            ThorsteinTalk.Line(ThorsteinQuestStage.WaitingForTools, true, false, rotation));
        Assert.Equal(ThorsteinTalk.NeedsAxe,
            ThorsteinTalk.Line(ThorsteinQuestStage.WaitingForTools, false, true, rotation));
        Assert.Equal(ThorsteinTalk.WaitingOnSettlement,
            ThorsteinTalk.Line(ThorsteinQuestStage.WaitingForTools, true, true, rotation));
    }

    [Fact]
    public void Once_recruited_every_remark_is_said_before_any_repeats()
    {
        DialogueRotation rotation = ThorsteinTalk.NewRotation();
        var said = new List<string?>();
        for (int press = 0; press < ThorsteinTalk.Remarks.Count; press++)
        {
            said.Add(ThorsteinTalk.Line(ThorsteinQuestStage.Recruited, false, false, rotation));
        }

        Assert.All(said, Assert.NotNull);
        Assert.Equal(ThorsteinTalk.Remarks.Count, said.Distinct().Count());
        Assert.True(said.All(line => ThorsteinTalk.Remarks.Values.Contains(line)));

        // And it keeps talking after the last one: a rotation, not a list that
        // runs out.
        Assert.NotNull(ThorsteinTalk.Line(ThorsteinQuestStage.Recruited, false, false, rotation));
    }

    [Fact]
    public void His_remarks_are_the_issues_and_name_no_unmet_companion()
    {
        Assert.Contains("A roof is a promise to whoever sleeps beneath it.", ThorsteinTalk.Remarks.Values);
        Assert.Contains("Measure twice. Carry the timber once.", ThorsteinTalk.Remarks.Values);

        // #295: cross-companion lines only once the other companion is known.
        // There is no such signal yet, so none is in the rotation.
        Assert.DoesNotContain(ThorsteinTalk.Remarks.Values, line => line.Contains("Hulgi"));
    }
}
