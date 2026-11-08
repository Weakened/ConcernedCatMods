using TheConcernedCat.ConcernedNPC.Interruption;

namespace TheConcernedCat.ConcernedNPC.Tests;

public sealed class UnknownActivityPriorityTests
{
    private static readonly NpcActivityPriority[] Known =
    {
        NpcActivityPriority.CampBorderStroll, NpcActivityPriority.SocialIdle,
        NpcActivityPriority.BackgroundMaintenance, NpcActivityPriority.RequiredRoleWork,
        NpcActivityPriority.PlayerOrder, NpcActivityPriority.Danger, NpcActivityPriority.LifecycleSafety
    };

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(8)]
    [InlineData(int.MaxValue)]
    public void Unknown_values_never_outrank_or_yield_to_any_named_activity(int raw)
    {
        var unknown = (NpcActivityPriority)raw;
        foreach (NpcActivityPriority known in Known)
        {
            Assert.False(NpcActivityArbiter.MayInterrupt(known, unknown));
            Assert.False(NpcActivityArbiter.MayInterrupt(unknown, known));
            Assert.False(NpcActivityArbiter.Interrupt(known, unknown, null, "unknown arriving").IsGranted);
            Assert.False(NpcActivityArbiter.Interrupt(unknown, known, null, "unknown running").IsGranted);
        }
        Assert.False(NpcActivityArbiter.MayInterrupt(unknown, unknown));
    }

    public static IEnumerable<object[]> EveryPhaseAndUnknown
    {
        get
        {
            using var shape = new PlanRehearsal();
            for (int phase = 0; phase <= shape.Operations.Count; phase++)
                foreach (int raw in new[] { int.MinValue, -1, 8, int.MaxValue })
                    yield return new object[] { phase, raw };
        }
    }

    [Theory]
    [MemberData(nameof(EveryPhaseAndUnknown))]
    public void Refusing_unknown_priorities_preserves_each_plan_phase_without_a_write(int after, int raw)
    {
        using var world = new PlanRehearsal();
        world.Start().RunUpTo(after);
        NpcPlanState before = world.Run.State;
        int writes = world.Codec.Writes;
        byte[] diskBefore = File.ReadAllBytes(world.PlanPath);
        foreach (NpcActivityPriority known in Known)
        {
            foreach (bool unknownArrives in new[] { true, false })
            {
                var running = unknownArrives ? known : (NpcActivityPriority)raw;
                var arriving = unknownArrives ? (NpcActivityPriority)raw : known;
                var handover = NpcActivityArbiter.Interrupt(running, arriving, world.Run, "must not be written");
                Assert.False(handover.IsGranted);
                Assert.False(handover.IsNoteRecorded);
                Assert.Contains("recognized", handover.Reason);
                Assert.Same(before, world.Run.State);
                Assert.Same(before, handover.Suspended);
                Assert.Equal(writes, world.Codec.Writes);
            }
        }
        Assert.Equal(diskBefore, File.ReadAllBytes(world.PlanPath));
    }
}
