using System;
using System.Globalization;
using System.Text;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Housing;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Recruitment;
using TheConcernedCat.Settlement.Tools;
using TheConcernedCat.Settlement.Register;
using TheConcernedCat.Settlement.Worker;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Settlement;

/// <summary>The four explicit player acts, and taking one back.
///
/// This is the surface CF-SET-004's evidence is gathered through, and it is
/// part of the deliverable rather than a convenience: "nothing is designated
/// implicitly" is only observable if there is a visible act that designates
/// something, and "undesignating cancels dependent orders cleanly" is only
/// observable if a player can see what clearing would cost <i>before</i> paying
/// it.
///
/// It grants nothing of its own. Every act goes through the same authority
/// answer the runtime uses on every tick, and every decision is made by the
/// game-free register — this class only turns a typed line into a request and a
/// result back into a sentence.</summary>
internal sealed class DesignationTools
{
    private readonly Func<bool> _hasAuthority;
    private readonly Func<string> _describeMissingAuthority;
    private readonly SettlementRecords _records;
    private readonly IDesignationSite _site;

    /// <summary>A plan the player has been shown and not yet confirmed.
    ///
    /// Clearing something can cancel work and move materials, so it is confirmed
    /// rather than done on the first word. The plan itself is kept, not
    /// recomputed on confirmation, so what gets applied is exactly what was
    /// shown — and if the world moved on in between,
    /// <see cref="SettlementRegister.ApplyUndesignation"/> refuses it as stale
    /// instead of silently doing something else.</summary>
    private UndesignationPlan? _pending;

    /// <summary>The custody subcommands (give, takeback, material answers,
    /// reconcile), or null where custody is not wired.</summary>
    private readonly Custody.CustodyTools? _custody;

    /// <summary>#286: measures the settlement's housing on demand, or null
    /// where nothing can measure it. A function rather than a value because a
    /// measurement taken at startup would be stale by the time anybody asked.
    ///
    /// It takes the area rather than finding one. This class has already opened
    /// the register and knows which settlement the player means; letting the
    /// measurement re-derive that gave it a second, silent way to fail, and the
    /// sentence it failed with claimed a measurement had been taken.</summary>
    private readonly Func<Designation, HousingCapacity>? _housing;

    internal DesignationTools(
        Func<bool> hasAuthority,
        Func<string> describeMissingAuthority,
        SettlementRecords records,
        Custody.CustodyTools? custody = null,
        Func<Designation, HousingCapacity>? housing = null)
        : this(hasAuthority, describeMissingAuthority, records, new WorldDesignationSite(), custody, housing)
    {
    }

    internal DesignationTools(
        Func<bool> hasAuthority,
        Func<string> describeMissingAuthority,
        SettlementRecords records,
        IDesignationSite site,
        Custody.CustodyTools? custody = null,
        Func<Designation, HousingCapacity>? housing = null)
    {
        _hasAuthority = hasAuthority;
        _describeMissingAuthority = describeMissingAuthority;
        _records = records;
        _site = site;
        _custody = custody;
        _housing = housing;
    }

    /// <summary>Drops a plan the player was shown but never confirmed.
    ///
    /// Called when a world goes away. A plan is a snapshot of one settlement's
    /// book, and carrying one into a different world would hand world A's plan
    /// to world B's register. <see cref="SettlementRegister.ApplyUndesignation"/>
    /// would refuse it as stale, so the damage was already bounded — but relying
    /// on a downstream guard for something this cheap to prevent is how the
    /// guard ends up being the only thing standing between a typo and a
    /// cancelled order.</summary>
    internal void Forget()
    {
        _pending = null;
    }

