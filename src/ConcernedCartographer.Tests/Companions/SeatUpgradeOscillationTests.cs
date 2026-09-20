using System.Reflection;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.ConcernedCartographer.Companions;

namespace ConcernedCartographer.Tests.Companions;

/// <summary>CC-NPC-009 (#306), third acceptance criterion: "a seat that becomes
/// occupied and free again must not produce a move more often than the interval
/// allows".
///
/// The other half of #306 - he moves only for a strictly better rank - is
/// pinned next door in <see cref="CompanionResidencyTests"/>, one decision at a
/// time. This file cannot be written that way. "He does not keep getting up" is
/// not a property of one decision; it is a property of a run of them, and the
/// only assertion that means anything is a count over simulated time. So every
/// test here drives a timeline and counts, and the interesting number is always
/// an upper bound.
///
/// The defect this was written against: the director armed the seat-upgrade
/// clock from its camp-fingerprint handler, and that fingerprint counts fires
/// and whether they burn, beds and whether they are claimed, seats, and doors
/// and whether they are open. Those tick over constantly in a lived-in camp, so
/// the half-minute floor was reset about as fast as it could be read, and a
/// chair a player sat in and got out of could have the companion up and down
/// all evening. It is the same family as #310's walk that was retried spot
/// after spot: each decision looked right on its own and the run of them was
/// the bug.</summary>
public sealed class SeatUpgradeOscillationTests
{
    /// <summary>The floor the director ships (<c>CompanionDirector
    /// .SeatUpgradeSeconds</c>). The gate takes it as an argument, so this is a
    /// contract being tested rather than a copy of a private constant.
    /// </summary>
    private const float Interval = 30f;

    /// <summary>The frame tick, the camp fingerprint and the residency pass, in
    /// frames. All three are far shorter than the interval, which is the point:
    /// the gate has to hold while it is asked hundreds of times for every look
    /// it grants.</summary>
    private const float Frame = 0.5f;
    private const int FramesPerSecond = 2;
    private const int CampCheckFrames = 2;
    private const int ResidencyFrames = 4;

    /// <summary>The most looks a run of <paramref name="seconds"/> may contain:
    /// one when the wait first runs out, then one per interval.</summary>
    private static int MostLooksIn(float seconds)
    {
        return (int)(seconds / Interval) + 1;
    }

    // ------------------------------------------------------------------
    // The criterion itself, as a count over time
    // ------------------------------------------------------------------

    [Fact]
    public void Sweep_ACampThatChangesEverySecondStillOnlyLooksTwiceAMinute()
    {
        // The camp-fingerprint handler runs once a second and reports a change
        // every time here: a fire fed, a door swung, somebody sitting down. Ten
        // minutes of that is 600 asks.
        var gate = new SeatSweepGate(Interval);
        int looks = 0;
        int asks = 0;
        const float Seconds = 600f;
        int frames = (int)(Seconds * FramesPerSecond);

        for (int frame = 1; frame <= frames; frame++)
        {
            gate.Tick(Frame);

            if (frame % CampCheckFrames == 0)
            {
                gate.CampChanged();
                asks++;
            }

            if (frame % ResidencyFrames == 0 && gate.TryTakeLook(settled: true, drinking: false))
            {
                looks++;
            }
        }

        Assert.Equal(600, asks);
        Assert.True(
            looks <= MostLooksIn(Seconds),
            $"{looks} looks in ten minutes off {asks} asks; at most {MostLooksIn(Seconds)} allowed");

        // And not a dead gate: he is still looking about twice a minute, which
        // is the first acceptance criterion and the reason the interval is 30 s
        // rather than an hour.
        Assert.True(looks >= MostLooksIn(Seconds) - 2, $"only {looks} looks in ten minutes; he has stopped looking");
    }

