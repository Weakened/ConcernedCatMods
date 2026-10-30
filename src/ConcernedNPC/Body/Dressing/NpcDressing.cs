using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Appearance;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Body.Dressing;

/// <summary>The dressing itself, shared by the two public dressers (#295):
/// <see cref="NpcFigureDresser"/>, which owns a presentation figure, and
/// <see cref="NpcWorkerDresser"/>, which dresses a role's own worker body
/// without owning it (owner decision 2026-10-29, option (b)).
///
/// <b>The pattern is Hulgi's</b>, from Concerned Cartographer's
/// <c>CompanionActor</c>: a preset is an item's <c>attach</c> or
/// <c>attach_skin</c> child cloned under an <b>inactive</b> holder and inspected
/// before it may wake (a forbidden component, or a script this build will not
/// let us destroy, refuses the piece); a skinned piece takes the body's own bone
/// array verbatim (#305); colour goes through a property block on
/// <c>_SkinColor</c>; and a moment after the body is first drawn every piece is
/// measured and one that is not where it was put is taken off.
///
/// <b>Two modes.</b> On a whole figure it may also swap the body, tint the
/// body's skin and paint garments: the figure is this package's own. On a
/// worker body it does <b>none</b> of that. It only attaches hair and beard
/// pieces as children and tints those pieces, because the body, its renderer
/// and its material belong to the game and to the role's runtime.
///
/// <b>It never destroys what it did not make.</b> <see cref="TakeOff"/> drops
/// the pieces it attached and the material instance it created; taking a whole
/// figure down is <see cref="NpcFigureDresser"/>'s, in its own file. Every
/// engine write that creates, destroys, parents, moves or reshapes is in
/// <see cref="NpcFigureParts"/>, where the repository's audit pins it.</summary>
internal sealed class NpcDressing
{
    private static readonly int SkinColourProperty = Shader.PropertyToID("_SkinColor");
    private static readonly int MainTexProperty = Shader.PropertyToID("_MainTex");
    private static readonly int SkinBumpProperty = Shader.PropertyToID("_SkinBumpMap");
    private static readonly int ChestTexProperty = Shader.PropertyToID("_ChestTex");
    private static readonly int ChestBumpProperty = Shader.PropertyToID("_ChestBumpMap");
    private static readonly int ChestMetalProperty = Shader.PropertyToID("_ChestMetal");
    private static readonly int LegsTexProperty = Shader.PropertyToID("_LegsTex");
    private static readonly int LegsBumpProperty = Shader.PropertyToID("_LegsBumpMap");
    private static readonly int LegsMetalProperty = Shader.PropertyToID("_LegsMetal");

    private readonly NpcPresentationFigure? _figure;
    private readonly bool _wholeFigure;
    private readonly GameObject _root;
    private readonly SkinnedMeshRenderer? _bodyModel;
    private readonly Transform? _helmetJoint;
    private readonly NpcLookSpec _spec;
    private readonly string _holderName;
    private readonly Action<string> _log;
    private readonly Dictionary<string, GameObject> _attached = new Dictionary<string, GameObject>(StringComparer.Ordinal);

    private Material? _bodyMaterial;
    private bool _verified;
    private bool _takenOff;
    private bool _applied;

    /// <summary>Dresses a whole presentation figure this package extracted.</summary>
    internal NpcDressing(in NpcPresentationFigure figure, NpcLookSpec spec, string holderName, Action<string>? log)
        : this(figure.Root, figure.BodyModel, figure.HelmetJoint, figure, true, spec, holderName, log)
    {
    }

    /// <summary>Dresses a worker body that belongs to a role: hair and beard
    /// pieces only. The caller has already proved it is that role's own body.</summary>
    internal NpcDressing(
        GameObject root, SkinnedMeshRenderer? bodyModel, Transform? helmetJoint, NpcLookSpec spec, string holderName,
        Action<string>? log)
        : this(root, bodyModel, helmetJoint, null, false, spec, holderName, log)
    {
    }