    internal string Execute(string[]? args)
    {
        string subcommand = (args != null && args.Length > 0)
            ? args[0].ToLowerInvariant()
            : "status";

        if (!_records.TryOpen(out SettlementRegister register, out SettlementJournal journal))
        {
            return "No world is loaded, so there is no settlement to mark anything in.";
        }

        switch (subcommand)
        {
            case "status": return Status(register, journal);
            case "housing": return Housing(register);
            case "area": return MarkArea(register, DesignationKind.SettlementArea, args);
            case "harvest": return MarkArea(register, DesignationKind.HarvestArea, args);
            case "supply": return MarkSupply(register, journal);
            case "recruit": return Recruit(register, args);
            case "dismiss": return Dismiss(register, args);
            case "clear": return Clear(register, journal, args);
            case "resolve": return Resolve(journal, args);
            case "give": return _custody == null ? NoCustody : _custody.Give(journal, args);
            case "takeback": return _custody == null ? NoCustody : _custody.TakeBack(journal, args);
            case "release": return _custody == null ? NoCustody : _custody.Release();
            case "reconcile": return _custody == null ? NoCustody : _custody.Reconcile();
            default:
                return "Unknown subcommand. Try: status, housing, area <radius>, harvest <radius>, " +
                    "supply, recruit [name], dismiss [name], clear <area|harvest|supply> [yes], " +
                    "give axe|hammer, takeback [axe|hammer], release, reconcile, " +
                    "resolve <request> mine|his|source|destination [count], resolve <order> lost.";
        }
    }

    private const string NoCustody = "Custody is not available in this build.";

    /// <summary>#286: what the settlement can actually house, measured from the
    /// real beds with vanilla's own rules rather than asserted by a blueprint.
    /// Read-only: no bed is claimed and nothing is written.</summary>
    private string Housing(SettlementRegister register)
    {
        if (_housing == null)
        {
            return "This build cannot measure housing.";
        }

        if (!register.TryGet(DesignationKind.SettlementArea, out Designation area))
        {
            // The register comes back EMPTY AND READ-ONLY when its file could
            // not be read, is from a newer build, or belongs to another world —
            // `SettlementRecords.TryOpen` still returns true. So "nothing is
            // marked" and "I could not read what you marked" arrive here
            // identically, and answering the first for the second is the same
            // affirmative-absence bug this command was just corrected for, one
            // frame up the stack.
            if (_records.IsReadOnly)
            {
                return HousingCapacity.NotSurveyed.Describe() +
                    (_records.Notice != null ? " " + _records.Notice : string.Empty);
            }

            return "No settlement area is marked, so there is nowhere for anybody to live yet.";
        }

        return _housing(area).Describe();
    }

    private string Status(SettlementRegister register, SettlementJournal journal)
    {
        var text = new StringBuilder();
        text.Append("Settlement: ");
        text.Append(_hasAuthority() ? "authority granted (opted in, host)." : _describeMissingAuthority() + ".");

        if (_records.IsReadOnly)
        {
            text.Append(" RECORD IS READ-ONLY.");
        }

        if (_records.Notice != null)
        {
            text.Append(' ').Append(_records.Notice);
        }

        text.Append(Environment.NewLine);

        if (register.Designations.Count == 0)
        {
            text.Append("Nothing is marked. Nothing is inferred from where you stand — ");
            text.Append("mark the settlement area first with: cf_settle area <radius>.");
        }
        else
        {
            foreach (Designation designation in register.Designations)
            {
                text.Append("  ").Append(designation);

                // Ask about the KIND, not about the row's own epoch. Using
                // IsStaleIdentity(null) as a stand-in for "is this the chest"
                // silently skipped the warning for a row that has no epoch at
                // all -- written by an older build, or hand-repaired -- which is
                // exactly the row most in need of it.
                if (designation.Kind == DesignationKind.SupplyContainer
                    && register.HasStaleSupplyIdentity)
                {
                    text.Append(
                        "  -- MARKED BEFORE THIS WORLD WAS LOADED. The game gives a chest no " +
                        "identity that survives a save, so this one currently points at nothing. " +
                        "Look at the chest and run: cf_settle supply");
                }

                text.Append(Environment.NewLine);
            }
        }

        text.Append(Environment.NewLine);

        if (register.Workers.Count == 0)
        {
            text.Append("Nobody is employed.");
        }
        else
        {
            foreach (WorkerRecord worker in register.Workers)
            {
                text.Append("  employed: ").Append(worker).Append(Environment.NewLine);
            }

            // Being on the roster and having a body in the world are different
            // things, and this build does not yet join them: the roster is the
            // record, and `cf_worker spawn` puts a body somewhere. Saying so is
            // better than a status line that implies a worker is standing
            // around when none is.
            text.Append("  (the roster is the record; use cf_worker to put a body in the world)");
        }

        if (_custody != null)
        {
            text.Append(Environment.NewLine).Append(_custody.Status());
        }

        ReplayResult state = journal.Replay();
        if (state.NeedsRepair)
        {
            text.Append(Environment.NewLine).Append("THIS SETTLEMENT NEEDS REPAIR:");
            foreach (string repair in state.Repairs)
            {
                text.Append(Environment.NewLine).Append("  ").Append(repair);
            }
        }

        return text.ToString();
    }

