using System;
using System.Reflection;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.ConcernedSteward.Domain;
using TheConcernedCat.ConcernedSteward.Domain.Npc;
using TheConcernedCat.ConcernedSteward.Runtime;
using UnityEngine;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>Her body as the game builds it (#372, F2e): Concerned NPC's
/// <see cref="NpcBody"/> - the shipped library file, linked - on an object
/// named the way the host names an instance of her prefab, with her keys
/// registered first, as the prefab build does before any world loads.
/// </summary>
internal static class StewardBodyFixtures
{
    /// <summary>What the host calls an instance of her prefab. The body finds
    /// its keys by it.</summary>
    internal const string Instance = StewardRole.BodyPrefabName + "(Clone)";

    /// <summary>What the prefab build does: tells Concerned NPC what bodies of
    /// her prefab store. Idempotent.</summary>
    internal static void Register() =>
        Assert.True(StewardSavedBodies.TryRegister(out string reason), reason);

    /// <summary>Unity calls <c>Start</c>; outside the player nothing does, so
    /// the test does. Reflection rather than a wider member on the shipped
    /// type.</summary>
    internal static void Start(NpcBody body)
    {
        Register();
        typeof(NpcBody).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(body, Array.Empty<object>());
    }

    /// <summary>Forgets her live bodies, as a world unload does.</summary>
    internal static void Forget()
    {
        Register();
        NpcBody.ForgetAll(StewardNpcRole.Contract);
    }
}
