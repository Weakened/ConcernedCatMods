using System;
using System.Collections.Generic;
using TheConcernedCat.Companions.Placement;

namespace TheConcernedCat.Companions.Surroundings;

/// <summary>A fire in camp, as a companion sees it.</summary>
internal readonly struct CampFire
{
    public CampFire(WorldPoint position, bool burning, bool sheltered, float hazardMetres)
    {
        Position = position;
        Burning = burning;
        Sheltered = sheltered;
        HazardMetres = Math.Max(0f, hazardMetres);
    }

    public WorldPoint Position { get; }

    public bool Burning { get; }

    /// <summary>Under a roof - which is what "a fire inside" means to him.
    /// </summary>
    public bool Sheltered { get; }

    /// <summary>How close to its centre is too close: the stones, the flames.
    /// A bonfire is wider than a fire pit.</summary>
    public float HazardMetres { get; }
}

/// <summary>A bed in camp.</summary>
internal readonly struct CampBed
{
    public CampBed(WorldPoint position, float yawDegrees, bool claimed, bool sheltered)
    {
        Position = position;
        YawDegrees = yawDegrees;
        Claimed = claimed;
        Sheltered = sheltered;
    }

    /// <summary>Where a sleeper lies: the bed's own spawn point.</summary>
    public WorldPoint Position { get; }

    public float YawDegrees { get; }

    /// <summary>Somebody's - the player's own or anybody else's. A companion
    /// never sleeps in a claimed bed.</summary>
    public bool Claimed { get; }

    public bool Sheltered { get; }
}

/// <summary>What his camp looks like right now.</summary>
internal sealed class CampSnapshot
{
    public CampSnapshot(
        WorldPoint home, bool night, bool wet, IReadOnlyList<CampFire>? fires, IReadOnlyList<CampBed>? beds)
    {
        Home = home;
        Night = night;
        Wet = wet;
        Fires = fires ?? Array.Empty<CampFire>();
        Beds = beds ?? Array.Empty<CampBed>();
    }

    /// <summary>His home point: the player's claimed bed, or the world's
    /// start when there is none.</summary>
    public WorldPoint Home { get; }

    public bool Night { get; }

    /// <summary>Raining, or wet enough that the game calls you exposed.
    /// </summary>
    public bool Wet { get; }

    public IReadOnlyList<CampFire> Fires { get; }

    public IReadOnlyList<CampBed> Beds { get; }
}

/// <summary>What a particular companion likes. Hulgi likes a fire and a drink,
/// and will take a spare bed for the night; a later companion can like
/// something else without anybody rewriting the rules.</summary>
internal sealed class CompanionTemperament
{
    public CompanionTemperament(bool lovesFire, bool sleepsInSpareBeds, float campRadiusMetres)
    {
        LovesFire = lovesFire;
        SleepsInSpareBeds = sleepsInSpareBeds;
        CampRadiusMetres = campRadiusMetres > 0f && !float.IsNaN(campRadiusMetres) ? campRadiusMetres : 25f;
    }

    public static readonly CompanionTemperament Hulgi =
        new CompanionTemperament(lovesFire: true, sleepsInSpareBeds: true, campRadiusMetres: 25f);

    public bool LovesFire { get; }

    public bool SleepsInSpareBeds { get; }

    /// <summary>How far from home something still belongs to his camp. A fire
    /// in the next village is not his fire.</summary>
    public float CampRadiusMetres { get; }
}

/// <summary>What he would like to be doing.</summary>
internal enum HangoutKind
{
    /// <summary>Asleep in a spare bed.</summary>
    Sleep = 0,

    /// <summary>By a particular fire.</summary>
    Fire = 1,

    /// <summary>Under a roof, anywhere near home.</summary>
    Shelter = 2,

    /// <summary>Somewhere around home. Always possible, so always last.
    /// </summary>
    Home = 3,
}

/// <summary>One thing he would like, and what it is about.</summary>
internal readonly struct HangoutIntent
{
    public HangoutIntent(HangoutKind kind, int target, WorldPoint focus)
    {
        Kind = kind;
        Target = target;
        Focus = focus;
    }

    public HangoutKind Kind { get; }

    /// <summary>Index into the snapshot's fires or beds; -1 for shelter and
    /// home.</summary>
    public int Target { get; }

    /// <summary>What he gathers around: the fire, the bed, or home.</summary>
    public WorldPoint Focus { get; }

    public override string ToString()
    {
        return Kind + (Target >= 0 ? " #" + Target : string.Empty) + " at " + Focus;
    }
}

