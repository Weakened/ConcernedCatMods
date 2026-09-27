using System.Reflection;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Reservations;
using PlanInterruption = TheConcernedCat.ConcernedNPC.Interruption.Interruption;

namespace TheConcernedCat.ConcernedNPC.Tests.Interruption;

/// <summary>The interruption runtime is an actual library adoption surface.
///
/// Until #379's first production adoption every type in the area was internal,
/// so a product that referenced the shipped ConcernedNPC assembly could not
/// name a codec, open a journal, construct a plan or revalidate one. Linking the
/// library sources into a test hid that defect. These reflection assertions pin
/// the exact path a separately compiled role needs.</summary>
public sealed class PlanAdoptionSurfaceTests
{
    [Fact]
    public void A_role_can_persist_run_and_reconstruct_a_plan()
    {
        foreach (Type type in new[]
                 {
                     typeof(INpcPlanCodec),
                     typeof(NpcPlanJournal),
                     typeof(NpcPlanLoad),
                     typeof(NpcPlanSave),
                     typeof(NpcPlanRun),
                     typeof(NpcPlanState),
                     typeof(NpcPlanPhase),
                     typeof(NpcPlanCustody),
                     typeof(NpcPlanEvidence),
                     typeof(NpcPlanRecovered),
                     typeof(NpcPlanRecovery),
                     typeof(PlanInterruption),
                     typeof(InterruptionCause),
                     typeof(InterruptionResponse),
                     typeof(InterruptionOutcome),
                     typeof(IInterruptionPolicy),
                     typeof(NpcWorkInterruptionPolicy),
                     typeof(ReservationId),
                     typeof(NpcMaterial),
                     typeof(NpcMaterialStack),
                 })
        {
            Assert.True(type.IsPublic, type.FullName + " is not public, so no product can adopt #379.");
        }

        AssertPublic(typeof(NpcPlanJournal), "TryOpen");
        AssertPublic(typeof(NpcPlanJournal), "Load");
        AssertPublic(typeof(NpcPlanJournal), "TryQuarantine");
        AssertPublic(typeof(NpcPlanRun), "Begin");
        AssertPublic(typeof(NpcPlanRun), "Resume");
        AssertPublic(typeof(NpcPlanRun), "Intend");
        AssertPublic(typeof(NpcPlanRun), "Conclude");
        AssertPublic(typeof(NpcPlanRun), "Advance");
        AssertPublic(typeof(NpcPlanRun), "Record");
        AssertPublic(typeof(NpcPlanRun), "Stop");
        AssertPublic(typeof(NpcPlanRun), "Adopt");
        AssertPublic(typeof(NpcPlanRun), "Reattach");
        AssertPublic(typeof(NpcPlanRecovery), "Reconstruct");
        AssertPublic(typeof(NpcPlanRecovery), "Revalidate");

        AssertPublicConstructor(typeof(NpcPlanState));
        AssertPublicConstructor(typeof(NpcPlanEvidence));
        AssertPublicConstructor(typeof(PlanInterruption));
        AssertPublicConstructor(typeof(InterruptionOutcome));
        AssertPublicConstructor(typeof(NpcMaterial));
        AssertPublicConstructor(typeof(NpcMaterialStack));
        AssertPublic(typeof(ReservationId), "For");
        AssertPublic(typeof(ReservationId), "TryParse");
    }

    private static void AssertPublic(Type type, string member)
    {
        MemberInfo[] found = type.GetMember(
            member, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        Assert.True(found.Length > 0, type.FullName + "." + member + " is not publicly reachable.");
    }

    private static void AssertPublicConstructor(Type type) =>
        Assert.NotNull(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).SingleOrDefault());
}
