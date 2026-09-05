using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Input;
using TheConcernedCat.ConcernedTeamster.Domain.Ui.Navigation;

namespace ConcernedTeamster.Tests;

/// <summary>CT-031: every interactive element is reachable in a deterministic
/// focus order, every panel is buttons-first, and accelerator conflicts
/// (external and internal) are detected and reported without override.</summary>
public class ControllerNavigationTests
{
    // -- focus ring traversal --

    [Fact]
    public void FocusRing_NextWrapsAndVisitsEveryItemOnce()
    {
        FocusRing ring = NavigationCatalog.RingFor(NavigationCatalog.CartStatusPanel);
        int count = ring.Count;
        Assert.True(count > 0);

        var visited = new List<string> { ring.Current.Id };
        for (int step = 1; step < count; step++)
        {
            visited.Add(ring.Next().Id);
        }

        Assert.Equal(count, new HashSet<string>(visited).Count); // all distinct
        // One more Next wraps back to the first.
        Assert.Equal(visited[0], ring.Next().Id);
    }

    [Fact]
    public void FocusRing_PreviousIsInverseOfNext()
    {
        FocusRing ring = NavigationCatalog.RingFor(NavigationCatalog.RoutePickerPanel);
        string start = ring.Current.Id;
        ring.Next();
        Assert.Equal(start, ring.Previous().Id);
        // Previous from the first wraps to the last.
        Assert.Equal(ring.Count - 1, WrapIndexAfterPreviousFromFirst(ring));
    }

    private static int WrapIndexAfterPreviousFromFirst(FocusRing ring)
    {
        ring.Reset();
        ring.Previous();
        return ring.CurrentIndex;
    }

    [Fact]
    public void FocusRing_Empty_HasNoFocusAndDoesNotThrow()
    {
        var ring = new FocusRing(System.Array.Empty<FocusItem>());
        Assert.False(ring.HasFocus);
        Assert.Equal(-1, ring.CurrentIndex);
        ring.Next();
        ring.Previous();
        Assert.False(ring.HasFocus);
    }

    [Fact]
    public void FocusRing_FocusById_MovesOrReportsMissing()
    {
        FocusRing ring = NavigationCatalog.RingFor(NavigationCatalog.CartStatusPanel);
        Assert.True(ring.FocusById("status.brake"));
        Assert.Equal("status.brake", ring.Current.Id);
        Assert.False(ring.FocusById("does.not.exist"));
        Assert.Equal("status.brake", ring.Current.Id); // unchanged
    }

    [Fact]
    public void FocusOrder_IsDeterministic_AcrossFreshRings()
    {
        FocusRing a = NavigationCatalog.RingFor(NavigationCatalog.TripHistoryPanel);
        FocusRing b = NavigationCatalog.RingFor(NavigationCatalog.TripHistoryPanel);
        var seqA = new List<string>();
        var seqB = new List<string>();
        for (int i = 0; i < a.Count; i++)
        {
            seqA.Add(a.Current.Id);
            a.Next();
            seqB.Add(b.Current.Id);
            b.Next();
        }

        Assert.Equal(seqA, seqB);
    }

    // -- reachability + buttons-first across every panel --

    [Fact]
    public void EveryPanel_IsNonEmptyEveryElementReachableAndButtonsFirst()
    {
        foreach (string panelId in NavigationCatalog.PanelIds)
        {
            Assert.True(NavigationCatalog.TryGetOrder(panelId, out IReadOnlyList<FocusItem> items));
            Assert.NotEmpty(items);

            // Reachable: a ring visits exactly the catalog items, in order.
            FocusRing ring = NavigationCatalog.RingFor(panelId);
            var reached = new List<string>();
            for (int i = 0; i < ring.Count; i++)
            {
                reached.Add(ring.Current.Id);
                ring.Next();
            }

            Assert.Equal(items.Count, reached.Count);

            // Buttons-first: every panel offers at least one button, and in
            // this UI every focusable IS a button (no accelerator-only or
            // focus-trap read-only element).
            Assert.Contains(items, item => item.IsButton);
            Assert.All(items, item => Assert.True(item.IsButton, panelId + ":" + item.Id));

            // Every item carries a non-empty id and label (nothing unlabeled
            // for a screen the player must operate).
            Assert.All(items, item =>
            {
                Assert.False(string.IsNullOrEmpty(item.Id));
                Assert.False(string.IsNullOrEmpty(item.Label));
            });
        }
    }

