using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Persistence;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>A role's own plan format, written here because a role is where a
/// format belongs.
///
/// <b>This file is the proof of the seam as much as the library is.</b> Every
/// durable decision is on this side of it: the row tags, the schema number, the
/// field order, the escaping and the file name. Not one of them appears in the
/// library, and the library could not read this file if it wanted to - it hands
/// over lines and takes lines back. That is what makes moving a role onto
/// <c>NpcPlanJournal</c> a code move rather than a migration.</summary>
internal sealed class RolePlanCodec : INpcPlanCodec
{
    /// <summary>The role's own schema number. A library that named one would
    /// have to change it to change anything, and every existing save would pay
    /// for it.</summary>
    private const int Schema = 3;

    private const string PlanRow = "p";
    private const string HoldRow = "r";
    private const string MaterialRow = "m";

    /// <summary>How many times this codec has been asked to write. A plan
    /// written down more often than its phases changed is a plan writing in a
    /// loop.</summary>
    internal int Writes { get; private set; }

    /// <summary>Set to refuse the next write, which is what a role whose own
    /// gate is shut does.</summary>
    internal bool Refuse { get; set; }

    /// <summary>Set to throw instead of encoding. A broken role must not be a
    /// broken world load.</summary>
    internal bool Throw { get; set; }

    /// <summary>Set to fail every read the way a role that cannot parse its own
    /// rows does.</summary>
    internal bool RefuseToDecode { get; set; }

    public IReadOnlyList<string>? Encode(NpcPlanState state)
    {
        Writes++;

        if (Throw)
        {
            throw new InvalidOperationException("the role's writer is broken");
        }

        if (Refuse)
        {
            return null;
        }

        var lines = new List<string>
        {
            string.Join(
                "\t",
                PlanRow,
                Schema.ToString(CultureInfo.InvariantCulture),
                NpcAtomicText.Escape(state.Identity.Product),
                NpcAtomicText.Escape(state.Identity.Role),
                NpcAtomicText.Escape(state.JobId),
                ((int)state.Phase).ToString(CultureInfo.InvariantCulture),
                ((int)state.Custody).ToString(CultureInfo.InvariantCulture),
                state.TargetsDone.ToString(CultureInfo.InvariantCulture),
                state.TargetsTotal.ToString(CultureInfo.InvariantCulture),
                NpcAtomicText.Escape(state.SourceKey),
                NpcAtomicText.Escape(state.DestinationKey),
                NpcAtomicText.Escape(state.VehicleKey),
                state.Attempt.ToString(CultureInfo.InvariantCulture),
                NpcAtomicText.Escape(state.Note)),
        };

        foreach (ReservationId hold in state.Reservations)
        {
            lines.Add(string.Join("\t", HoldRow, NpcAtomicText.Escape(hold.Value)));
        }

        foreach (NpcMaterialStack stack in state.Carried)
        {
            lines.Add(string.Join(
                "\t",
                MaterialRow,
                NpcAtomicText.Escape(stack.Material.ItemName),
                stack.Material.Quality.ToString(CultureInfo.InvariantCulture),
                stack.Material.Variant.ToString(CultureInfo.InvariantCulture),
                stack.Count.ToString(CultureInfo.InvariantCulture)));
        }

        return lines;
    }

