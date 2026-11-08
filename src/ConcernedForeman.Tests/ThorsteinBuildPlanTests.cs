using System;
using System.Collections.Generic;
using System.IO;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.ConcernedForeman.Domain.Npc;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.Settlement.Identity;

namespace ConcernedForeman.Tests;

/// <summary>#379 for the construction loop: every draw, commit and put-back is
/// announced to Thorstein's shared building plan before the material moves and
/// concluded after, a refused announcement moves nothing, and a reload with a
/// commit in flight never places or pays for that piece again until a person
/// resolves it - while the loop, its custody path and its conservation are
/// exactly what they were.</summary>
public sealed class ThorsteinBuildPlanTests : IDisposable
{
    private const float Step = 2f;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "cf-379-build", Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public ThorsteinBuildPlanTests()
    {
        Directory.CreateDirectory(_root);
        _path = new ThorsteinPlanFiles(_root).ResolvePath(new SettlementScope(91, new SettlementId("camp")), ThorsteinWorkPlan.BuildJobId);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // A locked temp file must never fail the suite.
        }
    }

    private sealed class Counting : IBuildMaterials
    {
        private readonly IBuildMaterials _inner;
        private readonly Func<ThorsteinWorkPlan> _plan;

        public Counting(IBuildMaterials inner, Func<ThorsteinWorkPlan> plan)
        {
            _inner = inner;
            _plan = plan;
        }

        public int Moves { get; private set; }

        public int UnannouncedMoves { get; private set; }

        public Action? DieInsideCommit { get; set; }

        private void Moving()
        {
            Moves++;
            if (_plan().State?.Custody != NpcPlanCustody.Pending)
            {
                UnannouncedMoves++;
            }
        }

        public void BeginOrder(string tag) => _inner.BeginOrder(tag);

        public bool TrySupply(out TheConcernedCat.Settlement.Worker.SitePoint at, out string refusal) =>
            _inner.TrySupply(out at, out refusal);

        public MaterialTally Carried => _inner.Carried;

        public bool IsReserved(CostedPiece piece) => _inner.IsReserved(piece);

        public BuildDraw Draw(IReadOnlyList<CostedPiece> pieces)
        {
            Moving();
            return _inner.Draw(pieces);
        }

        public bool Commit(CostedPiece piece, Func<bool> place, out MaterialTally spent, out string failure)
        {
            Moving();
            bool done = _inner.Commit(piece, place, out spent, out failure);
            DieInsideCommit?.Invoke();
            return done;
        }

        public MaterialTally PutBack(out string failure)
        {
            Moving();
            return _inner.PutBack(out failure);
        }
    }

    /// <summary>Writes until the process "dies": from then on nothing reaches
    /// the disk, which is what a kill looks like from the plan's side.</summary>
    private sealed class MortalCodec : INpcPlanCodec
    {
        private readonly INpcPlanCodec _inner = new ThorsteinPlanCodec(ForemanNpcRole.Id, ThorsteinWorkPlan.BuildJobId);

        public bool Dead { get; set; }

        public IReadOnlyList<string>? Encode(NpcPlanState state) => Dead ? null : _inner.Encode(state);

        public bool TryDecode(IReadOnlyList<string> lines, out NpcPlanState? state, out string reason) =>
            _inner.TryDecode(lines, out state, out reason);
    }

    private static NpcPlanEvidence Evidence(NpcWorldEpoch world) =>
        new NpcPlanEvidence(world, 1, false, true, false, true, true, true, false, 0f);

    private (ShelterBuildLoop Loop, ThorsteinWorkPlan Plan, Counting Counted, WitnessedBuildMaterials Witness) Session(
        BuildWorld world, NpcWorldEpoch epoch, INpcPlanCodec? codec = null)
    {
        var plan = new ThorsteinWorkPlan(ForemanNpcRole.Id, ThorsteinWorkPlan.BuildJobId, codec);
        plan.Open(_path, null);
        var counted = new Counting(world.Materials, () => plan);
        var witness = new WitnessedBuildMaterials(counted, plan, () => Evidence(epoch));
        var loop = new ShelterBuildLoop(
            () => ShelterPlan.For(world.Authorised ? world.Marker : world.Marker.Withdraw(), world.Recipes),
            () => world.Authorised,
            world.Sight,
            world.Walk,
            witness,
            world.Placer,
            world.Pose,
            world.Said.Add);
        return (loop, plan, counted, witness);
    }

    [Fact]
    public void EveryMovementOfTheShelterIsAnnouncedAndTheShelterStillGoesUpWhole()
    {
        var world = new BuildWorld();
        int wood = world.Total("Wood");
        (ShelterBuildLoop loop, ThorsteinWorkPlan plan, Counting counted, WitnessedBuildMaterials witness) =
            Session(world, NpcWorldEpoch.Mint());
        for (int tick = 0; tick < 80 && loop.Step != BuildStep.Finished; tick++)
        {
            loop.Tick(100f + (tick * Step));
        }

        Assert.Equal(BuildStep.Finished, loop.Step);
        Assert.Equal(world.Plan.Pieces.Count, world.Placer.Placed.Count);
        Assert.Equal(wood, world.Total("Wood"));
        Assert.True(counted.Moves > world.Plan.Pieces.Count);
        Assert.Equal(0, counted.UnannouncedMoves);

        witness.Ended(true, "the shelter is finished");
        Assert.Equal(ThorsteinPlanStanding.Idle, plan.Standing);
    }

    [Fact]
    public void ACommitTheProcessDiedInsideIsNeverPlacedOrPaidForAgainUntilResolved()
    {
        var world = new BuildWorld();
        int wood = world.Total("Wood");
        var mortal = new MortalCodec();
        (ShelterBuildLoop loop, _, Counting counted, _) = Session(world, NpcWorldEpoch.Mint(), mortal);

        // The third piece is placed and paid for, and the process dies before
        // the plan can write what that commit did.
        counted.DieInsideCommit = () => mortal.Dead |= world.Placer.Placed.Count == 3;
        for (int tick = 0; tick < 80 && !mortal.Dead; tick++)
        {
            loop.Tick(100f + (tick * Step));
        }

        Assert.True(mortal.Dead);

        int placed = world.Placer.Placed.Count;
        Assert.Equal(3, placed);

        // Reloaded: the commit has no outcome on the plan, so nothing moves.
        NpcWorldEpoch next = NpcWorldEpoch.Mint();
        (ShelterBuildLoop again, ThorsteinWorkPlan plan, Counting recounted, _) = Session(world, next);
        for (int tick = 0; tick < 40; tick++)
        {
            again.Tick(300f + (tick * Step));
        }

        Assert.Equal(ThorsteinPlanStanding.NeedsAttention, plan.Standing);
        Assert.Equal(0, recounted.Moves);
        Assert.Equal(placed, world.Placer.Placed.Count);
        Assert.Equal(wood, world.Total("Wood"));

        // A person resolves from what he holds; the shelter then finishes and
        // every piece stands exactly once.
        var held = new List<KeyValuePair<string, int>>();
        foreach (KeyValuePair<string, int> item in world.Carried)
        {
            held.Add(item);
        }

        Assert.True(plan.Resolve(next, held, out string said), said);
        (ShelterBuildLoop last, _, Counting lastCount, _) = Session(world, next);
        for (int tick = 0; tick < 120 && last.Step != BuildStep.Finished; tick++)
        {
            last.Tick(500f + (tick * Step));
        }

        Assert.Equal(BuildStep.Finished, last.Step);
        var seen = new HashSet<string>();
        foreach (string key in world.Placer.Placed)
        {
            Assert.True(seen.Add(key), "a piece was placed twice: " + key);
        }

        Assert.Equal(world.Plan.Pieces.Count, world.Placer.Placed.Count);
        Assert.Equal(wood, world.Total("Wood"));
        Assert.Equal(0, lastCount.UnannouncedMoves);
    }
}