/// <summary>A companion's common sense, as an ordered wish list.
///
/// The owner's words, 2026-09-16, are the specification: he likes to hang out
/// by a fire and have a drink; whichever fire is closest to the claimed bed,
/// inside or out; if the only fire is outside, he sits out there by day and
/// goes inside at night if he can; and if there is a spare bed, he sleeps in it
/// until morning. "Just make them have common sense."
///
/// So this does not pick a spot - it says, best first, what he would like. The
/// runtime walks the list and takes the first wish the world can actually
/// grant: a fire he can reach without going through a door he may not use, a
/// roof he can get under, a bed nobody has claimed. That split is what keeps
/// the rules arguable in a test while the world stays the world.</summary>
internal static class CommonSense
{
    public static IReadOnlyList<HangoutIntent> Preferences(CampSnapshot camp, CompanionTemperament temperament)
    {
        if (camp == null)
        {
            throw new ArgumentNullException(nameof(camp));
        }

        if (temperament == null)
        {
            throw new ArgumentNullException(nameof(temperament));
        }

        var wishes = new List<HangoutIntent>();
        List<int> fires = temperament.LovesFire ? Nearest(camp, camp.Fires.Count, i => camp.Fires[i].Position,
            i => camp.Fires[i].Burning, temperament.CampRadiusMetres) : new List<int>();

        // Night: a spare bed first. Only at night - a nap at noon is not what
        // anybody asked for - and never a claimed one.
        if (camp.Night && temperament.SleepsInSpareBeds)
        {
            foreach (int bed in Nearest(camp, camp.Beds.Count, i => camp.Beds[i].Position,
                         i => !camp.Beds[i].Claimed, temperament.CampRadiusMetres))
            {
                wishes.Add(new HangoutIntent(HangoutKind.Sleep, bed, camp.Beds[bed].Position));
            }
        }

        if (camp.Night || camp.Wet)
        {
            // In the dark or the wet, a fire under a roof is the best of both.
            foreach (int fire in fires)
            {
                if (camp.Fires[fire].Sheltered)
                {
                    wishes.Add(new HangoutIntent(HangoutKind.Fire, fire, camp.Fires[fire].Position));
                }
            }

            // Then any roof. "Inside if available" - so this comes before the
            // fire out in the open, and the fire outside is still there below
            // it for when no roof can be reached.
            wishes.Add(new HangoutIntent(HangoutKind.Shelter, -1, camp.Home));

            foreach (int fire in fires)
            {
                if (!camp.Fires[fire].Sheltered)
                {
                    wishes.Add(new HangoutIntent(HangoutKind.Fire, fire, camp.Fires[fire].Position));
                }
            }
        }
        else
        {
            // Daytime: the fire closest to home, inside or out, then the next.
            foreach (int fire in fires)
            {
                wishes.Add(new HangoutIntent(HangoutKind.Fire, fire, camp.Fires[fire].Position));
            }
        }

        wishes.Add(new HangoutIntent(HangoutKind.Home, -1, camp.Home));
        return wishes;
    }

    /// <summary>A place on the wish list, finer than the wish itself: for the
    /// same wish a seat beats the ground. Lower is better. He moves only for a
    /// strictly lower rank than the one he already has, so two equally good
    /// spots never have him pacing between them.</summary>
    public static int Rank(int wishIndex, bool onSeat)
    {
        return (Math.Max(0, wishIndex) * 2) + (onSeat ? 0 : 1);
    }

    /// <summary>The rank of somebody who satisfies none of the wishes - he is
    /// somewhere nobody would choose, and anything better will do.</summary>
    public const int Nowhere = int.MaxValue;

    private static List<int> Nearest(
        CampSnapshot camp, int count, Func<int, WorldPoint> position, Func<int, bool> wanted, float radius)
    {
        var picked = new List<int>();
        for (int index = 0; index < count; index++)
        {
            if (wanted(index) && position(index).HorizontalDistanceTo(camp.Home) <= radius)
            {
                picked.Add(index);
            }
        }

        // Nearest to home first; exact ties broken by position so the same camp
        // always gives the same answer.
        picked.Sort((left, right) =>
        {
            WorldPoint a = position(left);
            WorldPoint b = position(right);
            int byDistance = a.HorizontalDistanceTo(camp.Home).CompareTo(b.HorizontalDistanceTo(camp.Home));
            if (byDistance != 0)
            {
                return byDistance;
            }

            int byX = a.X.CompareTo(b.X);
            return byX != 0 ? byX : a.Z.CompareTo(b.Z);
        });

        return picked;
    }
}
