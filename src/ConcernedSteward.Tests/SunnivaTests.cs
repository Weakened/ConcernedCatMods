using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedSteward.Domain;
using TheConcernedCat.ConcernedSteward.Domain.Appearance;
using TheConcernedCat.ConcernedSteward.Domain.Npc;
using TheConcernedCat.ConcernedSteward.Domain.Persistence;
using TheConcernedCat.ConcernedSteward.Domain.Quest;
using TheConcernedCat.ConcernedSteward.Domain.Recruitment;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;
using TheConcernedCat.ConcernedSteward.Runtime;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>A recorder a test can break on purpose: one that writes, one that
/// cannot, and one that throws the way a disk does.</summary>
internal sealed class FakeQuestRecorder : IStewardQuestRecorder
{
    internal bool Writes { get; set; } = true;

    internal bool Throws { get; set; }

    internal List<(StewardQuestStage stage, StewardQuestStage furthest)> Written { get; } =
        new List<(StewardQuestStage, StewardQuestStage)>();

    public bool TryRecord(StewardQuestStage stage, StewardQuestStage furthest)
    {
        if (Throws)
        {
            throw new IOException("the record could not be written");
        }

        if (!Writes)
        {
            return false;
        }

        Written.Add((stage, furthest));
        return true;
    }
}

/// <summary>Sunniva's introduction: one flint and steel, ever.</summary>
public sealed class StewardQuestTests
{
    private static readonly StewardQuestFacts Ready = new StewardQuestFacts(true, true);

    [Fact]
    public void Picking_up_resin_the_first_time_finds_the_flint_and_steel()
    {
        var recorder = new FakeQuestRecorder();
        var quest = new StewardQuest(recorder);

        StewardQuestResult result = quest.NoticeResinPickedUp(Ready);

        Assert.True(result.Changed);
        Assert.Equal(StewardQuestStage.Found, result.Stage);
        Assert.Equal(StewardQuestSentences.Discovery, result.Announcement);
        Assert.Single(recorder.Written);
    }

    /// <summary>The whole point of the type. Neither a second pickup, nor a
    /// hundredth, nor a reload, nor an interrupted write produces another.
    /// </summary>
    [Fact]
    public void There_is_one_flint_and_steel_and_a_second_pickup_produces_nothing()
    {
        var recorder = new FakeQuestRecorder();
        var quest = new StewardQuest(recorder);

        Assert.True(quest.NoticeResinPickedUp(Ready).Changed);

        for (int again = 0; again < 20; again++)
        {
            StewardQuestResult result = quest.NoticeResinPickedUp(Ready);
            Assert.False(result.Changed);
            Assert.Null(result.Announcement);
        }

        Assert.Single(recorder.Written);
    }

    [Fact]
    public void A_reload_does_not_produce_another_one()
    {
        var recorder = new FakeQuestRecorder();
        var quest = new StewardQuest(recorder);
        Assert.True(quest.NoticeResinPickedUp(Ready).Changed);

        // A new session, restoring what the record said.
        var afterReload = new StewardQuest(new FakeQuestRecorder());
        afterReload.Restore(StewardQuestStage.Found, StewardQuestStage.Found);

        Assert.False(afterReload.NoticeResinPickedUp(Ready).Changed);
        Assert.True(afterReload.ObjectHasBeenFound);
    }

    /// <summary>Even if something later walks the stage backwards, the object
    /// does not come back: the grant is refused on the high-water mark, not on
    /// where the quest currently stands.</summary>
    [Fact]
    public void The_grant_is_refused_on_the_high_water_mark_and_not_on_the_stage()
    {
        var quest = new StewardQuest(new FakeQuestRecorder());

        // A damaged record claiming less than it has ever reached. The repair
        // raises the mark; it never lowers the stage, and the mark is what the
        // object is refused on.
        quest.Restore(StewardQuestStage.Unstarted, StewardQuestStage.Answered);

        Assert.True(quest.ObjectHasBeenFound);
        Assert.False(quest.NoticeResinPickedUp(Ready).Changed);
    }

