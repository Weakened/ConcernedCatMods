using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Appearance;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Body.Dressing;

/// <summary>Dresses, stands and takes down one extracted presentation figure:
/// the body it wears, hair, beard, colouring and garments, from a role's
/// <see cref="NpcLookSpec"/> (lifted from Concerned Foreman's
/// <c>ThorsteinDresser</c>, #295/#298, so Thorstein, Sunniva and Gunnar share
/// one copy).
///
/// <b>The pattern is Hulgi's</b>, from Concerned Cartographer's
/// <c>CompanionActor</c>, where it has been seen working in game for hair and
/// beard. Every rule that made it work there is kept:
///
/// <list type="bullet">
/// <item>A preset is the item's <c>attach</c> or <c>attach_skin</c> child,
/// never the item, which carries a network view. It is instantiated under an
/// <b>inactive</b> holder and inspected before it may wake. A forbidden
/// component, or a script this build will not let us destroy, refuses the
/// piece. Nothing is stripped from something that has run.</item>
/// <item>A skinned piece takes the body's own bone array verbatim, the way the
/// game binds one. Matching bones by name is what drew Hulgi's braid 0.99 m
/// from his head (#305).</item>
/// <item>Colour goes through a property block on <c>_SkinColor</c>, so the
/// vanilla material every character shares is never written.</item>
/// <item>A moment after the figure is first drawn, every piece is measured
/// against the head or body, and one that is not where it was put is taken off
/// with a log line rather than left floating.</item>
/// </list>
///
/// <b>The second body</b> is the source character's own, read from its body
/// list before the source was destroyed, and put on the way the game's
/// equipment component does it: the renderer's mesh, then the base textures on
/// the figure's own material instance.
///
/// <b>Local only.</b> Nothing here is networked, saved, given to anybody or
/// taken from anybody. A garment is drawn on the figure; it is not an item
/// anybody owns, and its armour value is never read. Every engine write that
/// creates, destroys, parents, moves or reshapes is in
/// <see cref="NpcFigureParts"/>, where the repository's audit pins it.
///
/// <b>Why every public member takes no object.</b> A consumer's own audits
/// (Foreman's #400, for one) pin the engine writes that product makes. A
/// library member that destroyed or moved whatever it was handed would be a
/// way round them that spells no audited token. So this type is constructed
/// from the figure handle the extraction returned, and acts only on that
/// figure and the pieces it put there itself.</summary>
public sealed class NpcFigureDresser
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

    private readonly NpcPresentationFigure _figure;
    private readonly GameObject _root;
    private readonly SkinnedMeshRenderer? _bodyModel;
    private readonly Transform? _helmetJoint;
    private readonly NpcLookSpec _spec;
    private readonly string _holderName;
    private readonly Action<string> _log;
    private readonly Dictionary<string, GameObject> _attached = new Dictionary<string, GameObject>(StringComparer.Ordinal);

    private Material? _bodyMaterial;
    private bool _verified;
    private bool _takenDown;
    private bool _applied;

    /// <param name="figure">A figure <see cref="NpcPresentationBody.TryExtract"/>
    /// built. A refused one is an argument error.</param>
    /// <param name="spec">What the role asks the figure to look like.</param>
    /// <param name="holderName">The name of the inactive holder each piece is
    /// cloned under. The role's, like the extraction's holder name.</param>
    /// <param name="log">Where every refusal and fallback is said.</param>
    public NpcFigureDresser(in NpcPresentationFigure figure, NpcLookSpec spec, string holderName, Action<string>? log)
    {
        _root = figure.Root ?? throw new ArgumentException("A built figure is required.", nameof(figure));
        _figure = figure;
        _bodyModel = figure.BodyModel;
        _helmetJoint = figure.HelmetJoint;
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        if (string.IsNullOrWhiteSpace(holderName))
        {
            throw new ArgumentException("The role names the holder.", nameof(holderName));
        }

        _holderName = holderName;
        _log = log ?? (_ => { });
    }

    public NpcLookChoice Hair { get; private set; } = NpcLookChoice.None;

    public NpcLookChoice Beard { get; private set; } = NpcLookChoice.None;

    public NpcLookChoice Chest { get; private set; } = NpcLookChoice.None;

    public NpcLookChoice Legs { get; private set; } = NpcLookChoice.None;

    public bool HairWorn { get; private set; }

    public bool BeardWorn { get; private set; }

    public bool ChestWorn { get; private set; }

    public bool LegsWorn { get; private set; }

    public bool SkinApplied { get; private set; }

    public bool PaletteObserved { get; private set; }

    /// <summary>True when the body asked for is the one worn: always for
    /// <see cref="NpcBodyModel.Male"/>, which changes nothing, and for
    /// <see cref="NpcBodyModel.Female"/> only once the second body was put on.
    /// A role that must not show the wrong body checks this after
    /// <see cref="Apply"/>.</summary>
    public bool ModelApplied { get; private set; }

    public string Fit { get; private set; } = "not measured yet";

    /// <summary>One line for a status command: what the figure is wearing, and
    /// whether it is the reference or a fallback. A slot the spec did not ask
    /// for is left out.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (_spec.Model != NpcBodyModel.Male)
        {
            parts.Add("body " + _spec.Model + (ModelApplied ? string.Empty : " (not applied)"));
        }

        AddSlot(parts, "hair", _spec.Hair, Hair, HairWorn);
        AddSlot(parts, "beard", _spec.Beard, Beard, BeardWorn);
        AddSlot(parts, "chest", _spec.Chest, Chest, ChestWorn);
        AddSlot(parts, "legs", _spec.Legs, Legs, LegsWorn);
        parts.Add("colour from " + (PaletteObserved ? "the game's palette" : "the stated fallback, palette not read")
            + (SkinApplied ? string.Empty : ", skin is the model's own"));
        parts.Add("fit [" + Fit + "]");
        return string.Join(", ", parts.ToArray());
    }

    /// <summary>Dresses the figure, once. Every part fails on its own and none of them
    /// can stop the figure existing: the worst case is the source model's own
    /// look, said in the log.</summary>
    public void Apply()
    {
        // Once. A second pass would attach a second set of pieces over the
        // first, and the fit check would then take the first set off as
        // misplaced; a role that wants another look builds another figure.
        if (_takenDown || _applied)
        {
            return;
        }

        _applied = true;

        try
        {
            ModelApplied = _spec.Model == NpcBodyModel.Male || ApplyModel((int)_spec.Model);

            List<NpcLookOption> hair = _spec.Hair == null
                ? new List<NpcLookOption>()
                : ReadItems(ItemDrop.ItemData.ItemType.Customization, "Hair", skipVariants: true);
            List<NpcLookOption> beards = _spec.Beard == null
                ? new List<NpcLookOption>()
                : ReadItems(ItemDrop.ItemData.ItemType.Customization, "Beard", skipVariants: true);
            List<NpcLookOption> chest = _spec.Chest == null
                ? new List<NpcLookOption>()
                : ReadItems(ItemDrop.ItemData.ItemType.Chest, string.Empty, skipVariants: false);
            List<NpcLookOption> legs = _spec.Legs == null
                ? new List<NpcLookOption>()
                : ReadItems(ItemDrop.ItemData.ItemType.Legs, string.Empty, skipVariants: false);

            Hair = _spec.Hair == null ? NpcLookChoice.None : NpcLookPlan.Choose(hair, _spec.Hair, _spec.HairOverride);
            Beard = _spec.Beard == null ? NpcLookChoice.None : NpcLookPlan.Choose(beards, _spec.Beard, _spec.BeardOverride);
            Chest = _spec.Chest == null ? NpcLookChoice.None : NpcLookPlan.Choose(chest, _spec.Chest, _spec.ChestOverride);
            Legs = _spec.Legs == null ? NpcLookChoice.None : NpcLookPlan.Choose(legs, _spec.Legs, _spec.LegsOverride);

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

    /// <summary>Measures every piece once, after the figure has been drawn for
    /// at least one frame. Before that a skinned mesh's bones are unposed and
    /// every piece looks right however it is bound.</summary>
    public void VerifyOnce()
    {
        if (_takenDown || _verified || _root == null || !_root.activeInHierarchy)
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

    /// <summary>Takes the figure down: dark at once, so it is never drawn beside
    /// a worker body created in the same frame, and gone at the end of the
    /// frame, with the body material instance this dresser made. Idempotent.
    /// </summary>
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

        if (_bodyMaterial != null)
        {
            NpcFigureParts.Drop(_bodyMaterial);
            _bodyMaterial = null;
        }

        _attached.Clear();
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

        if (!_figure.TryGetModel(index, out Mesh? mesh, out Material? baseMaterial) || mesh == null
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
