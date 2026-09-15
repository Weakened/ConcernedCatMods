using System;
using System.IO;
using BepInEx;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Register;

namespace TheConcernedCat.ConcernedForeman.Runtime.Settlement;

/// <summary>The two files one settlement owns, and the world they belong to.
///
/// <b>Loading is lazy and keyed by the world.</b> A world's id is only knowable
/// once <c>ZNet</c> is up, and "up" arrives at a moment nothing here controls,
/// so rather than race a scene event this resolves the scope on every use and
/// reloads whenever it changes. Loading a second world in one session therefore
/// cannot inherit the first world's settlement — which is the failure a cached
/// scope would produce, and it would produce it silently.
///
/// The register and the journal are separate files because they answer
/// different questions and fail differently. The register is current state:
/// what is marked and who is employed. The journal is history: what moved. A
/// damaged register costs the player their markings; a damaged journal costs
/// the answer to "whose wood is where". Both go read-only rather than being
/// replaced, and neither can make the other unreadable.</summary>
internal sealed class SettlementRecords
{
    /// <summary>One settlement per world, for the first proof.
    ///
    /// <see cref="SettlementScope"/> already carries a settlement id precisely so
    /// that a world can hold several and they stay isolated. This build marks
    /// one, because #273's first playable outcome is one cottage — naming and
    /// switching between settlements is a decision to take afterwards, with the
    /// loop working.</summary>
    private const string FirstSettlement = "home";

    private readonly Action<string> _log;
    private readonly SettlementRegisterStore _registers;
    private readonly JournalStore _journals;

    private SettlementScope _scope;
    private SettlementRegister? _register;
    private SettlementJournal? _journal;

    internal SettlementRecords(Action<string> log)
        : this(log, DefaultRoot())
    {
    }

    internal SettlementRecords(Action<string> log, string root)
    {
        _log = log;
        _registers = new SettlementRegisterStore(root);
        _journals = new JournalStore(root);
    }

    private static string DefaultRoot()
    {
        return Path.Combine(
            Paths.ConfigPath, "ConcernedCatMods", "ConcernedForeman", "settlements");
    }

    /// <summary>The notice from the most recent load, or null. Held so that
    /// "this settlement is read-only and here is why" is answerable at any time
    /// rather than only in the log line nobody scrolled back to.</summary>
    internal string? Notice { get; private set; }

    /// <summary>True when the record could not be fully read and this build
    /// must not write over it.</summary>
    internal bool IsReadOnly => _register != null && _register.IsReadOnly;

    /// <summary>Resolves the current world's records, loading them if this is
    /// the first use or the world has changed.
    ///
    /// Returns false when there is no world to belong to. It never invents a
    /// scope: a settlement with no world is a file that would overwrite the
    /// next world's.</summary>
    internal bool TryOpen(out SettlementRegister register, out SettlementJournal journal)
    {
        register = null!;
        journal = null!;

        ZNet net = ZNet.instance;
        if (net == null)
        {
            return false;
        }

        long worldId = net.GetWorldUID();
        if (worldId == 0L)
        {
            // Not yet known. Refusing beats writing into a scope that is about
            // to become a different one.
            return false;
        }

        var scope = new SettlementScope(worldId, new SettlementId(FirstSettlement));
        if (_register == null || _journal == null || !_scope.Equals(scope))
        {
            Load(scope);
        }

        register = _register!;
        journal = _journal!;
        return true;
    }

    private void Load(SettlementScope scope)
    {
        _scope = scope;

        SettlementRegisterStore.LoadReport registerReport = _registers.Load(scope);
        _register = registerReport.Register;

        JournalStore.LoadReport journalReport = _journals.Load(scope);
        _journal = journalReport.Journal;
        _journalReadOnly = journalReport.ReadOnly;

        Notice = registerReport.Notice ?? journalReport.Notice;
        if (Notice != null)
        {
            _log(Notice);
        }
    }

    private bool _journalReadOnly;

    /// <summary>Writes whatever changed. Both files are written, in the order
    /// that leaves the safer inconsistency if the second write fails: the
    /// journal first, because a journal entry with no matching register change
    /// reads as "this happened and the marking is still there", while the
    /// reverse would read as "the marking is gone and nothing was returned".</summary>
    internal string? Save()
    {
        if (_register == null || _journal == null)
        {
            return null;
        }

        JournalStore.SaveReport journal = _journals.Save(_journal, _journalReadOnly);
        SettlementRegisterStore.SaveReport register = _registers.Save(_register);

        return journal.Notice ?? register.Notice;
    }

    /// <summary>Drops everything when a world goes away, so the next world in
    /// the same session starts from its own files.</summary>
    internal void Forget()
    {
        _register = null;
        _journal = null;
        _scope = default;
        _journalReadOnly = false;
        Notice = null;
    }
}
