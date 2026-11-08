// Only the physics/native boundary is stubbed; tests link the shipped probe.
namespace UnityEngine
{
    public class Rigidbody : Component { }
    public class Collider : Component { public Rigidbody? attachedRigidbody; }
    public struct Bounds
    {
        public Vector3 center,size;
        public Bounds(Vector3 centre,Vector3 extent) { center=centre; size=extent; }
        public Vector3 min=>new(center.x-size.x*.5f,center.y-size.y*.5f,center.z-size.z*.5f);
    }
    public class CapsuleCollider : Collider
    {
        public bool enabled=true; public int direction=1;
        public Bounds bounds=new(new Vector3(0,.9f,0),new Vector3(.7f,1.8f,.7f));
    }
    public struct RaycastHit { public Collider collider; public Vector3 point,normal; public float distance; }
    public enum QueryTriggerInteraction { Ignore }
    public static class LayerMask
    {
        public static int Mask=1; public static string[] Requested=System.Array.Empty<string>();
        public static int GetMask(params string[] names) { Requested=names; return Mask; }
    }
    public static class Physics
    {
        public static System.Func<Vector3,RaycastHit[]> Rays=_=>System.Array.Empty<RaycastHit>();
        public static System.Func<Collider[]> Overlaps=()=>System.Array.Empty<Collider>();
        public static System.Func<Vector3,RaycastHit[]> Sweeps=_=>System.Array.Empty<RaycastHit>();
        public static int RaycastNonAlloc(Vector3 origin,Vector3 direction,RaycastHit[] buffer,float distance,int mask,QueryTriggerInteraction trigger)
            =>Copy(Rays(origin),buffer);
        public static int OverlapCapsuleNonAlloc(Vector3 low,Vector3 high,float radius,Collider[] buffer,int mask,QueryTriggerInteraction trigger)
            =>Copy(Overlaps(),buffer);
        public static int CapsuleCastNonAlloc(Vector3 low,Vector3 high,float radius,Vector3 direction,RaycastHit[] buffer,float distance,int mask,QueryTriggerInteraction trigger)
            =>Copy(Sweeps(direction),buffer);
        private static int Copy<T>(T[] from,T[] to) { int count=System.Math.Min(from.Length,to.Length); System.Array.Copy(from,to,count); return count; }
    }
}
public class Door : UnityEngine.MonoBehaviour { }
namespace TheConcernedCat.ConcernedForeman.Runtime.Settlement { internal class ForemanWorkerAI : UnityEngine.MonoBehaviour { } }
