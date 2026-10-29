using System;
using System.IO;
using System.Linq;
using TheConcernedCat.ConcernedSteward.Domain;
using TheConcernedCat.ConcernedSteward.Domain.Retirement;
using TheConcernedCat.ConcernedSteward.Runtime;
using UnityEngine;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>Owner decision S1 (2026-10-28): Sunniva's own worker body may leave
/// the world, and only empty-handed with no round open. These tests are the
/// control-flow half of what the validator's text audit cannot prove.</summary>
[Collection("scene")]
public sealed class StewardRetirementTests
{
    private static StewardRetireFacts Clear(
        int items = 0, bool readable = true, int fuel = 0, bool round = false, bool plan = false,
        int steps = 0, bool loss = false) =>
        new StewardRetireFacts(items, readable, fuel, round, plan, steps, loss);

    // ------------------------------------------------------------------
    // The decision
    // ------------------------------------------------------------------

    [Fact]
    public void Empty_handed_with_nothing_open_is_the_only_grant()
    {
        Assert.Equal(StewardRetireVerdict.MayRetire, StewardRetirement.Decide(Clear()));
        Assert.True(StewardRetirement.Allows(StewardRetireVerdict.MayRetire));
    }

    [Fact]
    public void Every_witness_refuses_on_its_own()
    {
        Assert.Equal(StewardRetireVerdict.RefusedUnreadable, StewardRetirement.Decide(Clear(readable: false)));
        Assert.Equal(StewardRetireVerdict.RefusedCarrying, StewardRetirement.Decide(Clear(items: 1)));
        Assert.Equal(StewardRetireVerdict.RefusedCarrying, StewardRetirement.Decide(Clear(fuel: 3)));
        Assert.Equal(StewardRetireVerdict.RefusedUnresolvedLoss, StewardRetirement.Decide(Clear(loss: true)));
        Assert.Equal(StewardRetireVerdict.RefusedRoundOpen, StewardRetirement.Decide(Clear(round: true)));
        Assert.Equal(StewardRetireVerdict.RefusedRoundOpen, StewardRetirement.Decide(Clear(plan: true)));
        Assert.Equal(StewardRetireVerdict.RefusedRoundOpen, StewardRetirement.Decide(Clear(steps: 1)));
    }

    [Fact]
    public void Unknown_is_never_empty_even_when_every_other_witness_is_clear()
    {
        // An unreadable pack that "holds 0" is the case a careless guard grants.
        Assert.False(StewardRetirement.Allows(
            StewardRetirement.Decide(Clear(items: 0, readable: false))));
    }

    [Fact]
    public void No_verdict_but_MayRetire_is_a_grant()
    {
        foreach (StewardRetireVerdict verdict in Enum.GetValues(typeof(StewardRetireVerdict)))
        {
            Assert.Equal(verdict == StewardRetireVerdict.MayRetire, StewardRetirement.Allows(verdict));
            Assert.False(string.IsNullOrEmpty(StewardRetirement.Describe(verdict, 2)));
        }
    }

    [Theory]
    [InlineData(0, false, 0, false, false, 0, false)]
    [InlineData(1, true, 0, false, false, 0, false)]
    [InlineData(0, true, 1, false, false, 0, false)]
    [InlineData(0, true, 0, true, false, 0, false)]
    [InlineData(0, true, 0, false, true, 0, false)]
    [InlineData(0, true, 0, false, false, 1, false)]
    [InlineData(0, true, 0, false, false, 0, true)]
    [InlineData(5, false, 9, true, true, 2, true)]
    public void Any_reason_at_all_refuses(
        int items, bool readable, int fuel, bool round, bool plan, int steps, bool loss)
    {
        Assert.False(StewardRetirement.Allows(
            StewardRetirement.Decide(Clear(items, readable, fuel, round, plan, steps, loss))));
    }

    // ------------------------------------------------------------------
    // The port, over the stubbed game
    // ------------------------------------------------------------------

    [Fact]
    public void An_empty_owned_body_is_removed_through_its_own_network_object_and_nothing_else()
    {
        using var scene = new Scene();
        (StewardBody body, ZNetView view) = PlaceBody();

        StewardRetireOutcome outcome = StewardRetirementPort.Retire(body, Clear(), out string answer);

        Assert.Equal(StewardRetireOutcome.Retired, outcome);
        Assert.Equal(1, view.DestroyCalls);
        Assert.Contains("left the world", answer);

        // And from that call on nothing can find her: the census cannot adopt a
        // body whose object is gone for the rest of the frame.
        Assert.Null(StewardBody.FindLive(StewardRole.Worker.Value));
    }

    [Fact]
    public void A_body_carrying_anything_is_never_removed_whatever_the_ledger_says()
    {
        using var scene = new Scene();
        (StewardBody body, ZNetView view) = PlaceBody();
        body.Inventory!.AddItem(Scene.Stack("$item_resin", 4));

        // The ledger claims empty; the pack is read from the body itself.
        StewardRetireOutcome outcome = StewardRetirementPort.Retire(body, Clear(), out string answer);

        Assert.Equal(StewardRetireOutcome.Refused, outcome);
        Assert.Equal(0, view.DestroyCalls);
        Assert.Contains("carrying", answer);
    }

