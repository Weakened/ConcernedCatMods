// Native boundaries only; the actual published capability is linked into tests.
public sealed class ZNet
{
    public static ZNet instance=null!;
    public long GetWorldUID()=>11;
}
namespace TheConcernedCat.ConcernedLadders.Runtime
{
    internal static class LadderSegment
    {
        internal static bool TryCopyWorkerSections(out System.Collections.Generic.List<Domain.WorkerLadderSection> sections)
        {
            if(global::ConcernedLadders.Tests.RuntimeBoundary.Throw) throw new System.InvalidOperationException();
            sections=new(global::ConcernedLadders.Tests.RuntimeBoundary.Sections);
            return global::ConcernedLadders.Tests.RuntimeBoundary.Ready;
        }
    }
}

namespace UnityEngine
{
    public static class Debug
    {
        public static readonly System.Collections.Generic.List<string> Warnings=new();
        public static void LogWarning(object message) => Warnings.Add(message.ToString()!);
    }
}
