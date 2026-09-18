using System;
using System.Collections.Generic;

// The vanilla surface the linked Foreman sources compile against, small enough
// to read in one sitting and shaped after the installed Valheim 1.0.14 build
// (assembly_valheim.dll SHA-256 f6499816…c8017fb6), as decompiled for the #316
// audits and confirmed by review R2.
//
// WHY THIS EXISTS. The adapters are the only Foreman code that touches the
// game, and no test project compiled them: review R2's M4. A test project
// cannot reference the game assemblies (they are never in this repository, and
// CI has no game), and Unity types cannot be instantiated outside the player,
// so the game itself is what gets stubbed here.
//
// WHAT IT IS NOT. It does not re-implement anything under test. The ports, the
// worker body's persistence, the tool classifier and the container identity are
// the real shipped sources, linked by the project file. Only the inventories,
// components and network objects they act on are modelled here, and only as far
// as the behaviour the adapters rely on:
//
//   - Inventory.AddItem merges into a stack with the same shared name, quality,
//     world level and cheated flag, decrementing the incoming stack as it
//     merges, and then uses an empty slot (decomp/Inventory.cs:112-141).
//   - Inventory.MoveItemToThis removes from the source only when AddItem
//     returned true, so a partial merge that then fails leaves the remainder at
//     the source (decomp/Inventory.cs:283-288).
//   - Every mutation raises Changed(), which is how Container - and the worker
//     body - learn to save.
//
// Anything the adapters do not use is left out on purpose rather than guessed
// at. Where a stub had to choose, it chooses the behaviour the decompile shows.
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

        public override string ToString() => "(" + x + ", " + y + ", " + z + ")";
    }

    /// <summary>A Unity object: a name, and the null-comparison Unity gives a
    /// destroyed object. Destroyed stubs answer true to <c>== null</c>, which is
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

        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
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

    public class MonoBehaviour : Component
    {
    }
}
