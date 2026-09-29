namespace TheConcernedCat.ConcernedTeamster.Zz;

internal sealed class ZzCarveoutProbe
{
    internal void Probe(object cart, object who)
    {
        ((dynamic)cart).AddItem(who);
    }
}