    private NpcDressing(
        GameObject? root, SkinnedMeshRenderer? bodyModel, Transform? helmetJoint, NpcPresentationFigure? figure,
        bool wholeFigure, NpcLookSpec spec, string holderName, Action<string>? log)
    {
        _root = root ?? throw new ArgumentException("A body to dress is required.", nameof(root));
        _figure = figure;
        _wholeFigure = wholeFigure;
        _bodyModel = bodyModel;
        _helmetJoint = helmetJoint;
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        if (string.IsNullOrWhiteSpace(holderName))
        {
            throw new ArgumentException("The role names the holder.", nameof(holderName));
        }

        _holderName = holderName;
        _log = log ?? (_ => { });
    }

    internal NpcLookChoice Hair { get; private set; } = NpcLookChoice.None;

    internal NpcLookChoice Beard { get; private set; } = NpcLookChoice.None;

    internal NpcLookChoice Chest { get; private set; } = NpcLookChoice.None;

    internal NpcLookChoice Legs { get; private set; } = NpcLookChoice.None;

    internal bool HairWorn { get; private set; }

    internal bool BeardWorn { get; private set; }

    internal bool ChestWorn { get; private set; }

    internal bool LegsWorn { get; private set; }

    internal bool SkinApplied { get; private set; }

    internal bool PaletteObserved { get; private set; }

    /// <summary>True when the body asked for is the one worn: always for
    /// <see cref="NpcBodyModel.Male"/>, which changes nothing, and for
    /// <see cref="NpcBodyModel.Female"/> only once the second body was put on.
    /// A role that must not show the wrong body checks this after
    /// <see cref="Apply"/>.</summary>
    internal bool ModelApplied { get; private set; }

    internal string Fit { get; private set; } = "not measured yet";

    /// <summary>One line for a status command: what the figure is wearing, and
    /// whether it is the reference or a fallback. A slot the spec did not ask
    /// for is left out.</summary>
    internal string Describe()
    {
        var parts = new List<string>();
        if (_spec.Model != NpcBodyModel.Male)
        {
            parts.Add("body " + _spec.Model + (ModelApplied ? string.Empty : " (not applied)"));
        }

        AddSlot(parts, "hair", _spec.Hair, Hair, HairWorn);
        AddSlot(parts, "beard", _spec.Beard, Beard, BeardWorn);
        AddSlot(parts, "chest", _wholeFigure ? _spec.Chest : null, Chest, ChestWorn);
        AddSlot(parts, "legs", _wholeFigure ? _spec.Legs : null, Legs, LegsWorn);
        parts.Add("colour from " + (PaletteObserved ? "the game's palette" : "the stated fallback, palette not read")
            + (SkinApplied ? string.Empty : ", skin is the model's own"));
        parts.Add("fit [" + Fit + "]");
        return string.Join(", ", parts.ToArray());
    }

