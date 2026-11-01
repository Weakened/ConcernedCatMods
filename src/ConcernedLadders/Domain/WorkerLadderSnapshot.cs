using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TheConcernedCat.Ladders;
namespace TheConcernedCat.ConcernedLadders.Domain;

/// <summary>Measured section data, never a landing promise or motor authority.</summary>
internal readonly struct WorkerLadderSection
{
    internal WorkerLadderSection(int id, LadderGeometry geometry, float frontDepth, float backDepth)
    { Id=id; Geometry=geometry; FrontDepth=frontDepth; BackDepth=backDepth; }
    internal int Id { get; }
    internal LadderGeometry Geometry { get; }
    internal float FrontDepth { get; }
    internal float BackDepth { get; }
    internal float[] Values() => new[] { Geometry.Bottom.X,Geometry.Bottom.Y,Geometry.Bottom.Z,
        Geometry.Top.X,Geometry.Top.Y,Geometry.Top.Z,Geometry.StandingSide.X,Geometry.StandingSide.Z,
        Geometry.Width,Geometry.RungPitch,FrontDepth,BackDepth };
}
/// <summary>A bounded, detached primitive snapshot for optional runtime consumers.
/// No shared/product type crosses the assembly boundary. Content, including
/// geometry motion, changes its generation; a new load gets a new epoch.</summary>
internal sealed class WorkerLadderSnapshot
{
    internal const int Schema=1;
    internal const int Stride=12;
    private WeakReference<object>? _load;
    private long _world;
    private string _epoch=string.Empty;
    private long _generation;
    private string[] _ids=Array.Empty<string>();
    private float[][] _sections=Array.Empty<float[]>();
    internal void Invalidate()
    { _load=null; _world=0; _epoch=string.Empty; _generation=0; _ids=Array.Empty<string>(); _sections=Array.Empty<float[]>(); }
    internal bool Read(object? load, long world, IReadOnlyList<WorkerLadderSection> measured,
        out string epoch,out long generation,out string[] ids,out float[][] sections)
    {
        epoch=string.Empty; generation=0; ids=Array.Empty<string>(); sections=Array.Empty<float[]>();
        if(load==null||world==0||measured==null||measured.Count>LadderRules.MaximumRegisteredSegments)
        { Invalidate(); return false; }
        var sorted=measured.OrderBy(item=>item.Id).ToArray();
        var names=new string[sorted.Length]; var values=new float[sorted.Length][];
        for(int i=0;i<sorted.Length;i++)
        {
            if(i>0&&sorted[i].Id==sorted[i-1].Id) { Invalidate(); return false; }
            var item=sorted[i]; var geometry=item.Geometry;
            if(!geometry.IsClimbable||!LadderRules.Finite(item.FrontDepth)||!LadderRules.Finite(item.BackDepth)||
                item.FrontDepth<0||item.FrontDepth>.5f||item.BackDepth<-.5f||item.BackDepth>=item.FrontDepth)
            { Invalidate(); return false; }
            names[i]="cl:"+item.Id.ToString(CultureInfo.InvariantCulture); values[i]=item.Values();
            if(values[i].Any(value=>!LadderRules.Finite(value))) { Invalidate(); return false; }
        }
        bool sameLoad=_load!=null&&_load.TryGetTarget(out object previous)&&ReferenceEquals(previous,load);
        bool newLoad=!sameLoad||_world!=world;
        bool changed=newLoad||!names.SequenceEqual(_ids,StringComparer.Ordinal)||values.Length!=_sections.Length;
        if(!changed) for(int i=0;i<values.Length;i++) if(!values[i].SequenceEqual(_sections[i])) { changed=true; break; }
        if(newLoad) { _epoch=Guid.NewGuid().ToString("N"); _generation=0; _load=new WeakReference<object>(load); }
        if(changed) _generation++;
        _world=world; _ids=names; _sections=values;
        epoch=_epoch; generation=_generation; ids=(string[])_ids.Clone();
        sections=_sections.Select(row=>(float[])row.Clone()).ToArray();
        return true;
    }
}
