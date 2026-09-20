using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Containers;

namespace TheConcernedCat.ConcernedNPC.Storage;

/// <summary>One line of what a player decided about one container: where it is,
/// and what NPCs may do with it.
///
/// <b>This is the whole persistence contract, and it is not a file.</b> A role
/// writes these into whatever it already writes - its own rows, its own tags,
/// its own schema number - and hands them back after a load. There is no codec
/// here, no header, no row tag, no file name and no schema, because a shared
/// runtime that can name a file in a role's data directory is the first-run
/// detection bug this repository has already shipped twice: a product decides
/// whether a player is new by looking for its own known files, and one stranger
/// among them grants a permanent unlock to every fresh install, silently.
///
/// Four numbers and a flag, then, and the role decides how they are spelled.
/// </summary>
internal readonly struct NpcContainerPermission
{
    internal NpcContainerPermission(NpcContainerPlace place, NpcContainerUse allowed)
    {
        Place = place;
        Allowed = allowed;
    }

    internal NpcContainerPlace Place { get; }

    /// <summary>What the player allowed. Never <see cref="NpcContainerUse.Off"/>
    /// in a record that came out of a book - off is the absence of a record,
    /// which is what makes an empty file mean "nothing is enabled" rather than
    /// "nothing is known".</summary>
    internal NpcContainerUse Allowed { get; }
}

/// <summary>Which containers a player has opened to NPCs, in one world, on this
/// computer.
///
/// <b>Off is the default and off is the absence of a row.</b> A container nobody
/// has spoken about is one an NPC may not touch, and every way of arriving at an
/// answer without a deliberate choice - a lookup miss, a row that failed to
/// parse, a book that could not be loaded, an empty book - lands there. A
/// player's chests are theirs until they say otherwise, once, per chest. This is
/// the door-permission shape, which the owner approved for the same question
/// about a different world object; the only difference is that a door is a
/// boolean and a container is four states, because letting an NPC put firewood
/// in the shed is not the same trust as letting it help itself.
///
/// <b>Found by place, not by id</b> - see <see cref="NpcContainerPlace"/>. That
/// is what makes a permission survive a reload without the player marking every
/// chest again each session.
///
/// <b>Local, and never written into the world.</b> Nothing about a container
/// changes in the save when it is marked. What one player lets their own NPCs do
/// is nobody else's business, and the door rule the owner set says the same
/// thing in the same words.</summary>
internal sealed class NpcContainerPermissionBook
{
    private readonly List<NpcContainerPermission> _allowed = new List<NpcContainerPermission>();

    /// <summary>Every container with a permission, in the order they were set.
    /// What a role persists.</summary>
    internal IReadOnlyList<NpcContainerPermission> Allowed => _allowed;

    /// <summary>How many containers are enabled at all.</summary>
    internal int Count => _allowed.Count;

    /// <summary>True once something changed since the last save.</summary>
    internal bool IsDirty { get; private set; }

    /// <summary>The next state in the cycle a player walks through: off, take,
    /// deposit, both, off. One order, written once, so every role's hover text
    /// and every role's key press agree about what comes next - and so the four
    /// states a player chooses between are exactly the four in the type.</summary>
    internal static NpcContainerUse NextState(NpcContainerUse current)
    {
        switch (current)
        {
            case NpcContainerUse.Take:
                return NpcContainerUse.Deposit;

            case NpcContainerUse.Deposit:
                return NpcContainerUse.Both;

            case NpcContainerUse.Both:
                return NpcContainerUse.Off;

            default:
                return NpcContainerUse.Take;
        }
    }

    /// <summary>What the player allows for this container.
    /// <see cref="NpcContainerUse.Off"/> for one nobody has spoken about, which
    /// is most of them.</summary>
    internal NpcContainerUse Allowance(NpcContainerPlace place)
    {
        int index = IndexOf(place);
        return index < 0 ? NpcContainerUse.Off : _allowed[index].Allowed;
    }

