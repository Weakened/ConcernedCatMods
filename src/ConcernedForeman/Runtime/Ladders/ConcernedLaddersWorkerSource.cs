using System;
using BepInEx.Bootstrap;
using TheConcernedCat.Workers.Traversal;
namespace TheConcernedCat.ConcernedForeman.Runtime.Ladders;
/// <summary>Only the installed optional plugin's assembly is consulted. No
/// product reference, scene-wide search, player controller or engine write.</summary>
internal sealed class ConcernedLaddersWorkerSource : IWorkerLadderPublicationSource
{
    private readonly ReflectedWorkerLadderSource _source;
    internal ConcernedLaddersWorkerSource(Action<string> log)
    {
        _source=new ReflectedWorkerLadderSource(()=>
            Chainloader.PluginInfos.TryGetValue("com.theconcernedcat.valheim.concernedladders",out var info)&&info.Instance!=null
                ?info.Instance.GetType().Assembly.GetType("TheConcernedCat.ConcernedLadders.Capabilities.WorkerLadderLinks",false):null,log);
    }
    public bool TryRead(out WorkerLadderPublication publication)=>_source.TryRead(out publication);
}
