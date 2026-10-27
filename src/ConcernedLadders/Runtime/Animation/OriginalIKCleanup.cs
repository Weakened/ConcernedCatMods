using System;
using HarmonyLib;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Runtime.Animation;

/// <summary>A bounded per-rig cleanup continuation, independent of a disposed presenter. A non-evaluating
/// rig owns its saved IK input values until its next native pass or destruction; it never retains the old
/// presenter/player, blocks a new Install, or writes IK from Update/OnDisable/OnDestroy.</summary>
internal sealed class OriginalIKCleanup : MonoBehaviour
{
    private const string Owner = "com.theconcernedcat.valheim.concernedladders.original-ik-cleanup";
    private static Harmony? _hook;
    private static int _count;
    private NativeIKLease? _lease;
    private bool _registered;

    internal static void Schedule(Animator animator, NativeIKLease lease)
    {
        if (animator == null || !lease.Pending) { return; }
        Harmony? created = null;
        try
        {
            if (_hook == null)
            {
                var target = AccessTools.Method(typeof(CharacterAnimEvent), "OnAnimatorIK", new[] { typeof(int) });
                if (target == null) { throw new InvalidOperationException("Native IK cleanup callback disappeared."); }
                created = new Harmony(Owner);
                var prefix = new HarmonyMethod(typeof(OriginalIKCleanup), nameof(BeforeNativeIK)) { priority = Priority.First };
                created.Patch(target, prefix: prefix);
                _hook = created;
            }
            OriginalIKCleanup cleanup = animator.GetComponent<OriginalIKCleanup>();
            if (cleanup == null) { cleanup = animator.gameObject.AddComponent<OriginalIKCleanup>(); }
            if (cleanup._lease != null && cleanup._lease.Pending)
            { throw new InvalidOperationException("A previous rig cleanup must complete before a second lease."); }
            cleanup._lease = lease;
            if (!cleanup._registered) { cleanup._registered = true; _count++; }
        }
        catch
        {
            // First registration is transactional. A failed Patch/AddComponent cannot leave a zero-count
            // hook that makes the next schedule skip installation. Existing continuations stay registered.
            if (_count == 0)
            {
                try { (_hook ?? created)?.UnpatchSelf(); } catch { }
                _hook = null;
            }
            throw;
        }
    }

    private static void BeforeNativeIK(CharacterAnimEvent __instance, int layerIndex)
    {
        if (layerIndex != 0) { return; }
        OriginalIKCleanup cleanup = __instance.GetComponent<OriginalIKCleanup>();
        if (cleanup == null || cleanup._lease == null) { return; }
        Animator animator = __instance.GetComponent<Animator>();
        if (animator == null) { return; }
        // Exceptions are contained at this callback: no gameplay failure or illegal out-of-pass setter.
        try { cleanup._lease.RestoreInIK(animator); }
        catch { return; } //retains only this bounded rig continuation for another native pass or destruction.
        cleanup._lease = null;
        cleanup.Unregister();
        // Keep an inert rig-local marker until rig destruction. Destroy is deferred by Unity; reusing
        // a component already queued for destruction could otherwise discard a new same-frame lease.
    }

    private void OnDestroy() { _lease = null; Unregister(); }

    private void Unregister()
    {
        if (!_registered) { return; }
        _registered = false; _count = Math.Max(0, _count - 1);
        if (_count != 0) { return; }
        try { _hook?.UnpatchSelf(); } catch { }
        _hook = null;
    }
}