    /// <summary>The ordering that closes the crash window. Advance-then-save
    /// comes back at Unstarted with the object already announced, and the next
    /// resin produces a second one.</summary>
    [Fact]
    public void Nothing_is_true_until_it_is_written_down()
    {
        var recorder = new FakeQuestRecorder { Writes = false };
        var quest = new StewardQuest(recorder);

        StewardQuestResult refused = quest.NoticeResinPickedUp(Ready);

        Assert.False(refused.Changed);
        Assert.Equal(StewardQuestRefusal.NotRecorded, refused.Refusal);
        Assert.Equal(StewardQuestStage.Unstarted, quest.Stage);
        Assert.False(quest.ObjectHasBeenFound);
        Assert.Null(refused.Announcement);

        // And the next attempt, once the disk is back, still works. A refusal
        // is not a spent chance.
        recorder.Writes = true;
        Assert.True(quest.NoticeResinPickedUp(Ready).Changed);
    }

    [Fact]
    public void A_recorder_that_throws_is_a_recorder_that_did_not_write()
    {
        var quest = new StewardQuest(new FakeQuestRecorder { Throws = true });

        // It must not escape: this runs from an inventory-changed callback, and
        // an exception there takes the player's own handler out with it.
        StewardQuestResult result = quest.NoticeResinPickedUp(Ready);

        Assert.False(result.Changed);
        Assert.Equal(StewardQuestRefusal.NotRecorded, result.Refusal);
    }

    [Fact]
    public void An_unwritable_record_refuses_the_grant_rather_than_making_it_in_memory()
    {
        var quest = new StewardQuest(new FakeQuestRecorder());

        StewardQuestResult result = quest.NoticeResinPickedUp(
            new StewardQuestFacts(somebodyIsThere: true, recordWritable: false));

        Assert.Equal(StewardQuestRefusal.NotRecorded, result.Refusal);
        Assert.False(quest.ObjectHasBeenFound);
    }

    [Fact]
    public void With_nobody_there_nothing_is_found()
    {
        var quest = new StewardQuest(new FakeQuestRecorder());

        Assert.Equal(
            StewardQuestRefusal.NobodyThere,
            quest.NoticeResinPickedUp(new StewardQuestFacts(false, true)).Refusal);
    }

    [Fact]
    public void The_beats_run_in_order_and_skipping_one_is_refused()
    {
        var quest = new StewardQuest(new FakeQuestRecorder());
        Assert.True(quest.NoticeResinPickedUp(Ready).Changed);

        Assert.Equal(
            StewardQuestRefusal.OutOfOrder,
            quest.Advance(StewardQuestStage.Answered, Ready).Refusal);

        Assert.True(quest.Advance(StewardQuestStage.Examined, Ready).Changed);
        Assert.True(quest.Advance(StewardQuestStage.Answered, Ready).Changed);
        Assert.True(quest.Advance(StewardQuestStage.Settled, Ready).Changed);

        // Repeating a beat is idempotent, never a second telling.
        StewardQuestResult again = quest.Advance(StewardQuestStage.Settled, Ready);
        Assert.False(again.Changed);
        Assert.Equal(StewardQuestOutcome.AlreadyThere, again.Outcome);
    }

    [Fact]
    public void There_is_no_step_back_to_not_having_found_anything()
    {
        var quest = new StewardQuest(new FakeQuestRecorder());
        Assert.True(quest.NoticeResinPickedUp(Ready).Changed);

        Assert.Equal(
            StewardQuestRefusal.OutOfOrder,
            quest.Advance(StewardQuestStage.Unstarted, Ready).Refusal);
        Assert.Equal(StewardQuestStage.Found, quest.Stage);
    }