    [Fact]
    public void UnknownPanel_YieldsEmptyRingNotThrow()
    {
        FocusRing ring = NavigationCatalog.RingFor("no-such-panel");
        Assert.Equal(0, ring.Count);
        Assert.False(ring.HasFocus);
    }

    // -- accelerator chord normalization --

    [Theory]
    [InlineData("Shift+M", "m+shift")]
    [InlineData("m + shift", "m+shift")]
    [InlineData("  CTRL + K ", "ctrl+k")]
    [InlineData("M", "m")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Chord_NormalizesCaseOrderAndWhitespace(string raw, string expected)
    {
        Assert.Equal(expected, AcceleratorBinding.Normalize(raw));
    }

    [Fact]
    public void Binding_UnboundWhenChordEmpty()
    {
        Assert.False(new AcceleratorBinding("x", "").IsBound);
        Assert.True(new AcceleratorBinding("x", "m").IsBound);
    }

    // -- external conflicts (vanilla / known-mod reserved) --

    [Fact]
    public void External_DetectsCollisionWithReservedChord_CaseAndOrderInsensitive()
    {
        IReadOnlyDictionary<string, string> reserved = BindingConflictChecker.BuildReserved(
            new[]
            {
                new KeyValuePair<string, string>("M", "vanilla: Map"),
                new KeyValuePair<string, string>("Shift+M", "vanilla: some combo"),
            });

        var bindings = new[]
        {
            new AcceleratorBinding("teamster.togglePanel", "m"),        // conflicts with Map
            new AcceleratorBinding("teamster.trips", "m + shift"),      // conflicts with the combo
            new AcceleratorBinding("teamster.report", "k"),             // free
        };

        IReadOnlyList<BindingConflictChecker.ExternalConflict> conflicts =
            BindingConflictChecker.FindExternalConflicts(bindings, reserved);

        Assert.Equal(2, conflicts.Count);
        Assert.Contains(conflicts, c => c.ActionId == "teamster.togglePanel" && c.ReservedLabel == "vanilla: Map");
        Assert.Contains(conflicts, c => c.ActionId == "teamster.trips");
        Assert.DoesNotContain(conflicts, c => c.ActionId == "teamster.report");
    }

    [Fact]
    public void External_UnboundActionsNeverConflict()
    {
        IReadOnlyDictionary<string, string> reserved = BindingConflictChecker.BuildReserved(
            new[] { new KeyValuePair<string, string>("M", "vanilla: Map") });

        var bindings = new[] { new AcceleratorBinding("teamster.togglePanel", "") };

        Assert.Empty(BindingConflictChecker.FindExternalConflicts(bindings, reserved));
    }

    // -- internal conflicts (two Teamster actions on one chord) --

    [Fact]
    public void Internal_DetectsTwoActionsSharingAChord()
    {
        var bindings = new[]
        {
            new AcceleratorBinding("teamster.trips", "t"),
            new AcceleratorBinding("teamster.routes", "T"),   // same chord, different case
            new AcceleratorBinding("teamster.report", "r"),
        };

        IReadOnlyList<BindingConflictChecker.InternalConflict> conflicts =
            BindingConflictChecker.FindInternalConflicts(bindings);

        BindingConflictChecker.InternalConflict conflict = Assert.Single(conflicts);
        Assert.Equal("t", conflict.Chord);
        Assert.Equal(2, conflict.ActionIds.Count);
        Assert.Contains("teamster.trips", conflict.ActionIds);
        Assert.Contains("teamster.routes", conflict.ActionIds);
    }

    [Fact]
    public void Internal_NoConflictWhenAllDistinctOrUnbound()
    {
        var bindings = new[]
        {
            new AcceleratorBinding("a", "t"),
            new AcceleratorBinding("b", "r"),
            new AcceleratorBinding("c", ""),
            new AcceleratorBinding("d", ""),
        };

        Assert.Empty(BindingConflictChecker.FindInternalConflicts(bindings));
    }
}
