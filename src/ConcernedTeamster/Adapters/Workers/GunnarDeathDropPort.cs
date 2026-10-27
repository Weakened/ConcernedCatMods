using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.Diagnostics;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>What the death drop asks of the runtime that owns Gunnar's work, at
/// the moment he dies. Implemented by the collection runtime, which is the only
/// thing that can have put anything in him.</summary>
internal interface IGunnarDeathDropHost
{
    /// <summary>D4's own teardown first: any cart hitched to this body is told to
    /// let go through the hauling runtime's ordinary release path, and the answer
    /// is whether no cart holds the body any more. Then the switches and the
    /// work-authority rule, read now.</summary>
    DeathDropFacts Prepare(Humanoid body, bool genuineDeath, bool inventoryLoaded);

    /// <summary>What happened, for the ledger, the durable record and the log.
    /// Always called once per genuine death, whatever the verdict.</summary>
    void Died(DeathDropReport report, Vector3 where);
}

/// <summary>A dying Gunnar's carried inventory goes on the ground through
/// vanilla's own drop (`DECISIONS.md` D16, owner decision 2026-09-29).
///
/// <b>Exactly one event reaches it.</b> The handler is subscribed to the body's
/// own <c>Character.m_onDeath</c>, which vanilla invokes from
/// <c>Character.OnDeath</c> - on the owner's machine only, after the body has
/// been marked dead and before vanilla destroys it. Nothing in this product calls
/// the handler, and <c>tools/validate_repo.py</c>'s <c>D16 death-drop audit</c>
/// refuses any other reference to it: a retirement (<c>ZNetView.Destroy</c>), a
/// cancellation, a despawn, a zone unload, a logout, a world change and a plugin
/// teardown never run vanilla's death path, so none of them reaches the drop.
/// The handler also asks the body itself, <c>IsDead()</c>, before anything else.
///
/// <b>One call, pinned.</b> <c>Humanoid.DropItem(Inventory, ItemData, int)</c>,
/// once, in this file, with the stack's own count - what Foreman's worker body
/// already does on its own death, and the same call vanilla uses when a player
/// drops something. Inside vanilla it removes the stack from his inventory and
/// creates the ground item from the stack's own data: nothing is built from a
/// name here, and nothing he did not hold can be dropped.
///
/// <b>Ordering.</b> The cart is detached first, through the hauling runtime's own
/// D4 teardown, and the drop runs only once no cart holds the body. A cart that
/// still does is <see cref="DeathDropVerdict.CartStillHeld"/>: nothing is
/// dropped, the stacks are recorded unresolved, and the evidence is kept.
///
/// <b>Measured, one stack at a time, and the report always happens.</b> Each
/// stack is counted before and after its drop, and
/// <see cref="DeathDropLeg.Classify"/> credits the ground only with what both
/// vanilla and the count agree on. One stack failing must not take the rest with
/// it, and must not cost the runtime the record of what did reach the ground,
/// which is Foreman's precedent for the same reason.
///
/// <b>Never observed in game.</b> This binds Unity; no test here can load it. The
/// decision and the accounting are unit-tested in
/// <c>WorkerDeathDropTests</c>; this file's shape is pinned by the validator.
/// </summary>
internal sealed class GunnarDeathDropPort : MonoBehaviour
{
    private Humanoid? _humanoid;
    private Inventory? _inventory;
    private Action? _onDeath;

    /// <summary>Set by the collection runtime while it is installed. Absent, the
    /// drop is refused as not opted in and the death is still logged.</summary>
    internal static IGunnarDeathDropHost? Host { get; set; }

    /// <summary>A death is a thing a player needs told, whatever the diagnostics
    /// settings say.</summary>
    internal static Action<string>? ErrorLog { get; set; }

    /// <summary>Whether the start-up probe verified every game member the drop
    /// binds. Asked before subscribing, not only in the verdict: a game that
    /// renamed <c>DropItem</c> would fail the handler's JIT inside vanilla's
    /// own death path, before any of this file's guards could run, and leave a
    /// dead body undestroyed. Unset is no.</summary>
    internal static Func<bool>? MayListen { get; set; }

    private void Start()
    {
        try
        {
            _humanoid = GetComponent<Humanoid>();
            if (_humanoid == null)
            {
                return;
            }

            Func<bool>? mayListen = MayListen;
            if (mayListen == null || !mayListen())
            {
                Report("Gunnar's death drop is off: this build of the game does not offer everything it needs, " +
                    "so what he carries would go with his body if he died.");
                return;
            }

            _onDeath = OnDeath;
            _humanoid.m_onDeath = (Action)Delegate.Combine(_humanoid.m_onDeath, _onDeath);
        }
        catch (Exception exception)
        {
            Report("could not listen for Gunnar's death, so what he carries would go with his body: " +
                SafeFailure.Brief(exception));
        }
    }

