using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Body;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>The presentation body, which moves by <b>refusal</b> rather than by
/// cleanup.
///
/// The property under test is not "the finished figure has no networking or AI
/// component in it". It is the stronger one: the finished figure has never
/// contained one. Every test below therefore checks two things - that the
/// candidate was refused, and that the root was never switched on.</summary>
public sealed class PresentationBodyTests
{
    private readonly List<string> _log = new List<string>();

    [Fact]
    public void A_source_with_no_animator_is_refused()
    {
        var source = new GameObject("Dverger");
        source.Add(new ZNetView());

        NpcPresentationFigure figure = Extract(source);

        Assert.False(figure.IsBuilt);
        Assert.Contains("no animator", figure.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void An_animator_on_the_same_object_as_the_game_components_is_refused()
    {
        // Extracting it would mean extracting them.
        var source = new GameObject("Dverger");
        source.Add(new ZNetView());
        source.Add(new Character());
        source.Add(new Animator());

        NpcPresentationFigure figure = Extract(source);

        Assert.False(figure.IsBuilt);
        Assert.Contains("same object as its game components", figure.Refusal, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ZNetView")]
    [InlineData("Player")]
    [InlineData("Character")]
    [InlineData("BaseAI")]
    [InlineData("Tameable")]
    public void A_forbidden_component_inside_the_visual_refuses_the_whole_candidate(string offender)
    {
        GameObject source = Character(visual =>
        {
            switch (offender)
            {
                case "ZNetView":
                    visual.Add(new ZNetView());
                    break;
                case "Player":
                    visual.Add(new Player());
                    break;
                case "Character":
                    visual.Add(new Character());
                    break;
                case "BaseAI":
                    visual.Add(new BaseAI());
                    break;
                default:
                    visual.Add(new Tameable());
                    break;
            }
        });

        NpcPresentationFigure figure = Extract(source);

        Assert.False(figure.IsBuilt);
        Assert.Contains(offender, figure.Refusal, StringComparison.Ordinal);

        // Nothing was stripped: the candidate was refused.
        Assert.Contains("refused", figure.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_script_this_build_will_not_destroy_refuses_the_candidate_rather_than_being_disabled_over()
    {
        // Disabling is not safety: the engine runs Awake on activation whether
        // a component is enabled or not.
        GameObject source = Character(visual =>
        {
            var stubborn = new CharacterAnimEvent();
            stubborn.Indestructible = true;
            visual.Add(stubborn);
        });

        NpcPresentationFigure figure = Extract(source);

        Assert.False(figure.IsBuilt);
        Assert.Contains("CharacterAnimEvent", figure.Refusal, StringComparison.Ordinal);
        Assert.Contains("Nothing was enabled", figure.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_clean_candidate_is_built_active_with_the_source_scripts_already_gone()
    {
        GameObject source = Character(visual => visual.Add(new CharacterAnimEvent()));

        NpcPresentationFigure figure = Extract(source);

        Assert.True(figure.IsBuilt);
        Assert.True(figure.Root!.activeSelf);
        Assert.Equal(1, figure.QuietedScripts);
        Assert.Contains("CharacterAnimEvent", figure.QuietedNames, StringComparison.Ordinal);
        Assert.Empty(figure.Root.GetComponentsInChildren<CharacterAnimEvent>(includeInactive: true));
        Assert.NotNull(figure.Animator);
    }

    [Fact]
    public void Physics_is_removed_rather_than_refused()
    {
        // A collider carries no registration, so removing one leaves no trace.
        // Leaving one would mean a player walking into an invisible wall where
        // their companion stands.
        GameObject source = Character(visual =>
        {
            visual.Add(new Collider());
            visual.Add(new Rigidbody());
        });

        NpcPresentationFigure figure = Extract(source);

        Assert.True(figure.IsBuilt);
        Assert.Empty(figure.Root!.GetComponentsInChildren<Collider>(includeInactive: true));
        Assert.Empty(figure.Root.GetComponentsInChildren<Rigidbody>(includeInactive: true));
    }

    [Fact]
    public void The_callers_own_scripts_are_left_where_the_caller_put_them()
    {
        GameObject source = Character(visual => visual.Add(new NpcBody()));

        NpcPresentationFigure figure = Extract(source, ownNamespacePrefix: "TheConcernedCat");

        Assert.True(figure.IsBuilt);
        Assert.NotNull(figure.Root!.GetComponentInChildren<NpcBody>(includeInactive: true));
        Assert.Equal(0, figure.QuietedScripts);
    }

    [Fact]
    public void An_empty_own_namespace_takes_every_script_out()
    {
        GameObject source = Character(visual => visual.Add(new NpcBody()));

        NpcPresentationFigure figure = Extract(source);

        Assert.True(figure.IsBuilt);
        Assert.Equal(1, figure.QuietedScripts);
    }

    [Fact]
    public void Joints_outside_the_part_that_was_kept_are_dropped_rather_than_dangling()
    {
        var source = new GameObject("Dverger");
        source.Add(new ZNetView());
        source.Add(new Character());
        var visual = new GameObject("Visual");
        visual.transform.SetParent(source.transform, worldPositionStays: false);
        visual.Add(new Animator());

        var insideJoint = new GameObject("RightHand");
        insideJoint.transform.SetParent(visual.transform, worldPositionStays: false);
        var outsideJoint = new GameObject("Helmet");
        outsideJoint.transform.SetParent(source.transform, worldPositionStays: false);

        VisEquipment equipment = source.Add(new VisEquipment());
        equipment.m_rightHand = insideJoint.transform;
        equipment.m_helmet = outsideJoint.transform;

        NpcPresentationFigure figure = Extract(source);

        Assert.True(figure.IsBuilt);
        Assert.NotNull(figure.RightHandJoint);
        Assert.Null(figure.HelmetJoint);
    }

    [Fact]
    public void There_is_no_figure_without_a_source()
    {
        NpcPresentationFigure figure = Extract(null);

        Assert.False(figure.IsBuilt);
        Assert.Contains("no prefab", figure.Refusal, StringComparison.Ordinal);
    }

    /// <summary>A character prefab shaped the way the game's are: the
    /// networking, the character and the mind on the root, the animated body in
    /// a child.</summary>
    private static GameObject Character(Action<GameObject>? decorateVisual = null)
    {
        var source = new GameObject("Dverger");
        source.Add(new ZNetView());
        source.Add(new Character());
        source.Add(new BaseAI());

        var visual = new GameObject("Visual");
        visual.transform.SetParent(source.transform, worldPositionStays: false);
        visual.Add(new Animator());
        visual.Add(new SkinnedMeshRenderer());
        decorateVisual?.Invoke(visual);
        return source;
    }

    private NpcPresentationFigure Extract(GameObject? source, string ownNamespacePrefix = "")
        => NpcPresentationBody.TryExtract(
            source, "Dverger", "Hulgi", "Harvest", ownNamespacePrefix, _log.Add);
}
