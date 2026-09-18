using System;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Custody;

/// <summary>Puts what the ledger says a worker is carrying into his hand
/// (CF-SET-007, #284).
///
/// <b>Vanilla has no carry.</b> The nearest thing is the right-hand equipment
/// slot, and this uses it through the game's own <c>VisEquipment.SetRightItem</c>
/// rather than instantiating a model and parenting it somewhere. That choice is
/// deliberate: a worker body is a live networked humanoid that already runs
/// <c>VisEquipment</c> every frame, so the game does the attaching, and there is
/// no component surgery to get wrong. The companion actor has to instantiate and
/// strip pieces itself only because it builds a figure that is never allowed to
/// wake up; that reasoning does not apply here.
///
/// <b>What it writes.</b> `SetRightItem` sets <c>ZDOVars.s_rightItem</c> on the
/// worker's own network object, guarded by vanilla's own <c>IsOwner</c> — the
/// same vanilla key, on the same object, that any creature holding a weapon
/// writes. It is not a mod key, it is not a vanilla object belonging to anybody
/// else, and D9 allows a worker body its own network object. Nothing else is
/// touched: no inventory, no ledger, no equip, no item.
///
/// <b>What it cannot promise.</b> <c>VisEquipment.AttachItem</c> looks for a
/// child named <c>attach</c> (or <c>attach_skin</c>) on the item's prefab and
/// returns null when there is none. Materials are not equipment, so whether
/// <c>Stone</c> and <c>Wood</c> carry one is a question about the game's asset
/// bundles that a static read of the assembly cannot answer. So this reports
/// what actually happened rather than assuming: <see cref="LastOutcome"/> says
/// whether a model appeared, and <c>cf_settle</c> prints it. #284's acceptance
/// is that what is visible matches the ledger and is never a decoration that
/// disagrees with it — a hand that silently shows nothing while claiming to show
/// stone would be exactly that.</summary>
internal sealed class CarriedVisual
{
    /// <summary>What the last attempt to show something did.</summary>
    internal enum Outcome
    {
        /// <summary>Nothing has been asked of it yet.</summary>
        Idle,

        /// <summary>The ledger says he carries nothing, so his hands are
        /// empty and that is correct.</summary>
        Empty,

        /// <summary>A model is in his hand.</summary>
        Shown,

        /// <summary>The item exists and has no attachable model in this build,
        /// so nothing is visible. The ledger is unaffected and right; only the
        /// picture is missing.</summary>
        NoModel,

        /// <summary>The body is gone, not owned here, or has no
        /// <c>VisEquipment</c>.</summary>
        NoBody,
    }

    private readonly Func<WorkerBody?> _body;
    private readonly Action<string>? _log;

    private CarriedDisplay _shown = CarriedDisplay.Nothing;
    private bool _bound;

    internal CarriedVisual(Func<WorkerBody?> body, Action<string>? log = null)
    {
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _log = log;
    }

    internal Outcome LastOutcome { get; private set; } = Outcome.Idle;

    /// <summary>What the hand is currently claiming. Only ever set from a
    /// <see cref="CarriedDisplay"/> the ledger produced.</summary>
    internal CarriedDisplay Showing => _shown;

    /// <summary>One sentence for <c>cf_settle</c>.</summary>
    internal string Describe()
    {
        switch (LastOutcome)
        {
            case Outcome.Shown:
                return "He is holding " + _shown + ".";
            case Outcome.Empty:
                return "His hands are empty, and the record agrees.";
            case Outcome.NoModel:
                return "He is carrying " + _shown + ", but this build has no model to put " +
                    "in his hand for it, so you will not see it. The record is unaffected.";
            case Outcome.NoBody:
                return "There is no worker body here to show anything.";
            default:
                return "Nothing has been shown yet.";
        }
    }

    /// <summary>Brings the hand into line with the ledger. Cheap to call every
    /// tick: it does nothing at all unless the answer changed.</summary>
    internal void Refresh(IMaterialCustodyView? view, OrderId order)
    {
        Apply(CarriedDisplay.For(view, order));
    }

    /// <summary>Empty hands, whatever the ledger says — for a body being
    /// retired, or an order that ended.</summary>
    internal void Clear() => Apply(CarriedDisplay.Nothing);

    private void Apply(CarriedDisplay wanted)
    {
        if (_bound && wanted.Equals(_shown) && LastOutcome != Outcome.NoBody)
        {
            return;
        }

        WorkerBody? body = _body();
        VisEquipment? visuals = body == null || !body.IsOwned
            ? null
            : body.Humanoid == null ? null : body.Humanoid.GetComponent<VisEquipment>();

        if (visuals == null)
        {
            // Not a failure to report loudly: a body out of range or not ours
            // is the ordinary case. Forget what we think is shown, so the next
            // body we do get is set from scratch rather than from a stale
            // memory of the last one.
            _bound = false;
            _shown = CarriedDisplay.Nothing;
            LastOutcome = Outcome.NoBody;
            return;
        }

        int hash = 0;
        if (wanted.ShowsSomething)
        {
            try
            {
                hash = CollectedResources.ItemPrefabName(wanted.Resource).GetStableHashCode();
            }
            catch (Exception)
            {
                // A resource with no item name is not one this build collects.
                hash = 0;
                wanted = CarriedDisplay.Nothing;
            }
        }

        try
        {
            visuals.SetRightItem(hash, 0);
        }
        catch (Exception exception)
        {
            _bound = false;
            LastOutcome = Outcome.NoBody;
            Report("the worker's hand could not be set (" + exception.GetType().Name + ")");
            return;
        }

        _bound = true;
        _shown = wanted;

        if (!wanted.ShowsSomething)
        {
            LastOutcome = Outcome.Empty;
            return;
        }

        // Did a model actually appear? VisEquipment only builds the instance
        // when the item prefab has an attach child, and it logs its own miss
        // rather than telling the caller. Asking the field afterwards is the
        // only way to know, and knowing is the difference between showing what
        // is carried and claiming to.
        LastOutcome = HasRightItemInstance(visuals) ? Outcome.Shown : Outcome.NoModel;
        if (LastOutcome == Outcome.NoModel)
        {
            Report(
                "this build has no model to put in a worker's hand for " +
                CollectedResources.ItemPrefabName(wanted.Resource) +
                ", so carrying it will not be visible");
        }
    }

    /// <summary>Whether the right-hand model exists. The field is private in
    /// vanilla and the build compiles against the publicized assemblies, where
    /// it is not — so this is a direct read where that holds and an honest
    /// "cannot tell" where it does not.</summary>
    private static bool HasRightItemInstance(VisEquipment visuals)
    {
        try
        {
            return visuals.m_rightItemInstance != null;
        }
        catch (Exception)
        {
            // Cannot tell. Claiming Shown would be the lie this class exists to
            // avoid, so it reports the weaker answer.
            return false;
        }
    }

    private void Report(string what)
    {
        try
        {
            _log?.Invoke(what);
        }
        catch (Exception)
        {
            // A broken log sink must not cost a presentation pass.
        }
    }
}
