using System;
using System.Globalization;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>The Pin Workbench edit model, shared by the map panel and the
/// console: a string field buffer loaded from a managed pin, validation,
/// and an all-fields Apply that lands as ONE undoable operation. Nothing
/// touches the store until Apply; Cancel simply discards the buffer.</summary>
internal sealed class PinWorkbenchController
{
    public bool IsOpen { get; private set; }
    public AtlasId TargetId { get; private set; }

    public string NameField = "";
    public string IconField = "";
    public string CategoryField = "";
    public string ColorField = "";
    public string SizeField = "1";
    public string NotesField = "";
    public string TagsField = "";
    public AtlasPinStatus StatusField;
    public bool CheckedField;
    public AtlasScope ScopeField = AtlasScope.Private;

    /// <summary>Read-only display line: source, coordinates, timestamps.</summary>
    public string InfoLine { get; private set; } = "";

    public void Open(AtlasPin pin)
    {
        TargetId = pin.Id;
        NameField = pin.Name;
        IconField = pin.IconId;
        CategoryField = pin.Category;
        ColorField = pin.ColorArgb is int argb
            ? (argb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture)
            : "";
        SizeField = pin.SizeScale.ToString("0.##", CultureInfo.InvariantCulture);
        NotesField = pin.Notes;
        TagsField = string.Join(", ", pin.Tags);
        StatusField = pin.Status;
        CheckedField = pin.Checked;
        ScopeField = pin.Scope;
        InfoLine = string.Format(
            CultureInfo.InvariantCulture,
            "{0} · ({1:0.#}, {2:0.#}) · created {3:yyyy-MM-dd} · edited {4:yyyy-MM-dd HH:mm} · rev {5}",
            pin.Source, pin.Position.X, pin.Position.Z, pin.CreatedUtc, pin.ModifiedUtc, pin.Revision);
        IsOpen = true;
    }

    public void Cancel()
    {
        IsOpen = false;
    }

    public AtlasPinStatus CycleStatus()
    {
        StatusField = StatusField == AtlasPinStatus.Warning ? AtlasPinStatus.None : StatusField + 1;
        return StatusField;
    }

    public AtlasScope CycleScope()
    {
        ScopeField = ScopeField == AtlasScope.Server ? AtlasScope.Private : ScopeField + 1;
        return ScopeField;
    }

    /// <summary>Validates the buffer and applies every field as one
    /// undoable edit. On validation failure nothing is written.</summary>
    public bool TryApply(PinOperations operations, out string message)
    {
        if (!IsOpen)
        {
            message = "The workbench is not open.";
            return false;
        }

        if (!TryParseColor(ColorField, out int? colorArgb))
        {
            message = "Color must be empty, RRGGBB, or AARRGGBB hex.";
            return false;
        }

        if (!float.TryParse(SizeField.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float size))
        {
            message = "Size must be a number between 0.5 and 2.";
            return false;
        }

        float clampedSize = Math.Max(0.5f, Math.Min(2f, size));
        string icon = IconField.Trim();
        bool knownIcon = IconRegistry.TryResolve(icon, out _);

        string name = NameField;
        string category = CategoryField.Trim();
        string notes = NotesField;
        var tags = AtlasText.SplitTags(AtlasText.JoinTags(TagsField.Split(',')));
        AtlasPinStatus status = StatusField;
        bool isChecked = CheckedField;
        AtlasScope scope = ScopeField;

        int applied = operations.BatchEdit(new[] { TargetId }, pin =>
        {
            pin.Name = name;
            pin.IconId = icon.Length == 0 ? IconRegistry.DefaultIconId : icon;
            pin.Category = category;
            pin.ColorArgb = colorArgb;
            pin.SizeScale = clampedSize;
            pin.Notes = notes;
            pin.Tags.Clear();
            pin.Tags.AddRange(tags);
            pin.Status = status;
            pin.Checked = isChecked;
            pin.Scope = scope;
        }, "workbench edit");

        if (applied == 0)
        {
            message = "The pin no longer exists.";
            return false;
        }

        IsOpen = false;
        message = knownIcon || icon.Length == 0
            ? "Saved."
            : $"Saved. Icon '{icon}' is not in the registry; it renders as the fallback but the identity is preserved.";
        return true;
    }

    private static bool TryParseColor(string field, out int? colorArgb)
    {
        string trimmed = field.Trim().TrimStart('#');
        if (trimmed.Length == 0)
        {
            colorArgb = null;
            return true;
        }

        if ((trimmed.Length == 6 || trimmed.Length == 8) &&
            uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
        {
            if (trimmed.Length == 6)
            {
                value |= 0xFF000000;
            }

            colorArgb = unchecked((int)value);
            return true;
        }

        colorArgb = null;
        return false;
    }
}