    /// <summary>Dresses the figure, once. Every part fails on its own and none of them
    /// can stop the figure existing: the worst case is the source model's own
    /// look, said in the log.</summary>
    internal void Apply()
    {
        // Once. A second pass would attach a second set of pieces over the
        // first, and the fit check would then take the first set off as
        // misplaced; a role that wants another look builds another figure.
        if (_takenOff || _applied)
        {
            return;
        }

        _applied = true;

        try
        {
            ModelApplied = _spec.Model == NpcBodyModel.Male || (_wholeFigure && ApplyModel((int)_spec.Model));

            List<NpcLookOption> hair = _spec.Hair == null
                ? new List<NpcLookOption>()
                : ReadItems(ItemDrop.ItemData.ItemType.Customization, "Hair", skipVariants: true);
            List<NpcLookOption> beards = _spec.Beard == null
                ? new List<NpcLookOption>()
                : ReadItems(ItemDrop.ItemData.ItemType.Customization, "Beard", skipVariants: true);
            List<NpcLookOption> chest = _spec.Chest == null || !_wholeFigure
                ? new List<NpcLookOption>()
                : ReadItems(ItemDrop.ItemData.ItemType.Chest, string.Empty, skipVariants: false);
            List<NpcLookOption> legs = _spec.Legs == null || !_wholeFigure
                ? new List<NpcLookOption>()
                : ReadItems(ItemDrop.ItemData.ItemType.Legs, string.Empty, skipVariants: false);

            Hair = _spec.Hair == null ? NpcLookChoice.None : NpcLookPlan.Choose(hair, _spec.Hair, _spec.HairOverride);
            Beard = _spec.Beard == null ? NpcLookChoice.None : NpcLookPlan.Choose(beards, _spec.Beard, _spec.BeardOverride);
            Chest = _spec.Chest == null || !_wholeFigure ? NpcLookChoice.None : NpcLookPlan.Choose(chest, _spec.Chest, _spec.ChestOverride);
            Legs = _spec.Legs == null || !_wholeFigure ? NpcLookChoice.None : NpcLookPlan.Choose(legs, _spec.Legs, _spec.LegsOverride);

            SayIfReferenceMissed();

            NpcLookPalette palette = NpcCustomizationPalette.Read(_log);
            PaletteObserved = palette.Observed;
            NpcLookRgb hairColour = _spec.HairColour(palette);

            if (_spec.Hair != null || _spec.Beard != null)
            {
                Transform? joint = _helmetJoint ?? FindHeadBone();
                if (joint == null)
                {
                    _log(_spec.Who + "'s head attachment point could not be found, so the hair and beard were "
                        + "not attached.");
                }
                else
                {
                    HairWorn = _spec.Hair != null && AttachPreset(Hair.PrefabName, joint, "hair", hairColour);
                    BeardWorn = _spec.Beard != null && AttachPreset(Beard.PrefabName, joint, "beard", hairColour);
                }
            }

            if (!_wholeFigure)
            {
                // A worker body's renderer and material are the game's and the
                // role's: nothing here tints, paints or reshapes them.
                return;
            }

            NpcLookRgb? skin = _spec.SkinColour(palette);
            SkinApplied = skin.HasValue && TintBody(0, skin.Value, "skin tone");
            TintBody(1, hairColour, "hair tint");

            ChestWorn = _spec.Chest != null && AttachGarment(Chest.PrefabName, "chest");
            LegsWorn = _spec.Legs != null && AttachGarment(Legs.PrefabName, "legs");
        }
        catch (Exception exception)
        {
            _log(_spec.Who + "'s look could not be applied on this build; the model's own is kept: " +
                SafeFailure.Brief(exception));
        }
    }

    /// <summary>Measures every piece once, after the figure has been drawn for
    /// at least one frame. Before that a skinned mesh's bones are unposed and
    /// every piece looks right however it is bound.</summary>
    internal void VerifyOnce()
    {
        if (_takenOff || _verified || _root == null || !_root.activeInHierarchy)
        {
            return;
        }

        _verified = true;
        if (_attached.Count == 0)
        {
            Fit = "nothing attached";
            return;
        }

        Transform? head = _helmetJoint ?? FindHeadBone();
        var measured = new List<string>();
        foreach (KeyValuePair<string, GameObject> piece in _attached)
        {
            if (piece.Value == null)
            {
                continue;
            }

            bool garment = piece.Key.StartsWith("chest", StringComparison.Ordinal)
                || piece.Key.StartsWith("legs", StringComparison.Ordinal);
            Vector3? reference = garment && _bodyModel != null
                ? _bodyModel.bounds.center
                : head != null ? head.position : (Vector3?)null;
            if (reference == null)
            {
                measured.Add(piece.Key + " unmeasured");
                continue;
            }

            float tolerance = garment ? NpcLookFit.GarmentToleranceMetres : NpcLookFit.HeadToleranceMetres;
            float distance = DistanceFrom(piece.Value, reference.Value);
            bool fits = NpcLookFit.Fits(distance, tolerance);
            measured.Add(piece.Key + " " + distance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                + "m" + (fits ? string.Empty : " REMOVED"));
            if (fits)
            {
                continue;
            }

            _log(_spec.Who + "'s " + piece.Key + " piece is not drawn where it was put on this build, so it was " +
                "taken off rather than left floating.");
            MarkRemoved(piece.Key);
            NpcFigureParts.Drop(piece.Value);
        }

        Fit = string.Join(", ", measured.ToArray());
    }

