using TheConcernedCat.Workers.Traversal;
namespace Shared.Settlement.Tests;
[CollectionDefinition("ladder-wire",DisableParallelization=true)] public sealed class LadderWireCollection { }
[Collection("ladder-wire")]
public sealed class ReflectedWorkerLadderSourceTests
{
    public static class Good
    {
        public const int Schema=1,SectionStride=12;
        public static bool Ready=true,Throw; public static Exception? Error; public static long Generation=1;
        public static bool TryRead(int schema,out string epoch,out long generation,out string[] ids,out float[][] sections)
        {
            if(Error!=null) throw Error;
            if(Throw) throw new InvalidOperationException();
            epoch=Guid.NewGuid().ToString("N"); generation=Generation; ids=new[]{"live"};
            sections=new[]{new float[]{0,0,0,0,2,0,0,1,.8f,1f/3,.24f,.08f}}; return Ready;
        }
    }
    public static class Unsupported
    {
        public const int Schema=2,SectionStride=12;
        public static bool TryRead(int schema,out string epoch,out long generation,out string[] ids,out float[][] sections)
        { throw new Exception("Must never invoke an unsupported schema."); }
    }
    public static class WrongSignature
    {
        public const int Schema=1,SectionStride=12;
        public static bool TryRead(int schema,ref string epoch,ref long generation,ref string[] ids,ref float[][] sections)
        { throw new Exception("ref is not the exact out contract."); }
    }
    public ReflectedWorkerLadderSourceTests() { Good.Ready=true; Good.Throw=false; Good.Error=null; Good.Generation=1; }
    [Fact] public void NativePrimitiveSignatureIsAcceptedWithoutReferencingTheProduct()
    {
        var messages=new List<string>(); var source=new ReflectedWorkerLadderSource(()=>typeof(Good),messages.Add);
        Assert.True(source.TryRead(out var snapshot)); Assert.Single(snapshot.Sections); Assert.Empty(messages);
        Good.Generation=0; Assert.False(source.TryRead(out var malformed)); Assert.Null(malformed);
    }
    [Fact] public void MissingFalseAndThrowingProviderNeverReusesAnEarlierGoodPublication()
    {
        Type? type=typeof(Good); var messages=new List<string>(); var source=new ReflectedWorkerLadderSource(()=>type,messages.Add);
        Assert.True(source.TryRead(out _)); type=null; Assert.False(source.TryRead(out var missing)); Assert.Null(missing);
        Assert.False(source.TryRead(out _)); Assert.Single(messages);
        type=typeof(Good); Good.Ready=false; Assert.False(source.TryRead(out var unavailable)); Assert.Null(unavailable);
        Good.Ready=true; Good.Throw=true; Assert.False(source.TryRead(out var failed)); Assert.Null(failed);
        Assert.Contains("InvalidOperationException",messages.Last());
    }
    [Fact] public void NativeExceptionDiagnosticsUseTheSharedPathScrubber()
    {
        Good.Error=new IOException(@"Cannot read C:\Users\private-agent\hidden-folder\secret-file.txt");
        var messages=new List<string>(); var source=new ReflectedWorkerLadderSource(()=>typeof(Good),messages.Add);
        Assert.False(source.TryRead(out _));
        Assert.Contains("IOException",messages.Single());
        Assert.DoesNotContain("private-agent",messages[0]); Assert.DoesNotContain("secret-file",messages[0]);
    }
    [Theory] [InlineData("schema")] [InlineData("signature")]
    public void VersionAndOutParameterContractAreCheckedBeforeInvocation(string kind)
    {
        var messages=new List<string>(); var source=new ReflectedWorkerLadderSource(()=>kind=="schema"?typeof(Unsupported):typeof(WrongSignature),messages.Add);
        Assert.False(source.TryRead(out var snapshot)); Assert.Null(snapshot);
        Assert.Single(messages); Assert.DoesNotContain("API failed",messages[0]);
    }
}
