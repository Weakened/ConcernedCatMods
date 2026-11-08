using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace TheConcernedCat.Workers.Traversal;

internal readonly struct WorkerLadderCapsule
{
    public WorkerLadderCapsule(float radius,float height)
    {
        if(!Finite(radius)||!Finite(height)||radius<.1f||radius>1f||height<2*radius||height>4f)
            throw new ArgumentException("A measured upright worker capsule is required.");
        Radius=radius; Height=height;
    }
    public float Radius { get; }
    public float Height { get; }
    public bool IsMeasured=>Radius>=.1f&&Height>=2*Radius;
    internal static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
}
internal readonly struct WorkerLadderSectionData
{
    internal WorkerLadderSectionData(string id,float[] row) { Id=id; Values=(float[])row.Clone(); }
    public string Id { get; }
    internal float[] Values { get; }
    public WorkPoint Bottom=>new(Values[0],Values[1],Values[2]);
    public WorkPoint Top=>new(Values[3],Values[4],Values[5]);
    public float X=>Values[6]; public float Z=>Values[7];
    public float Front=>Values[10]; public float Rear=>Values[11];
}
/// <summary>Owns its input copies. Unknown/malformed wire data is never a partial survey.</summary>
internal sealed class WorkerLadderPublication
{
    private WorkerLadderPublication(string epoch,long generation,WorkerLadderSectionData[] sections)
    { Epoch=epoch; Generation=generation; Sections=Array.AsReadOnly(sections); }
    public string Epoch { get; }
    public long Generation { get; }
    public IReadOnlyList<WorkerLadderSectionData> Sections { get; }
    public static bool TryRead(string? epoch,long generation,string[]? ids,float[][]? rows,out WorkerLadderPublication publication)
    {
        publication=null!;
        if(!Guid.TryParseExact(epoch,"N",out Guid parsed)||parsed==Guid.Empty||generation<=0||ids==null||rows==null||
            ids.Length!=rows.Length||ids.Length>2048) return false;
        var seen=new HashSet<string>(StringComparer.Ordinal); var sections=new WorkerLadderSectionData[ids.Length];
        for(int i=0;i<ids.Length;i++)
        {
            float[] row=rows[i]; string id=ids[i];
            if(string.IsNullOrWhiteSpace(id)||id.Length>96||!seen.Add(id)||row==null||row.Length!=12||row.Any(v=>!WorkerLadderCapsule.Finite(v))) return false;
            float rise=row[4]-row[1],dx=row[3]-row[0],dz=row[5]-row[2],heading=row[6]*row[6]+row[7]*row[7];
            if(rise<1.95f||rise>2.05f||Math.Abs(dx)>.02f||Math.Abs(dz)>.02f||Math.Abs(heading-1)>.002f||
                row[8]<.2f||row[8]>4||row[9]<.05f||row[9]>1||row[10]<0||row[10]>.5f||row[11]<-.5f||row[11]>=row[10]) return false;
            sections[i]=new WorkerLadderSectionData(id,row);
        }
        publication=new WorkerLadderPublication(epoch!,generation,sections.OrderBy(s=>s.Id,StringComparer.Ordinal).ToArray()); return true;
    }
}
internal enum WorkerLadderFace { Front,Rear }
internal sealed class WorkerLadderRunData
{
    internal WorkerLadderRunData(string epoch,IReadOnlyList<WorkerLadderSectionData> sections)
    {
        Sections=Array.AsReadOnly(sections.ToArray()); Bottom=sections[0].Bottom; Top=sections[sections.Count-1].Top;
        X=sections[0].X; Z=sections[0].Z;
        Front=sections.Max(s=>s.Front); Rear=sections.Min(s=>s.Rear);
        // IDs include every member, never rounded coordinates that alias nearby ladders.
        Key="cl-worker:"+epoch+":"+string.Join("/",sections.Select(s=>s.Id.Length.ToString(CultureInfo.InvariantCulture)+":"+s.Id));
    }
    public string Key { get; }
    public IReadOnlyList<WorkerLadderSectionData> Sections { get; }
    public WorkPoint Bottom { get; } public WorkPoint Top { get; }
    public float X { get; } public float Z { get; } public float Front { get; } public float Rear { get; }
    public WorkPoint Lane(bool top,WorkerLadderFace face,WorkerLadderCapsule capsule)
    {
        float offset=face==WorkerLadderFace.Front?Front+capsule.Radius+.08f:Rear-capsule.Radius-.08f;
        WorkPoint at=top?Top:Bottom; return new WorkPoint(at.X+X*offset,at.Y,at.Z+Z*offset);
    }
}
internal enum WorkerLadderProbeResult { Unavailable,Blocked,Supported }
internal interface IWorkerLadderLandingProbe
{
    WorkerLadderProbeResult TryMeasure(WorkerLadderRunData run,WorkerLadderFace face,WorkerLadderCapsule capsule,out WorkerLadderPassage passage);
}
internal interface IWorkerLadderMountProbe
{
    bool CanMount(WorkPoint actualStanding,WorkPoint checkedEntry);
}
internal interface IWorkerLadderSurveyProbe : IWorkerLadderLandingProbe { void BeginSurvey(); }
internal interface IWorkerLadderPublicationSource { bool TryRead(out WorkerLadderPublication publication); }
internal interface IWorkerLadderPathSource { bool TryPath(string linkId,out WorkerLadderPassage passage,out int generation); }
internal enum WorkerLadderSurveyResult { Disabled,Unavailable,Malformed,OverBudget,Ambiguous,Ready }
/// <summary>Read-only settlement link ownership. Publishes geometry only after
/// actual landing/clearance checks, and never clears another provider's links.</summary>
internal sealed class WorkerLadderLinkSurvey : IWorkerLadderPathSource
{
    private readonly TraversalLinkNetwork _network;
    private readonly TraversalLimits _limits;
    private readonly Dictionary<string,Published> _owned=new(StringComparer.Ordinal);
    private sealed class Published
    {
        internal Published(TraversalLink link,string fingerprint,int generation,WorkerLadderPassage passage) { Link=link; Fingerprint=fingerprint; Generation=generation; Passage=passage; }
        internal TraversalLink Link; internal string Fingerprint; internal int Generation; internal WorkerLadderPassage Passage;
    }
    public WorkerLadderLinkSurvey(TraversalLinkNetwork network,TraversalLimits limits) { _network=network??throw new ArgumentNullException(nameof(network)); limits.Validate();
        _limits=new TraversalLimits { ApproachRadiusMetres=limits.ApproachRadiusMetres,MinHeightMetres=limits.MinHeightMetres,
            MaxWorkerHeightMetres=limits.MaxWorkerHeightMetres,TraversalSpeedMetresPerSecond=limits.TraversalSpeedMetresPerSecond,
            WalkSpeedMetresPerSecond=limits.WalkSpeedMetresPerSecond,LinkOverheadMetres=limits.LinkOverheadMetres,
            MaxSearchMetres=limits.MaxSearchMetres }.Validate(); }
    public int Count=>_owned.Count;
    public WorkerLadderSurveyResult Refresh(WorkerLadderPublication? publication,WorkPoint centre,float radius,
        WorkerLadderCapsule capsule,IWorkerLadderLandingProbe probe,bool enabled)
    {
        if(!enabled) return Withdraw(WorkerLadderSurveyResult.Disabled);
        if(publication==null) return Withdraw(WorkerLadderSurveyResult.Unavailable);
        if(!capsule.IsMeasured||!Point(centre)||!WorkerLadderCapsule.Finite(radius)||radius<=0||radius>64||probe==null)
            return Withdraw(WorkerLadderSurveyResult.Malformed);
        var near=publication.Sections.Where(s=>s.Bottom.HorizontalDistanceTo(centre)<=radius &&
            Math.Abs(s.Bottom.Y-centre.Y)<=64).ToArray();
        if(near.Length>64) return Withdraw(WorkerLadderSurveyResult.OverBudget);
        var below=new Dictionary<string,WorkerLadderSectionData>(StringComparer.Ordinal);
        var above=new Dictionary<string,WorkerLadderSectionData>(StringComparer.Ordinal);
        for(int i=0;i<near.Length;i++) for(int j=i+1;j<near.Length;j++)
        {
            var a=near[i]; var b=near[j];
            if(a.Bottom.HorizontalDistanceTo(b.Bottom)>.03f||a.X*b.X+a.Z*b.Z<.999f) continue;
            if(Math.Min(a.Top.Y,b.Top.Y)-Math.Max(a.Bottom.Y,b.Bottom.Y)>.03f) return Withdraw(WorkerLadderSurveyResult.Ambiguous);
            WorkerLadderSectionData low=a.Bottom.Y<b.Bottom.Y?a:b,high=a.Bottom.Y<b.Bottom.Y?b:a;
            if(Math.Abs(low.Top.Y-high.Bottom.Y)>.03f||low.Top.HorizontalDistanceTo(high.Bottom)>.03f) continue;
            if(above.ContainsKey(low.Id)||below.ContainsKey(high.Id)) return Withdraw(WorkerLadderSurveyResult.Ambiguous);
            above[low.Id]=high; below[high.Id]=low;
        }
        var runs=new List<WorkerLadderRunData>(); var visited=new HashSet<string>(StringComparer.Ordinal);
        foreach(var first in near.Where(s=>!below.ContainsKey(s.Id)).OrderBy(s=>s.Id,StringComparer.Ordinal))
        {
            var members=new List<WorkerLadderSectionData>(); var current=first;
            while(true)
            {
                if(!visited.Add(current.Id)||current.Bottom.HorizontalDistanceTo(first.Bottom)>.03f) return Withdraw(WorkerLadderSurveyResult.Ambiguous);
                members.Add(current);
                if(!above.TryGetValue(current.Id,out current)) break;
            }
            if(members[members.Count-1].Top.Y-first.Bottom.Y<=_limits.MaxWorkerHeightMetres)
                runs.Add(new WorkerLadderRunData(publication.Epoch,members));
        }
        if(visited.Count!=near.Length) return Withdraw(WorkerLadderSurveyResult.Ambiguous);
        if(runs.Count>8) return Withdraw(WorkerLadderSurveyResult.OverBudget); // at most 16 face probes
        var next=new Dictionary<string,Published>(StringComparer.Ordinal);
        try
        {
            foreach(var run in runs) foreach(WorkerLadderFace face in new[]{WorkerLadderFace.Front,WorkerLadderFace.Rear})
            {
                var measured=probe.TryMeasure(run,face,capsule,out var passage);
                if(measured==WorkerLadderProbeResult.Unavailable) return Withdraw(WorkerLadderSurveyResult.Unavailable);
                if(measured==WorkerLadderProbeResult.Blocked) continue;
                if(measured!=WorkerLadderProbeResult.Supported) return Withdraw(WorkerLadderSurveyResult.Malformed);
                if(passage==null||!passage.Fits(run,face,capsule)) return Withdraw(WorkerLadderSurveyResult.Malformed);
                WorkPoint bottom=passage.Points[0],top=passage.Points[3];
                string id=run.Key+":"+face;
                if(!TraversalLink.TryCreate(id,TraversalLinkKind.Ladder,bottom,top,TraversalCapability.Ladders,_limits,out var link)) continue;
                string fingerprint=string.Join(";",run.Sections.SelectMany(s=>s.Values).Concat(new[]{capsule.Radius,capsule.Height}).Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
                if(_owned.TryGetValue(id,out var old)&&old.Link.Equals(link)&&old.Fingerprint==fingerprint&&old.Passage.SameAs(passage)&&
                    _network.TryGet(id,out var live,out int generation)&&generation==old.Generation&&live.Equals(link)) next[id]=old;
                else next[id]=new Published(link,fingerprint,0,passage);
            }
        }
        catch { return Withdraw(WorkerLadderSurveyResult.Unavailable); }
        foreach(string id in _owned.Keys) if(!next.ContainsKey(id)) _network.Retire(id);
        foreach(var item in next.Values) if(item.Generation==0) item.Generation=_network.Publish(item.Link);
        _owned.Clear(); foreach(var pair in next) _owned.Add(pair.Key,pair.Value);
        return WorkerLadderSurveyResult.Ready;
    }
    public bool TryPath(string linkId,out WorkerLadderPassage passage,out int generation)
    {
        passage=null!; generation=0;
        if(!_owned.TryGetValue(linkId,out var entry)||!_network.TryGet(linkId,out _,out int live)||live!=entry.Generation) return false;
        passage=entry.Passage; generation=entry.Generation; return true;
    }
    public void Forget()=>Withdraw(WorkerLadderSurveyResult.Unavailable);
    private WorkerLadderSurveyResult Withdraw(WorkerLadderSurveyResult result)
    { foreach(string id in _owned.Keys) _network.Retire(id); _owned.Clear(); return result; }
    private static bool Point(WorkPoint p)=>WorkerLadderCapsule.Finite(p.X)&&WorkerLadderCapsule.Finite(p.Y)&&WorkerLadderCapsule.Finite(p.Z);
}