    /// <summary>Drops every piece this dressing attached and the material
    /// instance it created, and nothing else: never the body it was put on.
    /// Idempotent. A piece already gone with its body is skipped.</summary>
    internal void TakeOff()
    {
        if (_takenOff)
        {
            return;
        }

        _takenOff = true;
        foreach (GameObject piece in _attached.Values)
        {
            if (piece != null)
            {
                NpcFigureParts.Drop(piece);
            }
        }

        _attached.Clear();
        HairWorn = false;
        BeardWorn = false;
        ChestWorn = false;
        LegsWorn = false;
        if (_bodyMaterial != null)
        {
            NpcFigureParts.Drop(_bodyMaterial);
            _bodyMaterial = null;
        }
    }

    private void SayIfReferenceMissed()
    {
        var asked = new List<string>();
        var got = new List<string>();
        if (_spec.Hair != null)
        {
            asked.Add(_spec.Hair.DisplayName);
            got.Add("hair " + Hair);
        }

        if (_spec.Beard != null)
        {
            asked.Add(_spec.Beard.DisplayName);
            got.Add("beard " + Beard);
        }

        bool missed = (_spec.Hair != null && !Hair.MatchesReference) || (_spec.Beard != null && !Beard.MatchesReference);
        if (missed)
        {
            _log(_spec.Who + "'s reference look (" + string.Join(", ", asked.ToArray()) + ") could not be matched "
                + "exactly on this build (" + string.Join(", ", got.ToArray()) + "). The closest available presets "
                + "are worn.");
        }
    }

    private static void AddSlot(List<string> parts, string slot, NpcLookRequest? asked, NpcLookChoice choice, bool worn)
    {
        if (asked != null)
        {
            parts.Add(slot + " " + choice + (worn ? string.Empty : " (not worn)"));
        }
    }

    // --- the body -------------------------------------------------------------------------------------

    /// <summary>The source character's body number <paramref name="index"/>,
    /// put on as the game's equipment component does: mesh first, then the
    /// base, chest and legs textures of that body on the figure's own
    /// material instance (a garment painted later overrides chest and legs).
    /// </summary>
    private bool ApplyModel(int index)
    {
        if (_bodyModel == null)
        {
            _log(_spec.Who + " has no body renderer on this build, so the " + (NpcBodyModel)index
                + " body could not be put on.");
            return false;
        }

        if (_figure == null || !_figure.Value.TryGetModel(index, out Mesh? mesh, out Material? baseMaterial)
            || mesh == null
            || baseMaterial == null)
        {
            _log("The source character offers no body number " + index + " on this build, so " + _spec.Who
                + " keeps the first body.");
            return false;
        }

        try
        {
            NpcFigureParts.Reshape(_bodyModel, mesh);
            Material body = _bodyMaterial ??= _bodyModel.material;
            CopyTexture(body, baseMaterial, MainTexProperty);
            CopyTexture(body, baseMaterial, SkinBumpProperty);
            CopyTexture(body, baseMaterial, ChestTexProperty);
            CopyTexture(body, baseMaterial, ChestBumpProperty);
            CopyTexture(body, baseMaterial, LegsTexProperty);
            CopyTexture(body, baseMaterial, LegsBumpProperty);
            return true;
        }
        catch (Exception exception)
        {
            _log(_spec.Who + "'s " + (NpcBodyModel)index + " body could not be put on: "
                + SafeFailure.Brief(exception));
            return false;
        }
    }

    private static void CopyTexture(Material to, Material from, int property)
    {
        if (from.HasProperty(property))
        {
            to.SetTexture(property, from.GetTexture(property));
        }
    }

    // --- hair and beard ------------------------------------------------------------------------------

