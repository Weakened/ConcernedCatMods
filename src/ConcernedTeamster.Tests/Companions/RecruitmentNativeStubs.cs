using UnityEngine;
// Native boundary only. The actual shipped cart reader is linked into this project.
public sealed class Player : MonoBehaviour
{
    public static Player? m_localPlayer;
    public GameObject? Hover;
    public GameObject? GetHoverObject()=>Hover;
}
public sealed class Catapult { }
public sealed class SiegeMachine { }
public sealed class Vagon : MonoBehaviour
{
    public ZNetView? m_nview=new(); public string m_name="$piece_cart";
    public object? m_attachJoin; public Container? m_container; public Chair? m_chair;
    public bool Used; public object? Kind;
    public bool InUse()=>Used;
    public T? GetComponent<T>() where T:class=>Kind as T;
}
public sealed class Container { public bool Used; public bool IsInUse()=>Used; }
public sealed class Chair { public bool Used; public bool IsInUse()=>Used; }
public sealed class ZNetView
{
    public bool Valid=true,Owner=true; public ZDO Data=new();
    public bool IsValid()=>Valid; public bool IsOwner()=>Owner;
    public ZDO GetZDO()=>Data;
}
public sealed class ZDO { public string m_uid="cart-one"; public bool Attached; public bool GetBool(int key)=>Attached; }
public static class ZDOVars { public const int s_attachJointHash=1; }
public sealed class Localization
{
    public static Localization? instance=new();
    public string Localize(string name)=>name=="$piece_cart"?"Cart":name;
}
namespace UnityEngine
{
    public static class RecruitmentHoverStub
    {
        public static readonly System.Collections.Generic.Dictionary<GameObject,object> Targets=new();
        public static T? GetComponentInParent<T>(this GameObject host) where T:class=>Targets.TryGetValue(host,out var value)?value as T:null;
    }
}
