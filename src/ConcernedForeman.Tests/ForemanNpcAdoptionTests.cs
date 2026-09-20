using System;
using System.IO;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Npc;
using TheConcernedCat.ConcernedForeman.Runtime.Custody;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.Workers;

// The library's Bodies namespace is NOT imported plainly on purpose: it and
// src/Shared/Workers each define ActorMode and ActorModeOutcome, which is the
// whole reason ArbiterActorMode exists. An alias keeps every assertion below
// explicit about which of the two vocabularies it is talking about.
using Lib = TheConcernedCat.ConcernedNPC.Bodies;

namespace ConcernedForeman.Tests;

/// <summary>Concerned Foreman's adoption of Concerned NPC, on both halves of the
/// thing that could go wrong.
///
/// <b>Half one: nothing on a player's disk changed.</b> A pre-adoption data
/// directory, dropped in unchanged, must still produce a bound Thorstein with his
/// issued tools and his designations, with no migration code having run. That
/// holds only while every durable name is byte-identical, so these tests spell the
/// shipped literals out and compare them, rather than comparing one constant to
/// another — which would pass happily after somebody changed both.
///
/// <b>Half two: the mode is the arbiter's, and it fails closed.</b> The mode used
/// to live in an <c>ActorModeOwner</c> compiled into this assembly, which nothing
/// outside the assembly could see. It now lives in the library's one arbiter, and
/// the tests below check that a hold for an identity the arbiter does not track
/// grants nothing and permits nothing — the worker-authority direction of the
/// program's rule, which is the opposite of the feature-access one.</summary>
public sealed class ForemanNpcAdoptionTests
{
    /// <summary>An absolute path, because the library refuses to register a role
    /// whose data root is not one. Nothing is read or written under it by anything
    /// in this test: Foreman's data paths refuse every purpose token.</summary>
    private static readonly string DataRoot = Path.Combine(Path.GetTempPath(),
        "ConcernedCatMods", "ConcernedForeman", "settlements");

    private static ForemanNpcAdoption Adopted(out NpcRoleRegistry registry)
    {
        // A PRIVATE registry, never NpcRoleRegistry.Shared: the process-wide one is
        // shared with every other test running in parallel, and an identity
        // registered there stays registered for the life of the run.
        registry = new NpcRoleRegistry();
        var adoption = new ForemanNpcAdoption(registry, new ForemanNpcRole(DataRoot));
        Assert.True(adoption.Register().IsRegistered);
        return adoption;
    }

    // --- Half one: the durable facts ---------------------------------------------------------------------

    [Fact]
    public void TheIdentityIsTheWorkerKeyTextByteForByte()
    {
        // foreman/thorstein, spelled out. This is what settlement journal rows and
        // the capability boundary already carry, and what a saved body stores under
        // tcc.worker.key. The library's own vocabulary had to fit the shipped text;
        // the shipped text was never going to be changed to fit the library.
        Assert.Equal("foreman/thorstein", WorkerKey.Thorstein.Value);
        Assert.Equal("foreman/thorstein", ForemanNpcRole.Id.Value);
        Assert.Equal(WorkerKey.Thorstein.Value, ForemanNpcRole.Id.Value);
        Assert.Equal("foreman", ForemanRole.Product);
        Assert.Equal("thorstein", ForemanRole.RoleKey);

        // Total in both directions, so an adapter at the boundary never has to
        // handle a conversion that could fail.
        Assert.True(WorkerKey.TryParse(ForemanNpcRole.Id.Value, out WorkerKey back));
        Assert.Equal(WorkerKey.Thorstein, back);
        Assert.True(NpcIdentity.TryParse(WorkerKey.Thorstein.Value, out NpcIdentity forward));
        Assert.Equal(ForemanNpcRole.Id, forward);
    }

    [Fact]
    public void TheBodyContractCarriesTheShippedPrefabNameAndKeyPrefix()
    {
        var role = new ForemanNpcRole(DataRoot);

        // CF_SettlementWorker: the name ForemanWorkerPrefab registers and every
        // saved worker body is found by. The host destroys any saved object whose
        // prefab is not registered, so a wrong value here deletes every existing
        // Thorstein - with the player's axe and hammer inside him - on the next
        // load, before any migration code could run.
        Assert.Equal("CF_SettlementWorker", ForemanRole.BodyPrefabName);
        Assert.Equal("CF_SettlementWorker", role.Body.PrefabName);

        // tcc.worker.: the prefix his body's own keys live under. A wrong value
        // leaves the body standing and empties it, with no evidence at all.
        Assert.Equal("tcc.worker.", ForemanRole.BodyKeyPrefix);
        Assert.Equal("tcc.worker.", role.Body.ZdoKeyPrefix);

        Assert.Equal(NpcBodyKind.Worker, role.Body.Kind);
        Assert.True(role.Body.TryValidate(out string reason), reason);
    }

