using TheConcernedCat.Settlement.Journal;

namespace TheConcernedCat.Settlement.Register;

/// <summary>What happened when a settlement's two files were written.</summary>
internal readonly struct RecordSaveOutcome
{
    internal RecordSaveOutcome(
        bool journalSaved, bool registerSaved, bool registerHeldBack, string? notice)
    {
        JournalSaved = journalSaved;
        RegisterSaved = registerSaved;
        RegisterHeldBack = registerHeldBack;
        Notice = notice;
    }

    public bool JournalSaved { get; }

    public bool RegisterSaved { get; }

    /// <summary>True when the register was deliberately <b>not</b> written
    /// because the journal write failed. Distinct from "the register had
    /// nothing to write", which is an ordinary quiet success.</summary>
    public bool RegisterHeldBack { get; }

    public string? Notice { get; }
}

/// <summary>Writes a settlement's journal and register together, in the order
/// whose only possible disagreement is the recoverable one.
///
/// <b>This exists as its own game-free type because the rule it enforces is the
/// most dangerous line in the leaf and it was previously unreachable by any
/// test.</b> It lived in the Foreman adapter, which references BepInEx and
/// <c>ZNet</c> and therefore cannot be linked into the game-free test project.
/// An independent review found the rule broken there, in code nothing could
/// exercise. Moving it here is the fix that keeps it fixed.
///
/// The rule, and why the ordering alone is not it:
///
/// The register records that a designation is <i>gone</i>. The journal records
/// that the material it held was <i>returned</i> and the order <i>cancelled</i>.
/// Writing the journal first is only worth anything if a failed journal write
/// <b>stops</b> the register write — otherwise the disk ends up holding
/// "the marking is gone and nothing was returned", which is precisely the state
/// the ordering exists to prevent, and it is unrepairable: the designation that
/// would have driven the cascade is the thing that disappeared.
///
/// The reverse inconsistency is survivable, which is why the journal goes
/// first. A journal entry with no matching register change reads as "this
/// happened and the marking is still there" — a replay reconciles it and the
/// player can clear it again.</summary>
internal sealed class SettlementRecordWriter
{
    private readonly JournalStore _journals;
    private readonly SettlementRegisterStore _registers;

    internal SettlementRecordWriter(JournalStore journals, SettlementRegisterStore registers)
    {
        _journals = journals;
        _registers = registers;
    }

    internal RecordSaveOutcome Save(SettlementJournal journal, SettlementRegister register)
    {
        if (journal == null || register == null)
        {
            return new RecordSaveOutcome(false, false, false, null);
        }

        JournalStore.SaveReport journalReport = _journals.Save(journal);

        // The dirty check is load-bearing, not defensive. A journal with
        // nothing to write also reports Saved == false, and treating that as a
        // failure would stop the register ever being written at all.
        if (!journalReport.Saved && journal.IsDirty)
        {
            return new RecordSaveOutcome(
                false,
                false,
                registerHeldBack: true,
                Join(
                    journalReport.Notice,
                    "What you marked was NOT changed either, so the two records still agree " +
                    "with each other."));
        }

        SettlementRegisterStore.SaveReport registerReport = _registers.Save(register);

        return new RecordSaveOutcome(
            journalReport.Saved,
            registerReport.Saved,
            registerHeldBack: false,
            Join(journalReport.Notice, registerReport.Notice));
    }

    /// <summary>Both notices, not the first one.
    ///
    /// Coalescing would tell a player the journal is unreadable and never
    /// mention that the register is too, or the reverse — and those are
    /// different repairs.</summary>
    internal static string? Join(string? first, string? second)
    {
        if (string.IsNullOrEmpty(first))
        {
            return string.IsNullOrEmpty(second) ? null : second;
        }

        return string.IsNullOrEmpty(second) ? first : first + " " + second;
    }
}
