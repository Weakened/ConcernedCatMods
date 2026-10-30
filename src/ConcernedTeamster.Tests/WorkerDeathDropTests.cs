using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>D16: what a dying Gunnar carries goes on the ground through
/// vanilla's own drop, on his genuine death and nothing else (`DECISIONS.md`
/// D16). The port binds Unity and no test here can load it, so the decision, the
/// per-stack classification, the accounting and the durable record are proved
/// here; the port's shape is pinned by `validate_repo.py`'s D16 audit.</summary>
public sealed class WorkerDeathDropTests
{
    private static DeathDropFacts Facts(
        bool died = true,
        bool released = true,
        bool optedIn = true,
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted,
        bool capability = true,
        bool loaded = true) =>
        new DeathDropFacts(died, released, optedIn, authority, capability, loaded);

    // ------------------------------------------------------------------
    // The decision
    // ------------------------------------------------------------------

    [Fact]
    public void Everything_granted_drops()
    {
        Assert.Equal(DeathDropVerdict.Drop, WorkerDeathDrop.Decide(Facts()));
    }

    [Fact]
    public void Nothing_but_a_genuine_death_ever_drops_whatever_else_is_true()
    {
        // The property D16 rests on, over every combination of every other fact:
        // a retirement, a cancellation, an unload, a teardown are all "not a
        // death", and not one of them may reach the drop.
        foreach (bool released in new[] { true, false })
        foreach (bool optedIn in new[] { true, false })
        foreach (WorkAuthorityVerdict authority in Enum.GetValues(typeof(WorkAuthorityVerdict)))
        foreach (bool capability in new[] { true, false })
        foreach (bool loaded in new[] { true, false })
        {
            DeathDropVerdict verdict = WorkerDeathDrop.Decide(
                Facts(died: false, released, optedIn, authority, capability, loaded));
            Assert.Equal(DeathDropVerdict.NotADeath, verdict);
        }
    }

    [Fact]
    public void A_cart_still_hitched_refuses_the_drop_even_with_everything_else_granted()
    {
        // D16 orders the detach FIRST and the drop only after it.
        Assert.Equal(DeathDropVerdict.CartStillHeld, WorkerDeathDrop.Decide(Facts(released: false)));
    }

    [Fact]
    public void Each_refusing_clause_refuses_on_its_own()
    {
        Assert.Equal(DeathDropVerdict.NotOptedIn, WorkerDeathDrop.Decide(Facts(optedIn: false)));
        Assert.Equal(DeathDropVerdict.SeamUnavailable, WorkerDeathDrop.Decide(Facts(capability: false)));
        Assert.Equal(DeathDropVerdict.InventoryUnreadable, WorkerDeathDrop.Decide(Facts(loaded: false)));
        foreach (WorkAuthorityVerdict authority in Enum.GetValues(typeof(WorkAuthorityVerdict)))
        {
            if (authority == WorkAuthorityVerdict.Granted)
            {
                continue;
            }

            Assert.Equal(DeathDropVerdict.NoAuthority, WorkerDeathDrop.Decide(Facts(authority: authority)));
        }
    }

    [Fact]
    public void Only_one_combination_of_facts_drops()
    {
        int drops = 0;
        foreach (bool died in new[] { true, false })
        foreach (bool released in new[] { true, false })
        foreach (bool optedIn in new[] { true, false })
        foreach (WorkAuthorityVerdict authority in Enum.GetValues(typeof(WorkAuthorityVerdict)))
        foreach (bool capability in new[] { true, false })
        foreach (bool loaded in new[] { true, false })
        {
            if (WorkerDeathDrop.Decide(Facts(died, released, optedIn, authority, capability, loaded)) == DeathDropVerdict.Drop)
            {
                drops++;
            }
        }

        Assert.Equal(1, drops);
    }

    [Fact]
    public void A_defaulted_fact_set_never_drops()
    {
        Assert.Equal(DeathDropVerdict.NotADeath, WorkerDeathDrop.Decide(default));
    }