    private bool AttachPreset(string? prefabName, Transform joint, string slot, NpcLookRgb colour)
    {
        GameObject? prefab = FindItem(prefabName);
        if (prefab == null)
        {
            return false;
        }

        GameObject? attach = FindAttachChild(prefab, out bool namedForSkin);
        if (attach == null)
        {
            _log(_spec.Who + "'s " + slot + " preset \"" + prefabName + "\" has no attachment mesh on this build.");
            return false;
        }

        GameObject holder = NpcFigureParts.Holder(_holderName);
        GameObject? piece = null;
        try
        {
            piece = NpcFigureParts.Harvest(attach, holder);
            if (!Quieten(piece, slot, prefabName!))
            {
                return false;
            }

            bool skinned = namedForSkin || piece.GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true) != null;
            if (skinned && _bodyModel != null)
            {
                NpcFigureParts.Seat(piece.transform, _bodyModel.transform.parent);
                BindToBody(piece);
            }
            else
            {
                // worldPositionStays defaults to true, which is what keeps the
                // piece at its own world scale under a joint that is not at
                // unit scale. Hulgi's braid hovered a metre up without it.
                NpcFigureParts.Seat(piece.transform, joint);
            }

            NpcFigureParts.Offset(prefab, piece.transform);
            Tint(piece, colour);

            GameObject attached = piece;
            piece = null;
            attached.SetActive(true);
            _attached[slot] = attached;
            return true;
        }
        catch (Exception exception)
        {
            _log(_spec.Who + "'s " + slot + " preset could not be attached: " + SafeFailure.Brief(exception));
            return false;
        }
        finally
        {
            NpcFigureParts.Discard(piece);
            NpcFigureParts.Discard(holder);
        }
    }

    // --- garments -------------------------------------------------------------------------------------

    private bool AttachGarment(string? prefabName, string slot)
    {
        GameObject? prefab = FindItem(prefabName);
        if (prefab == null)
        {
            return false;
        }

        bool anything = PaintGarment(prefab, slot);
        int children = prefab.transform.childCount;
        for (int index = 0; index < children; index++)
        {
            Transform child = prefab.transform.GetChild(index);
            if (child == null || !child.gameObject.name.StartsWith("attach_", StringComparison.Ordinal))
            {
                continue;
            }

            string jointName = child.gameObject.name.Substring("attach_".Length);
            if (AttachGarmentPiece(child.gameObject, jointName, slot, prefabName!))
            {
                anything = true;
            }
        }

        return anything;
    }

    private bool AttachGarmentPiece(GameObject source, string jointName, string slot, string prefabName)
    {
        GameObject holder = NpcFigureParts.Holder(_holderName);
        GameObject? piece = null;
        try
        {
            piece = NpcFigureParts.Harvest(source, holder);
            if (!Quieten(piece, slot, prefabName))
            {
                return false;
            }

            if (string.Equals(jointName, "skin", StringComparison.Ordinal))
            {
                if (_bodyModel == null)
                {
                    return false;
                }

                NpcFigureParts.Seat(piece.transform, _bodyModel.transform.parent);
                BindToBody(piece);
            }
            else
            {
                Transform? joint = FindBoneNamed(jointName);
                if (joint == null)
                {
                    return false;
                }

                NpcFigureParts.Seat(piece.transform, joint);
            }

            GameObject attached = piece;
            piece = null;
            attached.SetActive(true);
            _attached[slot + ":" + jointName] = attached;
            return true;
        }
        catch (Exception exception)
        {
            _log(_spec.Who + "'s " + slot + " garment could not be attached: " + SafeFailure.Brief(exception));
            return false;
        }
        finally
        {
            NpcFigureParts.Discard(piece);
            NpcFigureParts.Discard(holder);
        }
    }

    /// <summary>The garment's own chest or leg textures on the body, as the
    /// game does when armour is put on, so the skin under a tunic does not read
    /// as bare. Written to this renderer's own material instance, never the
    /// shared asset.</summary>
    private bool PaintGarment(GameObject prefab, string slot)
    {
        if (_bodyModel == null)
        {
            return false;
        }

        try
        {
            Material? armour = prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_armorMaterial;
            if (armour == null)
            {
                return false;
            }

            bool legs = string.Equals(slot, "legs", StringComparison.Ordinal);
            int tex = legs ? LegsTexProperty : ChestTexProperty;
            int bump = legs ? LegsBumpProperty : ChestBumpProperty;
            int metal = legs ? LegsMetalProperty : ChestMetalProperty;

            Material body = _bodyMaterial ??= _bodyModel.material;
            body.SetTexture(tex, armour.GetTexture(tex));
            body.SetTexture(bump, armour.GetTexture(bump));
            body.SetTexture(metal, armour.GetTexture(metal));
            return true;
        }
        catch (Exception exception)
        {
            _log(_spec.Who + "'s " + slot + " garment texture could not be applied: " + SafeFailure.Brief(exception));
            return false;
        }
    }

    // --- refusing a piece rather than cleaning it -----------------------------------------------------

    /// <summary>Whether a component inside a harvested piece refuses it. A
    /// method rather than a list so the set is in one place (and so this
    /// package keeps no string table).</summary>
    private static bool IsForbiddenComponentName(string name)
    {
        switch (name)
        {
            case "ZNetView":
            case "ZSyncAnimation":
            case "Player":
            case "Character":
            case "Humanoid":
            case "BaseAI":
            case "MonsterAI":
            case "AnimalAI":
            case "NpcTalk":
            case "Tameable":
            case "ItemDrop":
                return true;
            default:
                return false;
        }
    }

    /// <summary>Takes physics, cloth and scripts out of a piece that has not
    /// woken. False when the piece must be refused: a forbidden component is
    /// inside it, or a script survives two passes.</summary>
    private bool Quieten(GameObject piece, string slot, string prefabName)
    {
        foreach (Component component in piece.GetComponentsInChildren<Component>(includeInactive: true))
        {
            if (component != null && IsForbiddenComponentName(component.GetType().Name))
            {
                _log(_spec.Who + "'s " + slot + " \"" + prefabName + "\" carries " + component.GetType().Name +
                    ", so it was refused. Nothing was stripped.");
                return false;
            }
        }

        foreach (Collider collider in piece.GetComponentsInChildren<Collider>(includeInactive: true))
        {
            NpcFigureParts.Discard(collider);
        }

        foreach (Rigidbody body in piece.GetComponentsInChildren<Rigidbody>(includeInactive: true))
        {
            NpcFigureParts.Discard(body);
        }

        foreach (Cloth cloth in piece.GetComponentsInChildren<Cloth>(includeInactive: true))
        {
            NpcFigureParts.Discard(cloth);
        }

        // Third-party cloth (MagicaCloth) is matched by name, never referenced,
        // and disabled rather than destroyed: a component another one requires
        // cannot be destroyed, and the engine refuses that by logging, not by
        // throwing. One that never receives OnEnable never builds itself.
        foreach (Behaviour behaviour in piece.GetComponentsInChildren<Behaviour>(includeInactive: true))
        {
            if (behaviour != null && behaviour.GetType().Name.IndexOf("Cloth", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                behaviour.enabled = false;
            }
        }

        var refused = new List<string>();
        for (int pass = 0; pass < 2; pass++)
        {
            refused.Clear();
            foreach (MonoBehaviour script in piece.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
            {
                if (script == null)
                {
                    continue;
                }

                string name = script.GetType().Name;
                if (name.IndexOf("Cloth", StringComparison.OrdinalIgnoreCase) >= 0 && !script.enabled)
                {
                    // Already quiet, and possibly required by something else.
                    continue;
                }

                NpcFigureParts.Discard(script);
                if (script != null)
                {
                    script.enabled = false;
                    refused.Add(name);
                }
            }
        }

        if (refused.Count > 0)
        {
            _log(_spec.Who + "'s " + slot + " \"" + prefabName + "\" keeps " + string.Join(", ", refused.ToArray()) +
                ", which this build will not let us remove, so it was refused rather than worn.");
            return false;
        }

        return true;
    }

    // --- binding and colour ---------------------------------------------------------------------------

    private void BindToBody(GameObject piece)
    {
        if (_bodyModel == null || _bodyModel.bones == null || _bodyModel.bones.Length == 0)
        {
            return;
        }

        Transform[] bodyBones = _bodyModel.bones;
        foreach (SkinnedMeshRenderer mesh in piece.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
        {
            if (mesh == null)
            {
                continue;
            }

            int bindposes = mesh.sharedMesh == null ? -1 : mesh.sharedMesh.bindposes.Length;
            switch (NpcLookSkinBindings.Decide(bindposes, bodyBones.Length))
            {
                case NpcLookSkinBinding.AdoptBodySkeleton:
                    mesh.rootBone = _bodyModel.rootBone;
                    mesh.bones = bodyBones;
                    mesh.updateWhenOffscreen = true;
                    break;

                case NpcLookSkinBinding.MatchByName:
                    RebindByName(mesh, bodyBones);
                    break;
            }
        }
    }

    private void RebindByName(SkinnedMeshRenderer mesh, Transform[] bodyBones)
    {
        Transform[] source = mesh.bones;
        if (source == null || source.Length == 0)
        {
            return;
        }

        var byName = new Dictionary<string, Transform>(StringComparer.Ordinal);
        foreach (Transform bone in bodyBones)
        {
            if (bone != null && !byName.ContainsKey(bone.name))
            {
                byName[bone.name] = bone;
            }
        }

        var mapped = new Transform[source.Length];
        for (int index = 0; index < source.Length; index++)
        {
            if (source[index] == null || !byName.TryGetValue(source[index].name, out Transform? match))
            {
                // Half a skeleton is a worse bug than none: leave this mesh as
                // it came, and let the fit check take it off if it misses.
                return;
            }

            mapped[index] = match!;
        }

        mesh.bones = mapped;
        mesh.rootBone = _bodyModel!.rootBone;
        mesh.updateWhenOffscreen = true;
    }

    private static void Tint(GameObject piece, NpcLookRgb colour)
    {
        var tint = new Color(colour.R, colour.G, colour.B, 1f);
        var block = new MaterialPropertyBlock();
        foreach (Renderer renderer in piece.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            if (renderer == null)
            {
                continue;
            }

            renderer.GetPropertyBlock(block);
            block.SetColor(SkinColourProperty, tint);
            renderer.SetPropertyBlock(block);
        }
    }

    /// <summary>Skin on submaterial 0 and the model's own hair on 1, the split
    /// the game uses. A property block, so the shared material is untouched.</summary>
    private bool TintBody(int materialIndex, NpcLookRgb colour, string what)
    {
        if (_bodyModel == null)
        {
            return false;
        }

        try
        {
            var block = new MaterialPropertyBlock();
            _bodyModel.GetPropertyBlock(block, materialIndex);
            block.SetColor(SkinColourProperty, new Color(colour.R, colour.G, colour.B, 1f));
            _bodyModel.SetPropertyBlock(block, materialIndex);
            return true;
        }
        catch (Exception exception)
        {
            _log(_spec.Who + "'s " + what + " could not be applied: " + SafeFailure.Brief(exception));
            return false;
        }
    }

    // --- reading the build ----------------------------------------------------------------------------

    /// <summary>What this build offers, asked of the item table exactly as the
    /// game's own customization screen asks, with each label localized.
    /// Read-only: names, tokens and labels are copied out and nothing is
    /// created.</summary>
    private static List<NpcLookOption> ReadItems(ItemDrop.ItemData.ItemType type, string prefix, bool skipVariants)
    {
        var options = new List<NpcLookOption>();
        try
        {
            ObjectDB database = ObjectDB.instance;
            if (database == null)
            {
                return options;
            }

            List<ItemDrop> items = database.GetAllItems(type, prefix);
            if (items == null)
            {
                return options;
            }

            foreach (ItemDrop item in items)
            {
                if (item == null || item.gameObject == null)
                {
                    continue;
                }

                string name = item.gameObject.name;
                if (string.IsNullOrEmpty(name) || (skipVariants && name.IndexOf('_') >= 0))
                {
                    // The game's own filter: variants such as Hair11_Sami are
                    // not offered on the customization screen.
                    continue;
                }

                string? token = item.m_itemData?.m_shared?.m_name;
                options.Add(new NpcLookOption(name, Localize(token), string.IsNullOrEmpty(token) ? null : token));
            }
        }
        catch (Exception)
        {
            // An empty list is an ordinary "no options": the figure keeps its own look.
        }

        return options;
    }

    private static string? Localize(string? token)
    {
        try
        {
            if (string.IsNullOrEmpty(token) || Localization.instance == null)
            {
                return null;
            }

            string label = Localization.instance.Localize(token!);

            // An unresolved token comes back still looking like a token.
            return string.IsNullOrEmpty(label) || label.IndexOf('$') >= 0 ? null : label;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private GameObject? FindItem(string? prefabName)
    {
        if (string.IsNullOrEmpty(prefabName))
        {
            return null;
        }

        try
        {
            GameObject? prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            if (prefab == null)
            {
                _log("\"" + prefabName + "\" is not in this build's item table, so " + _spec.Who + " is not wearing it.");
            }

            return prefab;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The child the game attaches: <c>attach</c>, or
    /// <c>attach_skin</c> for a skinned preset. First one wins.</summary>
    private static GameObject? FindAttachChild(GameObject prefab, out bool skinned)
    {
        skinned = false;
        for (int index = 0; index < prefab.transform.childCount; index++)
        {
            Transform child = prefab.transform.GetChild(index);
            if (child == null)
            {
                continue;
            }

            if (child.gameObject.name == "attach")
            {
                return child.gameObject;
            }

            if (child.gameObject.name == "attach_skin")
            {
                skinned = true;
                return child.gameObject;
            }
        }

        return null;
    }

    private Transform? FindBoneNamed(string name)
    {
        if (_bodyModel != null && _bodyModel.bones != null)
        {
            foreach (Transform bone in _bodyModel.bones)
            {
                if (bone != null && string.Equals(bone.name, name, StringComparison.Ordinal))
                {
                    return bone;
                }
            }
        }

        foreach (Transform bone in _root.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            if (bone != null && string.Equals(bone.name, name, StringComparison.Ordinal))
            {
                return bone;
            }
        }

        return null;
    }

    /// <summary>The head bone, else the neck: first among the body's own
    /// bones, then anywhere in the figure. The order is the one the figure was
    /// first fitted with, kept exactly.</summary>
    private Transform? FindHeadBone()
    {
        if (_bodyModel != null && _bodyModel.bones != null)
        {
            Transform? fromBody = FindAmong(_bodyModel.bones, "head") ?? FindAmong(_bodyModel.bones, "neck");
            if (fromBody != null)
            {
                return fromBody;
            }
        }

        Transform[] all = _root.GetComponentsInChildren<Transform>(includeInactive: true);
        return FindAmong(all, "head") ?? FindAmong(all, "neck");
    }

    private static Transform? FindAmong(Transform[] bones, string wanted)
    {
        foreach (Transform bone in bones)
        {
            if (bone != null && string.Equals(bone.name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return bone;
            }
        }

        return null;
    }

    private static float DistanceFrom(GameObject piece, Vector3 reference)
    {
        bool any = false;
        Bounds bounds = default;
        foreach (Renderer renderer in piece.GetComponentsInChildren<Renderer>(includeInactive: false))
        {
            if (renderer == null)
            {
                continue;
            }

            if (!any)
            {
                bounds = renderer.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return any ? Vector3.Distance(bounds.center, reference) : float.NaN;
    }

    private void MarkRemoved(string slot)
    {
        if (slot.StartsWith("hair", StringComparison.Ordinal))
        {
            HairWorn = false;
        }
        else if (slot.StartsWith("beard", StringComparison.Ordinal))
        {
            BeardWorn = false;
        }
        else if (slot.StartsWith("chest", StringComparison.Ordinal))
        {
            ChestWorn = false;
        }
        else if (slot.StartsWith("legs", StringComparison.Ordinal))
        {
            LegsWorn = false;
        }
    }
}