    /// <summary>The introduction is several beats and not one speech, and each
    /// of the things #382 asks it to establish is actually said somewhere. A
    /// deleted beat fails here rather than quietly losing the character.
    /// </summary>
    [Fact]
    public void The_beats_establish_who_she_is()
    {
        string found = string.Join(" ", StewardQuestSentences.LinesFor(StewardQuestStage.Found));
        string examined = string.Join(" ", StewardQuestSentences.LinesFor(StewardQuestStage.Examined));
        string answered = string.Join(" ", StewardQuestSentences.LinesFor(StewardQuestStage.Answered));
        string settled = string.Join(" ", StewardQuestSentences.LinesFor(StewardQuestStage.Settled));

        // Several beats, not one exposition dump.
        Assert.True(StewardQuestSentences.LinesFor(StewardQuestStage.Examined).Length >= 2);
        Assert.True(StewardQuestSentences.LinesFor(StewardQuestStage.Answered).Length >= 3);

        // The object, word for word as the issue specifies it.
        Assert.Contains(StewardQuestSentences.Discovery, found);

        // It was hers, and she has a name.
        Assert.Contains("Sunniva", examined);

        // A warrior-priestess who fell in battle, carrying light into dark
        // places.
        Assert.Contains("priestess", answered);
        Assert.Contains("spear", answered);
        Assert.Contains("fell", answered);
        Assert.Contains("light into", answered);

        // The flames are a responsibility she takes seriously.
        Assert.Contains("Fires go out", answered);

        // And after being taken on, she stays.
        Assert.Contains("not going anywhere", settled);

        // Nothing has anything to say before anything has happened.
        Assert.Empty(StewardQuestSentences.LinesFor(StewardQuestStage.Unstarted));
    }
}

/// <summary>Noticing a pickup without patching the game.</summary>
public sealed class ResinWatchTests
{
    [Fact]
    public void The_first_look_is_a_baseline_and_never_a_pickup()
    {
        var watch = new ResinWatch();

        // Somebody who loads a save with forty resin already in their pack has
        // not just picked any up.
        Assert.Equal(ResinSighting.Nothing, watch.Observe(40, aWindowIsOpen: false));
        Assert.True(watch.IsPrimed);
    }

    [Fact]
    public void More_resin_with_no_window_open_is_a_pickup()
    {
        var watch = new ResinWatch();
        watch.Observe(0, aWindowIsOpen: false);

        Assert.Equal(ResinSighting.PickedUp, watch.Observe(1, aWindowIsOpen: false));
    }

    [Fact]
    public void Taking_resin_out_of_a_chest_is_not_a_pickup()
    {
        var watch = new ResinWatch();
        watch.Observe(0, aWindowIsOpen: false);

        // The window opens, the player takes ten, the window closes. The
        // baseline moves on every frame the window is open, so by the time it
        // shuts there is nothing to notice.
        Assert.Equal(ResinSighting.Moved, watch.Observe(0, aWindowIsOpen: true));
        Assert.Equal(ResinSighting.Moved, watch.Observe(10, aWindowIsOpen: true));
        Assert.Equal(ResinSighting.Nothing, watch.Observe(10, aWindowIsOpen: false));
    }

    [Fact]
    public void A_pack_that_could_not_be_counted_forgets_rather_than_keeping_a_stale_baseline()
    {
        var watch = new ResinWatch();
        watch.Observe(0, aWindowIsOpen: false);

        Assert.Equal(ResinSighting.Unreadable, watch.Observe(-1, aWindowIsOpen: false));
        Assert.False(watch.IsPrimed);

        // "I could not see for a moment" must not become "and then twenty
        // appeared".
        Assert.Equal(ResinSighting.Nothing, watch.Observe(20, aWindowIsOpen: false));
        Assert.Equal(ResinSighting.PickedUp, watch.Observe(21, aWindowIsOpen: false));
    }

    [Fact]
    public void Spending_resin_is_not_a_pickup_and_the_baseline_follows_it_down()
    {
        var watch = new ResinWatch();
        watch.Observe(10, aWindowIsOpen: false);

        Assert.Equal(ResinSighting.Nothing, watch.Observe(4, aWindowIsOpen: false));
        Assert.Equal(ResinSighting.PickedUp, watch.Observe(5, aWindowIsOpen: false));
    }