    private string MarkArea(SettlementRegister register, DesignationKind kind, string[]? args)
    {
        Player player = Player.m_localPlayer;
        if (player == null)
        {
            return "Refused: there is no local player to mark around.";
        }

        if (args == null || args.Length < 2
            || !float.TryParse(
                args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float radius))
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Usage: cf_settle {0} <radius>. Between {1:0.#} and {2:0.#} metres, measured from " +
                "where you are standing.",
                kind == DesignationKind.SettlementArea ? "area" : "harvest",
                DesignationBook.MinRadius,
                DesignationBook.MaxRadius);
        }

        Vector3 here = player.transform.position;
        DesignationResult result = register.Designate(
            DesignationRequest.Area(kind, new SitePoint(here.x, here.y, here.z), radius),
            _site,
            _hasAuthority());

        return Report(result, register);
    }

    private string MarkSupply(SettlementRegister register, SettlementJournal journal)
    {
        if (!SettlementTargets.TryResolveHoveredContainer(
                out Container? container, out string? key, out string failure))
        {
            return "Refused: " + failure;
        }

        // With the record, so a chest marked before this world was loaded is
        // replaced through the undesignation cascade: whatever was drawn from it
        // is returned in the record and its orders cancelled first (#294).
        Vector3 at = container!.transform.position;
        DesignationResult result = register.Designate(
            DesignationRequest.Container(new SitePoint(at.x, at.y, at.z), key),
            _site,
            _hasAuthority(),
            journal,
            out UndesignationPlan? replaced);

        if (replaced != null)
        {
            _pending = null;
            string? notice = _records.Save();
            return "The chest marked before this world was loaded was replaced. " + replaced.Describe() + " " +
                result.Describe() + (notice == null ? string.Empty : " " + notice);
        }

        return Report(result, register);
    }

    private string Recruit(SettlementRegister register, string[]? args)
    {
        string name = (args != null && args.Length > 1) ? args[1] : "worker-1";

        WorkerId id;
        try
        {
            id = new WorkerId(name);
        }
        catch (ArgumentException)
        {
            return "Refused: \"" + name + "\" is not a usable worker name. Use lower-case " +
                "letters, digits and dashes.";
        }

        RecruitmentResult result = register.Recruit(id, WorkerRoles.Labourer, _hasAuthority());
        if (result.Outcome == RecruitmentOutcome.Recruited)
        {
            string? notice = _records.Save();
            return result.Describe() + (notice == null ? string.Empty : " " + notice);
        }

        return result.Describe();
    }

    private string Dismiss(SettlementRegister register, string[]? args)
    {
        string name = (args != null && args.Length > 1) ? args[1] : "worker-1";

        WorkerId id;
        try
        {
            id = new WorkerId(name);
        }
        catch (ArgumentException)
        {
            return "Refused: \"" + name + "\" is not a usable worker name.";
        }

        switch (register.Dismiss(id, _hasAuthority()))
        {
            case DismissalOutcome.Dismissed:
                string? notice = _records.Save();
                return "Dismissed " + id.Value + "." +
                    (notice == null ? string.Empty : " " + notice);

            case DismissalOutcome.NotOnRoster:
                return "Nobody called " + id.Value + " is employed here.";

            default:
                return "Refused: " + _describeMissingAuthority() + ".";
        }
    }

    private string Clear(SettlementRegister register, SettlementJournal journal, string[]? args)
    {
        if (args == null || args.Length < 2 || !TryParseKind(args[1], out DesignationKind kind))
        {
            return "Usage: cf_settle clear <area|harvest|supply>. Add \"yes\" to confirm one " +
                "that would cancel work.";
        }

        bool confirmed = args.Length > 2
            && string.Equals(args[2], "yes", StringComparison.OrdinalIgnoreCase);

        UndesignationPlan plan = confirmed && _pending != null && _pending.Kind == kind
            ? _pending
            : register.PlanUndesignation(kind, journal.Replay(), _hasAuthority());

        if (plan.IsRefused)
        {
            _pending = null;
            return DesignationResult.Refused(plan.Refusal).Describe();
        }

        if (plan.ChangesNothing)
        {
            _pending = null;
            return plan.Describe();
        }

        bool costsSomething = plan.OrdersToCancel.Count > 0 || plan.ToRefund.Count > 0;
        if (costsSomething && !confirmed)
        {
            _pending = plan;
            return plan.Describe() + " Nothing has happened yet. Repeat with \"yes\" to go ahead: " +
                "cf_settle clear " + args[1].ToLowerInvariant() + " yes";
        }

        UndesignationOutcome outcome =
            register.ApplyUndesignation(plan, journal, _hasAuthority());
        _pending = null;

        switch (outcome)
        {
            case UndesignationOutcome.Removed:
                string? notice = _records.Save();
                return plan.Describe() + " Done." + (notice == null ? string.Empty : " " + notice);

            case UndesignationOutcome.NotDesignated:
                return plan.Describe();

            case UndesignationOutcome.Stale:
                return "Nothing was cleared: this settlement changed since that was worked out. " +
                    "Run the same command again to see the current answer.";

            default:
                // Apply refuses for two reasons, and they need different
                // sentences: no authority, or a record this build must not
                // write over. Reporting the authority one for both would send a
                // player looking at the wrong thing.
                return _records.IsReadOnly
                    ? "Refused: this settlement's records could not be fully read, so nothing " +
                        "is being written over them. " + (_records.Notice ?? string.Empty)
                    : "Refused: " + _describeMissingAuthority() + ".";
        }
    }

    /// <summary>Settles an interrupted tool handover, the way a person says it
    /// actually went.
    ///
    /// This exists because the repair message told a player to "say which way it
    /// went" and there was no way to say it — an instruction with no command
    /// behind it, which an independent review caught. Only a person answers;
    /// nothing here works it out.</summary>
    private string Resolve(SettlementJournal journal, string[]? args)
    {
        if (!_hasAuthority())
        {
            // Every other mutating subcommand asks this, and this one writes a
            // journal row and saves both settlement files -- so it asking was
            // never optional. Without it the class summary above and the
            // command's own help text were both false.
            return "Refused: " + _describeMissingAuthority() + ".";
        }

        if (args == null || args.Length < 3)
        {
            return "Usage: cf_settle resolve <request> mine|his for a tool (\"mine\": you still have it; " +
                "\"his\": he does), <request> source|destination [count] for material, or <order> lost to " +
                "record what reconciliation found missing. Run cf_settle status to see what is waiting.";
        }

        bool workerHasIt;
        string answer = args[2].ToLowerInvariant();
        switch (answer)
        {
            case "his": workerHasIt = true; break;
            case "mine": workerHasIt = false; break;
            case "source":
            case "destination":
            case "lost":
                return _custody == null ? NoCustody : _custody.Resolve(args[1], answer, args);
            default:
                return "Say \"mine\" or \"his\" for a tool, \"source\" or \"destination\" for material, or " +
                    "\"lost\" for an order's shortfall. Nothing has been changed.";
        }

        RequestId transaction;
        try
        {
            transaction = new RequestId(args[1]);
        }
        catch (ArgumentException)
        {
            return "That is not a handover reference. Run cf_settle status to see the one that " +
                "is waiting.";
        }

        // Replayed fresh, so the answer is given against what the record says
        // right now rather than against a cached view of it.
        ToolLedger tools = journal.Replay().Tools;

        ToolResolution.TryRecord(
            transaction, workerHasIt, tools, journal, _records.TryPersistJournal,
            out string message);

        return message;
    }

    private string Report(DesignationResult result, SettlementRegister register)
    {
        if (result.Outcome != DesignationOutcome.Designated)
        {
            return result.Describe();
        }

        // Marking something can invalidate a plan the player was shown a moment
        // ago, and applying that plan would then remove a designation they have
        // since replaced. Dropping it here means the worst case is being asked
        // to look again.
        _pending = null;

        string? notice = _records.Save();
        return result.Describe() + (notice == null ? string.Empty : " " + notice);
    }

    private static bool TryParseKind(string text, out DesignationKind kind)
    {
        switch (text.ToLowerInvariant())
        {
            case "area":
            case "settlement":
                kind = DesignationKind.SettlementArea;
                return true;

            case "harvest":
                kind = DesignationKind.HarvestArea;
                return true;

            case "supply":
            case "container":
                kind = DesignationKind.SupplyContainer;
                return true;

            default:
                kind = DesignationKind.None;
                return false;
        }
    }
}
