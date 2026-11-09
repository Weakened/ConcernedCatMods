using System;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.ConcernedNPC.Camp;
using TheConcernedCat.ConcernedNPC.Work;
namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;
internal enum GunnarCampAction { None, Show, Retain, Reposition, Hide }
internal readonly struct GunnarCampStep
{
    public GunnarCampStep(GunnarCampAction action,PlacementResult placement) { Action=action; Placement=placement; }
    public GunnarCampAction Action { get; }
    public PlacementResult Placement { get; }
}

/// <summary>Where Gunnar's figure stands at home, and when it moves (#461).
/// Cached local presentation targets only: no worker, cart, door or motor port
/// exists here, and a changed bed invalidates only this figure's plan.
///
/// <b>Camp is Concerned NPC's.</b> The camp is the library's public
/// <see cref="NpcCampView"/> (the facade released in Concerned NPC 0.5.0); no
/// private library type is reached or reimplemented here.
///
/// <b>The spot</b> is <see cref="GunnarFigurePlacement"/>'s: the shared
/// planner's ring sweep 1.5-4 m from home, on bare ground (never a seat: a
/// standing figure is never stood on a chair), clear of other keepsakes - and
/// then admitted only inside camp once there is one, with room to stand, not
/// in or beside a doorway, and not across a doorway or a wall from home
/// (<see cref="GunnarRestBounds"/>).
///
/// <b>Fails closed.</b> A look at the pieces and doors near home that could not
/// be completed shows him nowhere new: a figure already standing keeps the spot
/// it was admitted to while they were known, and none is shown otherwise.
///
/// <b>Cheap when nothing changed.</b> The plan is kept until the anchor moves,
/// camp's revision changes or the pieces near home change; a step with none of
/// those allocates nothing and probes nothing.</summary>
internal sealed class GunnarCampRoutine
{
    private readonly Func<WorldPoint, bool> _admits;
    private readonly WorldPoint[] _line = new WorldPoint[2];
    private CompanionAnchor _anchor;
    private PlacementResult _placement;
    private int _revision=-1;
    private int _around=-1;
    private bool _planned;
    private bool _shown;

    // Only while a plan is being made: what the admission check measures from.
    private CompanionAnchor _planningFor;
    private NpcCampView? _planningCamp;
    private IGunnarRestSurroundings? _planningAround;

    public GunnarCampRoutine() { _admits=Admits; }

    public void NoteFigureShown(bool shown) { _shown=shown; }
    public void Forget() { _shown=false; _planned=false; _revision=-1; _around=-1; _anchor=default; _placement=default; }

    /// <param name="sightRead">Whether the last look at the pieces and doors
    /// near home was completed. False shows him nowhere new.</param>
    public GunnarCampStep Step(in GunnarBodyFacts facts,CompanionAnchor anchor,NpcCampView? camp,bool sightRead,
        NpcWorldEpoch epoch,IPlacementProbe probe,IGunnarRestSurroundings surroundings)
    {
        if (probe==null) throw new ArgumentNullException(nameof(probe));
        if (surroundings==null) throw new ArgumentNullException(nameof(surroundings));
        // Range changes do not destroy an existing eligible local figure. The
        // renderer culls it; returning to range uses the same figure/plan.
        var atHome=new GunnarBodyFacts(facts.Registered,facts.WorldLoaded,facts.Host,facts.Enabled,
            facts.Stage,facts.SavedWorkers,facts.Resting,facts.AnchorKnown,facts.Near || _shown);
        if (GunnarBodyPlan.Decide(atHome)!=GunnarBodyKind.Presentation || !anchor.IsValid || camp==null || !camp.Epoch.Matches(epoch) || !Finite(anchor.Position))
        { bool had=_shown; Forget(); return new GunnarCampStep(had ? GunnarCampAction.Hide : GunnarCampAction.None,default); }
        if (!facts.Near) return new GunnarCampStep(_shown ? GunnarCampAction.Retain : GunnarCampAction.None,_placement);
        if (!sightRead)
        {
            // With the doors near home unknown, nothing new is admitted. A spot
            // he already stands on was admitted while they were known.
            return _shown ? new GunnarCampStep(GunnarCampAction.Retain,_placement) : new GunnarCampStep(GunnarCampAction.None,default);
        }

        bool changed=!_planned || anchor.DiffersFrom(_anchor,PlacementRules.DefaultAnchorMoveTolerance) ||
            camp.Revision!=_revision || surroundings.Revision!=_around;
        bool moved=false;
        if (changed)
        {
            PlacementResult planned=Plan(anchor,camp,probe,surroundings);
            if (!planned.Found && (planned.BlockedBy & PlacementRejection.NotLoaded)!=0)
            {
                // An unanswered question rather than a refusal: asked again on
                // the next step. A figure already standing keeps its spot.
                _planned=false;
                return _shown ? new GunnarCampStep(GunnarCampAction.Retain,_placement) : new GunnarCampStep(GunnarCampAction.None,planned);
            }

            moved=!_placement.Found || !planned.Found || !planned.Position.Equals(_placement.Position);
            _placement=planned; _anchor=anchor; _revision=camp.Revision; _around=surroundings.Revision; _planned=true;
        }
        if (!_placement.Found)
        { bool had=_shown; _shown=false; return new GunnarCampStep(had ? GunnarCampAction.Hide : GunnarCampAction.None,_placement); }
        return new GunnarCampStep(!_shown ? GunnarCampAction.Show : moved ? GunnarCampAction.Reposition : GunnarCampAction.Retain,_placement);
    }

    private PlacementResult Plan(CompanionAnchor anchor,NpcCampView camp,IPlacementProbe probe,IGunnarRestSurroundings surroundings)
    {
        _planningFor=anchor; _planningCamp=camp; _planningAround=surroundings;
        try { return GunnarFigurePlacement.Plan(anchor,probe,surroundings.OtherKeepsakes(anchor),_admits); }
        finally { _planningCamp=null; _planningAround=null; _planningFor=default; }
    }

    /// <summary>Camp, the door rule, room to stand and the wall check, cheapest
    /// first, for a spot that already passed every ground rule.</summary>
    private bool Admits(WorldPoint spot)
    {
        NpcCampView? camp=_planningCamp; IGunnarRestSurroundings? around=_planningAround;
        if (camp==null || around==null) return false;
        WorldPoint home=_planningFor.Position;
        if (camp.IsEstablished && !camp.Contains(new NpcPoint(spot.X,spot.Y,spot.Z))) return false;
        if (GunnarRestBounds.BesideADoorway(spot,around.Doors) || GunnarRestBounds.AcrossADoor(home,spot,around.Doors,_line)) return false;
        return around.HasRoomToStand(spot) && !around.IsWallBetween(home,spot);
    }

    private static bool Finite(WorldPoint p) => !(float.IsNaN(p.X)||float.IsInfinity(p.X)||
        float.IsNaN(p.Y)||float.IsInfinity(p.Y)||float.IsNaN(p.Z)||float.IsInfinity(p.Z));
    public static float? FacingYaw(WorldPoint figure,WorldPoint player)
    {
        float x=player.X-figure.X,z=player.Z-figure.Z;
        if (float.IsNaN(x)||float.IsInfinity(x)||float.IsNaN(z)||float.IsInfinity(z)||x*x+z*z<0.0001f) return null;
        return (float)(Math.Atan2(x,z)*180.0/Math.PI);
    }
}