    [Fact]
    public void A_world_that_went_away_takes_the_baseline_with_it()
    {
        var watch = new ResinWatch();
        watch.Observe(10, aWindowIsOpen: false);

        watch.Forget();

        Assert.False(watch.IsPrimed);
        Assert.Equal(ResinSighting.Nothing, watch.Observe(0, aWindowIsOpen: false));
    }
}

/// <summary>What the Steward hands Concerned NPC, and what it hands back.
/// </summary>
public sealed class StewardNpcAdoptionTests
{
    /// <summary>A registry of this test's own. The product uses the
    /// process-wide <c>Shared</c> one, which is the whole point of the arbiter;
    /// a test that used it would be sharing state with every other test in the
    /// suite, which runs its collections in parallel.</summary>
    private static NpcRoleRegistry PrivateRegistry() =>
        (NpcRoleRegistry)Activator.CreateInstance(typeof(NpcRoleRegistry), nonPublic: true)!;

    private static StewardNpcAdoption Adopted(out NpcRoleRegistry registry)
    {
        registry = PrivateRegistry();
        var adoption = new StewardNpcAdoption(
            registry, new StewardNpcRole(Path.Combine(Path.GetTempPath(), "cs-steward-tests")));
        Assert.True(adoption.Register().IsRegistered);
        return adoption;
    }

    /// <summary>Zero migrations, checked rather than promised: the three facts
    /// handed to the library are the three already on a player's disk.</summary>
    [Fact]
    public void The_durable_facts_handed_over_are_the_ones_already_on_disk()
    {
        var role = new StewardNpcRole(Path.Combine(Path.GetTempPath(), "cs-steward-tests"));

        // The identity is the worker key this product already writes.
        Assert.Equal(WorkerKey.Steward.Value, role.Identity.Value);
        Assert.Equal("steward/steward", role.Identity.Value);

        // The prefab name a saved body is found by. Renaming it deletes every
        // existing Steward on the next load.
        Assert.Equal(StewardRole.BodyPrefabName, role.Body.PrefabName);
        Assert.Equal("CS_Steward", role.Body.PrefabName);

        // And the key prefix composes the three keys StewardBody has always
        // written, character for character.
        Assert.Equal("tcc.steward.", role.Body.ZdoKeyPrefix);
        Assert.Equal("tcc.steward.key", role.Body.ZdoKeyPrefix + "key");
        Assert.Equal("tcc.steward.inventory", role.Body.ZdoKeyPrefix + "inventory");
        Assert.Equal("tcc.steward.revision", role.Body.ZdoKeyPrefix + "revision");

        Assert.True(role.Body.TryValidate(out _));
    }

    /// <summary>The literals those keys are actually read by, pinned beside the
    /// prefix so the two cannot drift apart. <c>StewardBody</c> deliberately
    /// keeps its own literals: a saved body is read by them, and recomposing
    /// them from parts would be a change to a player's save file dressed as a
    /// tidy-up.</summary>
    [Fact]
    public void The_shipped_key_literals_still_compose_from_the_prefix()
    {
        Assert.Equal(StewardRole.BodyKeyPrefix + "key", StewardBody.KeyField);
        Assert.Equal(StewardRole.BodyKeyPrefix + "inventory", StewardBody.InventoryField);
        Assert.Equal(StewardRole.BodyKeyPrefix + "revision", StewardBody.RevisionField);
    }

    [Fact]
    public void Registering_twice_is_offered_once()
    {
        StewardNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);