    public bool TryDecode(IReadOnlyList<string> lines, out NpcPlanState? state, out string reason)
    {
        state = null;
        reason = string.Empty;

        if (RefuseToDecode)
        {
            reason = "this build cannot read that row version";
            return false;
        }

        NpcPlanState? plan = null;
        var holds = new List<ReservationId>();
        var carried = new List<NpcMaterialStack>();

        foreach (string line in lines)
        {
            string[] fields = line.Split('\t');
            if (fields.Length == 0)
            {
                continue;
            }

            if (fields[0] == PlanRow)
            {
                if (fields.Length < 14 || fields[1] != Schema.ToString(CultureInfo.InvariantCulture))
                {
                    reason = "that is not a plan row this build writes";
                    return false;
                }

                plan = new NpcPlanState(
                    new NpcIdentity(NpcAtomicText.Unescape(fields[2]), NpcAtomicText.Unescape(fields[3])),
                    NpcAtomicText.Unescape(fields[4]),
                    (NpcPlanPhase)int.Parse(fields[5], CultureInfo.InvariantCulture),
                    (NpcPlanCustody)int.Parse(fields[6], CultureInfo.InvariantCulture),
                    null,
                    null,
                    int.Parse(fields[7], CultureInfo.InvariantCulture),
                    int.Parse(fields[8], CultureInfo.InvariantCulture),
                    NpcAtomicText.Unescape(fields[9]),
                    NpcAtomicText.Unescape(fields[10]),
                    NpcAtomicText.Unescape(fields[11]),
                    // The role cannot mint an epoch and does not try: what world
                    // a plan's keys belong to is the library's business, and a
                    // plan off the disk belongs to none.
                    NpcWorldEpoch.Unknown,
                    int.Parse(fields[12], CultureInfo.InvariantCulture),
                    NpcAtomicText.Unescape(fields[13]));
                continue;
            }

            if (fields[0] == HoldRow && fields.Length >= 2
                && ReservationId.TryParse(NpcAtomicText.Unescape(fields[1]), out ReservationId hold))
            {
                holds.Add(hold);
                continue;
            }

            if (fields[0] == MaterialRow && fields.Length >= 5)
            {
                carried.Add(new NpcMaterialStack(
                    new NpcMaterial(
                        NpcAtomicText.Unescape(fields[1]),
                        int.Parse(fields[2], CultureInfo.InvariantCulture),
                        int.Parse(fields[3], CultureInfo.InvariantCulture)),
                    int.Parse(fields[4], CultureInfo.InvariantCulture)));
            }
        }

        if (plan == null)
        {
            reason = "there was no plan row";
            return false;
        }

        state = plan.WithHoldings(holds, carried);
        return true;
    }
}

/// <summary>One plan worked from beginning to end, in a real directory, over a
/// world small enough to count by hand - and stoppable between any two
/// operations.
///
/// <b>Why a script of named operations rather than a test per case.</b> The
/// pipeline has fifteen operations that can each be the last thing that happened
/// before the process died, and a suite that killed at one convenient point would
/// prove almost nothing: the interesting failures are at the boundaries - after
/// reserving and before carrying, after carrying and before committing, after
/// committing and before recording. So the operations are a list, the kill is an
/// index into it, and every index is a case. Adding a phase to the pipeline adds
/// cases here rather than being silently untested.
///
/// <b>The world is three numbers.</b> Units in the source chest, units on the
/// NPC's back, units in the destination chest. Their sum is the conservation
/// invariant, and it is checkable after every operation, which is what makes
/// "material is neither minted nor lost" a property rather than an
/// aspiration.</summary>
internal sealed class PlanRehearsal : IDisposable
{
    /// <summary>How many units the player owns. Every assertion about
    /// conservation is against this one number.</summary>
    internal const int Units = 10;

    /// <summary>How many targets the plan covers. Never zero: a total of zero
    /// says the plan covers no work, which is a claim rather than a default.
    /// </summary>
    internal const int Targets = 4;

    private readonly string _root;
    private readonly List<Action> _script = new List<Action>();
    private readonly List<string> _names = new List<string>();

    private NpcPlanRun? _run;
    private int _halfMoveAt = -1;
    private int _current = -1;