    [Fact]
    public void Sweep_ASeatTakenAndFreedOverAndOverMovesHimAtMostOncePerInterval()
    {
        // The criterion's own wording, played out through the shipped planner.
        // A player uses the chair Hulgi is on, gets up, sits down again - every
        // four seconds, for ten minutes. Each time the seat comes free the
        // sweep can offer a strictly better spot than the grass he is on, so
        // the strictly-better rule does not bound this on its own: only the
        // interval does.
        //
        // Giving the seat UP is deliberately not gated and is counted
        // separately. He must never be left sitting inside the player who just
        // sat down, so a lost seat still moves him the same pass.
        var world = new FlickeringSeat(occupiedSeconds: 4, freeSeconds: 4);
        var gate = new SeatSweepGate(Interval);
        bool onSeat = false;
        int upgradeMoves = 0;
        int giveUps = 0;
        const float Seconds = 600f;
        int frames = (int)(Seconds * FramesPerSecond);

        for (int frame = 1; frame <= frames; frame++)
        {
            gate.Tick(Frame);
            int second = frame / FramesPerSecond;

            // The fingerprint notices the chair being used and freed.
            if (frame % CampCheckFrames == 0 && world.ChangedAt(second))
            {
                gate.CampChanged();
            }

            if (frame % ResidencyFrames != 0)
            {
                continue;
            }

            bool taken = world.IsOccupiedAt(second);
            SeatStatus seat = onSeat
                ? (taken ? SeatStatus.Lost : SeatStatus.Held)
                : SeatStatus.NotSeated;

            SeatUpgrade upgrade = SeatUpgrade.NotSurveyed;
            if (gate.TryTakeLook(settled: true, drinking: false))
            {
                // What the sweep would score: where he is now, against the best
                // thing in camp he could reach this instant.
                int current = onSeat && !taken ? 1 : 0;
                int offered = taken ? 0 : 1;
                upgrade = new SeatUpgrade(current, offered);
            }

            ResidencyAction action = ResidencyPlanner.Decide(Inputs(seat, upgrade));
            if (action != ResidencyAction.Rehome)
            {
                continue;
            }

            if (seat == SeatStatus.Lost)
            {
                onSeat = false;
                giveUps++;
                continue;
            }

            onSeat = true;
            upgradeMoves++;
        }

        Assert.True(
            upgradeMoves <= MostLooksIn(Seconds),
            $"he got up and took the seat {upgradeMoves} times in ten minutes; at most {MostLooksIn(Seconds)}");

        // He does eventually take it back, and he does give it up each time it
        // is used. A gate that hit the bound by never moving him, or by leaving
        // him sitting in the player, would pass the line above.
        Assert.True(upgradeMoves > 0, "he never took the seat back at all");
        Assert.True(giveUps > 0, "he never gave the seat up");
        Assert.True(giveUps >= upgradeMoves, "he took the seat more often than he lost it");
    }

    [Fact]
    public void Sweep_NoOrderOfWorldEventsGetsTwoLooksInsideOneInterval()
    {
        // The route that was wrong is fixed above. This one is about the next
        // route: every stimulus the world can apply, mixed in a deterministic
        // shuffle - camp changes at any moment, him settling, getting up to
        // stroll, and drinking - with the gap between consecutive looks
        // measured directly rather than averaged.
        var gate = new SeatSweepGate(Interval);
        var dice = new Random(20260920);
        float lastLook = 0f;
        int looks = 0;
        const float Seconds = 1200f;
        int frames = (int)(Seconds * FramesPerSecond);

        for (int frame = 1; frame <= frames; frame++)
        {
            float now = frame * Frame;
            gate.Tick(Frame);

            if (dice.Next(4) == 0)
            {
                gate.CampChanged();
            }

            bool settled = dice.Next(3) != 0;
            bool drinking = dice.Next(5) == 0;

            if (!gate.TryTakeLook(settled, drinking))
            {
                continue;
            }

            if (looks > 0)
            {
                Assert.True(
                    now - lastLook >= Interval,
                    $"two looks {now - lastLook:0.0}s apart at t={now:0.0}s; the floor is {Interval}s");
            }

            lastLook = now;
            looks++;
        }

        Assert.True(looks > 0, "nothing ever looked; the test proved nothing");
        Assert.True(looks <= MostLooksIn(Seconds), $"{looks} looks in twenty minutes");
    }

