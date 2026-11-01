namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;

/// <summary>Only the current character/world may be returned. Writes happen
/// synchronously in the record, so logout needs no delayed flush.</summary>
internal sealed class GunnarCompanionSession
{
    private readonly string _directory;
    private GunnarCompanionRecord? _current;
    public GunnarCompanionSession(string directory) { _directory = directory; }

    public GunnarCompanionRecord? Resolve(long world, long character)
    {
        if (world == 0 || character == 0)
        {
            Forget();
            return null;
        }
        if (_current == null || _current.Scope.World.Value != world ||
            _current.Scope.Character.Value != character)
        {
            Forget();
            _current = GunnarCompanionRecord.Open(_directory, world, character);
        }
        return _current;
    }

    public void Forget() { _current?.Close(); _current = null; }
}
