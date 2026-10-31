using System;
using TheConcernedCat.ConcernedNPC.Appearance;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Body.Dressing;

/// <summary>Dresses, stands and takes down one extracted presentation figure:
/// the body it wears, hair, beard, colouring and garments, from a role's
/// <see cref="NpcLookSpec"/> (lifted from Concerned Foreman's
/// <c>ThorsteinDresser</c>, #295/#298, so Thorstein, Sunniva and Gunnar share
/// one copy). The dressing itself is <see cref="NpcDressing"/>, shared with
/// <see cref="NpcWorkerDresser"/>; this type adds what only a figure has - it is
/// this package's own, so it may be stood somewhere and taken down whole.
///
/// <b>Local only.</b> Nothing here is networked, saved, given to anybody or
/// taken from anybody.
///
/// <b>Why every public member takes no object.</b> A consumer's own audits
/// (Foreman's #400, for one) pin the engine writes that product makes. A
/// library member that destroyed or moved whatever it was handed would be a
/// way round them that spells no audited token. So this type is constructed
/// from the figure handle the extraction returned, and acts only on that
/// figure and the pieces it put there itself.</summary>
public sealed class NpcFigureDresser
{
    private readonly GameObject _root;
    private readonly NpcDressing _dressing;
    private bool _takenDown;

    /// <param name="figure">A figure <see cref="NpcPresentationBody.TryExtract"/>
    /// built. A refused one is an argument error.</param>
    /// <param name="spec">What the role asks the figure to look like.</param>
    /// <param name="holderName">The name of the inactive holder each piece is
    /// cloned under. The role's, like the extraction's holder name.</param>
    /// <param name="log">Where every refusal and fallback is said.</param>
    public NpcFigureDresser(in NpcPresentationFigure figure, NpcLookSpec spec, string holderName, Action<string>? log)
    {
        _root = figure.Root ?? throw new ArgumentException("A built figure is required.", nameof(figure));
        _dressing = new NpcDressing(figure, spec, holderName, log);
    }

    public NpcLookChoice Hair => _dressing.Hair;

    public NpcLookChoice Beard => _dressing.Beard;

    public NpcLookChoice Chest => _dressing.Chest;

    public NpcLookChoice Legs => _dressing.Legs;

    public bool HairWorn => _dressing.HairWorn;

    public bool BeardWorn => _dressing.BeardWorn;

    public bool ChestWorn => _dressing.ChestWorn;

    public bool LegsWorn => _dressing.LegsWorn;

    public bool SkinApplied => _dressing.SkinApplied;

    public bool PaletteObserved => _dressing.PaletteObserved;

    /// <summary>True when the body asked for is the one worn: always for
    /// <see cref="NpcBodyModel.Male"/>, which changes nothing, and for
    /// <see cref="NpcBodyModel.Female"/> only once the second body was put on.
    /// A role that must not show the wrong body checks this after
    /// <see cref="Apply"/>.</summary>
    public bool ModelApplied => _dressing.ModelApplied;

    public string Fit => _dressing.Fit;

    /// <summary>One line for a status command.</summary>
    public string Describe() => _dressing.Describe();

    /// <summary>Dresses the figure, once.</summary>
    public void Apply()
    {
        if (!_takenDown)
        {
            _dressing.Apply();
        }
    }

    /// <summary>Stands the figure at <paramref name="spot"/>, facing
    /// <paramref name="facing"/>. Only this dresser's own figure.</summary>
    public void StandAt(Vector3 spot, Quaternion facing)
    {
        if (_takenDown || _root == null)
        {
            return;
        }

        NpcFigureParts.Place(_root.transform, spot, facing);
    }

    /// <summary>Measures every piece once, after the figure has been drawn.</summary>
    public void VerifyOnce()
    {
        if (!_takenDown)
        {
            _dressing.VerifyOnce();
        }
    }

    /// <summary>Takes the figure down: dark at once, so it is never drawn beside
    /// a worker body created in the same frame, and gone at the end of the
    /// frame, with every piece and the body material instance the dressing
    /// made. Idempotent.</summary>
    public void TakeDown()
    {
        if (_takenDown)
        {
            return;
        }

        _takenDown = true;
        if (_root != null)
        {
            _root.SetActive(false);
            NpcFigureParts.Drop(_root);
        }

        _dressing.TakeOff();
    }
}