    [Fact]
    public void Every_verdict_has_a_sentence_of_its_own()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (DeathDropVerdict verdict in Enum.GetValues(typeof(DeathDropVerdict)))
        {
            string said = WorkerDeathDrop.Describe(verdict);
            Assert.False(string.IsNullOrWhiteSpace(said));
            Assert.True(seen.Add(said), verdict + " shares a sentence with another verdict");
        }
    }

    [Fact]
    public void Every_refusing_sentence_says_nothing_was_dropped()
    {
        foreach (DeathDropVerdict verdict in Enum.GetValues(typeof(DeathDropVerdict)))
        {
            if (verdict == DeathDropVerdict.Drop)
            {
                continue;
            }

            Assert.Contains("nothing was dropped", WorkerDeathDrop.Describe(verdict), StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------
    // One stack, measured
    // ------------------------------------------------------------------

    [Fact]
    public void A_stack_is_on_the_ground_only_when_vanilla_and_the_count_agree()
    {
        DeathDropLeg leg = DeathDropLeg.Classify("Stone", asked: 5, before: 5, after: 0, returned: true, threw: false);
        Assert.Equal(5, leg.Dropped);
        Assert.Equal(0, leg.Unresolved);
    }

    [Fact]
    public void Ground_credit_requires_all_three_of_returned_not_thrown_and_the_exact_count()
    {
        // A grid over every way the three can disagree: the ground is credited
        // in exactly one cell per asked count, and never with more than asked.
        for (int asked = 1; asked <= 4; asked++)
        for (int removed = 0; removed <= asked + 2; removed++)
        foreach (bool returned in new[] { true, false })
        foreach (bool threw in new[] { true, false })
        {
            DeathDropLeg leg = DeathDropLeg.Classify("Stone", asked, before: 10, after: 10 - removed, returned, threw);
            bool exact = returned && !threw && removed == asked;
            Assert.Equal(exact ? asked : 0, leg.Dropped);
            Assert.True(leg.Dropped <= asked);
            Assert.Equal(asked, leg.Held);
        }
    }

    [Fact]
    public void Nothing_left_him_is_kept_and_unresolved()
    {
        DeathDropLeg declined = DeathDropLeg.Classify("Stone", 3, before: 3, after: 3, returned: false, threw: false);
        Assert.Equal(0, declined.Dropped);
        Assert.Equal(3, declined.Kept);
        Assert.Equal(3, declined.Unresolved);
        Assert.Contains("declined", declined.Detail, StringComparison.Ordinal);

        DeathDropLeg threwEarly = DeathDropLeg.Classify("Stone", 3, before: 3, after: 3, returned: false, threw: true);
        Assert.Equal(3, threwEarly.Kept);
        Assert.Contains("threw", threwEarly.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_drop_that_threw_after_removing_the_stack_is_uncertain_never_credited()
    {
        // Vanilla removes the stack BEFORE it builds the ground item; a throw in
        // between leaves material that may be nowhere. It is not on the ground
        // as far as anybody can prove.
        DeathDropLeg leg = DeathDropLeg.Classify("Stone", 4, before: 4, after: 0, returned: false, threw: true);
        Assert.Equal(0, leg.Dropped);
        Assert.Equal(4, leg.Uncertain);
        Assert.Equal(0, leg.Kept);
    }

    [Fact]
    public void A_partial_removal_splits_into_uncertain_and_kept()
    {
        DeathDropLeg leg = DeathDropLeg.Classify("Wood", 10, before: 10, after: 4, returned: true, threw: false);
        Assert.Equal(0, leg.Dropped);
        Assert.Equal(6, leg.Uncertain);
        Assert.Equal(4, leg.Kept);
        Assert.Equal(leg.Held, leg.Dropped + leg.Unresolved);
    }

    [Fact]
    public void More_leaving_than_was_asked_is_never_balanced_away()
    {
        DeathDropLeg leg = DeathDropLeg.Classify("Stone", 2, before: 7, after: 0, returned: true, threw: false);
        Assert.Equal(0, leg.Dropped);
        Assert.Equal(7, leg.Uncertain);

        var report = new DeathDropReport(DeathDropVerdict.Drop);
        report.Add(leg);
        Assert.False(report.Conserves);
        Assert.Contains("do not reconcile", WorkerDeathRows.Summary(report), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreadable_or_rising_count_is_wholly_unresolved()
    {
        Assert.Equal(3, DeathDropLeg.Classify("Stone", 3, before: -1, after: 0, returned: true, threw: false).Uncertain);
        Assert.Equal(3, DeathDropLeg.Classify("Stone", 3, before: 3, after: -1, returned: true, threw: false).Uncertain);
        DeathDropLeg rose = DeathDropLeg.Classify("Stone", 3, before: 3, after: 5, returned: true, threw: false);
        Assert.Equal(0, rose.Dropped);
        Assert.Equal(3, rose.Unresolved);
    }

    [Fact]
    public void A_stack_the_verdict_refused_is_wholly_kept()
    {
        DeathDropLeg leg = DeathDropLeg.NotAttempted("Stone", 6, "refused");
        Assert.Equal(0, leg.Dropped);
        Assert.Equal(6, leg.Kept);
        Assert.Equal(6, leg.Unresolved);
    }

    [Fact]
    public void Every_unit_is_on_the_ground_or_unresolved_for_every_consistent_measurement()
    {
        for (int asked = 0; asked <= 5; asked++)
        for (int removed = 0; removed <= asked; removed++)
        foreach (bool returned in new[] { true, false })
        foreach (bool threw in new[] { true, false })
        {
            DeathDropLeg leg = DeathDropLeg.Classify("Stone", asked, before: asked, after: asked - removed, returned, threw);
            Assert.Equal(leg.Held, leg.Dropped + leg.Unresolved);
        }
    }

    // ------------------------------------------------------------------
    // The report
    // ------------------------------------------------------------------

    [Fact]
    public void Totals_add_up_per_item_in_a_stable_order()
    {
        var report = new DeathDropReport(DeathDropVerdict.Drop);
        report.Add(DeathDropLeg.Classify("Wood", 5, 5, 0, true, false));
        report.Add(DeathDropLeg.Classify("Stone", 3, 6, 3, true, false));
        report.Add(DeathDropLeg.Classify("Stone", 3, 3, 3, false, false));

        IReadOnlyList<DeathDropTotal> totals = report.Totals();
        Assert.Equal(new[] { "Stone", "Wood" }, totals.Select(t => t.Item).ToArray());
        Assert.Equal(6, totals[0].Held);
        Assert.Equal(3, totals[0].Dropped);
        Assert.Equal(3, totals[0].Unresolved);
        Assert.Equal(5, totals[1].Dropped);
        Assert.Equal(11, report.Held);
        Assert.Equal(8, report.Dropped);
        Assert.Equal(3, report.Unresolved);
        Assert.True(report.Conserves);
    }

    // ------------------------------------------------------------------
    // The ledger
    // ------------------------------------------------------------------

    [Fact]
    public void The_ledger_moves_the_ground_and_the_unresolved_and_stays_conserved()
    {
        var account = new CollectionAccount();
        account.Record("pick-1", "Stone", 4, StopResult.Took());
        account.Record("pick-2", "Wood", 2, StopResult.Took());
        account.Record("pick-3", "Stone", 3, StopResult.Took(CargoPlace.InCart));

        var report = new DeathDropReport(DeathDropVerdict.Drop);
        report.Add(DeathDropLeg.Classify("Stone", 4, 4, 0, true, false));
        report.Add(DeathDropLeg.Classify("Wood", 2, 2, 0, false, true));

        account.RecordDeath("death-4", report);

        Assert.True(account.Ledger.IsConserved);
        Assert.Equal(4, account.Ledger.At("Stone", CargoPlace.DroppedOnDeath));
        Assert.Equal(2, account.Ledger.At("Wood", CargoPlace.UnresolvedAtDeath));
        Assert.Equal(0, account.Ledger.At("Stone", CargoPlace.Carried));
        // D16: the cart, and everything in it, is left exactly as the game left it.
        Assert.Equal(3, account.Ledger.At("Stone", CargoPlace.InCart));
        Assert.Equal(9, account.Ledger.Acquired);
    }

    [Fact]
    public void Units_the_session_never_saw_him_take_are_not_acquired_on_paper()
    {
        // He picked one stone this session and was carrying five from last night.
        var account = new CollectionAccount();
        account.Record("pick-1", "Stone", 1, StopResult.Took());

        var report = new DeathDropReport(DeathDropVerdict.Drop);
        report.Add(DeathDropLeg.Classify("Stone", 6, 6, 0, true, false));
        account.RecordDeath("death-2", report);

        Assert.Equal(1, account.Ledger.Acquired);
        Assert.Equal(1, account.Ledger.At("Stone", CargoPlace.DroppedOnDeath));
        Assert.True(account.Ledger.IsConserved);
    }

    [Fact]
    public void A_death_recorded_twice_moves_nothing_the_second_time()
    {
        var account = new CollectionAccount();
        account.Record("pick-1", "Stone", 5, StopResult.Took());
        var report = new DeathDropReport(DeathDropVerdict.Drop);
        report.Add(DeathDropLeg.Classify("Stone", 2, 5, 3, true, false));

        account.RecordDeath("death-2", report);
        account.RecordDeath("death-2", report);

        Assert.Equal(2, account.Ledger.At("Stone", CargoPlace.DroppedOnDeath));
        Assert.Equal(3, account.Ledger.At("Stone", CargoPlace.Carried));
        Assert.True(account.Ledger.IsConserved);
    }

    [Fact]
    public void A_refused_drop_is_unresolved_in_the_ledger_never_on_the_ground()
    {
        var account = new CollectionAccount();
        account.Record("pick-1", "Stone", 3, StopResult.Took());
        var report = new DeathDropReport(DeathDropVerdict.CartStillHeld);
        report.Add(DeathDropLeg.NotAttempted("Stone", 3, WorkerDeathDrop.Describe(DeathDropVerdict.CartStillHeld)));

        account.RecordDeath("death-2", report);

        Assert.Equal(0, account.Ledger.At("Stone", CargoPlace.DroppedOnDeath));
        Assert.Equal(3, account.Ledger.At("Stone", CargoPlace.UnresolvedAtDeath));
        Assert.True(account.Ledger.IsConserved);
    }

    // ------------------------------------------------------------------
    // The durable record
    // ------------------------------------------------------------------

    [Fact]
    public void Each_row_is_one_line_and_each_field_one_column()
    {
        var report = new DeathDropReport(DeathDropVerdict.Drop);
        report.Add(DeathDropLeg.Classify("Sto\tne\nX", 2, 2, 0, true, false));
        report.MarkInterrupted("a\tb\r\nc");

        string rows = WorkerDeathRows.Serialize(
            new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc), "gunnar", 1.5f, float.NaN, -3f, report);
        string[] lines = rows.TrimEnd('\n').Split('\n');

        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.Equal(12, line.Split('\t').Length));
        Assert.StartsWith("death\t2026-10-27T12:00:00Z\tgunnar\t1.5\t?\t-3\tDrop\t*\t2\t2\t0\t", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("stack\t", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Appending_keeps_every_byte_that_was_there()
    {
        string first = WorkerDeathRows.Append(null, "death\tone\n");
        Assert.StartsWith(WorkerDeathRows.Header, first, StringComparison.Ordinal);

        string second = WorkerDeathRows.Append(first, "death\ttwo\n");
        Assert.StartsWith(first, second, StringComparison.Ordinal);
        Assert.EndsWith("death\ttwo\n", second, StringComparison.Ordinal);

        string unterminated = WorkerDeathRows.Append("hand-edited", "death\tthree\n");
        Assert.Equal("hand-edited\ndeath\tthree\n", unterminated);
    }

    [Fact]
    public void A_refused_death_is_still_recorded_with_its_reason()
    {
        var report = new DeathDropReport(DeathDropVerdict.NoAuthority);
        report.Add(DeathDropLeg.NotAttempted("Stone", 4, WorkerDeathDrop.Describe(DeathDropVerdict.NoAuthority)));

        string rows = WorkerDeathRows.Serialize(DateTime.UtcNow, "gunnar", 0f, 0f, 0f, report);

        Assert.Contains("\tNoAuthority\t", rows, StringComparison.Ordinal);
        Assert.Contains("\t4\t0\t4\t", rows, StringComparison.Ordinal);
    }
}
