namespace TheConcernedCat.ConcernedCartographer.Runtime;

internal static class WorldContext
{
    public static bool TryGetWorldUid(out long uid)
    {
        uid = 0L;
        if (ZNet.instance is null)
        {
            return false;
        }

        uid = ZNet.instance.GetWorldUID();
        return uid != 0L;
    }
}
