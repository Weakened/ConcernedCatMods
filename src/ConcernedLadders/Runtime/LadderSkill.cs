using System;
using Jotunn.Configs;
using Jotunn.Managers;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Runtime;

/// <summary>The native Climbing skill. Registered once in plugin Awake, before any player's Skills awake,
/// whether or not climbing is enabled. Progress, its UI, save/load and death loss are all the game's own.</summary>
internal sealed class LadderSkill
{
    internal const string Identifier = "com.theconcernedcat.valheim.concernedladders.climbing";

    private readonly Action<string> _log;
    private Player? _levelOwner;
    private float _levelAge = float.PositiveInfinity;
    private float _level;

    internal LadderSkill(Action<string> log) => _log = log;

    /// <summary>AddSkill's returned enum value. SkillManager.GetSkill would return the definition, not progress.</summary>
    internal Skills.SkillType Type { get; private set; } = Skills.SkillType.None;

    internal bool Ready => Type != Skills.SkillType.None;

    internal void Register()
    {
        if (Ready) { return; }
        try
        {
            Type = SkillManager.Instance.AddSkill(new SkillConfig
            {
                Identifier = Identifier,
                Name = "$cl_skill_climbing",
                Description = "$cl_skill_climbing_description",
                Icon = CreateIcon(),
                IncreaseStep = 1f,
            });
            if (!Ready) { _log("Climbing skill registration returned no skill type; climbing works without skill progress."); }
        }
        catch (Exception exception)
        {
            Type = Skills.SkillType.None;
            _log("Climbing skill registration failed (" + SafeFailure.Brief(exception) + "); climbing works without skill progress.");
        }
    }

    /// <summary>The player's current learned level: the stored level, not a buffed or rounded value.</summary>
    internal float LearnedLevel(Player player, float delta)
    {
        if (!Ready || player == null) { return 0f; }
        _levelAge += ClimbPolicy.Finite(delta) && delta > 0f ? delta : 0f;
        if (ReferenceEquals(player, _levelOwner) && _levelAge < 0.25f) { return _level; }
        _levelOwner = player;
        _levelAge = 0f;
        _level = 0f;
        Skills skills = player.GetSkills();
        if (skills == null) { return 0f; }
        foreach (Skills.Skill skill in skills.GetSkillList())
        {
            if (skill?.m_info != null && skill.m_info.m_skill == Type)
            {
                _level = ClimbPolicy.Finite(skill.m_level) ? skill.m_level : 0f;
                break;
            }
        }

        return _level;
    }

    /// <summary>A completed skill metre changes progress; the next read must see the new level at once.</summary>
    internal void Raise(Player player, int metres)
    {
        if (!Ready || player == null || metres <= 0) { return; }
        for (int index = 0; index < Math.Min(metres, 4); index++)
        {
            player.RaiseSkill(Type, ClimbPolicy.SkillFactorPerMetre);
        }

        _levelAge = float.PositiveInfinity;
    }

    internal void Forget() { _levelOwner = null; _levelAge = float.PositiveInfinity; }

    /// <summary>An original icon drawn in code: two rails, rungs and an upward chevron, no game asset.</summary>
    private static Sprite CreateIcon()
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "cl_skill_climbing_icon" };
        var clear = new Color(0f, 0f, 0f, 0f);
        var wood = new Color(0.93f, 0.83f, 0.62f, 1f);
        var edge = new Color(0.2f, 0.13f, 0.07f, 1f);
        var pixels = new Color[size * size];
        for (int index = 0; index < pixels.Length; index++) { pixels[index] = clear; }

        void Box(int x0, int y0, int x1, int y1, Color colour)
        {
            for (int y = Math.Max(0, y0); y <= Math.Min(size - 1, y1); y++)
                for (int x = Math.Max(0, x0); x <= Math.Min(size - 1, x1); x++)
                    pixels[y * size + x] = colour;
        }

        void Outlined(int x0, int y0, int x1, int y1)
        {
            Box(x0 - 2, y0 - 2, x1 + 2, y1 + 2, edge);
            Box(x0, y0, x1, y1, wood);
        }

        Outlined(14, 4, 20, 50);
        Outlined(43, 4, 49, 50);
        for (int rung = 0; rung < 4; rung++)
        {
            int y = 9 + rung * 11;
            Outlined(21, y, 42, y + 3);
        }

        for (int step = 0; step < 9; step++)
        {
            Box(31 - step - 2, 60 - step / 2 - 1, 32 + step + 2, 60 - step / 2 + 1, edge);
        }

        for (int step = 0; step < 8; step++)
        {
            Box(31 - step, 60 - step / 2, 32 + step, 60 - step / 2, wood);
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
