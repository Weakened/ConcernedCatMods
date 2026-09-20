using System;
using System.Collections.Generic;

// The engine half of the game surface the linked library sources compile
// against. VanillaStubs.cs carries the Valheim half and JotunnStubs.cs the
// mod-framework half.
//
// WHY THIS EXISTS. Everything under src/ConcernedNPC/Body touches the game, and
// it is the half of this package that can lose a player's saved body and
// everything in it. A test project cannot reference the game assemblies - they
// are never in this repository and no runner has them - and engine objects
// cannot be created outside the player, so the game is what gets stubbed. The
// files under test are the SHIPPED sources, linked by the project file, never
// copies of them: the same text compiles here against these stubs and, in
// verify.ps1, against the real assemblies.
//
// WHAT IT IS NOT. It re-implements nothing that is under test. Where a stub had
// to choose a behaviour it reproduces what the shipped products' own comments
// and tests record about the installed build - including the quirks, because a
// stub without the quirk is a test of something else. Two are load-bearing
// here: the iterative object scan repeats its first sector on the terminating
// call, and a destroyed object compares equal to null.
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

        public float sqrMagnitude => (x * x) + (y * y) + (z * z);

        public float magnitude => (float)Math.Sqrt(sqrMagnitude);

        public Vector3 normalized
        {
            get
            {
                float length = magnitude;
                return length <= 0f ? zero : new Vector3(x / length, y / length, z / length);
            }
        }

        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);

        public override string ToString() => "(" + x + ", " + y + ", " + z + ")";
    }

    public struct Quaternion
    {
        public static Quaternion identity => default;
    }

    /// <summary>An engine object: a name, and the null comparison the engine
    /// gives a destroyed one. A destroyed stub answers true to <c>== null</c>,
    /// which is what the shipped null checks mean and is the whole mechanism
    /// the presentation extraction's refusal path turns on.</summary>
    public class Object
    {
        public string name = string.Empty;

        /// <summary>Set by a test: the engine refuses to destroy this one, the
        /// way it refuses a component another component declares a dependency
        /// on - by writing to the log and carrying on, never by throwing.
        ///
        /// A field rather than a property so an instantiated copy inherits it,
        /// the way a real component's serialized state is inherited.</summary>
        public bool Indestructible;

        internal bool Destroyed { get; private set; }

        public static void Destroy(Object? target)
        {
            if (target != null && !target.Indestructible)
            {
                target.Kill();
            }
        }

        public static void DestroyImmediate(Object? target)
        {
            if (target is null || target.Indestructible)
            {
                return;
            }

            target.Kill();
        }

        public static GameObject Instantiate(GameObject? original, Transform parent)
        {
            if (original is null)
            {
                throw new ArgumentNullException(nameof(original));
            }

            GameObject copy = original.Clone();
            copy.transform.SetParent(parent, worldPositionStays: false);
            return copy;
        }

        public static GameObject Instantiate(GameObject? original, Vector3 position, Quaternion rotation)
        {
            if (original is null)
            {
                throw new ArgumentNullException(nameof(original));
            }

            GameObject copy = original.Clone();
            copy.transform.position = position;
            return copy;
        }

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

        internal virtual void Kill() => Destroyed = true;
    }

    public class Transform : Object
    {
        private readonly List<Transform> _children = new List<Transform>();

        internal Transform(GameObject owner)
        {
            GameObject = owner;
        }

        public GameObject GameObject { get; }

        public Transform? parent { get; private set; }

        public Vector3 position { get; set; }

        public Vector3 localPosition { get; set; }

        public Quaternion localRotation { get; set; }

        public Vector3 forward { get; set; } = new Vector3(0f, 0f, 1f);

        public Vector3 lossyScale { get; set; } = new Vector3(1f, 1f, 1f);

        public IReadOnlyList<Transform> Children => _children;

        public void SetParent(Transform? next, bool worldPositionStays)
        {
            parent?._children.Remove(this);
            parent = next;
            next?._children.Add(this);
        }

        public bool IsChildOf(Transform? ancestor)
        {
            for (Transform? here = this; here != null; here = here.parent)
            {
                if (ReferenceEquals(here, ancestor))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public class GameObject : Object
    {
        private readonly List<Component> _components = new List<Component>();

        public GameObject(string name = "")
        {
            this.name = name;
            transform = new Transform(this);
        }

        public Transform transform { get; }

        public bool activeSelf { get; private set; } = true;

        public void SetActive(bool active) => activeSelf = active;

        /// <summary>Adds an existing component. The test's own way in; the
        /// shipped sources use the generic overload below.</summary>
        public T Add<T>(T component)
            where T : Component
        {
            component.Attach(this);
            _components.Add(component);
            return component;
        }

        public T AddComponent<T>()
            where T : Component, new() => Add(new T());

        // Returns a non-nullable T, matching the unannotated game assemblies:
        // the shipped sources assign the result straight to a non-nullable
        // local and then null-check it with the engine's own operator, and a
        // stub that annotated it would raise warnings the real build does not.
        public T GetComponent<T>()
            where T : class
        {
            foreach (Component component in _components)
            {
                if (component is T match && component != null)
                {
                    return match;
                }
            }

            return null!;
        }

        public T GetComponentInParent<T>()
            where T : class
        {
            for (Transform? here = transform; here != null; here = here.parent)
            {
                T found = here.GameObject.GetComponent<T>();
                if (found != null)
                {
                    return found;
                }
            }

            return null!;
        }

        public T GetComponentInChildren<T>(bool includeInactive = false)
            where T : class
        {
            foreach (T match in GetComponentsInChildren<T>(includeInactive))
            {
                return match;
            }

            return null!;
        }

        public T[] GetComponentsInChildren<T>(bool includeInactive = false)
            where T : class
        {
            var found = new List<T>();
            Collect(this, includeInactive, found);
            return found.ToArray();
        }

        /// <summary>A copy of this object and everything under it, with
        /// references that pointed inside the original pointing inside the
        /// copy - which is what the engine's own instantiation does, and what
        /// the presentation extraction's joint handling depends on. Only the
        /// root takes the suffix, as the engine does.</summary>
        internal GameObject Clone()
        {
            var map = new Dictionary<Object, Object>();
            GameObject copy = CloneInto(map);
            copy.name = name + "(Clone)";

            foreach (KeyValuePair<Object, Object> pair in map)
            {
                if (pair.Value is Component component)
                {
                    Remap(component, map);
                }
            }

            return copy;
        }

        private GameObject CloneInto(Dictionary<Object, Object> map)
        {
            var copy = new GameObject(name);
            map[this] = copy;
            map[transform] = copy.transform;

            foreach (Component component in _components)
            {
                if (component != null)
                {
                    map[component] = copy.Add(component.CloneComponent());
                }
            }

            foreach (Transform child in transform.Children)
            {
                GameObject childCopy = child.GameObject.CloneInto(map);
                childCopy.transform.SetParent(copy.transform, worldPositionStays: false);
            }

            return copy;
        }

        private static void Remap(Component component, Dictionary<Object, Object> map)
        {
            foreach (System.Reflection.FieldInfo field in component.GetType().GetFields(
                         System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            {
                if (field.GetValue(component) is Object pointed && map.TryGetValue(pointed, out Object? copy))
                {
                    field.SetValue(component, copy);
                }
            }
        }

        private static void Collect<T>(GameObject here, bool includeInactive, List<T> found)
            where T : class
        {
            if (!includeInactive && !here.activeSelf)
            {
                return;
            }

            foreach (Component component in here._components)
            {
                if (component is T match && component != null)
                {
                    found.Add(match);
                }
            }

            foreach (Transform child in here.transform.Children)
            {
                Collect(child.GameObject, includeInactive, found);
            }
        }

        internal override void Kill()
        {
            base.Kill();
            foreach (Component component in _components)
            {
                component.Kill();
            }
        }
    }

    public class Component : Object
    {
        private GameObject? _gameObject;

        public GameObject gameObject => _gameObject ??= new GameObject();

        public Transform transform => gameObject.transform;

        internal void Attach(GameObject owner) => _gameObject = owner;

        /// <summary>A component of the same runtime type with the same public
        /// fields, which is what the engine's own instantiation copies.
        ///
        /// Fields only, and shallow. Properties are deliberately not copied,
        /// so each instance gets its own network object and its own inventory
        /// exactly as it would in the game - and so a test cannot accidentally
        /// prove something about serialization that only the real engine could
        /// decide.</summary>
        internal virtual Component CloneComponent()
        {
            var copy = (Component)Activator.CreateInstance(GetType())!;
            foreach (System.Reflection.FieldInfo field in
                     GetType().GetFields(System.Reflection.BindingFlags.Instance
                         | System.Reflection.BindingFlags.Public))
            {
                field.SetValue(copy, field.GetValue(this));
            }

            return copy;
        }

        public T GetComponent<T>()
            where T : class => gameObject.GetComponent<T>();

        public T GetComponentInParent<T>()
            where T : class => gameObject.GetComponentInParent<T>();

        public T GetComponentInChildren<T>(bool includeInactive = false)
            where T : class => gameObject.GetComponentInChildren<T>(includeInactive);

        public T[] GetComponentsInChildren<T>(bool includeInactive = false)
            where T : class => gameObject.GetComponentsInChildren<T>(includeInactive);
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; } = true;
    }

    public class MonoBehaviour : Behaviour
    {
        public List<string> Cancelled { get; } = new List<string>();

        public void CancelInvoke(string methodName) => Cancelled.Add(methodName);

        public void InvokeRepeating(string methodName, float time, float repeatRate)
        {
        }
    }

    public class Rigidbody : Component
    {
        public float mass { get; set; } = 70f;

        public bool isKinematic { get; set; }

        public bool useGravity { get; set; } = true;
    }

    public class Collider : Component
    {
    }

    public class Renderer : Component
    {
    }

    public class SkinnedMeshRenderer : Renderer
    {
    }

    public class Animator : Component
    {
    }

    public static class Time
    {
        public static float fixedDeltaTime { get; set; } = 0.05f;

        public static float time { get; set; }
    }

    public static class Mathf
    {
        public static float Clamp(float value, float low, float high) =>
            value < low ? low : value > high ? high : value;
    }
}