    internal PlanRehearsal()
    {
        _root = Path.Combine(Path.GetTempPath(), "cnpc-plan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        PlanPath = Path.Combine(_root, "gunnar.plan.tsv");

        Registry = Identities.EmptyRegistry();
        Assert.True(Registry.Register(
            new FakeRole(Identity, NpcBodyContract.ForWorker(BodyFixtures.TeamsterPrefab, BodyFixtures.WorkerKeyPrefix)))
            .IsRegistered);
        World = Registry.BeginWorldLoad(out _);
        Holds = new NpcReservationBook<NpcCustodyLocation>(World);

        Build();
    }

    /// <summary>Where the role keeps this plan. Composed here, by the role,
    /// which is the only place a path may be composed.</summary>
    internal string PlanPath { get; }

    internal RolePlanCodec Codec { get; } = new RolePlanCodec();

    internal NpcRoleRegistry Registry { get; }

    internal NpcWorldEpoch World { get; private set; }

    internal NpcReservationBook<NpcCustodyLocation> Holds { get; private set; }

    internal NpcIdentity Identity => Identities.Gunnar;

    internal string JobName => "haul-round";

    internal NpcMaterial Stone => NpcMaterial.Of("stone");

    /// <summary>The cart this plan is assigned. <b>Not empty, deliberately.</b>
    /// Every rehearsal plan used to carry <c>string.Empty</c> here, which meant
    /// "the cart came through an interruption" was asserted over a plan that never
    /// had one - a review of #379 found the claim resting on two empty strings
    /// being equal.</summary>
    internal string Cart => "the-cart";

    internal int AtSource { get; private set; } = Units;

    internal int OnBack { get; private set; }

    internal int AtDestination { get; private set; }

    /// <summary>How many times material actually left the source, and how many
    /// times it actually arrived. <b>The duplication counters.</b> A resumed plan
    /// that fetched or delivered the same load twice would show up here and
    /// nowhere else - a record can be perfectly consistent with itself while the
    /// world holds two of something.
    ///
    /// <b>What they can and cannot prove, stated honestly.</b> Each is incremented
    /// by exactly one operation of the script, and no index runs twice, so
    /// <c>Gathers &lt;= 1</c> and <c>Deliveries &lt;= 1</c> are arithmetic
    /// properties of this fixture rather than discoveries about the library. They
    /// are worth asserting for one reason: the recovery path has no inventory port
    /// at all today, and the day something gives it one - a role's resume that
    /// replays a step, a compensating write - these are what turn red. They are a
    /// tripwire, not evidence.</summary>
    internal int Gathers { get; private set; }

    internal int Deliveries { get; private set; }

    /// <summary>The conservation sum. Never anything but <see cref="Units"/>.
    /// <b>Also an arithmetic property of this fixture</b> - every movement below is
    /// one subtraction and one matching addition - and asserted for the same
    /// tripwire reason as the counters above.</summary>
    internal int TotalInTheWorld => AtSource + OnBack + AtDestination;

    /// <summary>How many of the three places are holding anything. <b>Two means
    /// the load is genuinely torn</b>: a transfer the process died in the middle
    /// of has part of it at each end, which is the one shape where both of a
    /// plan's writes can land and the record can still be wrong. One means the
    /// movement either did not start or ran to completion, and a test that meant
    /// to truncate a transfer and did not would say one here.</summary>
    internal int PlacesHoldingMaterial =>
        (AtSource > 0 ? 1 : 0) + (OnBack > 0 ? 1 : 0) + (AtDestination > 0 ? 1 : 0);

    /// <summary>The operations, in order, by name - so a failure says which
    /// boundary it happened at rather than which index.</summary>
    internal IReadOnlyList<string> Operations => _names;

    internal NpcPlanRun Run => _run ?? throw new InvalidOperationException("the plan has not been started");

    /// <summary>Opens the journal the way a role does: a path the role composed
    /// and a codec the role wrote.</summary>
    internal NpcPlanJournal Journal()
    {
        Assert.True(NpcPlanJournal.TryOpen(PlanPath, Codec, out NpcPlanJournal? journal, out string reason), reason);
        return journal!;
    }

    /// <summary>Starts the plan, which writes its opening record before anything
    /// else happens.</summary>
    internal PlanRehearsal Start()
    {
        _run = NpcPlanRun.Begin(
            Journal(),
            NpcPlanState.Opening(Identity, JobName, World, Targets),
            out NpcPlanSave written);

        Assert.True(written.IsSaved, written.Failure);
        Assert.NotNull(_run);
        return this;
    }

    /// <summary>Runs the first <paramref name="operations"/> operations and then
    /// stops as abruptly as a process death does: nothing is flushed, nothing is
    /// tidied and nothing is told.</summary>
    internal PlanRehearsal RunUpTo(int operations, int halfMoveAt = -1)
    {
        _halfMoveAt = halfMoveAt;
        for (int index = 0; index < operations && index < _script.Count; index++)
        {
            _current = index;
            _script[index]();
            Assert.Equal(Units, TotalInTheWorld);
        }

        return this;
    }

    /// <summary>Whether the movement being carried out right now moves only part
    /// of the load - a transfer the process died in the middle of, which is the
    /// one case where the record and the world can disagree while both writes
    /// landed.</summary>
    internal bool HalfMoves => _current == _halfMoveAt;

    /// <summary>What a new process sees: a fresh journal on the same path, and a
    /// fresh world load, because quitting to the menu destroys every saved object
    /// with the scene.</summary>
    internal NpcPlanLoad Reload()
    {
        _run = null;
        World = Registry.BeginWorldLoad(out _);
        Holds = new NpcReservationBook<NpcCustodyLocation>(World);
        return Journal().Load();
    }

    /// <summary>Rewrites the phase field of the plan row on the disk to zero, the
    /// way a truncated row, an older schema or a bug in this codec would.
    ///
    /// <b>Done by hand rather than through the codec, deliberately.</b>
    /// <c>NpcPlanJournal.Save</c> refuses to write a plan in no phase at all, which
    /// is the point: this is a record the library would never have produced, and a
    /// role still has to survive finding one. Which field holds the phase is the
    /// role's own business, which is why this is in the role's file.</summary>
    internal void RewriteThePhaseAsNobodySet()
    {
        string[] lines = File.ReadAllLines(PlanPath);
        for (int index = 0; index < lines.Length; index++)
        {
            string[] fields = lines[index].Split('\t');
            if (fields.Length >= 14 && fields[0] == "p")
            {
                fields[5] = "0";
                lines[index] = string.Join("\t", fields);
            }
        }

        File.WriteAllLines(PlanPath, lines);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // A temporary directory a scanner is holding open is not a failure.
        }
    }

