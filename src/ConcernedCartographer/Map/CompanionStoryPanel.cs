using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Companions;
using TheConcernedCat.ConcernedCartographer.Reporting;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>Hulgi's introduction, read one page at a time.
///
/// Three properties are doing real work here.
///
/// <b>It is never opened for the player.</b> The only way in is examining the
/// compass or asking to replay, so "no modal interruption during combat,
/// loading or death" is satisfied at the source rather than by trying to
/// detect danger. If any of those states arrives while it is open, the panel
/// closes itself and hands input straight back.
///
/// <b>Closing decides nothing.</b> Escape, dying, or the world unloading leaves
/// the quest exactly where it was, so a player who walks away mid-story comes
/// back to the same offer. Only the two buttons on the last page finish the
/// introduction, and each says plainly what it does.
///
/// <b>It works without a mouse.</b> Arrows page, Enter advances, Escape leaves;
/// the first control is focused on open, so a controller can drive all of it.
/// The cursor is available too, but nothing requires it.</summary>
internal sealed class CompanionStoryPanel
{
    private const float PanelWidth = 560f;
    private const float PanelHeight = 420f;
    private const float BodyHeight = 190f;

    private readonly ManualLogSource _log;
    private readonly ModalInputBlock _inputBlock;

    private GameObject? _panel;
    private Text? _speaker;
    private Text? _body;
    private Text? _pageLabel;
    private Button? _backButton;
    private Button? _nextButton;
    private Button? _welcomeButton;
    private Button? _toolsOnlyButton;
    private bool _failed;
    private bool _choicesAllowed = true;

    private StoryReader _reader = new StoryReader();

    public CompanionStoryPanel(ManualLogSource log)
    {
        _log = log;
        _inputBlock = new ModalInputBlock(GUIManager.BlockInput);
    }

    /// <summary>Raised when the player welcomes Hulgi.</summary>
    public Action? WelcomeChosen;

    /// <summary>Raised when the player chooses the tools without the story.</summary>
    public Action? ToolsOnlyChosen;

    /// <summary>Raised whenever the panel closes, however it closed.</summary>
    public Action? Closed;

    public bool IsVisible => _panel != null && _panel.activeSelf;

    public bool HasFailed => _failed;

    /// <summary>Personal size preference, applied on show like every other CC
    /// surface.</summary>
    public float UiScale = 1f;

    /// <summary>Which page the reader is on, so the owner can resume the story
    /// at the same place inside one session.</summary>
    public int PageIndex => _reader.PageIndex;