    [Fact]
    public void TheThreeSavedKeysStillComposeFromThePrefixAndAreUnchanged()
    {
        // The three literals WorkerBody has always written, spelled out here and
        // left spelled out there. A saved body is READ by them, so they are not
        // recomposed from the prefix in the product - that refactor would be a
        // change to a player's save file wearing the clothes of a tidy-up. This is
        // what stops the prefix and the keys drifting apart instead.
        Assert.Equal("tcc.worker.key", WorkerBody.KeyField);
        Assert.Equal("tcc.worker.inventory", WorkerBody.InventoryField);
        Assert.Equal("tcc.worker.revision", WorkerBody.RevisionField);

        Assert.Equal(ForemanRole.BodyKeyPrefix + "key", WorkerBody.KeyField);
        Assert.Equal(ForemanRole.BodyKeyPrefix + "inventory", WorkerBody.InventoryField);
        Assert.Equal(ForemanRole.BodyKeyPrefix + "revision", WorkerBody.RevisionField);
    }

    [Fact]
    public void TheRoleAnswersAnAbsoluteRootAndRefusesEveryPurpose()
    {
        var role = new ForemanNpcRole(DataRoot);
        Assert.Equal(DataRoot, role.Paths.Root);

        // A guessed path would let a shared runtime drop a file into this
        // product's data root that this product's own loader knows nothing about.
        Assert.False(role.Paths.TryResolveFile("journal", out string path));
        Assert.Equal(string.Empty, path);
        Assert.False(role.Paths.TryResolveFile(string.Empty, out _));
    }

    [Fact]
    public void RegisteringThorsteinIsAcceptedAndIsOfferedOnlyOnce()
    {
        var registry = new NpcRoleRegistry();
        var adoption = new ForemanNpcAdoption(registry, new ForemanNpcRole(DataRoot));

        RoleRegistration first = adoption.Register();
        Assert.True(first.IsRegistered);
        Assert.True(adoption.IsRegistered);
        Assert.Equal(1, registry.RoleCount);

        // Called again from a second plugin start or a reload: offered once, so the
        // second call is not a DuplicateIdentity refusal in the player's log.
        Assert.Equal(RoleRegistrationStatus.Unspecified, adoption.Register().Status);
        Assert.Equal(1, registry.RoleCount);
    }

    // --- Half two: the mode comes from the arbiter -------------------------------------------------------

    [Fact]
    public void AnIdentityTheArbiterDoesNotTrackGrantsNothingAndPermitsNothing()
    {
        // Registration refused at load, or the library never registered at all.
        var registry = new NpcRoleRegistry();
        var adoption = new ForemanNpcAdoption(registry, new ForemanNpcRole(DataRoot));
        IActorModeHold hold = adoption.Modes;

        Assert.False(hold.IsIdentityKnown);

        // No job may take him. Unspecified, not RefusedBusy: the reason is that
        // nothing can be established, which is a different fact from "busy".
        Assert.Equal(ActorModeOutcome.Unspecified, hold.Enter(ActorMode.Working, "collect-1"));
        Assert.False(ActorModeGrants.IsGranted(hold.Enter(ActorMode.Working, "collect-1")));

        // And nothing may move or retire his body, because "I could not tell" is
        // not "nobody is standing in there". This is the fail-closed direction:
        // worker authority refuses on ambiguous evidence. Only feature access
        // grants on it, and this is not feature access.
        Assert.Equal(ActorMode.Unspecified, hold.Mode);
        Assert.Null(hold.JobId);
        Assert.False(hold.MayRetireBody);
        Assert.False(hold.MayRelocateHome);
        Assert.False(hold.IsHeldBy("collect-1"));
        Assert.False(hold.IsHeldBy(null));
    }

    [Fact]
    public void TakingTheModeIsVisibleOnTheArbitersOwnOwnerAndGivingItBackReturnsHimToResting()
    {
        ForemanNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);
        IActorModeHold hold = adoption.Modes;

        Assert.Equal(ActorMode.Resting, hold.Mode);
        Assert.True(hold.MayRetireBody);

        Assert.Equal(ActorModeOutcome.Entered, hold.Enter(ActorMode.Working, "collect-1"));

        // The point of the whole adoption: this is the LIBRARY's owner, the one
        // object every product in the process reads. Before the adoption it said
        // Resting while Foreman's private owner said Working.
        Lib.ActorModeOwner owner = registry.ModeOf(adoption.Identity)!;
        Assert.Equal(Lib.ActorMode.Working, owner.Mode);
        Assert.Equal("collect-1", owner.JobId);
        Assert.False(owner.MayRetireBody);

