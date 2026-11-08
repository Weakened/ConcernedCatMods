using TheConcernedCat.Companions.Quest;
namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;

internal enum GunnarBodyKind { None, Presentation, Worker }
internal readonly struct GunnarBodyFacts
{
    public GunnarBodyFacts(bool registered, bool worldLoaded, bool host, bool enabled,
        QuestState? stage, int savedWorkers, bool resting, bool anchorKnown, bool near)
    { Registered=registered; WorldLoaded=worldLoaded; Host=host; Enabled=enabled; Stage=stage;
      SavedWorkers=savedWorkers; Resting=resting; AnchorKnown=anchorKnown; Near=near; }
    public bool Registered { get; }
    public bool WorldLoaded { get; }
    public bool Host { get; }
    public bool Enabled { get; }
    public QuestState? Stage { get; }
    public int SavedWorkers { get; }
    public bool Resting { get; }
    public bool AnchorKnown { get; }
    public bool Near { get; }
}
internal static class GunnarBodyPlan
{
    public static bool MaySurvey(bool registered, bool worldLoaded, bool host, bool optedIn) =>
        registered && worldLoaded && host && optedIn;
    public static GunnarBodyKind Decide(in GunnarBodyFacts f)
    {
        // An existing saved body is never replaced by a figure, even when the
        // intro is disabled. This answer does not create or retire a worker.
        if (f.SavedWorkers > 0) return GunnarBodyKind.Worker;
        if (!f.Registered || !f.WorldLoaded || !f.Host || !f.Enabled || f.SavedWorkers != 0 ||
            !f.Resting || !f.AnchorKnown || !f.Near || !f.Stage.HasValue) return GunnarBodyKind.None;
        return f.Stage == QuestState.IntroPending || f.Stage == QuestState.Recruited
            ? GunnarBodyKind.Presentation : GunnarBodyKind.None;
    }
}

/// <summary>The hand-over's arrival guard, decided game-free (#458). Between
/// the hand-over and the census counting the body it placed, his saved bodies
/// are unknown - never zero - and the guard goes only once the census has the
/// body, or has said "no body" for longer than a stale answer could last.
/// Unknown holds it.</summary>
internal static class GunnarArrival
{
    public const float PatienceSeconds = 5f;

    public static int SavedWorkers(bool handingOver, bool? present) =>
        handingOver ? -1 : present == true ? 1 : present == false ? 0 : -1;

    public static bool MayRelease(bool? present, float heldSeconds) =>
        present == true || (present == false && heldSeconds >= PatienceSeconds);
}
