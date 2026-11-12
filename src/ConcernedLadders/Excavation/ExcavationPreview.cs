using System;
using Jotunn.Configs;
using Jotunn.Managers;
using TheConcernedCat.ConcernedLadders.Domain.Excavation;
using TheConcernedCat.Diagnostics;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheConcernedCat.ConcernedLadders.Excavation;

/// <summary>The Iron Shovel's preview (#487 slice 4; reworked by #551), behind the off-by-default <c>[Excavation]
/// Enabled</c> switch: constructed only in the switch's branch of the plugin.
///
/// <para>While the shovel is the local player's equipped weapon and no menu, inventory, map, chat, console, store or build
/// menu is open, it asks <see cref="ExcavationRuntime.RefreshPreview"/> (read-only, rate-limited) what the dig session shows
/// and draws it:</para>
/// <list type="bullet">
/// <item>the flat outlines of <see cref="DigPreviewShape"/> (the hole's top edge at ground level, the floor at its own
/// height, the four wall lines between them, and the water surface when the floor would flood), coloured by whether a dig
/// would go ahead, do nothing, or be refused, and drawn through the terrain so a floor still underground can be seen. They
/// are level, never draped over uneven ground (owner playtest 2026-11-10);</item>
/// <item>one short line under the crosshair (<see cref="DigText"/>): what to do next, or the one reason it cannot;</item>
/// <item>the game's own key-hint panel (through Jötunn's KeyHintManager) for the two buttons, from the player's bindings.</item>
/// </list>
///
/// <para>In the depth stage the wheel (or the controller's alternate-keys plus zoom buttons) sets the depth, and
/// <see cref="ExcavationShovelInput"/> holds the camera's distance so it does not zoom at the same time.</para>
///
/// <para><b>Local and bounded.</b> One root object and exactly <see cref="DigPreviewShape.LineCount"/> line renderers, created
/// once per scene and reused, one shared material, and one HUD text. No network view, no ZDO, nothing saved: plain local
/// scene objects with no collider, so neither the aim ray nor the object scan can meet them. Aiming, setting corners,
/// changing depth or moving changes nothing in the world: this class only draws.</para>
///
/// <para><b>Not observed in game.</b> The look, the line visibility through terrain, the HUD placement and the controller
/// buttons are OWNER GO-AROUND PENDING; controller: CONTROLLER HARDWARE BLOCKED.</para></summary>
internal sealed class ExcavationPreview : IDisposable
{
    /// <summary>Built-in shader whose depth test can be switched off, so a floor still under the ground shows through it.</summary>
    private const string OverlayShader = "Hidden/Internal-Colored";

    /// <summary>The fallback when the overlay shader is not in this build: depth-tested, so underground lines are hidden.</summary>
    private const string FallbackShader = "Sprites/Default";

    private const float LineWidth = 0.05f;

    private static readonly Color ReadyColor = new Color(0.35f, 0.9f, 0.45f, 0.95f);
    private static readonly Color IdleColor = new Color(0.95f, 0.85f, 0.35f, 0.95f);
    private static readonly Color RefusedColor = new Color(0.95f, 0.35f, 0.3f, 0.95f);
    private static readonly Color WaterColor = new Color(0.3f, 0.6f, 1f, 0.95f);

    private readonly Action<string> _log;
    private readonly ExcavationRuntime _runtime;
    private readonly LineRenderer?[] _lines = new LineRenderer?[DigPreviewShape.LineCount];
    private readonly Vector3[][] _buffers = new Vector3[DigPreviewShape.MaximumPoints + 1][];
    private GameObject? _root;
    private Material? _material;
    private bool _shaderMissing;
    private TextMeshProUGUI? _label;
    private KeyHintConfig? _hint;
    private int _labelVersion = int.MinValue;
    private bool _shownLastFrame;
    private bool _faulted;

    internal ExcavationPreview(Action<string> log, ExcavationRuntime runtime)
    {
        _log = log;
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        for (int count = 0; count < _buffers.Length; count++) _buffers[count] = new Vector3[count];
    }

