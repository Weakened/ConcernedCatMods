using System;
using System.Collections.Generic;
using UnityEngine;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.ConcernedLadders.Runtime;
namespace TheConcernedCat.ConcernedLadders.Capabilities;

/// <summary>Optional, read-only main-thread capability for worker products.
/// Resolve this type from the installed plugin's assembly; no product reference
/// or copied DLL is required. This publishes measurements, NOT safe landings,
/// an accepted route, a worker capability, or permission to move anything.</summary>
public static class WorkerLadderLinks
{
    public const int Schema=WorkerLadderSnapshot.Schema;
    public const int SectionStride=WorkerLadderSnapshot.Stride;
    private static readonly WorkerLadderSnapshot Snapshot=new WorkerLadderSnapshot();
    private static string _reportedFailure=string.Empty;
    public static bool TryRead(int schema,out string loadEpoch,out long generation,out string[] ids,out float[][] sections)
    {
        loadEpoch=string.Empty; generation=0; ids=Array.Empty<string>(); sections=Array.Empty<float[]>();
        if(schema!=Schema) return false;
        try
        {
            ZNet net=ZNet.instance;
            if(net==null||!LadderSegment.TryCopyWorkerSections(out List<WorkerLadderSection> measured))
            { Snapshot.Invalidate(); return false; }
            bool ready=Snapshot.Read(net,net.GetWorldUID(),measured,out loadEpoch,out generation,out ids,out sections);
            if(ready) _reportedFailure=string.Empty;
            return ready;
        }
        catch(Exception exception)
        {
            // A partial/unloaded snapshot can never preserve old route permission.
            Snapshot.Invalidate();
            string failure=exception.GetType().Name;
            if(_reportedFailure!=failure)
            {
                _reportedFailure=failure;
                Debug.LogWarning("Concerned Ladders worker geometry is unavailable ("+failure+
                    "). Workers must stop and revalidate; check the installed ladder/game versions and loaded pieces.");
            }
            loadEpoch=string.Empty; generation=0; ids=Array.Empty<string>(); sections=Array.Empty<float[]>();
            return false;
        }
    }
}