    [Fact]
    public void Sweep_TheOnlyWayToWaiveTheWaitIsThePlayerAsking()
    {
        // A net for the route nobody has written yet. The wait is only as good
        // as the number of ways there are to skip it, and the director no
        // longer owns a clock of its own to poke - it owns this object. If a
        // later change adds a third way in, this fails and asks for the
        // argument to be made in the open.
        string[] ways = typeof(SeatSweepGate)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "CampChanged", "PlayerAsked", "Tick", "TryTakeLook" }, ways);

        // And no writable state for anyone to reach round the side.
        Assert.DoesNotContain(
            typeof(SeatSweepGate).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.CanWrite);
        Assert.Empty(typeof(SeatSweepGate).GetFields(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void Sweep_TheCampFingerprintDoesNotReachTheOneWayRound()
    {
        // The gate above cannot be tested through the director: that file needs
        // Unity, a Player and a loaded world. But the director is where the
        // defect lived - it held the clock in a float field and armed it from
        // the camp-fingerprint handler - so the wiring is worth a source audit
        // of its own, in the manner of the Teamster and NPC audits.
        //
        // Three things, and all three are about there being exactly one way to
        // waive the wait:
        string source = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "ConcernedCartographer", "Runtime", "Companions", "CompanionDirector.cs"));

        // 1. The director keeps no clock of its own to poke. The interval is
        //    named once, where the gate is built.
        Assert.Equal(1, Occurrences(source, "new SeatSweepGate(SeatUpgradeSeconds)"));
        Assert.DoesNotContain("_seatUpgradeElapsed", source);

        // 2. One place waives the wait, and one place asks without waiving it.
        Assert.Equal(1, Occurrences(source, "_sweep.PlayerAsked()"));
        Assert.Equal(1, Occurrences(source, "_sweep.CampChanged()"));

        // 3. The camp fingerprint - which counts seats, fires, beds and doors,
        //    every one of them a thing that flickers - is not the place that
        //    waives it.
        string handler = MethodBody(source, "private void NoticeCampChanges()");
        Assert.Contains("LookAgainSoon()", handler);
        Assert.DoesNotContain("LookAgainNow()", handler);

        string waives = MethodBody(source, "private void LookAgainNow()");
        Assert.Contains("_sweep.PlayerAsked()", waives);
    }

    [Fact]
    public void Sweep_APlayerCommandDoesNotHandTheWorldASecondLook()
    {
        // The documented exception: summoning him, or changing which doors
        // companions may use, looks again at once. It is allowed because a
        // command does not re-score the camp - repeat it and the strictly
        // better rule has nothing better to offer - whereas a camp that changes
        // on its own does re-score it.
        //
        // What must not follow is the exception widening the world's route:
        // taking the look spends the wait like any other, so a camp change
        // arriving a frame later still waits its turn.
        var gate = new SeatSweepGate(Interval);

        gate.PlayerAsked();
        Assert.True(gate.TryTakeLook(settled: true, drinking: false));

        int refusals = 0;
        for (int frame = 1; frame < Interval * FramesPerSecond; frame++)
        {
            gate.Tick(Frame);
            gate.CampChanged();
            Assert.False(gate.TryTakeLook(settled: true, drinking: false));
            refusals++;
        }

        Assert.Equal(59, refusals);
        gate.Tick(Frame);
        Assert.True(gate.TryTakeLook(settled: true, drinking: false));
    }

    // ------------------------------------------------------------------
    // ...without losing the thing #306 is actually about
    // ------------------------------------------------------------------

    [Fact]
    public void Sweep_AChairBuiltBesideASettledCompanionIsLookedAtOnTheNextPass()
    {
        // The headline case, and the one an owner go-around will watch. He has
        // been sitting for minutes, so the wait is long over; the chair
        // appears; the very next residency pass looks. Nothing about the
        // anti-oscillation fix may cost this, which is why the wait is capped
        // at the interval rather than counted upwards forever.
        var gate = new SeatSweepGate(Interval);
        Wait(gate, 300f);

        // Nothing has changed and nothing has asked, but he is settled, so the
        // slow sweep of the first acceptance criterion runs anyway.
        Assert.True(gate.WaitIsOver);
        Assert.True(gate.TryTakeLook(settled: true, drinking: false));

        Wait(gate, 300f);
        gate.CampChanged();
        Assert.True(gate.TryTakeLook(settled: true, drinking: false));
    }

    [Fact]
    public void Sweep_ACampChangeInsideTheWaitIsRememberedNotDropped()
    {
        // The failure mode the fix could have introduced, and the reason the
        // ask is a flag rather than an immediate answer: "a seat built after he
        // settles is never taken" must not come back as "a seat built just
        // after a sweep is never taken". One camp change, once, early in the
        // wait - and no further stimulus of any kind.
        var gate = new SeatSweepGate(Interval);
        Wait(gate, Interval);
        Assert.True(gate.TryTakeLook(settled: true, drinking: false));

        gate.Tick(Frame);
        gate.CampChanged();
        Assert.True(gate.LookPending);

        float lookedAt = -1f;
        for (int frame = 2; frame <= Interval * 2f * FramesPerSecond; frame++)
        {
            gate.Tick(Frame);
            if (!gate.TryTakeLook(settled: true, drinking: false))
            {
                continue;
            }

            // The first look after the ask, and the only one this test is
            // about: whatever the gate does later is the slow sweep's business,
            // which the run above already counts.
            lookedAt = frame * Frame;
            break;
        }

        Assert.True(lookedAt >= Interval, $"the deferred look came {lookedAt:0.0}s in; the floor is {Interval}s");
        Assert.False(gate.LookPending);
    }

    [Fact]
    public void Sweep_AnAskReachesHimMidStrollAndMidDrinkButNotEarly()
    {
        // What the ask is for: an answer while he is on his feet or has a mug
        // in his hand, neither of which would otherwise get a look at all. It
        // waives those two conditions and never the clock.
        var gate = new SeatSweepGate(Interval);
        Wait(gate, Interval);

        Assert.False(gate.TryTakeLook(settled: false, drinking: false));
        Assert.False(gate.TryTakeLook(settled: true, drinking: true));

        gate.CampChanged();
        Assert.True(gate.TryTakeLook(settled: false, drinking: true));

        // Same ask, same instant, no wait served: refused.
        gate.CampChanged();
        Assert.False(gate.TryTakeLook(settled: false, drinking: true));
    }

    [Fact]
    public void Sweep_AQuietHourBanksNoLooks()
    {
        // An hour with nothing happening must not leave two looks in hand, or
        // the first two camp changes after it come back to back and the floor
        // means nothing at the moment it is most needed.
        var gate = new SeatSweepGate(Interval);
        Wait(gate, 3600f);

        Assert.Equal(Interval, gate.WaitedSeconds);

        gate.CampChanged();
        Assert.True(gate.TryTakeLook(settled: true, drinking: false));

        gate.CampChanged();
        Assert.False(gate.TryTakeLook(settled: true, drinking: false));
    }

    // ------------------------------------------------------------------
    // Nothing here touches what he has access to
    // ------------------------------------------------------------------

    [Fact]
    public void Sweep_NoAmountOfLookingAroundChangesWhatHeHasAccessTo()
    {
        // Feature access is monotonic and independent of where he is sitting. A
        // sweep that found nowhere better, a sweep that never ran, and a player
        // who hid him are seating answers and nothing else: the planner's own
        // contract is that a hidden companion is Removed from view, which is
        // presentation, and the unlock is not an input to any of it.
        Assert.False(SeatUpgrade.NotSurveyed.IsWorthMoving);
        Assert.False(new SeatUpgrade(currentValue: 1, offeredValue: 1).IsWorthMoving);

        Assert.Equal(
            ResidencyAction.Remove,
            ResidencyPlanner.Decide(new ResidencyInputs(
                companionRecruited: true,
                visibilityEnabled: false,
                actorPresent: true,
                presentationSupported: true,
                currentAnchor: Home,
                placedAnchor: Home,
                validity: AnchorValidity.Valid,
                seat: SeatStatus.NotSeated,
                upgrade: new SeatUpgrade(CompanionPose.SitOnGround, CompanionPose.SitOnSeat))));
    }

    // ------------------------------------------------------------------

    private static CompanionAnchor Home =>
        new CompanionAnchor(AnchorKind.ClaimedBed, new WorldPoint(100f, 30f, 100f));

    private static ResidencyInputs Inputs(SeatStatus seat, SeatUpgrade upgrade)
    {
        return new ResidencyInputs(
            companionRecruited: true,
            visibilityEnabled: true,
            actorPresent: true,
            presentationSupported: true,
            currentAnchor: Home,
            placedAnchor: Home,
            validity: AnchorValidity.Valid,
            seat: seat,
            upgrade: upgrade);
    }

    private static readonly Lazy<string> _repoRoot = new(() =>
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("ConcernedCatMods.sln not found above the test output.");
    });

    private static string RepoRoot => _repoRoot.Value;

    private static int Occurrences(string text, string needle)
    {
        int count = 0;
        int at = text.IndexOf(needle, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    /// <summary>One method's body, by brace matching from its signature. Crude,
    /// and enough: the point is only to read what a named method does, and a
    /// signature that has moved fails loudly rather than quietly passing.
    /// </summary>
    private static string MethodBody(string source, string signature)
    {
        int at = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{signature} is no longer in CompanionDirector.cs; this audit needs rewriting");

        int open = source.IndexOf('{', at);
        Assert.True(open > 0, $"{signature} has no body");

        int depth = 0;
        for (int index = open; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source.Substring(open, index - open + 1);
            }
        }

        throw new InvalidOperationException($"{signature} has no closing brace.");
    }

    private static void Wait(SeatSweepGate gate, float seconds)
    {
        for (int frame = 1; frame <= seconds * FramesPerSecond; frame++)
        {
            gate.Tick(Frame);
        }
    }

    /// <summary>A chair somebody keeps sitting in and getting out of.</summary>
    private sealed class FlickeringSeat
    {
        private readonly int _occupied;
        private readonly int _cycle;

        public FlickeringSeat(int occupiedSeconds, int freeSeconds)
        {
            _occupied = occupiedSeconds;
            _cycle = occupiedSeconds + freeSeconds;
        }

        public bool IsOccupiedAt(int second)
        {
            return second % _cycle < _occupied;
        }

        public bool ChangedAt(int second)
        {
            return second > 0 && IsOccupiedAt(second) != IsOccupiedAt(second - 1);
        }
    }
}
