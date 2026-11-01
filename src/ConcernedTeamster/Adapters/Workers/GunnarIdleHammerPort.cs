using System;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>Gunnar's idle hammer swing (#381, owner decision 2026-10-31,
/// <c>DECISIONS.md</c> D19): the one vanilla call that makes
/// <c>CartUpkeepIdle</c> visible.
///
/// <b>One call, on his own body, of vanilla's own animation.</b>
/// <c>ZSyncAnimation.SetTrigger</c> on the <c>ZSyncAnimation</c> of the body
/// carrying <see cref="TeamsterWorkerAI"/> - which only Gunnar's worker prefab
/// carries - with the trigger name the vanilla Hammer item itself plays, read
/// from the item database (<c>m_shared.m_attack.m_attackAnimation</c>), exactly
/// as vanilla's own build and repair swing reads it from the hammer in the
/// player's hand. The product invents no animation name: a body that cannot play
/// the hammer's own trigger does nothing.
///
/// <b>What it is and is not.</b> An animation trigger is transient and unsaved:
/// inside vanilla it is passed to every client's animator and is gone the moment
/// it has played. It repairs nothing, consumes nothing, restores no hit points,
/// grants no buff, writes no network-object field, touches no cart and reads no
/// inventory. <c>tools/validate_repo.py</c> pins the call verbatim and once in
/// this file, refuses <c>.SetTrigger(</c> everywhere else in Teamster and in the
/// shared trees it compiles in, and pins the two lines that decide whose animator
/// it is and where the name comes from.
///
/// <b>Fails closed.</b> No body, a dead or unowned one, no animator sync, no
/// Hammer in the item database, an empty animation name, or an animator that
/// has no trigger of that name: nothing is played, and the reason is logged
/// once. Whether the body Gunnar is cloned from (<c>Dverger</c> by default)
/// carries the hammer's trigger at all could not be established without running
/// the game; if it does not, this answers false every time and says so in the
/// log, and he walks to his cart, faces it and stands there.</summary>
internal sealed class GunnarIdleHammerPort
{
    /// <summary>The vanilla item whose own swing he plays. A prefab name is data,
    /// not an API: if this build has no such item, the port refuses.</summary>
    internal const string HammerItem = "Hammer";

    private readonly Action<string> _report;
    private string _lastRefusal = string.Empty;

    internal GunnarIdleHammerPort(Action<string> report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
    }

    /// <summary>How many swings vanilla was asked to play this session.</summary>
    internal int Played { get; private set; }

    /// <summary>Plays the vanilla hammer swing once on Gunnar's own body.
    /// Answers whether it was asked for.</summary>
    internal bool PlayOnce(TeamsterWorkerAI? gunnar)
    {
        if (gunnar == null)
        {
            return Refuse("no body is bound");
        }

        Character body = gunnar.GetComponent<Character>();
        if (body == null || body.IsDead())
        {
            return Refuse("his body is not a living character");
        }

        ZNetView view = body.m_nview;
        if (view == null || !view.IsValid() || !view.IsOwner())
        {
            return Refuse("this game does not own his body");
        }

        ZSyncAnimation animation = body.m_zanim;
        if (animation == null)
        {
            return Refuse("his body has no animation sync");
        }

        string swing = HammerSwing();
        if (swing.Length == 0)
        {
            return Refuse("the vanilla " + HammerItem + "'s own swing could not be read from the item database");
        }

        if (!animation.HasParameter(swing, AnimatorControllerParameterType.Trigger))
        {
            return Refuse("his body's animator has no '" + swing + "' trigger, so there is no hammer swing for it to play");
        }

        animation.SetTrigger(swing);
        Played++;
        _lastRefusal = string.Empty;
        return true;
    }

    /// <summary>The trigger the vanilla Hammer item plays when it is swung, read
    /// from the item database, or empty when it cannot be read.</summary>
    private static string HammerSwing()
    {
        ObjectDB database = ObjectDB.instance;
        GameObject? prefab = database != null ? database.GetItemPrefab(HammerItem) : null;
        ItemDrop? item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        ItemDrop.ItemData? data = item != null ? item.m_itemData : null;
        ItemDrop.ItemData.SharedData? shared = data != null ? data.m_shared : null;
        Attack? attack = shared != null ? shared.m_attack : null;
        string? name = attack != null ? attack.m_attackAnimation : null;
        return string.IsNullOrEmpty(name) ? string.Empty : name!;
    }

    private bool Refuse(string why)
    {
        if (!string.Equals(why, _lastRefusal, StringComparison.Ordinal))
        {
            _lastRefusal = why;
            _report("Gunnar's idle hammer swing is not played: " + why + ".");
        }

        return false;
    }
}
