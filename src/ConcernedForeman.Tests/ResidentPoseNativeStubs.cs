// Only the native boundary is stubbed; tests compile the shipped ResidentPose.
namespace UnityEngine
{
    public enum AnimatorControllerParameterType { Float, Int, Bool, Trigger }
    public sealed class AnimatorControllerParameter
    {
        public string name = "";
        public AnimatorControllerParameterType type;
    }
    public sealed class Animator
    {
        public bool applyRootMotion;
        public AnimatorControllerParameter[] parameters = System.Array.Empty<AnimatorControllerParameter>();
        public readonly System.Collections.Generic.Dictionary<string, bool> Values = new();
        public int Writes;
        public void SetBool(string name, bool value) { Writes++; Values[name] = value; }
    }
}
namespace TheConcernedCat.ConcernedNPC.Body
{
    public readonly struct NpcPresentationFigure
    {
        public NpcPresentationFigure(UnityEngine.Animator? animator, bool built = true) { Animator = animator; IsBuilt = built; }
        public UnityEngine.Animator? Animator { get; }
        public bool IsBuilt { get; }
    }
}