    private void Build()
    {
        Add("record the plan", () => Assert.True(Run.Advance(NpcPlanPhase.Planned, "a plan exists").IsSaved));
        Add("record the manifest", () => Assert.True(Run.Advance(NpcPlanPhase.Manifested, "totalled").IsSaved));
        Add("record the reservations", () => Assert.True(
            Run.Record(Run.State
                    .WithHoldings(new[] { ReservationId.For(JobName, 0) }, Run.State.Carried)
                    .WithRoute("the-pile", "the-depot", Cart)
                    .WithPhase(NpcPlanPhase.Reserved, "set aside"))
                .IsSaved));
        Add("take the holds", () => Assert.Equal(
            ReservationOutcome.Reserved,
            Holds.Reserve(Source(), ReservationId.For(JobName, 0))));
        Add("say the gather is about to happen", () => Assert.True(Run.Intend("about to load up").IsSaved));
        Add("gather", Gather);
        Add("say what the gather did", () => Assert.True(
            Run.Conclude(OnBack == Units, Carrying(), Run.State.TargetsDone, "loaded").IsSaved));
        Add("record that he is loaded", () => Assert.True(
            Run.Advance(NpcPlanPhase.Provisioned, "on his back").IsSaved));
        Add("record the route", () => Assert.True(Run.Advance(NpcPlanPhase.Routed, "walk ordered").IsSaved));
        Add("record that the steps have begun", () => Assert.True(
            Run.Advance(NpcPlanPhase.Executing, "walking it").IsSaved));
        Add("say the delivery is about to happen", () => Assert.True(Run.Intend("about to hand over").IsSaved));
        Add("deliver", Deliver);
        Add("say what the delivery did", () => Assert.True(
            Run.Conclude(OnBack == 0, Carrying(), OnBack == 0 ? Targets : Run.State.TargetsDone, "delivered")
                .IsSaved));
        Add("record that the books are closing", () => Assert.True(
            Run.Advance(NpcPlanPhase.Reconciling, "closing the books").IsSaved));
        Add("record that it is finished", () => Assert.True(
            Run.Advance(NpcPlanPhase.Settled, "done").IsSaved));
    }

    private void Add(string name, Action operation)
    {
        _names.Add(name);
        _script.Add(operation);
    }

    private NpcCustodyLocation Source() =>
        new NpcCustodyLocation(NpcCustodyPlace.Stored, "the-pile", World);

    private void Gather()
    {
        int moving = HalfMoves ? Units / 2 : Units;
        AtSource -= moving;
        OnBack += moving;
        Gathers++;
    }

    private void Deliver()
    {
        int moving = HalfMoves ? OnBack / 2 : OnBack;
        OnBack -= moving;
        AtDestination += moving;
        Deliveries++;
    }

    private IReadOnlyList<NpcMaterialStack> Carrying() =>
        OnBack == 0
            ? new NpcMaterialStack[0]
            : new[] { new NpcMaterialStack(Stone, OnBack) };
}
