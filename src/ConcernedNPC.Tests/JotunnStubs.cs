using System;
using System.Collections.Generic;
using UnityEngine;

// The mod-framework half of the game surface. See UnityStubs.cs for why.
//
// Only the prefab manager is modelled, and only the six members the worker
// prefab build uses. It records the two calls the factory makes separately so a
// test can say that both happened.
//
// WHAT THIS STUB CANNOT MODEL, stated because an earlier version of this comment
// claimed the opposite. In the shipped framework the two AddPrefab overloads are
// the SAME call - one constructs the wrapper and delegates to the other - and
// neither registers anything into the network scene. What does that is a postfix
// the framework puts on the scene's own wake-up, which walks every prefab it
// knows and registers all of them on every world load. Modelling that would mean
// modelling a Harmony patch on a type this file does not have, so the stub does
// not try. It therefore proves that the factory makes both calls and nothing at
// all about what either call achieves; the reason the second one is kept is in
// the factory's own comment.
namespace Jotunn.Managers
{
    public class PrefabManager
    {
        private readonly Dictionary<string, GameObject> _prefabs =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);

        public static PrefabManager Instance { get; set; } = null!;

        public static event Action? OnVanillaPrefabsAvailable;

        /// <summary>Prefabs added to the framework's own table.</summary>
        public List<string> Added { get; } = new List<string>();

        /// <summary>Prefabs the explicit scene-registration call was made for.
        /// Recorded so a test can assert the call happened - not because the
        /// call is what decides whether a saved object survives, which it is
        /// not (see the header).</summary>
        public List<string> RegisteredToScene { get; } = new List<string>();

        /// <summary>Set by a test: cloning refuses, the way it does when the
        /// name is already taken.</summary>
        public bool CloningRefuses { get; set; }

        public static void RaiseVanillaPrefabsAvailable() => OnVanillaPrefabsAvailable?.Invoke();

        /// <summary>Drops every subscriber, so one test's factory does not
        /// still be listening during the next one's.</summary>
        public static void ResetSubscribersForTests() => OnVanillaPrefabsAvailable = null;

        public static int SubscriberCount =>
            OnVanillaPrefabsAvailable == null ? 0 : OnVanillaPrefabsAvailable.GetInvocationList().Length;

        public void Preload(string name, GameObject prefab) => _prefabs[name] = prefab;

        public GameObject? GetPrefab(string name) =>
            _prefabs.TryGetValue(name, out GameObject? prefab) ? prefab : null;

        public GameObject? CreateClonedPrefab(string name, GameObject basePrefab)
        {
            if (CloningRefuses || basePrefab == null)
            {
                return null;
            }

            GameObject clone = UnityEngine.Object.Instantiate(basePrefab, Vector3.zero, Quaternion.identity);
            clone.name = name;
            clone.SetActive(false);
            return clone;
        }

        public void AddPrefab(Jotunn.Entities.CustomPrefab prefab)
        {
            Added.Add(prefab.Prefab.name);
            _prefabs[prefab.Prefab.name] = prefab.Prefab;
        }

        public void AddPrefab(GameObject prefab)
        {
            Added.Add(prefab.name);
            _prefabs[prefab.name] = prefab;
        }

        public void RegisterToZNetScene(GameObject prefab) => RegisteredToScene.Add(prefab.name);

        public void DestroyPrefab(string name) => _prefabs.Remove(name);
    }
}

namespace Jotunn.Entities
{
    public class CustomPrefab
    {
        public CustomPrefab(GameObject prefab, bool fixReference)
        {
            Prefab = prefab;
            FixReference = fixReference;
        }

        public GameObject Prefab { get; }

        public bool FixReference { get; }
    }
}
