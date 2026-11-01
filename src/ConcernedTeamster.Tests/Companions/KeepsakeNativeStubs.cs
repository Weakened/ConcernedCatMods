// Test doubles only. No game assembly or asset is copied into this project.
namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 up=>new(0,1,0); public static Vector3 down=>new(0,-1,0);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator *(Vector3 a,float s)=>new(a.x*s,a.y*s,a.z*s);
    }
    public sealed class Transform { public Vector3 position; }
    public sealed class GameObject { public bool activeInHierarchy=true; public string name=""; public Transform transform=new(); }
    public class MonoBehaviour { public GameObject gameObject=new(); public Transform transform=>gameObject.transform; }
    public sealed class Rigidbody { }
    public sealed class Collider
    {
        public GameObject gameObject=new(); public Rigidbody? attachedRigidbody;
        public object[] Parents=Array.Empty<object>();
        public T? GetComponentInParent<T>() where T:class => Parents.OfType<T>().FirstOrDefault();
        public void GetComponentsInParent<T>(bool includeInactive,List<T> results) { results.AddRange(Parents.OfType<T>()); }
    }
    public struct RaycastHit { public Collider collider; public Vector3 point,normal; }
    public enum QueryTriggerInteraction { Ignore,Collide }
    public static class LayerMask { public static int Mask=1; public static int GetMask(params string[] names)=>Mask; }
    public static class Physics
    {
        public static Func<float,Collider[]> Overlaps=_=>Array.Empty<Collider>();
        public static RaycastHit[] Hits=Array.Empty<RaycastHit>(); public static bool Blocked;
        public static int OverlapSphereNonAlloc(Vector3 p,float radius,Collider[] buffer,int mask,QueryTriggerInteraction query)
        { var all=Overlaps(radius); int count=Math.Min(all.Length,buffer.Length); Array.Copy(all,buffer,count); return count; }
        public static int RaycastNonAlloc(Vector3 origin,Vector3 direction,RaycastHit[] buffer,float distance,int mask,QueryTriggerInteraction query)
        { int count=Math.Min(Hits.Length,buffer.Length); Array.Copy(Hits,buffer,count); return count; }
        public static bool CheckSphere(Vector3 p,float radius,int mask,QueryTriggerInteraction query)=>Blocked;
    }
}
public sealed class Game
{
    public static Game? instance; public string m_StartLocation="actual-start-name"; public PlayerProfile Profile=new();
    public PlayerProfile GetPlayerProfile()=>Profile;
}
public sealed class PlayerProfile
{
    public bool Custom; public UnityEngine.Vector3 Spawn;
    public bool HaveCustomSpawnPoint()=>Custom; public UnityEngine.Vector3 GetCustomSpawnPoint()=>Spawn;
}
public sealed class ZoneSystem
{
    public static ZoneSystem? instance; public float m_waterLevel=-10;
    public bool Loaded=true,StartFound=true; public UnityEngine.Vector3 Start=new(20,0,20);
    public bool IsZoneLoaded(UnityEngine.Vector3 p)=>Loaded;
    public bool GetLocationIcon(string actualName,out UnityEngine.Vector3 point) { point=Start; return StartFound; }
}
public sealed class Bed { public bool Current; public bool IsCurrent()=>Current; }
namespace TheConcernedCat.ConcernedNPC.Body.Keepsakes { public sealed class NpcKeepsakeBehaviour:UnityEngine.MonoBehaviour { } }
namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions { public sealed class BrokenCompassObject:UnityEngine.MonoBehaviour { } }
