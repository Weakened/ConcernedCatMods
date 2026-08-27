using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

public class PinWorkbenchControllerTests
{
    private static (PinStore Store, PinOperations Ops, AtlasPin Pin) Fixture()
    {
        var store = new PinStore(() => new DateTime(2026, 8, 27, 7, 0, 0, DateTimeKind.Utc));
        AtlasPin pin = store.Create(created =>
        {
            created.Name = "Old Dock";
            created.IconId = "cc:harbor";
            created.Category = "Travel";
            created.ColorArgb = unchecked((int)0xFF112233);
            created.SizeScale = 1.5f;
            created.Notes = "old notes";
            created.Tags.AddRange(new[] { "sea", "trade" });
            created.Status = AtlasPinStatus.Todo;
            created.Checked = true;
            created.Scope = AtlasScope.Table;
            created.Position = new RoadPoint(100f, 30f, -50f);
        });
        return (store, new PinOperations(store), pin);
    }

    [Fact]
    public void Open_LoadsEveryFieldIntoTheBuffer()
    {
        (_, _, AtlasPin pin) = Fixture();
        var controller = new PinWorkbenchController();

        controller.Open(pin);

        Assert.True(controller.IsOpen);
        Assert.Equal("Old Dock", controller.NameField);
        Assert.Equal("cc:harbor", controller.IconField);
        Assert.Equal("Travel", controller.CategoryField);
        Assert.Equal("112233", controller.ColorField);
        Assert.Equal("1.5", controller.SizeField);
        Assert.Equal("old notes", controller.NotesField);
        Assert.Equal("sea, trade", controller.TagsField);
        Assert.Equal(AtlasPinStatus.Todo, controller.StatusField);
        Assert.True(controller.CheckedField);
        Assert.Equal(AtlasScope.Table, controller.ScopeField);
        Assert.Contains("Managed", controller.InfoLine);
        Assert.Contains("rev 1", controller.InfoLine);
    }

    [Fact]
    public void Cancel_WritesNothing()
    {
        (_, _, AtlasPin pin) = Fixture();
        var controller = new PinWorkbenchController();
        long revision = pin.Revision;

        controller.Open(pin);
        controller.NameField = "Changed";
        controller.Cancel();

        Assert.False(controller.IsOpen);
        Assert.Equal("Old Dock", pin.Name);
        Assert.Equal(revision, pin.Revision);
    }

    [Fact]
    public void Apply_WritesAllFields_AsOneUndoStep()
    {
        (PinStore store, PinOperations ops, AtlasPin pin) = Fixture();
        var controller = new PinWorkbenchController();
        controller.Open(pin);

        controller.NameField = "New Dock";
        controller.IconField = "vanilla:portal";
        controller.CategoryField = "Base";
        controller.ColorField = "#00FF00";
        controller.SizeField = "0.75";
        controller.NotesField = "fresh\nnotes";
        controller.TagsField = "sea, iron";
        controller.StatusField = AtlasPinStatus.Done;
        controller.CheckedField = false;
        controller.ScopeField = AtlasScope.Private;

        Assert.True(controller.TryApply(ops, out string message));
        Assert.Equal("Saved.", message);
        Assert.False(controller.IsOpen);

        Assert.Equal("New Dock", pin.Name);
        Assert.Equal("vanilla:portal", pin.IconId);
        Assert.Equal("Base", pin.Category);
        Assert.Equal(unchecked((int)0xFF00FF00), pin.ColorArgb);
        Assert.Equal(0.75f, pin.SizeScale);
        Assert.Equal("fresh\nnotes", pin.Notes);
        Assert.Equal(new[] { "sea", "iron" }, pin.Tags);
        Assert.Equal(AtlasPinStatus.Done, pin.Status);
        Assert.False(pin.Checked);
        Assert.Equal(AtlasScope.Private, pin.Scope);
        Assert.Equal(new RoadPoint(100f, 30f, -50f), pin.Position);

        Assert.True(ops.Undo(out _));
        Assert.Equal("Old Dock", pin.Name);
        Assert.Equal("cc:harbor", pin.IconId);
        Assert.Equal(new[] { "sea", "trade" }, pin.Tags);
        Assert.True(pin.Checked);
    }

    [Theory]
    [InlineData("zzz")]
    [InlineData("12345")]
    [InlineData("1234567")]
    public void InvalidColor_FailsWithoutPartialWrites(string color)
    {
        (_, PinOperations ops, AtlasPin pin) = Fixture();
        var controller = new PinWorkbenchController();
        controller.Open(pin);
        controller.NameField = "Should not land";
        controller.ColorField = color;

        Assert.False(controller.TryApply(ops, out string message));
        Assert.Contains("Color", message);
        Assert.Equal("Old Dock", pin.Name);
        Assert.True(controller.IsOpen);
    }

    [Fact]
    public void InvalidSize_FailsWithoutPartialWrites()
    {
        (_, PinOperations ops, AtlasPin pin) = Fixture();
        var controller = new PinWorkbenchController();
        controller.Open(pin);
        controller.SizeField = "big";

        Assert.False(controller.TryApply(ops, out string message));
        Assert.Contains("Size", message);
        Assert.Equal(1.5f, pin.SizeScale);
    }

    [Fact]
    public void UnknownIcon_IsPreservedWithAWarning()
    {
        (_, PinOperations ops, AtlasPin pin) = Fixture();
        var controller = new PinWorkbenchController();
        controller.Open(pin);
        controller.IconField = "othermod:crystal";

        Assert.True(controller.TryApply(ops, out string message));
        Assert.Contains("fallback", message);
        Assert.Equal("othermod:crystal", pin.IconId);
    }

    [Fact]
    public void SizeIsClamped_AndEmptyColorClears()
    {
        (_, PinOperations ops, AtlasPin pin) = Fixture();
        var controller = new PinWorkbenchController();
        controller.Open(pin);
        controller.SizeField = "9";
        controller.ColorField = "";

        Assert.True(controller.TryApply(ops, out _));
        Assert.Equal(2f, pin.SizeScale);
        Assert.Null(pin.ColorArgb);
    }

    [Fact]
    public void CycleHelpers_WrapAround()
    {
        var controller = new PinWorkbenchController();
        controller.StatusField = AtlasPinStatus.Warning;
        Assert.Equal(AtlasPinStatus.None, controller.CycleStatus());
        controller.ScopeField = AtlasScope.Server;
        Assert.Equal(AtlasScope.Private, controller.CycleScope());
    }
}