    /// <summary>Adds the shovel's entry to the game's key-hint panel. A failure only loses the panel hint.</summary>
    internal void Install()
    {
        if (Application.isBatchMode) return;
        try
        {
            _hint = new KeyHintConfig
            {
                Item = IronShovel.PrefabName,
                ButtonConfigs = new[]
                {
                    new ButtonConfig { Name = "Attack", HintToken = IronShovel.DigHintToken },
                    new ButtonConfig { Name = "SecondaryAttack", HintToken = IronShovel.SizeHintToken },
                },
            };
            if (!KeyHintManager.Instance.AddKeyHint(_hint)) _hint = null;
        }
        catch (Exception exception)
        {
            _hint = null;
            _log("Iron Shovel key hints unavailable (" + SafeFailure.Brief(exception) + "); the HUD line still says what to do.");
        }
    }

    /// <summary>True while any screen that takes the game's input or covers the world is open. The game already withholds
    /// the attack edges then (PlayerController.TakeInput and InInventoryEtc, read from the installed game); the preview
    /// hides and the press ignores them as well. A cursor that is not locked counts too: during play the game's camera keeps
    /// it locked and frees it for any screen, the Ctrl+F1 free cursor, the barber and other mods' windows, so a click on any
    /// of those never digs (independent review, N4).</summary>
    internal static bool UiOpen()
    {
        Chat chat = Chat.instance;
        return InventoryGui.IsVisible() || Menu.IsVisible() || Minimap.IsOpen() || Console.IsVisible() || TextInput.IsVisible() ||
               StoreGui.IsVisible() || Hud.IsPieceSelectionVisible() || Hud.InRadial() || GameCamera.InFreeFly() ||
               (chat != null && chat.HasFocus()) || ZCursor.LockState != CursorLockMode.Locked;
    }

    /// <summary>True while the preview is up in the depth stage: the wheel sets depth then, and the camera must not zoom.</summary>
    internal bool SettingDepth => _shownLastFrame && _runtime.Session.Stage == DigStage.Depth;

    /// <summary>Called every frame after the runtime's own update.</summary>
    internal void Update()
    {
        if (_faulted) return;
        try
        {
            Player player = Player.m_localPlayer;
            ItemDrop.ItemData? shovel = player != null ? player.GetCurrentWeapon() : null;
            bool visible = player != null && !player.IsDead() && !player.InPlaceMode() && shovel?.m_shared != null &&
                           IronShovel.IsShovelName(shovel.m_shared.m_name) && !UiOpen();
            if (!visible)
            {
                _runtime.HidePreview();
                if (_shownLastFrame) HideVisuals();
                _shownLastFrame = false;
                return;
            }

            if (_runtime.Session.Stage == DigStage.Depth) _runtime.ChangeDepth(DepthSteps());
            bool refreshed = _runtime.RefreshPreview(player!, shovel!, Time.frameCount, Time.realtimeSinceStartup);
            bool created = EnsureLines();
            if (refreshed || created || !_shownLastFrame) DrawLines();
            UpdateLabel();
            _shownLastFrame = true;
        }
        catch (Exception exception)
        {
            _faulted = true;
            try { HideVisuals(); } catch { /* Teardown only. */ }
            // A dig needs the preview, so a preview fault stops excavation (and disables the recipe) for the session.
            ExcavationShovelInput.Show(_runtime.StopAfterFault(exception));
        }
    }

    /// <summary>This frame's depth change: wheel down (or the controller's zoom-out) is a metre deeper, wheel up (zoom-in) a
    /// metre shallower; one step per frame however far the wheel turned.</summary>
    private static int DepthSteps()
    {
        float wheel = ZInput.GetMouseScrollWheel();
        if (wheel < 0f) return 1;
        if (wheel > 0f) return -1;
        if (ZInput.GetButton("JoyAltKeys") && !Hud.InRadial())
        {
            if (ZInput.GetButtonDown("JoyCamZoomOut")) return 1;
            if (ZInput.GetButtonDown("JoyCamZoomIn")) return -1;
        }

        return 0;
    }

