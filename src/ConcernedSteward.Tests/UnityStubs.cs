using System;
using System.Collections.Generic;

// The engine half of the game surface the linked Steward adapters compile
// against. VanillaStubs.cs carries the Valheim half.
//
// WHY THIS EXISTS. The adapters are the only Steward code that touches the
// game, and one of them -- WorldFuelTargets -- is the only code in the product
// that can destroy a player's item. A test project cannot reference the game
// assemblies (they are never in this repository, and CI has no game) and Unity
// types cannot be instantiated outside the player, so the game is what gets
// stubbed.
//
// WHAT IT IS NOT. It does not re-implement anything under test. The ports, the
// fireplace adapter, the body's persistence and the census are the real shipped
// sources, linked by the project file. Only the engine objects they act on are
// modelled here, and only as far as the behaviour the adapters actually rely
// on. Where a stub had to choose, it chooses what the decompile of the
// installed 1.0.14 build shows (assembly_valheim.dll SHA-256
// f6499816...c8017fb6) -- see docs/mods/concerned-steward/VALHEIM_FIRE_API_AUDIT.md.
namespace UnityEngine
{
    public struct Vector3
    {
        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public float x;
        public float y;
        public float z;

        public static Vector3 zero => new Vector3(0f, 0f, 0f);

        public float magnitude => (float)Math.Sqrt((x * x) + (y * y) + (z * z));

        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);

        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;

        public override string ToString() => "(" + x + ", " + y + ", " + z + ")";
    }

    /// <summary>A Unity object: a name, and the null-comparison Unity gives a
    /// destroyed one. A destroyed stub answers true to <c>== null</c>, which is
    /// what the adapters' null checks mean.</summary>
    public class Object
    {
        public string name = string.Empty;

        internal bool Destroyed { get; private set; }

        public static void Destroy(Object target) => target.Destroyed = true;

        public static bool operator ==(Object? left, Object? right)
        {
            bool leftGone = left is null || left.Destroyed;
            bool rightGone = right is null || right.Destroyed;
            return leftGone && rightGone ? true : ReferenceEquals(left, right);
        }

        public static bool operator !=(Object? left, Object? right) => !(left == right);

        public override bool Equals(object? obj) => ReferenceEquals(this, obj);

        public override int GetHashCode() =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }

    public class Transform : Object
    {
        public Vector3 position;
    }

    public class GameObject : Object
    {
        private readonly List<Component> _components = new List<Component>();

        public GameObject(string name = "")
        {
            this.name = name;
            transform = new Transform();
        }

        public Transform transform { get; }

        public GameObject? Parent { get; set; }

        public T Add<T>(T component)
            where T : Component
        {
            component.Attach(this);
            _components.Add(component);
            return component;
        }

        public T? GetComponent<T>()
            where T : class
        {
            foreach (Component component in _components)
            {
                if (component is T match)
                {
                    return match;
                }
            }

            return null;
        }

        public T? GetComponentInParent<T>()
            where T : class
        {
            for (GameObject? here = this; here != null; here = here.Parent)
            {
                T? found = here.GetComponent<T>();
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }

    public class Component : Object
    {
        private GameObject? _gameObject;

        public GameObject gameObject => _gameObject ??= new GameObject();

        public Transform transform => gameObject.transform;

        internal void Attach(GameObject owner) => _gameObject = owner;

        public T? GetComponent<T>()
            where T : class => gameObject.GetComponent<T>();

        public T? GetComponentInParent<T>()
            where T : class => gameObject.GetComponentInParent<T>();
    }

    /// <summary>Only the three helpers the adapters and the Fireplace stub
    /// need, with Unity's own semantics.</summary>
    public static class Mathf
    {
        public static int CeilToInt(float value) => (int)Math.Ceiling((double)value);

        public static float Clamp(float value, float low, float high) =>
            value < low ? low : value > high ? high : value;

        public static int Min(int a, int b) => a < b ? a : b;
    }

    public class MonoBehaviour : Component
    {
    }
}
