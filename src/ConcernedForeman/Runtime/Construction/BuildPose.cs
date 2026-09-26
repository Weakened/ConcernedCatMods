using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Custody;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.Settlement.Tools;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedForeman.Runtime.Construction;

/// <summary>The visible work: Thorstein stands still at the placement with his
/// hammer out, and then the piece is there.
///
/// <b>Everything here happens on the worker's own body and nowhere else.</b> Not
/// the player's. The precedent this follows is <c>Runtime/Ladders/ClimbPose</c>,
/// which drives <c>ZSyncAnimation</c> parameters on the body its controller owns;
/// the calls are the same two kinds and the object is the worker.
///
/// <b>No trigger, and no RPC of any kind.</b> <c>ZSyncAnimation.SetTrigger</c>
/// is the obvious way to play a swing and it is forbidden: it sends an RPC, this
/// repository's own compatibility audit records that
/// (<c>docs/mods/concerned-cartographer/COMPANION_COMPATIBILITY.md</c> section 3),
/// and a cosmetic hammer trigger was explicitly <b>not</b> authorised when the
/// neighbouring product asked for one. So the two things done here are things
/// vanilla already does to this body through its own methods:
///
/// <list type="bullet">
/// <item><c>ZSyncAnimation.SetFloat</c> on the worker's own component, zeroing
/// <c>forward_speed</c>, <c>sideway_speed</c> and <c>turn_speed</c> - the three
/// parameter names this repository has already verified against the installed
/// build, in <c>ClimbPose</c>. He stops jogging on the spot and stands squarely
/// at the piece.</item>
/// <item><c>Humanoid.EquipItem(item, triggerEquipEffects: false)</c> for a
/// hammer he is already carrying, and <c>UnequipItem</c> after. The precedent is
/// in this product: <c>WorkerBody.RestoreEquipment</c> equips a carried tool on
/// exactly this body with exactly this call, for exactly the reason that a
/// worker holding nothing looks wrong.</item>
/// </list>
///
/// <b>What is deliberately missing, said here rather than discovered.</b> There
/// is no arm swing. A real building animation needs either a trigger (an RPC,
/// refused above) or the name of an animator parameter for the build state, and
/// naming one without the game in front of us would be inventing an API. So this
/// is a hammer, a stance and a pause, which is honest, and a swing is an owner
/// decision with a measurement attached rather than a guess in this file.
///
/// <b>It never stops a build.</b> Every call is inside a try, a failure is
/// logged once, and the loop that owns it swallows the rest. A cottage that goes
/// up without the hammer being seen is a disappointment; a cottage that does not
/// go up because of an animator would be a defect.</summary>
internal sealed class BuildPose : IBuildPose
{
    private static readonly int ForwardSpeed = ZSyncAnimation.GetHash("forward_speed");
    private static readonly int SidewaySpeed = ZSyncAnimation.GetHash("sideway_speed");
    private static readonly int TurnSpeed = ZSyncAnimation.GetHash("turn_speed");

    private readonly Func<WorkerBody?> _body;
    private readonly Action<string> _log;

    private ItemDrop.ItemData? _equipped;
    private bool _saidFailure;

    /// <param name="body">Thorstein's own persisted body, looked up live. A body
    /// captured once would be a body from the previous world load.</param>
    internal BuildPose(Func<WorkerBody?> body, Action<string> log)
    {
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Whether a hammer is currently in his hand because of this pose.
    /// For the status line and for the tests, so "the hammer came out" is a fact
    /// somebody can read rather than a claim.</summary>
    internal bool HoldingTool => _equipped != null;

    /// <inheritdoc />
    public void Working(bool on)
    {
        try
        {
            WorkerBody? body = _body();
            if (body == null || !body.IsLoaded || !body.IsOwned)
            {
                // Not ours to dress. Forget any hammer we thought we had put in
                // a hand that is not there any more.
                _equipped = null;
                return;
            }

            if (on)
            {
                Stand(body);
                TakeOutTool(body);
            }
            else
            {
                PutAwayTool(body);
            }
        }
        catch (Exception exception)
        {
            if (!_saidFailure)
            {
                _saidFailure = true;
                _log("Build order: the working pose failed soft (" + SafeFailure.Brief(exception) + "). The building itself is unaffected.");
            }

            _equipped = null;
        }
    }

    /// <summary>Zeroes the three locomotion parameters on the worker's own
    /// <c>ZSyncAnimation</c>, the same three <c>ClimbPose</c> writes on the local
    /// player's.</summary>
    private static void Stand(WorkerBody body)
    {
        ZSyncAnimation? animation = body.GetComponent<ZSyncAnimation>();
        if (animation == null)
        {
            return;
        }

        animation.SetFloat(ForwardSpeed, 0f);
        animation.SetFloat(SidewaySpeed, 0f);
        animation.SetFloat(TurnSpeed, 0f);
    }

    private void TakeOutTool(WorkerBody body)
    {
        if (_equipped != null)
        {
            return;
        }

        Humanoid? humanoid = body.Humanoid;
        Inventory? inventory = body.Inventory;
        if (humanoid == null || inventory == null)
        {
            return;
        }

        foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(inventory.GetAllItems()))
        {
            if (ToolClassifier.Classify(item) != ToolKind.Hammer)
            {
                continue;
            }

            if (humanoid.IsItemEquiped(item))
            {
                // Already in his hand - from a previous round, or restored with
                // the body. Remembered so it is not put away as though this pose
                // had produced it.
                _equipped = item;
                return;
            }

            if (humanoid.EquipItem(item, triggerEquipEffects: false))
            {
                _equipped = item;
            }

            return;
        }
    }

    private void PutAwayTool(WorkerBody body)
    {
        ItemDrop.ItemData? item = _equipped;
        _equipped = null;
        Humanoid? humanoid = body.Humanoid;
        if (item == null || humanoid == null)
        {
            return;
        }

        if (humanoid.IsItemEquiped(item))
        {
            humanoid.UnequipItem(item, triggerEquipEffects: false);
        }
    }
}