    private void OnDestroy()
    {
        if (_humanoid != null && _onDeath != null)
        {
            _humanoid.m_onDeath = (Action)Delegate.Remove(_humanoid.m_onDeath, _onDeath);
        }
    }

    /// <summary>Vanilla's death path, and nothing else, calls this.</summary>
    private void OnDeath()
    {
        if (_humanoid == null || !_humanoid.IsDead())
        {
            // The body's own word, asked first. A call that is not its death
            // reaches nothing below.
            return;
        }

        Vector3 where = transform.position;
        IGunnarDeathDropHost? host = Host;
        DeathDropReport report = new DeathDropReport(DeathDropVerdict.Unspecified);
        List<ItemDrop.ItemData>? held = null;
        int visited = 0;
        try
        {
            _inventory = _humanoid.GetInventory();
            if (_inventory == null)
            {
                report.MarkInterrupted("his inventory could not be read");
                return;
            }

            held = new List<ItemDrop.ItemData>(_inventory.GetAllItems());
            TeamsterWorkerRecord? record = TeamsterWorkerRecord.On(this);
            bool loaded = record != null && record.IsLoaded;
            DeathDropFacts facts = host != null
                ? host.Prepare(_humanoid, genuineDeath: _humanoid.IsDead(), inventoryLoaded: loaded)
                : new DeathDropFacts(_humanoid.IsDead(), false, false, WorkAuthorityVerdict.Unspecified, false, loaded);
            DeathDropVerdict verdict = WorkerDeathDrop.Decide(facts);
            report = new DeathDropReport(verdict);

            // Indexed, so an interruption knows exactly which stack it stopped
            // at: that one and every one after it are accounted for below.
            for (visited = 0; visited < held.Count; visited++)
            {
                ItemDrop.ItemData item = held[visited];
                string prefab = NameOf(item);
                int count = item.m_stack;
                if (verdict != DeathDropVerdict.Drop)
                {
                    report.Add(DeathDropLeg.NotAttempted(prefab, count, WorkerDeathDrop.Describe(verdict)));
                    continue;
                }

                int before = CountOf(_inventory, prefab);
                bool returned = false;
                bool threw = false;
                try
                {
                    returned = _humanoid.DropItem(_inventory, item, count);
                }
                catch (Exception exception)
                {
                    threw = true;
                    Report("Gunnar died and his " + prefab + " could not be dropped cleanly: " +
                        SafeFailure.Describe(exception));
                }

                report.Add(DeathDropLeg.Classify(prefab, count, before, CountOf(_inventory, prefab), returned, threw));
            }
        }
        catch (Exception exception)
        {
            report.MarkInterrupted(SafeFailure.Brief(exception));

            // Every carried unit is on the ground or accounted unresolved: the
            // stacks the walk never reached are recorded, not forgotten.
            AccountForUnvisited(report, held, visited);
        }
        finally
        {
            try
            {
                if (host != null)
                {
                    host.Died(report, where);
                }
                else
                {
                    Report("Gunnar died with no collection runtime installed: " + WorkerDeathRows.Summary(report) +
                        " (" + report.Held + " held, " + report.Dropped + " dropped, " + report.Unresolved +
                        " unresolved).");
                }
            }
            catch (Exception exception)
            {
                Report("Gunnar died and what became of what he carried could not all be recorded: " +
                    SafeFailure.Describe(exception));
            }
        }
    }

    /// <summary>Records every stack the interrupted walk never reached as kept,
    /// which is unresolved. Best effort: a stack that cannot even be read is
    /// left to the interruption sentence.</summary>
    private static void AccountForUnvisited(DeathDropReport report, List<ItemDrop.ItemData>? held, int visited)
    {
        if (held == null)
        {
            return;
        }

        for (int index = visited; index < held.Count; index++)
        {
            try
            {
                ItemDrop.ItemData item = held[index];
                report.Add(DeathDropLeg.NotAttempted(NameOf(item), item.m_stack, "the drop was interrupted before this stack"));
            }
            catch (Exception)
            {
                // Already interrupted; the record says so.
            }
        }
    }

    /// <summary>The prefab name the collection ledger keys on, or the item's own
    /// name when it has no prefab.</summary>
    private static string NameOf(ItemDrop.ItemData item) =>
        item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;

    /// <summary>How many of one item the inventory holds, or -1 when it could not
    /// be read - which the classification reads as unreconcilable rather than as
    /// zero.</summary>
    private static int CountOf(Inventory inventory, string prefab)
    {
        try
        {
            int total = 0;
            foreach (ItemDrop.ItemData stack in inventory.GetAllItems())
            {
                if (string.Equals(NameOf(stack), prefab, StringComparison.Ordinal))
                {
                    total += stack.m_stack;
                }
            }

            return total;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private static void Report(string message)
    {
        try
        {
            ErrorLog?.Invoke(message);
        }
        catch
        {
            // Inside vanilla's death path: nothing may escape.
        }
    }
}