        // A role is registered per process, never per world. Offering again
        // would be refused as a duplicate and would fill a log with it.
        Assert.False(adoption.Register().IsRegistered);
        Assert.True(adoption.IsRegistered);
        Assert.Equal(1, registry.RoleCount);
    }

    [Fact]
    public void A_body_cannot_be_held_before_a_world_is_loaded()
    {
        StewardNpcAdoption adoption = Adopted(out _);

        BodyClaim refused = adoption.ClaimBody();

        Assert.Equal(BodyClaimStatus.RefusedNoWorld, refused.Status);
        Assert.False(adoption.HoldsBody);
    }

    [Fact]
    public void One_body_is_claimed_and_re_asking_is_a_grant_rather_than_a_refusal()
    {
        StewardNpcAdoption adoption = Adopted(out _);
        adoption.NoteWorldLoaded();

        Assert.Equal(BodyClaimStatus.Claimed, adoption.ClaimBody().Status);
        Assert.True(adoption.HoldsBody);

        // Re-asked every tick on purpose.
        Assert.Equal(BodyClaimStatus.AlreadyHeld, adoption.ClaimBody().Status);
        Assert.True(adoption.HoldsBody);
    }

    /// <summary>What the removed <c>ActorModeOwner</c> could never say: the
    /// refusal is process-wide, so a second runtime in a second assembly is
    /// refused too.</summary>
    [Fact]
    public void A_second_holder_of_the_same_identity_is_refused()
    {
        StewardNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);
        adoption.NoteWorldLoaded();
        Assert.True(adoption.ClaimBody().IsGranted);

        BodyClaim second = registry.TryClaimBody(
            adoption.Identity, NpcBodyKind.Worker, "somebody-else");

        Assert.Equal(BodyClaimStatus.RefusedHolderConflict, second.Status);
        Assert.False(second.IsGranted);
    }

    [Fact]
    public void The_hold_ends_with_the_world_it_stood_in()
    {
        StewardNpcAdoption adoption = Adopted(out NpcRoleRegistry registry);
        adoption.NoteWorldLoaded();
        Assert.True(adoption.ClaimBody().IsGranted);

        adoption.NoteWorldUnloaded();

        Assert.False(adoption.HoldsBody);
        Assert.True(registry.CurrentWorld.IsUnknown);
        Assert.Equal(NpcBodyKind.Unspecified, registry.CurrentBodyKind(adoption.Identity));

        // And the next world starts clean rather than refused for ever by a
        // holder string nothing still has.
        adoption.NoteWorldLoaded();
        Assert.Equal(BodyClaimStatus.Claimed, adoption.ClaimBody().Status);
    }

    [Fact]
    public void Releasing_a_body_she_does_not_hold_changes_nothing_and_is_not_an_error()
    {
        StewardNpcAdoption adoption = Adopted(out _);
        adoption.NoteWorldLoaded();

        // Every cleanup path calls this unconditionally, which is the point.
        Assert.Equal(BodyClaimStatus.NotHeld, adoption.ReleaseBody().Status);
        Assert.False(adoption.HoldsBody);
    }

    [Fact]
    public void An_unregistered_Steward_claims_nothing_rather_than_throwing()
    {
        NpcRoleRegistry registry = PrivateRegistry();
        var adoption = new StewardNpcAdoption(
            registry, new StewardNpcRole(Path.Combine(Path.GetTempPath(), "cs-steward-tests")));

        adoption.NoteWorldLoaded();

        Assert.False(adoption.IsRegistered);
        Assert.Equal(BodyClaimStatus.Unspecified, adoption.ClaimBody().Status);
        Assert.False(adoption.HoldsBody);
    }

    [Fact]
    public void A_role_that_cannot_say_where_its_data_lives_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new StewardNpcRole(string.Empty));
    }

    /// <summary>The library never names a file, and this is the half of that
    /// rule the Steward owns: every purpose token is refused rather than turned
    /// into a composed path.</summary>
    [Fact]
    public void No_purpose_token_resolves_to_a_file_this_product_did_not_choose()
    {
        var paths = new StewardDataPaths(Path.Combine(Path.GetTempPath(), "cs-steward-tests"));

        foreach (string purpose in new[] { "sidecar", "journal", "anything", string.Empty })
        {
            Assert.False(paths.TryResolveFile(purpose, out string resolved));
            Assert.Equal(string.Empty, resolved);
        }
    }
}

