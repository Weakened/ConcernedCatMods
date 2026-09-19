using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary>What the world holds of the Steward, loaded or not.
///
/// <see cref="Verdict.Unknown"/> is zero, so a census nobody took never reads
/// as "exactly one, all well".</summary>
internal enum StewardCensusVerdict
{
    /// <summary>The world could not be asked. Refuse to act on it.</summary>
    Unknown = 0,

    /// <summary>No saved body carries his identity. He has not been recruited,
    /// or his body was destroyed.</summary>
    Missing = 1,

    /// <summary>Exactly one, and it is loaded and usable here.</summary>
    Present = 2,

    /// <summary>Exactly one, but its ground is not loaded. Not a fault: a
    /// settlement the player has walked away from unloads, and his body with
    /// it.</summary>
    Unloaded = 3,

    /// <summary>More than one body carries his identity.
    ///
    /// <b>Reported and never repaired.</b> Two bodies could mean a duplicated
    /// save, a restored backup, or a bug — and the only "repair" available is
    /// to destroy one, which would destroy whatever it was carrying. Refusing
    /// to work until a person chooses is the smaller harm.</summary>
    Duplicated = 4,
}

/// <summary>One reading of how many Stewards the world thinks there are.
/// </summary>
internal sealed class StewardCensus
{
    internal StewardCensus(
        StewardCensusVerdict verdict, int saved, int unidentified, StewardBody? live)
    {
        Verdict = verdict;
        Saved = saved;
        Unidentified = unidentified;
        Live = live;
    }

    public StewardCensusVerdict Verdict { get; }

    /// <summary>Saved bodies carrying his identity, loaded or not.</summary>
    public int Saved { get; }

    /// <summary>Steward-prefab bodies with no identity stamped at all: spawned
    /// by a build that did not stamp one, or interrupted between spawning and
    /// stamping.
    ///
    /// <b>Counted, never adopted and never destroyed.</b> Adopting one would
    /// mean guessing that an anonymous body is his, and destroying one would
    /// destroy whatever it holds.</summary>
    public int Unidentified { get; }

    /// <summary>The loaded body, when there is exactly one and it is here.
    /// </summary>
    public StewardBody? Live { get; }

    public bool CanWork => Verdict == StewardCensusVerdict.Present;

    /// <summary>One sentence a player can act on.</summary>
    public string Describe()
    {
        switch (Verdict)
        {
            case StewardCensusVerdict.Present:
                return "The Steward is here.";

            case StewardCensusVerdict.Unloaded:
                return "The Steward is in the world but her part of it is not loaded, so she is " +
                    "not doing anything. Go back to the settlement.";

            case StewardCensusVerdict.Missing:
                if (Unidentified == 0)
                {
                    return "There is no Steward in this world.";
                }

                return "There is no Steward. " +
                    (Unidentified == 1
                        ? "One body without an identity was"
                        : Unidentified.ToString(CultureInfo.InvariantCulture) +
                          " bodies without an identity were") +
                    " found and left alone — nothing has been adopted or destroyed.";

            case StewardCensusVerdict.Duplicated:
                return "There are " + Saved.ToString(CultureInfo.InvariantCulture) + " bodies " +
                    "carrying the Steward's identity. She will not work until there is one. " +
                    "Nothing has been destroyed, because one of them may be carrying your " +
                    "materials — please report this.";

            default:
                return "How many Stewards this world has could not be established, so she is " +
                    "not working.";
        }
    }

    public override string ToString() => Describe();
}

/// <summary>Counting the Stewards in a world.
///
/// <b>Saved objects, not loaded ones.</b> Asking the scene how many are here
/// would answer "none" for a settlement the player has walked away from, and
/// "none" is the answer that would let a second Steward be recruited. The count
/// comes from <c>ZDOMan</c>, which knows about objects whose ground is not
/// loaded.</summary>
internal static class StewardCensusTaker
{
    /// <summary>A ceiling on the sector walk, so a damaged world cannot spin
    /// here. Far above the number of sectors a real world has.</summary>
    private const int SectorGuard = 100000;

    internal static StewardCensus Take(string prefabName, string identityKey)
    {
        ZDOMan? manager = ZDOMan.instance;
        if (manager == null || string.IsNullOrEmpty(prefabName) || string.IsNullOrEmpty(identityKey))
        {
            return new StewardCensus(StewardCensusVerdict.Unknown, 0, 0, null);
        }

        var all = new List<ZDO>();
        try
        {
            int index = 0;
            int guard = 0;
            while (!manager.GetAllZDOsWithPrefabIterative(prefabName, all, ref index) && guard++ < SectorGuard)
            {
            }
        }
        catch (Exception)
        {
            return new StewardCensus(StewardCensusVerdict.Unknown, 0, 0, null);
        }

        // The iterative scan's terminating call walks sector 0 again — verified
        // in 1.0.14, as it was in 1.0.12 — so the same object can come back
        // twice, and a body counted twice would read as a duplicate identity and
        // stop the Steward working for no reason.
        var distinct = new HashSet<ZDO>();
        int saved = 0;
        int unidentified = 0;
        foreach (ZDO zdo in all)
        {
            if (zdo == null || !distinct.Add(zdo))
            {
                continue;
            }

            string stored = zdo.GetString(StewardBody.KeyField, string.Empty);
            if (stored.Length == 0)
            {
                unidentified++;
            }
            else if (string.Equals(stored, identityKey, StringComparison.Ordinal))
            {
                saved++;
            }
        }

        if (saved == 0)
        {
            return new StewardCensus(StewardCensusVerdict.Missing, 0, unidentified, null);
        }

        if (saved > 1)
        {
            return new StewardCensus(StewardCensusVerdict.Duplicated, saved, unidentified, null);
        }

        StewardBody? live = StewardBody.FindLive(identityKey);
        return new StewardCensus(
            live != null && live.IsLoaded && live.Fault == null
                ? StewardCensusVerdict.Present
                : StewardCensusVerdict.Unloaded,
            saved,
            unidentified,
            live);
    }
}