    private bool EnsureLines()
    {
        if (_root != null) return false;
        Material? material = Material();
        if (material == null) return false;
        _root = new GameObject("tcc_excavation_preview");
        for (int i = 0; i < _lines.Length; i++)
        {
            var child = new GameObject("tcc_excavation_preview_line");
            child.transform.SetParent(_root.transform, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = material;
            line.widthMultiplier = LineWidth;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = 0;
            line.enabled = false;
            _lines[i] = line;
        }

        return true;
    }

    /// <summary>The one shared material, created once (again only if something destroyed it). Null when neither shader is
    /// in this build; then only the HUD line shows (reported once).</summary>
    private Material? Material()
    {
        if (_material != null) return _material;
        if (_shaderMissing) return null;
        Shader shader = Shader.Find(OverlayShader);
        bool overlay = shader != null;
        if (!overlay) shader = Shader.Find(FallbackShader);
        if (shader == null)
        {
            _shaderMissing = true;
            _log("Iron Shovel preview outlines unavailable: neither " + OverlayShader + " nor " + FallbackShader + " is in this build. The HUD line still shows the plan.");
            return null;
        }

        _material = new Material(shader) { name = "tcc_excavation_preview" };
        if (overlay)
        {
            _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_Cull", (int)CullMode.Off);
            _material.SetInt("_ZWrite", 0);
            _material.SetInt("_ZTest", (int)CompareFunction.Always);
        }
        else
        {
            _log("Iron Shovel preview: " + OverlayShader + " is not in this build; outlines below the ground will be hidden by it.");
        }

        _material.renderQueue = 3100;
        return _material;
    }

    private void DrawLines()
    {
        if (_root == null) return;
        if (!_root.activeSelf) _root.SetActive(true);
        DigPreviewShape shape = _runtime.Shape;
        Color tone = ColorFor(DigPreviewShape.ToneFor(_runtime.Shown.Refusal));
        for (int i = 0; i < _lines.Length; i++)
        {
            LineRenderer? line = _lines[i];
            if (line == null) continue;
            int count = shape.Count(i);
            if (count < 2)
            {
                line.enabled = false;
                continue;
            }

            Vector3[] points = _buffers[count];
            for (int p = 0; p < count; p++) points[p] = new Vector3(shape.X(i, p), shape.Y(i, p), shape.Z(i, p));
            line.positionCount = count;
            line.SetPositions(points);
            line.loop = shape.Loop(i);
            Color color = i == DigPreviewShape.Water ? WaterColor : tone;
            line.startColor = color;
            line.endColor = color;
            line.enabled = true;
        }
    }

    private void UpdateLabel()
    {
        TextMeshProUGUI? label = Label();
        if (label == null) return;
        if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
        DigPreviewState shown = _runtime.Shown;
        if (shown.Version == _labelVersion) return;
        _labelVersion = shown.Version;
        string tone = ColorUtility.ToHtmlStringRGB(ColorFor(shown.HasTarget ? DigPreviewShape.ToneFor(shown.Refusal) : DigTone.Refused));
        label.text = "<color=#" + tone + "><noparse>" + shown.Line + "</noparse></color>";
    }

    /// <summary>The HUD line, created once under the game's HUD root (so it hides with the HUD) with the game's own message
    /// font. Null until the HUD exists.</summary>
    private TextMeshProUGUI? Label()
    {
        if (_label != null) return _label;
        Hud hud = Hud.instance;
        MessageHud messages = MessageHud.instance;
        if (hud == null || hud.m_rootObject == null || messages == null || messages.m_messageText == null) return null;
        var holder = new GameObject("tcc_excavation_label", typeof(RectTransform));
        var rect = (RectTransform)holder.transform;
        rect.SetParent(hud.m_rootObject.transform, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -70f);
        rect.sizeDelta = new Vector2(700f, 32f);
        TextMeshProUGUI text = holder.AddComponent<TextMeshProUGUI>();
        TMP_Text source = messages.m_messageText;
        text.font = source.font;
        text.spriteAsset = source.spriteAsset;
        text.fontSize = 18f;
        text.alignment = TextAlignmentOptions.Top;
        text.richText = true;
        text.raycastTarget = false;
        text.color = new Color(0.95f, 0.92f, 0.86f, 1f);
        text.text = "";
        _label = text;
        _labelVersion = int.MinValue;
        return _label;
    }

    private static Color ColorFor(DigTone tone) => tone switch
    {
        DigTone.Ready => ReadyColor,
        DigTone.Idle => IdleColor,
        DigTone.Water => WaterColor,
        _ => RefusedColor,
    };

    private void HideVisuals()
    {
        if (_root != null && _root.activeSelf) _root.SetActive(false);
        if (_label != null && _label.gameObject.activeSelf) _label.gameObject.SetActive(false);
    }

    public void Dispose()
    {
        _faulted = true;
        _shownLastFrame = false;
        try { _runtime.HidePreview(); } catch { /* Teardown only. */ }
        try { HideVisuals(); } catch { /* Teardown only. */ }
        try { if (_hint != null) KeyHintManager.Instance.RemoveKeyHint(_hint); } catch { /* Teardown only. */ }
        _hint = null;
    }
}