    /// <summary>Sets what NPCs may do with this container. Setting
    /// <see cref="NpcContainerUse.Off"/> forgets it, so the book only ever holds
    /// allowances and an unreadable book is a book of refusals. Returns whether
    /// anything changed.</summary>
    internal bool SetAllowance(NpcContainerPlace place, NpcContainerUse allowed)
    {
        NpcContainerUse wanted = Sanitise(allowed);
        int index = IndexOf(place);

        if (wanted == NpcContainerUse.Off)
        {
            if (index < 0)
            {
                return false;
            }

            _allowed.RemoveAt(index);
            IsDirty = true;
            return true;
        }

        if (!place.IsLocatable)
        {
            // A container with no real position can never be found again, so
            // recording a permission for it would be a permission nothing could
            // ever match - and a book full of them.
            return false;
        }

        if (index >= 0)
        {
            if (_allowed[index].Allowed == wanted)
            {
                return false;
            }

            // Replaced rather than edited: the stored place stays the one the
            // permission was first set at, so a chest whose recorded position
            // drifts by a centimetre per rebuild cannot walk out of its own
            // tolerance one rebuild at a time.
            _allowed[index] = new NpcContainerPermission(_allowed[index].Place, wanted);
            IsDirty = true;
            return true;
        }

        _allowed.Add(new NpcContainerPermission(place, wanted));
        IsDirty = true;
        return true;
    }

    /// <summary>Moves this container to the next of the four states and returns
    /// what it is now. What a key press does.</summary>
    internal NpcContainerUse Cycle(NpcContainerPlace place)
    {
        NpcContainerUse next = NextState(Allowance(place));
        SetAllowance(place, next);
        return Allowance(place);
    }

    /// <summary>Forgets every container. Returns how many there were. The verb
    /// an uninstall uses, and the one a player uses when they have lost track.
    /// </summary>
    internal int Clear()
    {
        int count = _allowed.Count;
        if (count > 0)
        {
            _allowed.Clear();
            IsDirty = true;
        }

        return count;
    }

    internal void MarkClean() => IsDirty = false;

    /// <summary>Puts back one record read from wherever the role keeps them,
    /// without marking the book dirty.
    ///
    /// <b>Every refusal here is a forgotten permission, and that is the safe
    /// direction.</b> A record with no real position, with no allowance, or for
    /// a container already in the book is dropped and counted. The worst a
    /// damaged store can do is leave a container off, which only ever keeps an
    /// NPC out; the alternative - guessing at a half-read row - is an NPC in a
    /// chest the player never opened to it.</summary>
    internal bool Restore(NpcContainerPermission record)
    {
        if (!record.Place.IsLocatable || Sanitise(record.Allowed) == NpcContainerUse.Off || IndexOf(record.Place) >= 0)
        {
            return false;
        }

        _allowed.Add(new NpcContainerPermission(record.Place, Sanitise(record.Allowed)));
        return true;
    }

    /// <summary>Rebuilds a book from what a role read back. Returns how many
    /// records were dropped, so the role can tell a player that some of their
    /// permissions did not come back rather than leaving them to find out at a
    /// chest.</summary>
    internal static NpcContainerPermissionBook Restore(
        IEnumerable<NpcContainerPermission>? records, out int dropped)
    {
        var book = new NpcContainerPermissionBook();
        dropped = 0;
        if (records != null)
        {
            foreach (NpcContainerPermission record in records)
            {
                if (!book.Restore(record))
                {
                    dropped++;
                }
            }
        }

        book.MarkClean();
        return book;
    }

    /// <summary>Anything that is not one of the three real allowances becomes
    /// off. A flags enum can hold values nobody defined - a row that read as 7,
    /// a cast from an int - and the only safe reading of a value nobody chose is
    /// the one that refuses.</summary>
    private static NpcContainerUse Sanitise(NpcContainerUse allowed)
    {
        NpcContainerUse known = allowed & NpcContainerUse.Both;
        return known;
    }

    private int IndexOf(NpcContainerPlace place)
    {
        for (int index = 0; index < _allowed.Count; index++)
        {
            if (_allowed[index].Place.SamePlaceAs(place))
            {
                return index;
            }
        }

        return -1;
    }
}