/// <summary>What she looks like, decided without the game running.</summary>
public sealed class StewardLookTests
{
    [Fact]
    public void The_leather_is_always_the_last_candidate_and_needs_no_dependency()
    {
        StewardLook look = StewardLooks.From(
            modelIndex: 1, preferredChest: null, preferredLegs: null,
            hairItem: null, hairColour: StewardLooks.DefaultHairColour, skinColour: null);

        Outfit only = Assert.Single(look.Clothing);
        Assert.Equal(StewardLooks.LeatherChest, only.Chest);
        Assert.Equal(StewardLooks.LeatherLegs, only.Legs);
    }

    [Fact]
    public void A_garment_somebody_named_is_tried_before_the_leather_and_never_instead_of_it()
    {
        StewardLook look = StewardLooks.From(
            modelIndex: 1, preferredChest: "  ArmorSomeRobe  ", preferredLegs: null,
            hairItem: null, hairColour: null, skinColour: null);

        Assert.Equal(2, look.Clothing.Count);
        Assert.Equal("ArmorSomeRobe", look.Clothing[0].Chest);
        Assert.Equal(string.Empty, look.Clothing[0].Legs);

        // The fallback is still there, so a name this build does not have
        // produces leather rather than nothing.
        Assert.Equal(StewardLooks.LeatherChest, look.Clothing[1].Chest);
    }