        // And the same fact read back through the hold, in this product's words.
        Assert.Equal(ActorMode.Working, hold.Mode);
        Assert.Equal("collect-1", hold.JobId);
        Assert.True(hold.IsHeldBy("collect-1"));
        Assert.False(hold.IsHeldBy("collect-2"));
        Assert.False(hold.MayRetireBody);

        Assert.Equal(ActorModeOutcome.Released, hold.Release("collect-1"));
        Assert.Equal(ActorMode.Resting, hold.Mode);
        Assert.Null(hold.JobId);
        Assert.True(hold.MayRetireBody);
        Assert.Equal(Lib.ActorMode.Resting, owner.Mode);
    }

    [Fact]
    public void ASecondJobIsRefusedBusyAndReleasingOneHeNeverHeldChangesNothing()
    {
        ForemanNpcAdoption adoption = Adopted(out _);
        IActorModeHold hold = adoption.Modes;

        Assert.Equal(ActorModeOutcome.Entered, hold.Enter(ActorMode.Surveying, "collect-1"));
        Assert.Equal(ActorModeOutcome.AlreadyInMode, hold.Enter(ActorMode.Surveying, "collect-1"));
        Assert.Equal(ActorModeOutcome.Entered, hold.Enter(ActorMode.Working, "collect-1"));

        Assert.Equal(ActorModeOutcome.RefusedBusy, hold.Enter(ActorMode.Working, "build-2"));
        Assert.Equal(ActorMode.Working, hold.Mode);
        Assert.Equal("collect-1", hold.JobId);

        Assert.Equal(ActorModeOutcome.NotHeld, hold.Release("build-2"));
        Assert.Equal("collect-1", hold.JobId);
    }

    [Fact]
    public void RestingAndANamelessJobAreRefusedRatherThanThrown()
    {
        // Four products register into one process and one of them getting this
        // wrong must not take the others down, so the library answers rather than
        // throwing - and this hold passes that answer through unchanged.
        ForemanNpcAdoption adoption = Adopted(out _);
        IActorModeHold hold = adoption.Modes;

        Assert.Equal(ActorModeOutcome.Unspecified, hold.Enter(ActorMode.Resting, "collect-1"));
        Assert.Equal(ActorModeOutcome.Unspecified, hold.Enter(ActorMode.Unspecified, "collect-1"));
        Assert.Equal(ActorModeOutcome.Unspecified, hold.Enter(ActorMode.Working, string.Empty));
        Assert.Equal(ActorModeOutcome.Unspecified, hold.Release(string.Empty));
        Assert.Equal(ActorMode.Resting, hold.Mode);
    }

    [Fact]
    public void WhileAJobHoldsHimNothingElseInTheProcessMayTakeHisBody()
    {
        // The refusal the adoption actually adds. Before it, the arbiter could not
        // see that Thorstein was working, so this claim was granted - which is one
        // identity with two bodies, the exact thing the never-coexist rule forbids.
        ForemanNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);
        adoption.NoteWorldLoaded();

        Assert.Equal(ActorModeOutcome.Entered, adoption.Modes.Enter(ActorMode.Working, "collect-1"));

        Lib.BodyClaim intruder = registry.TryClaimBody(adoption.Identity, NpcBodyKind.Worker, "some-other-runtime");
        Assert.False(intruder.IsGranted);
        Assert.Equal(Lib.BodyClaimStatus.RefusedHolderConflict, intruder.Status);
        Assert.Contains("collect-1", intruder.Reason);

        // The job that holds the mode is not refused: it is the holder.
        Assert.True(registry.TryClaimBody(adoption.Identity, NpcBodyKind.Worker, "collect-1").IsGranted);
    }

    [Fact]
    public void AWorldUnloadEndsAHoldNothingIsLeftToReleaseByName()
    {
        // A job belongs to one world load. Its body dies with the scene and nothing
        // is left holding its id, so an ordinary release - which needs the holder's
        // own string - cannot help. Without this, the identity is busy for the life
        // of the process and no later order can ever start.
        ForemanNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);
        adoption.NoteWorldLoaded();
        Assert.Equal(ActorModeOutcome.Entered, adoption.Modes.Enter(ActorMode.Working, "collect-1"));

        adoption.NoteWorldUnloaded();

        Assert.Equal(ActorMode.Resting, adoption.Modes.Mode);
        Assert.Null(adoption.Modes.JobId);
        Assert.True(adoption.Modes.MayRetireBody);
        Assert.True(registry.CurrentWorld.IsUnknown);

        // And the next world can start an order again.
        adoption.NoteWorldLoaded();
        Assert.Equal(ActorModeOutcome.Entered, adoption.Modes.Enter(ActorMode.Working, "collect-2"));
    }

    [Fact]
    public void AWorldLoadNoticeIsIdempotentAndSurvivesAnotherProductGettingThereFirst()
    {
        ForemanNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);

        NpcWorldEpoch first = adoption.NoteWorldLoaded();
        Assert.False(first.IsUnknown);

        // Re-asked on a later frame, and asked by a second product's runtime: the
        // same epoch, and nothing forgotten. A mode taken in this world must not be
        // dropped because somebody said "a world loaded" twice.
        Assert.Equal(ActorModeOutcome.Entered, adoption.Modes.Enter(ActorMode.Working, "collect-1"));
        Assert.True(first.Matches(adoption.NoteWorldLoaded()));
        Assert.True(first.Matches(registry.BeginWorldLoad(out int forgotten)));
        Assert.Equal(0, forgotten);
        Assert.Equal("collect-1", adoption.Modes.JobId);
    }

    // --- The two vocabularies --------------------------------------------------------------------------

    [Fact]
    public void EveryModeMapsToItsCounterpartInBothDirections()
    {
        // Mapped member by member rather than cast, so a value either side gains
        // that the other does not know becomes a refusal instead of a silently
        // wrong mode. This pins every pair that exists today, in both directions.
        var pairs = new List<(ActorMode Mine, Lib.ActorMode Theirs)>
        {
            (ActorMode.Unspecified, Lib.ActorMode.Unspecified),
            (ActorMode.Resting, Lib.ActorMode.Resting),
            (ActorMode.Surveying, Lib.ActorMode.Surveying),
            (ActorMode.Working, Lib.ActorMode.Working),
            (ActorMode.Paused, Lib.ActorMode.Paused),
            (ActorMode.Recovering, Lib.ActorMode.Recovering),
        };

        // Every member of BOTH enums is covered, so a member added on either side
        // without a mapping fails here rather than becoming Unspecified in silence.
        Assert.Equal(Enum.GetValues(typeof(ActorMode)).Length, pairs.Count);
        Assert.Equal(Enum.GetValues(typeof(Lib.ActorMode)).Length, pairs.Count);

        foreach ((ActorMode mine, Lib.ActorMode theirs) in pairs)
        {
            Assert.Equal(theirs, ArbiterActorMode.ToLibrary(mine));
            Assert.Equal(mine, ArbiterActorMode.FromLibrary(theirs));
        }
    }

    [Fact]
    public void EveryOutcomeIsReportedAsItself()
    {
        // Unspecified stays Unspecified. Translating it into RefusedBusy so that
        // pre-adoption callers kept working would be a lie about why the hold was
        // refused, and would hide the case the hardened intake gate exists for.
        var pairs = new List<(Lib.ActorModeOutcome Theirs, ActorModeOutcome Mine)>
        {
            (Lib.ActorModeOutcome.Unspecified, ActorModeOutcome.Unspecified),
            (Lib.ActorModeOutcome.Entered, ActorModeOutcome.Entered),
            (Lib.ActorModeOutcome.AlreadyInMode, ActorModeOutcome.AlreadyInMode),
            (Lib.ActorModeOutcome.RefusedBusy, ActorModeOutcome.RefusedBusy),
            (Lib.ActorModeOutcome.Released, ActorModeOutcome.Released),
            (Lib.ActorModeOutcome.NotHeld, ActorModeOutcome.NotHeld),
        };

        Assert.Equal(Enum.GetValues(typeof(Lib.ActorModeOutcome)).Length, pairs.Count);
        Assert.Equal(Enum.GetValues(typeof(ActorModeOutcome)).Length, pairs.Count);

        foreach ((Lib.ActorModeOutcome theirs, ActorModeOutcome mine) in pairs)
        {
            Assert.Equal(mine, ArbiterActorMode.FromLibrary(theirs));
        }
    }

    [Fact]
    public void AHoldMustBeForAWorkerAndAnIdentity()
    {
        var registry = new NpcRoleRegistry();
        Assert.Throws<ArgumentNullException>(
            () => new ArbiterActorMode(null!, WorkerKey.Thorstein, ForemanNpcRole.Id));
        Assert.Throws<ArgumentException>(
            () => new ArbiterActorMode(registry, default, ForemanNpcRole.Id));
        Assert.Throws<ArgumentException>(
            () => new ArbiterActorMode(registry, WorkerKey.Thorstein, default));
    }

    [Fact]
    public void TheHoldSaysWhoItIsFor()
    {
        // CollectionRuntime checks this before it drives anything: a runtime handed
        // somebody else's hold would move Thorstein on another identity's
        // authority.
        ForemanNpcAdoption adoption = Adopted(out _);
        Assert.Equal(WorkerKey.Thorstein, adoption.Modes.Worker);
        Assert.Equal(WorkerKey.Thorstein, adoption.Worker);
        Assert.NotEqual(WorkerKey.Gunnar, adoption.Modes.Worker);
    }
}
