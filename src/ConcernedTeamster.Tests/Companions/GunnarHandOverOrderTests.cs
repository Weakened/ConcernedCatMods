using System.Text.RegularExpressions;
namespace TheConcernedCat.ConcernedTeamster.Tests.Companions;

/// <summary>#458, modelled on Foreman's ThorsteinHandOverOrderTests: the order
/// of the hand-over in the game-bound runtime, which no test project can run.
/// The leases are tested for real in <see cref="GunnarBodyTests"/>. What those
/// tests cannot see is whether the runtime builds the figure only after its
/// lease, whether <c>ct_haul spawn</c> asks for the hand-over before the body
/// exists, and whether the plugin wires any of it at all - a runtime with no
/// caller is not a feature. So the shipped source is read.
///
/// A text check with the limits of one: it proves the calls are written in this
/// order inside these methods, not that control flow could not route around
/// them. A reviewer still reads the files.</summary>
public sealed class GunnarHandOverOrderTests
{
    private static readonly string Presenter = Read("src", "ConcernedTeamster", "Adapters", "Companions", "GunnarFigurePresenter.cs");
    private static readonly string Runtime = Read("src", "ConcernedTeamster", "Adapters", "Companions", "GunnarPresentation.cs");
    private static readonly string Hauling = Read("src", "ConcernedTeamster", "Adapters", "Workers", "GunnarHaulingRuntime.cs");
    private static readonly string Role = Read("src", "ConcernedTeamster", "Adapters", "Workers", "GunnarNpcRole.cs");
    private static readonly string Plugin = Read("src", "ConcernedTeamster", "Plugin.cs");
    private static readonly string Prefab = Read("src", "ConcernedTeamster", "Adapters", "Workers", "TeamsterWorkerPrefab.cs");

    [Fact]
    public void TheRegisteredRoleIsTheGameFreeContract()
    {
        // The test of the contract's values is GunnarBodyTests; this proves the
        // role the plugin registers is built from it and from nothing else.
        Assert.Contains("Body = Domain.Companions.GunnarBodyContract.Create();", Role);
        Assert.DoesNotContain("NpcBodyContract.ForWorker(", Role);
        // #372: the contract the body is built under is the same game-free one.
        Assert.Contains("internal static NpcBodyContract Contract { get; } = Domain.Companions.GunnarBodyContract.Create();", Role);
    }

    [Fact]
    public void TheBodyIsBuiltByTheGatedBuildUnderTheRolesContractAndTheHandOversLease()
    {
        // #372: the prefab goes in with the lease Spawn was handed and the role's
        // own contract; the validator refuses any instantiation in this product.
        string spawn = Method(Prefab, "internal static TeamsterWorkerAI? Spawn(");
        Assert.Contains("internal static TeamsterWorkerAI? Spawn(BodyLease? lease,", spawn);
        Assert.Contains("NpcWorkerPrefabFactory.TrySpawnRoleBody(\n            lease, GunnarNpcRole.Contract, Prefab, position, rotation, Stamp, out failure);", spawn);
        string stamp = Method(Prefab, "private static bool Stamp(");
        AssertBefore(stamp, "if (view == null || ai == null)", "view.GetZDO().Set(\"tcc.worker.key\", WorkerKey.Gunnar.Value);");
    }

    [Fact]
    public void TheFigureIsBuiltOnlyAfterItsLeaseIsGranted()
    {
        string body = Method(Presenter, "public void Show(");
        AssertBefore(body, "if (!_leases.TakePresentation()) { Hide(); return; }", "NpcPresentationBody.TryExtract(");
        AssertBefore(body, "if (_dresser != null && !_leases.HoldsPresentation) RemoveFigure();", "_leases.TakePresentation()");
    }

    [Fact]
    public void AFailedRigHoldsNoLease()
    {
        string body = Method(Presenter, "public void Show(");
        AssertBefore(body, "if (_dresser == null && BuildFailed) { Hide(); return; }", "_leases.TakePresentation()");
    }

