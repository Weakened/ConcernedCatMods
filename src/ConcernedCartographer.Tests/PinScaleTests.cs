using System.Diagnostics;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>CC-024 scale bar: pin management must stay responsive an order
/// of magnitude beyond the 1,000-pin requirement. Bounds are deliberately
/// loose so CI machines never flake; the point is catching accidental
/// O(n²) regressions, and the measured numbers are reported in the sprint
/// evidence.</summary>
public class PinScaleTests
{
    private const int PinCount = 10_000;

    private static PinStore BuildLargeStore()
    {
        var store = new PinStore(() => new DateTime(2026, 8, 27, 8, 0, 0, DateTimeKind.Utc));
        var random = new Random(42);
        for (int index = 0; index < PinCount; index++)
        {
            int i = index;
            store.Create(pin =>
            {
                pin.Name = $"Pin {i}";
                pin.IconId = IconRegistry.All[i % IconRegistry.All.Count].Id;
                pin.Category = i % 7 == 0 ? "Base" : "Explore";
                pin.Tags.Add(i % 2 == 0 ? "even" : "odd");
                pin.Position = new RoadPoint(
                    (float)(random.NextDouble() * 20000 - 10000),
                    30f,
                    (float)(random.NextDouble() * 20000 - 10000));
            });
        }

        return store;
    }

    [Fact]
    public void TenThousandPins_CreateMutateSerializeReplay_StaysFast()
    {
        var stopwatch = Stopwatch.StartNew();
        PinStore store = BuildLargeStore();
        long createMs = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        var operations = new PinOperations(store);
        var batch = new List<AtlasId>();
        foreach (AtlasPin pin in store.Living)
        {
            if (pin.Category == "Base")
            {
                batch.Add(pin.Id);
            }
        }

        int edited = operations.BatchEdit(batch, pin => pin.Status = AtlasPinStatus.Todo);
        long batchMs = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        var lines = new List<string>(PinCodec.Serialize(store.All));
        long serializeMs = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        PinCodec.ParseResult replay = PinCodec.Parse(lines);
        long parseMs = stopwatch.ElapsedMilliseconds;

        Assert.Equal(PinCount, store.Count);
        Assert.True(edited > 1000);
        Assert.Equal(PinCount, replay.Pins.Count);
        Assert.Equal(0, replay.MalformedRows);

        Assert.True(createMs < 3000, $"create {createMs} ms");
        Assert.True(batchMs < 2000, $"batch {batchMs} ms");
        Assert.True(serializeMs < 3000, $"serialize {serializeMs} ms");
        Assert.True(parseMs < 3000, $"parse {parseMs} ms");
    }

    [Fact]
    public void TenThousandPins_UndoOfLargeBatch_Reverts()
    {
        PinStore store = BuildLargeStore();
        var operations = new PinOperations(store);
        var all = new List<AtlasId>();
        foreach (AtlasPin pin in store.Living)
        {
            all.Add(pin.Id);
        }

        operations.BatchEdit(all, pin => pin.Category = "Rewritten");
        Assert.True(operations.Undo(out _));

        int rewritten = 0;
        foreach (AtlasPin pin in store.Living)
        {
            if (pin.Category == "Rewritten")
            {
                rewritten++;
            }
        }

        Assert.Equal(0, rewritten);
    }

    [Fact]
    public void DuplicateScan_AtScale_CompletesQuickly()
    {
        PinStore store = BuildLargeStore();
        var operations = new PinOperations(store);

        var stopwatch = Stopwatch.StartNew();
        List<List<AtlasPin>> groups = operations.FindDuplicateGroups(10f);
        stopwatch.Stop();

        // Random spread over 20 km yields few collisions; the scan itself is
        // the thing under test.
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"scan {stopwatch.ElapsedMilliseconds} ms");
        Assert.True(groups.Count < PinCount / 2);
    }
}