    /// <summary>Opens the introduction.</summary>
    /// <param name="resumeAtPage">Where to resume, clamped. Negative starts at
    /// the beginning.</param>
    /// <param name="allowChoices">False for a replay: the story is readable
    /// but the two completion buttons are not offered, because the
    /// introduction is already finished and re-deciding it is not a thing that
    /// exists.</param>
    public void Show(int resumeAtPage, bool allowChoices)
    {
        if (!EnsureBuilt())
        {
            return;
        }

        try
        {
            _choicesAllowed = allowChoices;
            _reader = new StoryReader();
            _reader.Resume(resumeAtPage);

            _panel!.transform.localScale = Vector3.one * UiScale;
            _panel.SetActive(true);
            _inputBlock.Acquire();
            Refresh();
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    public void Hide()
    {
        // The input block is released first and unconditionally. Whatever else
        // goes wrong below, the player gets their controls back.
        _inputBlock.Release();

        if (_panel != null && _panel.activeSelf)
        {
            _panel.SetActive(false);
            try
            {
                Closed?.Invoke();
            }
            catch (Exception exception)
            {
                _log.LogWarning(
                    $"A companion story close handler failed: {SafeLogText.Brief(exception)}");
            }
        }
    }

    /// <summary>Per-frame keyboard handling and the safety closes. Called every
    /// tick, including while the mod is otherwise dormant, so a panel can never
    /// be left holding the input block.</summary>
    public void HandleFrame()
    {
        if (!IsVisible)
        {
            // Defensive: if the GameObject died under us (a scene change
            // destroys it), the block must not survive it.
            _inputBlock.Release();
            return;
        }

        try
        {
            if (ShouldCloseForSafety())
            {
                Hide();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Hide();
                return;
            }

            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                _reader.Back();
                Refresh();
                return;
            }

            if (Input.GetKeyDown(KeyCode.RightArrow) ||
                Input.GetKeyDown(KeyCode.Return) ||
                Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (_reader.Next())
                {
                    Refresh();
                }

                return;
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    /// <summary>The states where a story panel has no business being on
    /// screen. Each one is checked defensively, because every one of them can
    /// arrive between two frames.</summary>
    private bool ShouldCloseForSafety()
    {
        try
        {
            if (Game.instance == null)
            {
                return true;
            }

            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead())
            {
                return true;
            }

            return false;
        }
        catch
        {
            // If the question itself cannot be answered, close. Erring towards
            // giving control back is always the right way round.
            return true;
        }
    }

    private void Refresh()
    {
        if (_panel == null)
        {
            return;
        }

        StoryPage page = CompanionStory.Pages[_reader.PageIndex];
        if (_speaker != null)
        {
            _speaker.text = AtlasStrings.Get(page.SpeakerKey);
        }

        if (_body != null)
        {
            _body.text = AtlasStrings.Get(page.BodyKey);
        }

        if (_pageLabel != null)
        {
            _pageLabel.text = AtlasStrings.Format(
                "story.page", _reader.PageNumber, _reader.PageCount);
        }

        bool onLastPage = _reader.IsLastPage;
        SetActive(_backButton, !_reader.IsFirstPage);
        SetActive(_nextButton, !onLastPage);
        SetActive(_welcomeButton, onLastPage && _choicesAllowed);
        SetActive(_toolsOnlyButton, onLastPage && _choicesAllowed);

        FocusFirstControl();
    }

    private static void SetActive(Button? button, bool active)
    {
        if (button != null)
        {
            button.gameObject.SetActive(active);
        }
    }

    private void FocusFirstControl()
    {
        try
        {
            Selectable? first = _panel!.GetComponentInChildren<Selectable>(includeInactive: false);
            if (first != null)
            {
                UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(first.gameObject);
            }
        }
        catch
        {
            // Focus is a convenience; keyboard paging works regardless.
        }
    }

    private void Choose(Action? chosen)
    {
        try
        {
            chosen?.Invoke();
        }
        catch (Exception exception)
        {
            _log.LogError(
                $"A companion introduction choice could not be applied: {SafeLogText.Describe(exception)}");
        }

        Hide();
    }

    private bool EnsureBuilt()
    {
        if (_failed)
        {
            return false;
        }

        if (_panel != null)
        {
            return true;
        }

        if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null)
        {
            return false;
        }

        try
        {
            Build();
            return _panel != null;
        }
        catch (Exception exception)
        {
            Fail(exception);
            return false;
        }
    }

    private void Build()
    {
        GUIManager gui = GUIManager.Instance;
        Font font = gui.AveriaSerifBold;
        var headerColor = new Color(0.9f, 0.8f, 0.6f, 1f);
        var bodyColor = new Color(0.94f, 0.92f, 0.86f, 1f);

        _panel = gui.CreateWoodpanel(
            GUIManager.CustomGUIFront!.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f),
            PanelWidth, PanelHeight, draggable: true);

        _speaker = gui.CreateText(
            "", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f),
            font, 20, headerColor, outline: true, Color.black,
            PanelWidth - 48f, 32f, addContentSizeFitter: false)
            .GetComponent<Text>();
        _speaker.alignment = TextAnchor.MiddleCenter;

        _body = gui.CreateText(
            "", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f - (BodyHeight / 2f)),
            font, 16, bodyColor, outline: false, Color.black,
            PanelWidth - 56f, BodyHeight, addContentSizeFitter: false)
            .GetComponent<Text>();
        _body.alignment = TextAnchor.UpperLeft;
        _body.verticalOverflow = VerticalWrapMode.Truncate;

        _pageLabel = gui.CreateText(
            "", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -290f),
            font, 13, new Color(0.78f, 0.74f, 0.66f, 1f), outline: false, Color.black,
            PanelWidth - 48f, 22f, addContentSizeFitter: false)
            .GetComponent<Text>();
        _pageLabel.alignment = TextAnchor.MiddleCenter;

        _backButton = CreateButton("story.back", -150f, -330f, 130f, () =>
        {
            _reader.Back();
            Refresh();
        });

        _nextButton = CreateButton("story.next", 150f, -330f, 130f, () =>
        {
            _reader.Next();
            Refresh();
        });

        _welcomeButton = CreateButton("story.welcome", -110f, -330f, 200f,
            () => Choose(WelcomeChosen));

        _toolsOnlyButton = CreateButton("story.toolsOnly", 110f, -330f, 200f,
            () => Choose(ToolsOnlyChosen));

        CreateButton("story.close", 0f, -372f, 130f, Hide);

        _panel.SetActive(false);
    }

    private Button CreateButton(string labelKey, float x, float y, float width, Action onClick)
    {
        GameObject button = GUIManager.Instance.CreateButton(
            AtlasStrings.Get(labelKey), _panel!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, y), width, 32f);
        var component = button.GetComponent<Button>();
        component.onClick.AddListener(() =>
        {
            try
            {
                onClick();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        });
        return component;
    }

    private void Fail(Exception exception)
    {
        _failed = true;
        _inputBlock.Release();
        if (_panel != null)
        {
            _panel.SetActive(false);
        }

        _log.LogError(
            "The companion introduction panel failed and was disabled for this session; the console " +
            $"command `companion story` still describes it: {SafeLogText.Describe(exception)}");
    }

    /// <summary>Scene teardown. The GameObject dies with the scene, so the
    /// reference is dropped and the input block released; the panel rebuilds
    /// itself on the next open.</summary>
    public void ResetForSceneChange()
    {
        _inputBlock.Release();
        _panel = null;
        _speaker = null;
        _body = null;
        _pageLabel = null;
        _backButton = null;
        _nextButton = null;
        _welcomeButton = null;
        _toolsOnlyButton = null;
    }
}
