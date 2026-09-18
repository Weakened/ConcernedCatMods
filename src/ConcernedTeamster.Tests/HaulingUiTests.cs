using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Ui.Hauling;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>#317 COOP-04: Gunnar's panel. Every phase and every attention
/// reason has its own catalog key, so a player never reads an enum name; the
/// buttons a player may press follow the state, and stopping is always one of
/// them while he holds a cart.</summary>
public class HaulingUiTests
{
    private static readonly Regex CatalogKey = new Regex("^[a-z][a-z0-9]*([.\\-][a-z0-9]+)+$");

    [Fact]
    public void EveryPhaseAndReasonHasItsOwnCatalogKey()
    {
        var keys = new HashSet<string>();
        foreach (HaulPhase phase in HaulPhases.All())
        {
            string key = HaulPanelKeys.ForPhase(phase);
            Assert.Matches(CatalogKey, key);
            Assert.True(keys.Add(key), "two phases share " + key);
        }

        keys.Clear();
        foreach (HaulAttentionReason reason in Enum.GetValues<HaulAttentionReason>())
        {
            if (reason == HaulAttentionReason.Unspecified)
            {
                continue;
            }

            string key = HaulPanelKeys.ForReason(reason);
            Assert.Matches(CatalogKey, key);
            Assert.True(keys.Add(key), "two reasons share " + key);
        }
    }

    [Fact]
    public void WithNoCartThePanelAsksForOneAndOffersNothingElse()
    {
        HaulPanelView view = HaulPanelPresenter.Present(Facts(hovered: string.Empty));

        Assert.Contains(view.Lines, line => line.Key == HaulPanelKeys.NoLease);
        Assert.Contains(view.Lines, line => line.Key == HaulPanelKeys.NoHovered);
        Assert.Contains(view.Lines, line => line.Key == HaulPanelKeys.NoDestination);
        Assert.False(view.CanAssign || view.CanRelease || view.CanSetDestination || view.CanStart || view.CanStop || view.CanDetach);

        HaulPanelView looking = HaulPanelPresenter.Present(Facts());
        Assert.True(looking.CanAssign);
        Assert.Contains(looking.Lines, line => line.Key == HaulPanelKeys.Hovered && line.Arguments[0] == "a cart 4 m away");
    }

    [Fact]
    public void AnAssignedCartCanBeSentOnlyWithADestinationAndOnlyWhenHeIsFree()
    {
        HaulPanelView ready = HaulPanelPresenter.Present(Facts(lease: "5:42 (4 m away)", destination: "the spot you marked"));
        Assert.True(ready.CanStart && ready.CanRelease && ready.CanSetDestination);
        Assert.False(ready.CanStop || ready.CanDetach || ready.CanAssign);

        HaulPanelView noDestination = HaulPanelPresenter.Present(Facts(lease: "5:42"));
        Assert.False(noDestination.CanStart);

        HaulPanelView pulling = HaulPanelPresenter.Present(
            Facts(lease: "5:42", destination: "there", phase: HaulPhase.Pulling, attached: true));
        Assert.False(pulling.CanStart);
        Assert.True(pulling.CanStop && pulling.CanDetach);
    }

    [Fact]
    public void StoppingIsOfferedEvenWhenHeMayNoLongerWorkBecauseLettingGoIsAlwaysAllowed()
    {
        HaulPanelView view = HaulPanelPresenter.Present(Facts(
            lease: "5:42", phase: HaulPhase.NeedsAttention, attention: HaulAttentionReason.AuthorityLost,
            authority: WorkAuthorityVerdict.NotHost, attached: true));

        Assert.True(view.CanStop && view.CanDetach && view.CanRelease);
        Assert.False(view.CanStart || view.CanSetDestination);
        Assert.Contains(view.Lines, line => line.Key == HaulPanelKeys.Authority);
        Assert.Contains(
            view.Lines,
            line => line.Key == HaulPanelKeys.Reason && line.Arguments[0] == HaulPanelKeys.ForReason(HaulAttentionReason.AuthorityLost));
    }

    [Fact]
    public void AHaulAnotherModsOrderIsDrivingIsSaidSoAndIsNotRestarted()
    {
        HaulPanelView view = HaulPanelPresenter.Present(Facts(
            lease: "5:42", destination: "there", phase: HaulPhase.Waiting, attached: true, orderDriven: true));

        Assert.Contains(view.Lines, line => line.Key == HaulPanelKeys.OrderDriven);
        Assert.False(view.CanStart);
        Assert.True(view.CanStop);
    }

    [Fact]
    public void HaulingTurnedOffOrAChangedGameSaysSoBeforeAnythingElse()
    {
        HaulPanelView off = HaulPanelPresenter.Present(Facts(haulingEnabled: false));
        Assert.Equal(HaulPanelKeys.HaulingOff, off.Lines.First().Key);
        Assert.False(off.CanAssign);

        HaulPanelView seam = HaulPanelPresenter.Present(Facts(seamAvailable: false));
        Assert.Equal(HaulPanelKeys.SeamGone, seam.Lines.First().Key);
        Assert.False(seam.CanAssign);
    }

    private static HaulPanelFacts Facts(
        bool haulingEnabled = true,
        bool seamAvailable = true,
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted,
        string lease = "",
        string hovered = "a cart 4 m away",
        string destination = "",
        HaulPhase phase = HaulPhase.Ready,
        HaulAttentionReason attention = HaulAttentionReason.Unspecified,
        bool attached = false,
        bool orderDriven = false) =>
        new HaulPanelFacts(
            haulingEnabled, seamAvailable, authority, workerAvailable: true, leaseCartLabel: lease,
            hoveredCartLabel: hovered, destinationLabel: destination, phase: phase, attention: attention,
            attached: attached, orderDriven: orderDriven);
}