    [Fact]
    public void She_is_the_female_model_by_default_and_light_haired()
    {
        StewardLook look = StewardLooks.From(
            modelIndex: 1, preferredChest: null, preferredLegs: null,
            hairItem: null, hairColour: StewardLooks.DefaultHairColour, skinColour: null);

        Assert.Equal(1, look.ModelIndex);
        Assert.NotNull(look.HairColour);
        Assert.True(look.HairColour!.Value.Red > 0.8f);
        Assert.True(look.HairColour!.Value.Blue < look.HairColour!.Value.Red);

        // Nothing is said about her skin or her hair mesh, because nothing can
        // be proved about them from the assembly.
        Assert.Null(look.SkinColour);
        Assert.Equal(string.Empty, look.HairItem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1,2")]
    [InlineData("1,2,3,4")]
    [InlineData("red,green,blue")]
    [InlineData("0.5,,0.5")]
    public void A_colour_that_cannot_be_read_leaves_the_base_creature_alone(string text)
    {
        // Never black. A setting nobody can parse must not be the setting that
        // turns her into a silhouette.
        Assert.False(LookColour.TryParse(text, out _));

        StewardLook look = StewardLooks.From(1, null, null, null, text, text);
        Assert.Null(look.HairColour);
        Assert.Null(look.SkinColour);
    }

    [Fact]
    public void A_colour_outside_the_range_is_clamped_rather_than_refused()
    {
        Assert.True(LookColour.TryParse("2,-1,0.5", out LookColour colour));
        Assert.Equal(1f, colour.Red);
        Assert.Equal(0f, colour.Green);
        Assert.Equal(0.5f, colour.Blue);
    }

    [Fact]
    public void A_look_that_says_nothing_applies_nothing()
    {
        var silent = new StewardLook(0, null, null, null, null);

        Assert.False(silent.SaysAnything);
    }
}

/// <summary>The quest on disk, and the promise that nothing else moved.
/// </summary>
public sealed class QuestRecordTests
{
    private static SettlementScope Scope => new SettlementScope(0x1234L, new SettlementId("home"));

    [Fact]
    public void A_world_where_nothing_has_been_found_writes_the_file_it_always_wrote()
    {
        using var folder = new TemporaryFolder();
        var store = new StewardRecordStore(folder.Path);

        Assert.Null(store.Save(
            Scope, IntroductionStage.Engaged, IntroductionStage.Engaged,
            Array.Empty<Designation>(), Array.Empty<UpkeepIntent>(),
            recordedLoss: 0, fuelItemName: string.Empty));

        string[] lines = File.ReadAllLines(store.ResolvePath(Scope));

        // No quest row at all. A file written before #382 and a file written by
        // this build for a world where nothing has happened are the same bytes.
        Assert.DoesNotContain(lines, line => line.StartsWith("quest", StringComparison.Ordinal));
        Assert.Contains(lines, line => line == "format\t1");
    }

    [Fact]
    public void A_record_with_no_quest_row_loads_as_nothing_having_been_found()
    {
        using var folder = new TemporaryFolder();
        var store = new StewardRecordStore(folder.Path);

        var settlement = new Designation(
            DesignationKind.SettlementArea, new SitePoint(4f, 0f, 4f), 32f, null);

        Assert.Null(store.Save(
            Scope, IntroductionStage.Engaged, IntroductionStage.Engaged,
            new[] { settlement }, Array.Empty<UpkeepIntent>(),
            recordedLoss: 0, fuelItemName: string.Empty));

        RecordLoadReport report = store.Load(Scope);

        // Zero migrations: the designation is still there, the introduction is
        // still there, and no migration code ran.
        Assert.Equal(RecordLoadOutcome.Intact, report.Outcome);
        Assert.Equal(IntroductionStage.Engaged, report.Stage);
        Assert.Single(report.Designations);
        Assert.Equal(0, report.SkippedLines);

        Assert.Equal(StewardQuestStage.Unstarted, report.QuestStage);
        Assert.Equal(StewardQuestStage.Unstarted, report.QuestFurthest);
    }

    [Fact]
    public void The_quest_round_trips_with_its_high_water_mark()
    {
        using var folder = new TemporaryFolder();
        var store = new StewardRecordStore(folder.Path);

        Assert.Null(store.Save(
            Scope, IntroductionStage.Offered, IntroductionStage.Engaged,
            Array.Empty<Designation>(), Array.Empty<UpkeepIntent>(),
            recordedLoss: 0, fuelItemName: string.Empty,
            questStage: StewardQuestStage.Examined,
            questFurthest: StewardQuestStage.Settled));

        RecordLoadReport report = store.Load(Scope);

        Assert.Equal(RecordLoadOutcome.Intact, report.Outcome);
        Assert.Equal(StewardQuestStage.Examined, report.QuestStage);
        Assert.Equal(StewardQuestStage.Settled, report.QuestFurthest);
        Assert.Equal(0, report.SkippedLines);
    }

    /// <summary>The whole recovery path in one test: a session finds the flint
    /// and steel, the game closes, and the next session comes back knowing it.
    /// </summary>
    [Fact]
    public void After_a_reload_she_has_still_been_found_and_is_never_found_again()
    {
        using var folder = new TemporaryFolder();
        var store = new StewardRecordStore(folder.Path);

        // Session one: the write happens first, and only then is it true.
        var written = new List<(StewardQuestStage, StewardQuestStage)>();
        var quest = new StewardQuest(new DelegatingRecorder((stage, furthest) =>
        {
            written.Add((stage, furthest));
            return store.Save(
                Scope, IntroductionStage.Unmet, IntroductionStage.Unmet,
                Array.Empty<Designation>(), Array.Empty<UpkeepIntent>(),
                recordedLoss: 0, fuelItemName: string.Empty,
                questStage: stage, questFurthest: furthest) == null;
        }));

        Assert.True(quest.NoticeResinPickedUp(new StewardQuestFacts(true, true)).Changed);
        Assert.Single(written);

        // Session two.
        RecordLoadReport report = store.Load(Scope);
        var reloaded = new StewardQuest(new FakeQuestRecorder());
        reloaded.Restore(report.QuestStage, report.QuestFurthest);

        Assert.Equal(StewardQuestStage.Found, reloaded.Stage);
        Assert.True(reloaded.ObjectHasBeenFound);
        Assert.False(reloaded.NoticeResinPickedUp(new StewardQuestFacts(true, true)).Changed);
    }

    private sealed class DelegatingRecorder : IStewardQuestRecorder
    {
        private readonly Func<StewardQuestStage, StewardQuestStage, bool> _record;

        internal DelegatingRecorder(Func<StewardQuestStage, StewardQuestStage, bool> record)
        {
            _record = record;
        }

        public bool TryRecord(StewardQuestStage stage, StewardQuestStage furthest) =>
            _record(stage, furthest);
    }
}