    [Fact]
    public void TheRuntimeUsesTheGameFreeArrivalDecisions()
    {
        Assert.Contains("GunnarArrival.MayRelease(present, Time.unscaledTime - _arrivalSince)", Method(Runtime, "private void SettleArrival("));
        string pass = Method(Runtime, "private void Pass(");
        Assert.Contains("GunnarArrival.SavedWorkers(_arrivalPending || _leases.HoldsWorker,", pass);
        AssertBefore(pass, "if (saved != 0)", "_environment.TryAnchor(");
        string take = Method(Runtime, "public bool TakeWorkerBody(");
        Assert.Contains("if (standing.HasValue && !HasHeadroom(standing.Value)) standing = null;", take);
        // A second spawn before the first body is counted is refused before the
        // hand-over is even asked.
        AssertBefore(take, "if (_arrivalPending)", "_figure.HandOverToWorker(");
        // #372: the lease handed back is the arrival guard, read only after the
        // hand-over was granted, and no lease is a refusal.
        AssertBefore(take, "_figure.HandOverToWorker(", "lease = _leases.WorkerLease;");
        Assert.Matches(new Regex(@"lease = _leases\.WorkerLease;\s*if \(lease == null\)\s*\{[^}]*return false;", RegexOptions.Singleline), take);
        AssertBefore(take, "lease = _leases.WorkerLease;", "_arrivalPending = true;");
    }

    [Fact]
    public void EveryReleaseTakesTheFigureDownFirst()
    {
        Assert.Contains("public void Hide() => _leases.ReleasePresentation(RemoveFigure);", Presenter);
        string handOver = Method(Presenter, "public bool HandOverToWorker(");
        Assert.Contains("_leases.TryTakeWorker(RemoveFigure, out refusal)", handOver);
        string remove = Method(Presenter, "private void RemoveFigure(");
        AssertBefore(remove, "_dresser?.TakeDown();", "_dresser = null;");
    }

    [Fact]
    public void SpawnAsksForTheHandOverBeforeTheBodyIsCreatedAndPlacesItWhereHeStood()
    {
        string body = Method(Hauling, "private string Spawn(");
        const string handOver = "handOver(out Vector3? standing, out BodyLease? lease, out string handOverRefusal)";
        const string spawn = "TeamsterWorkerPrefab.Spawn(lease, position, facing, out string failure)";
        AssertBefore(body, "WorkerBodyCensus.MaySpawn(_bodyStatus)", handOver);
        AssertBefore(body, handOver, spawn);
        AssertBefore(body, "Vector3 position = standing ??", spawn);
        // #372: no hand-over is no lease, and no lease is no body.
        Assert.Matches(new Regex(@"if \(handOver == null\)\s*\{\s*return ""Refused: ", RegexOptions.Singleline), body);
        AssertBefore(body, "if (handOver == null)", handOver);
        // A body that was not placed gives the hand-over's lease back.
        Assert.Matches(new Regex(@"if \(body == null\)\s*\{\s*ReleaseWorkerBody\?\.Invoke\(\);", RegexOptions.Singleline), body);
        AssertBefore(body, spawn, "BodiesChanged?.Invoke();");
    }

    [Fact]
    public void ARetireTellsThePresentationOnlyAfterTheBodyIsGone()
    {
        string body = Method(Hauling, "private string Retire(");
        AssertBefore(body, "view.Destroy();", "BodiesChanged?.Invoke();");
        AssertBefore(body, "case BodyRetirementOutcome.Retired:", "BodiesChanged?.Invoke();\n                return \"Gunnar was retired");
    }

    [Fact]
    public void AWorldUnloadTakesTheFigureDownBeforeTheSharedRuntimeEndsTheWorld()
    {
        string unload = Method(Hauling, "private void OnWorldUnloaded(");
        AssertBefore(unload, "BeforeWorldEnds?.Invoke();", "NpcRoleRegistry.Shared.EndWorldLoad();");
        string figure = Method(Runtime, "public void OnWorldUnloading(");
        AssertBefore(figure, "_figure.Hide();", "_leases.Forget();");
    }

