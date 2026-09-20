using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Persistence;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>Durable plan state, against a real temporary directory - and the
/// boundary that keeps the format and the path on the role's side of it.
///
/// <b>What these tests are really about.</b> Not that a file can be written; the
/// sidecar suite already proves that. They are about who decided what is in it.
/// Every durable decision in this area is made in <c>RolePlanCodec</c>, which is a
/// test double standing in for a role, and the library cannot reach any of them -
/// it hands over lines and takes lines back. That is what makes adopting this
/// mechanism a code move for a shipped product rather than a migration, and it is
/// the one property here that a reviewer cannot check by reading the library
/// alone.</summary>
public class PlanJournalTests : IDisposable
{
    private readonly string _root;

    public PlanJournalTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cnpc-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // A temporary directory something is holding open is not a failure.
        }
    }

    [Fact]
    public void This_library_composes_no_path_of_its_own_and_invents_no_format()
    {
        Assert.False(NpcPlanJournal.TryOpen(null, new RolePlanCodec(), out _, out string reason));
        Assert.Contains("composes none", reason, StringComparison.Ordinal);

        Assert.False(NpcPlanJournal.TryOpen("plan.tsv", new RolePlanCodec(), out _, out reason));
        Assert.Contains("must be absolute", reason, StringComparison.Ordinal);

        // And with nowhere to get the bytes from, there is no fallback shape it
        // could write instead.
        Assert.False(NpcPlanJournal.TryOpen(Path.Combine(_root, "plan.tsv"), null, out _, out reason));
        Assert.Contains("owns no format", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void No_plan_file_is_a_first_run_rather_than_a_failure()
    {
        NpcPlanJournal journal = Open("nothing-here.tsv", new RolePlanCodec());

        NpcPlanLoad load = journal.Load();

        Assert.True(load.IsAbsent);
        Assert.False(load.IsLoaded);
        Assert.False(load.IsUnreadable);
        Assert.Null(load.Plan);
        Assert.False(journal.Exists);
    }

    [Fact]
    public void Every_durable_field_survives_the_round_trip()
    {
        var codec = new RolePlanCodec();
        NpcPlanJournal journal = Open("plan.tsv", codec);
        NpcPlanState written = Loaded();

        Assert.True(journal.Save(written).IsSaved);
        NpcPlanLoad load = journal.Load();

        Assert.True(load.IsLoaded, load.Failure);
        NpcPlanState read = load.Plan!;

        // The acceptance criterion's own list: plan identity, current phase,
        // reservations, carried inventory, target progress, assigned vehicle,
        // source and destination, custody state.
        Assert.Equal(written.Identity, read.Identity);
        Assert.Equal(written.JobId, read.JobId);
        Assert.Equal(written.Phase, read.Phase);
        Assert.Equal(written.Custody, read.Custody);
        Assert.Equal(written.Reservations, read.Reservations);
        Assert.Equal(written.Carried, read.Carried);
        Assert.Equal(written.TargetsDone, read.TargetsDone);
        Assert.Equal(written.TargetsTotal, read.TargetsTotal);
        Assert.Equal(written.SourceKey, read.SourceKey);
        Assert.Equal(written.DestinationKey, read.DestinationKey);
        Assert.Equal(written.VehicleKey, read.VehicleKey);
        Assert.Equal(written.Note, read.Note);

        // And the three things reconstruction changes on the way back, which are
        // the library's and not the codec's.
        Assert.True(read.World.IsUnknown);
        Assert.Equal(written.Attempt + 1, read.Attempt);
    }

    [Fact]
    public void A_plan_that_came_back_from_disk_is_in_no_world_at_all()
    {
        // Asserted against the library's own function rather than through a codec,
        // deliberately. A role cannot mint an epoch - the constructor is internal
        // and roles live in other assemblies - so a codec can only ever hand back
        // an unknown one, and a test that went through a codec would pass whether
        // this rule existed or not. It has to exist: every key a plan holds was
        // minted in a world load that has ended, and an unknown epoch is what makes
        // them all refuse rather than name whatever now answers to the number.
        NpcPlanState live = Loaded();
        Assert.False(live.World.IsUnknown);

        NpcPlanState recovered = live.AsRecovered();

        Assert.True(recovered.World.IsUnknown);
        Assert.False(recovered.IsLiveIn(live.World));
        Assert.Equal(live.Attempt + 1, recovered.Attempt);
        Assert.True(recovered.CarriesTheSameWorkAs(live));
    }

    [Fact]
    public void A_plan_that_was_mid_movement_comes_back_uncertain_rather_than_pending()
    {
        var codec = new RolePlanCodec();
        NpcPlanJournal journal = Open("plan.tsv", codec);

        Assert.True(journal.Save(Loaded().WithCustody(NpcPlanCustody.Pending, "about to hand over")).IsSaved);

        NpcPlanState read = journal.Load().Plan!;

        // The codec wrote "pending" and read "pending" back; the library is what
        // turns it into a question for a person. A role cannot forget to do it
        // because a role is not what does it.
        Assert.Equal(NpcPlanCustody.Uncertain, read.Custody);
        Assert.True(read.IsUncertain);
    }

    [Fact]
    public void A_custody_field_nobody_set_is_treated_exactly_like_one_that_was_in_flight()
    {
        var codec = new RolePlanCodec();
        NpcPlanJournal journal = Open("plan.tsv", codec);

        // Written straight past the run, the way a role with its own writer and a
        // bug in it would.
        Assert.True(journal.Save(Loaded().WithCustody(NpcPlanCustody.Unspecified, "who knows")).IsSaved);

        Assert.Equal(NpcPlanCustody.Uncertain, journal.Load().Plan!.Custody);
    }

    [Fact]
    public void A_plan_the_role_cannot_read_is_left_exactly_where_it_is()
    {
        var codec = new RolePlanCodec();
        NpcPlanJournal journal = Open("plan.tsv", codec);
        Assert.True(journal.Save(Loaded()).IsSaved);
        byte[] before = File.ReadAllBytes(Path.Combine(_root, "plan.tsv"));

        codec.RefuseToDecode = true;
        NpcPlanLoad load = journal.Load();

        Assert.True(load.IsUnreadable);
        Assert.False(load.IsAbsent);
        Assert.Contains("row version", load.Failure, StringComparison.Ordinal);

        // Nothing was deleted and nothing was written over: the file that could
        // not be read is the only copy of what this NPC is holding.
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_root, "plan.tsv")));
    }

    [Fact]
    public void A_role_whose_reader_throws_does_not_take_the_world_load_with_it()
    {
        var codec = new ThrowingReaderCodec();
        NpcPlanJournal journal = Open("plan.tsv", new RolePlanCodec());
        Assert.True(journal.Save(Loaded()).IsSaved);

        NpcPlanLoad load = Open("plan.tsv", codec).Load();

        // An exception, not a refusal, and the answer is still "could not read"
        // rather than "there was no plan" - which is the difference between
        // stopping and writing an empty plan over a full one.
        Assert.True(load.IsUnreadable);
        Assert.False(load.IsAbsent);
        Assert.Contains("InvalidOperationException", load.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_role_that_refuses_or_throws_while_writing_leaves_the_last_good_plan_alone()
    {
        var codec = new RolePlanCodec();
        NpcPlanJournal journal = Open("plan.tsv", codec);
        Assert.True(journal.Save(Loaded()).IsSaved);
        byte[] good = File.ReadAllBytes(Path.Combine(_root, "plan.tsv"));

        codec.Refuse = true;
        NpcPlanSave refused = journal.Save(Loaded().WithProgress(3));
        Assert.False(refused.IsSaved);
        Assert.Contains("refused", refused.Failure, StringComparison.Ordinal);

        codec.Refuse = false;
        codec.Throw = true;
        NpcPlanSave threw = journal.Save(Loaded().WithProgress(3));
        Assert.False(threw.IsSaved);
        Assert.Contains("InvalidOperationException", threw.Failure, StringComparison.Ordinal);

        Assert.Equal(good, File.ReadAllBytes(Path.Combine(_root, "plan.tsv")));
    }

    [Fact]
    public void A_plan_that_cannot_be_resumed_is_never_written_in_the_first_place()
    {
        var codec = new RolePlanCodec();
        NpcPlanJournal journal = Open("plan.tsv", codec);

        Assert.False(journal.Save(null).IsSaved);
        Assert.False(journal.Save(NpcPlanState.Opening(default, "haul", Identities.AWorld(), 2)).IsSaved);
        Assert.False(journal.Save(NpcPlanState.Opening(Identities.Gunnar, null, Identities.AWorld(), 2)).IsSaved);
        Assert.False(journal.Save(Loaded().WithPhase(NpcPlanPhase.Unspecified, "nowhere")).IsSaved);

        // Not one of them reached the role's writer, so not one of them could have
        // dropped a file into the role's data root.
        Assert.Equal(0, codec.Writes);
        Assert.False(journal.Exists);
    }

    [Fact]
    public void A_temporary_file_left_by_an_interrupted_write_is_not_the_plan()
    {
        var codec = new RolePlanCodec();
        NpcPlanJournal journal = Open("plan.tsv", codec);
        Assert.True(journal.Save(Loaded().WithProgress(2)).IsSaved);

        // Exactly what a process killed inside the write leaves behind: a
        // complete-or-partial temporary beside a complete live file.
        File.WriteAllText(
            Path.Combine(_root, "plan.tsv.tmp"),
            "p\t3\tteamster\tgunnar\thaul\t9\t1\t99\t99\t\t\t\t0\thalf a line",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        NpcPlanState read = journal.Load().Plan!;

        Assert.Equal(2, read.TargetsDone);
        Assert.NotEqual(NpcPlanPhase.Settled, read.Phase);
    }

    private NpcPlanJournal Open(string fileName, INpcPlanCodec codec)
    {
        Assert.True(
            NpcPlanJournal.TryOpen(Path.Combine(_root, fileName), codec, out NpcPlanJournal? journal, out string why),
            why);
        return journal!;
    }

    /// <summary>A plan with every field set to something distinguishable, so a
    /// round trip that dropped one is a failure rather than a coincidence.
    /// </summary>
    private static NpcPlanState Loaded() =>
        new NpcPlanState(
            Identities.Gunnar,
            "haul-round",
            NpcPlanPhase.Executing,
            NpcPlanCustody.Clear,
            new[] { ReservationId.For("haul-round", 0), ReservationId.For("haul-round", 3) },
            new[]
            {
                new NpcMaterialStack(NpcMaterial.Of("stone"), 7),
                new NpcMaterialStack(new NpcMaterial("axe", 3, 1), 1),
            },
            2,
            5,
            "the-pile",
            "the-depot",
            "the-cart",
            Identities.AWorld(),
            0,
            "walking it, with a 100% full back");

    private sealed class ThrowingReaderCodec : INpcPlanCodec
    {
        public IReadOnlyList<string>? Encode(NpcPlanState state) => new string[0];

        public bool TryDecode(IReadOnlyList<string> lines, out NpcPlanState? state, out string reason) =>
            throw new InvalidOperationException("the role's reader is broken");
    }
}