    [Fact]
    public void A_pack_stated_in_the_ledger_only_ever_adds_a_refusal()
    {
        using var scene = new Scene();
        (StewardBody body, ZNetView view) = PlaceBody();

        // The body is empty and readable; a caller that says otherwise is
        // believed in the direction that keeps her.
        Assert.Equal(StewardRetireOutcome.Refused,
            StewardRetirementPort.Retire(body, Clear(items: 1), out _));
        Assert.Equal(StewardRetireOutcome.Refused,
            StewardRetirementPort.Retire(body, Clear(items: int.MaxValue), out _));
        Assert.Equal(StewardRetireOutcome.Refused,
            StewardRetirementPort.Retire(body, Clear(readable: false), out _));
        Assert.Equal(0, view.DestroyCalls);
    }

    [Fact]
    public void A_body_whose_last_change_was_not_saved_is_unreadable_and_kept()
    {
        using var scene = new Scene();
        (StewardBody body, ZNetView view) = PlaceBody();
        view.Zdo.FailWrites = true;
        body.Inventory!.AddItem(Scene.Stack("$item_wood", 1));
        body.Inventory.RemoveItem(body.Inventory.GetAllItems()[0], 1);

        StewardRetireOutcome outcome = StewardRetirementPort.Retire(body, Clear(), out _);

        Assert.Equal(StewardRetireOutcome.Refused, outcome);
        Assert.Equal(0, view.DestroyCalls);
    }

    [Fact]
    public void An_open_round_keeps_an_empty_body()
    {
        using var scene = new Scene();
        (StewardBody body, ZNetView view) = PlaceBody();

        Assert.Equal(StewardRetireOutcome.Refused,
            StewardRetirementPort.Retire(body, Clear(round: true), out _));
        Assert.Equal(StewardRetireOutcome.Refused,
            StewardRetirementPort.Retire(body, Clear(plan: true), out _));
        Assert.Equal(StewardRetireOutcome.Refused,
            StewardRetirementPort.Retire(body, Clear(fuel: 2), out _));
        Assert.Equal(0, view.DestroyCalls);
    }

    [Fact]
    public void A_body_another_peer_owns_is_not_this_games_to_remove()
    {
        using var scene = new Scene();
        (StewardBody body, ZNetView view) = PlaceBody();
        view.Ownership = StubOwnership.Other;

        Assert.Equal(StewardRetireOutcome.NotOurs, StewardRetirementPort.Retire(body, Clear(), out _));
        Assert.Equal(0, view.DestroyCalls);
        Assert.Equal(StewardRetireOutcome.NotOurs, StewardRetirementPort.Retire(null, Clear(), out _));
    }

    // ------------------------------------------------------------------
    // Reachability
    // ------------------------------------------------------------------

    [Fact]
    public void The_verb_is_reachable_from_the_console_and_hands_every_witness_to_the_port()
    {
        string command = ProductSources.WithoutComments(File.ReadAllText(
            Path.Combine(ProductSources.Product, "Runtime", "StewardCommand.cs")));
        string runtime = ProductSources.WithoutComments(File.ReadAllText(
            Path.Combine(ProductSources.Product, "Runtime", "StewardRuntime.cs")));

        Assert.Contains("case \"retire\":", command);
        Assert.Contains("return _runtime.Retire();", command);
        Assert.Contains("StewardRetirementPort.Retire(_census.Live, ledger, out string answer)", runtime);
        foreach (string witness in new[]
        {
            "fuelCarried: _loop.Custody.Carried",
            "roundOpen: _loop.IsWorking || _loop.Phase == UpkeepPhase.NeedsAttention",
            "planOpen: _plan.HasActivePlan || _plan.IsBlocked",
            "unresolvedSteps: _journal.UnresolvedIntents.Count",
            "unresolvedLoss: _loop.Custody.HasLoss",
        })
        {
            Assert.Contains(witness, runtime);
        }

        // One caller, in the product.
        int callers = ProductSources.Files(ProductSources.Product)
            .Sum(path => CountOf(
                ProductSources.WithoutComments(File.ReadAllText(path)), "StewardRetirementPort.Retire("));
        Assert.Equal(1, callers);
    }

    private static int CountOf(string text, string token)
    {
        int count = 0;
        for (int at = text.IndexOf(token, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(token, at + token.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static (StewardBody body, ZNetView view) PlaceBody()
    {
        var host = new GameObject("steward");
        ZNetView view = host.Add(new ZNetView());
        view.Zdo.m_uid = new ZDOID(7L, 77u);
        view.Zdo.Set(StewardBody.KeyField, StewardRole.Worker.Value);
        host.Add(new Humanoid());
        StewardBody body = host.Add(new StewardBody());
        typeof(StewardBody)
            .GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(body, Array.Empty<object>());
        return (body, view);
    }
}