    [Fact]
    public void ThePassReadsTheStoryBeforeAnythingPhysical()
    {
        // #443's lesson: a player who has not opted in, or not met him, pays for
        // no anchor lookup and no probe.
        string pass = Method(Runtime, "private void Pass(");
        AssertBefore(pass, "GunnarBodyPlan.MaySurvey(", "_companion.Current()");
        AssertBefore(pass, "_companion.Current()", "_environment.TryAnchor(");
        AssertBefore(pass, "if (stage == null)", "_environment.TryAnchor(");
        // #461: the look at the pieces near home and the probing step come
        // after the story too, and after the body decision.
        AssertBefore(pass, "if (stage == null)", "LookIfDue(");
        AssertBefore(pass, "if (stage == null)", "_routine.Step(");
        AssertBefore(pass, "GunnarBodyPlan.Decide(facts)", "LookIfDue(");
        AssertBefore(pass, "GunnarBodyPlan.Decide(facts)", "_figure.Show(");
        Assert.DoesNotContain("GetAllZDOs", Runtime);
    }

    [Fact]
    public void TheArrivalGuardIsSettledEvenAfterAFault()
    {
        // A guard nothing releases would refuse every haul in the world.
        string tick = Method(Runtime, "public void Tick(");
        AssertBefore(tick, "SettleArrival();", "if (_faulted) return;");
    }

    [Fact]
    public void ThePluginBuildsTheRuntimeWiresTheHandOverAndTicksIt()
    {
        string tight = Regex.Replace(Plugin, @"\s+", string.Empty);
        Assert.Contains("_gunnarFigure=Adapters.Companions.GunnarPresentation.Install(", tight);
        Assert.Contains("_hauling.TakeWorkerBody=_gunnarFigure.TakeWorkerBody;", tight);
        Assert.Contains("_hauling.ReleaseWorkerBody=_gunnarFigure.ReleaseWorkerBody;", tight);
        Assert.Contains("_hauling.BodiesChanged=_gunnarFigure.NoteBodiesChanged;", tight);
        Assert.Contains("_hauling.BeforeWorldEnds=_gunnarFigure.OnWorldUnloading;", tight);
        Assert.Contains("_gunnarFigure?.Tick(UnityEngine.Time.deltaTime);", tight);
        string destroy = Method(Plugin, "private void OnDestroy(");
        AssertBefore(destroy, "_gunnarFigure?.Dispose();", "GunnarHaulingRuntime.Uninstall(_hauling);");
        AssertBefore(Plugin, "_hauling = Adapters.Workers.GunnarHaulingRuntime.Install(", "_gunnarFigure = Adapters.Companions.GunnarPresentation.Install(");
    }

    [Fact]
    public void AnOlderLibraryIsADependencyErrorNotAMissingMethod()
    {
        Assert.Contains("[BepInDependency(\"com.theconcernedcat.valheim.concernednpc\", \"0.5.0\")]", Plugin);
    }

    private static void AssertBefore(string text, string first, string second)
    {
        int a = text.IndexOf(first, StringComparison.Ordinal);
        int b = text.IndexOf(second, StringComparison.Ordinal);
        Assert.True(a >= 0, "missing: " + first);
        Assert.True(b >= 0, "missing: " + second);
        Assert.True(a < b, "'" + first + "' must come before '" + second + "'");
    }

    private static string Method(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "method not found: " + signature);
        int open = source.IndexOf('{', start);
        int arrow = source.IndexOf("=>", start, StringComparison.Ordinal);
        if (arrow >= 0 && arrow < open) return source.Substring(start, source.IndexOf(';', arrow) - start + 1);
        int depth = 0;
        for (int index = open; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) return source.Substring(start, index - start + 1);
        }

        throw new InvalidOperationException("unbalanced braces after " + signature);
    }

    private static string Read(params string[] relative)
    {
        string path = Path.Combine(relative);
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here != null)
        {
            string candidate = Path.Combine(here.FullName, path);
            if (File.Exists(candidate)) return File.ReadAllText(candidate).Replace("\r\n", "\n");
            here = here.Parent;
        }

        throw new FileNotFoundException("Shipped source not found above the test assembly.", path);
    }
}
