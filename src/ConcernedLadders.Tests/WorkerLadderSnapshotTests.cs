using TheConcernedCat.ConcernedLadders.Capabilities;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.Ladders;
namespace ConcernedLadders.Tests;
[CollectionDefinition("worker-link-stubs", DisableParallelization=true)]
public sealed class WorkerLinkCollection { }
[Collection("worker-link-stubs")]
public sealed class WorkerLadderSnapshotTests : IDisposable
{
    public WorkerLadderSnapshotTests() { ZNet.instance=new ZNet(); RuntimeBoundary.Reset(); }
    public void Dispose() { ZNet.instance=null!; RuntimeBoundary.Reset(); }
    private static WorkerLadderSection Section(int id=1,float x=0,float front=.24f,float back=.08f)
    {
        Assert.True(LadderGeometry.TryMeasure(new ClimbPoint(x,0,0),new ClimbPoint(x,2,0),ClimbHeading.FromXz(0,1),.8f,1f/3f,out var geometry));
        return new WorkerLadderSection(id,geometry,front,back);
    }
    private static bool Read(out string epoch,out long generation,out string[] ids,out float[][] rows) =>
        WorkerLadderLinks.TryRead(1,out epoch,out generation,out ids,out rows);
    [Fact]
    public void NativeCapabilityPublishesDetachedAsymmetricGeometryWithoutAnyMotor()
    {
        RuntimeBoundary.Sections.Add(Section());
        Assert.True(Read(out var epoch,out long generation,out var ids,out var rows));
        Assert.NotEmpty(epoch); Assert.True(generation>0); Assert.Single(ids); Assert.Equal(12,rows[0].Length);
        Assert.Equal(.24f,rows[0][10]); Assert.Equal(.08f,rows[0][11]);
        ids[0]="mutated"; rows[0][0]=1000;
        Assert.True(Read(out var same,out var unchanged,out ids,out rows));
        Assert.Equal(epoch,same); Assert.Equal(generation,unchanged); Assert.Equal("cl:1",ids[0]); Assert.Equal(0,rows[0][0]);
    }
    [Fact]
    public void MovementAndRemovalInvalidateGenerationEvenWithoutRegistryEnableEvent()
    {
        RuntimeBoundary.Sections.Add(Section()); Assert.True(Read(out _,out long first,out _,out _));
        RuntimeBoundary.Sections[0]=Section(x:4); Assert.True(Read(out _,out long moved,out _,out _)); Assert.True(moved>first);
        RuntimeBoundary.Sections.Clear(); Assert.True(Read(out _,out long removed,out var ids,out _));
        Assert.True(removed>moved); Assert.Empty(ids);
    }
    [Fact]
    public void SameWorldReloadChangesEpochAndUnavailableSnapshotCannotRetainOldLinks()
    {
        RuntimeBoundary.Sections.Add(Section()); Assert.True(Read(out var first,out _,out _,out _));
        ZNet.instance=new ZNet(); Assert.True(Read(out var next,out _,out _,out _)); Assert.NotEqual(first,next);
        RuntimeBoundary.Ready=false; Assert.False(Read(out var unknown,out var generation,out var ids,out var rows));
        Assert.Empty(unknown); Assert.Equal(0,generation); Assert.Empty(ids); Assert.Empty(rows);
        RuntimeBoundary.Ready=true; Assert.True(Read(out var restored,out _,out _,out _)); Assert.NotEqual(next,restored);
    }
    [Fact]
    public void NativeFailureWithdrawsEverythingAndReportsOnceUntilRecovery()
    {
        Assert.True(Read(out _,out _,out _,out _));
        RuntimeBoundary.Throw=true;
        for(int i=0;i<2;i++)
        {
            Assert.False(Read(out var epoch,out var generation,out var ids,out var rows));
            Assert.Empty(epoch); Assert.Equal(0,generation); Assert.Empty(ids); Assert.Empty(rows);
        }
        Assert.Single(UnityEngine.Debug.Warnings);
        Assert.Contains("revalidate",UnityEngine.Debug.Warnings[0]);
        RuntimeBoundary.Throw=false; Assert.True(Read(out _,out _,out _,out _));
        RuntimeBoundary.Throw=true; Assert.False(Read(out _,out _,out _,out _));
        Assert.Equal(2,UnityEngine.Debug.Warnings.Count);
    }
    [Fact]
    public void DuplicateOverBudgetInvalidOrUnknownSchemaRefusesEntireSnapshot()
    {
        RuntimeBoundary.Sections.Add(Section()); RuntimeBoundary.Sections.Add(Section());
        Assert.False(Read(out _,out _,out _,out _)); RuntimeBoundary.Sections.Clear();
        for(int i=0;i<2049;i++) RuntimeBoundary.Sections.Add(Section(i));
        Assert.False(Read(out _,out _,out _,out _)); RuntimeBoundary.Sections.Clear();
        RuntimeBoundary.Sections.Add(Section(front:float.NaN)); Assert.False(Read(out _,out _,out _,out _));
        Assert.False(WorkerLadderLinks.TryRead(2,out _,out _,out _,out _));
    }
}
internal static class RuntimeBoundary
{
    internal static bool Ready=true;
    internal static bool Throw;
    internal static readonly List<WorkerLadderSection> Sections=new();
    internal static void Reset() { Ready=true; Throw=false; Sections.Clear(); UnityEngine.Debug.Warnings.Clear(); }
}
