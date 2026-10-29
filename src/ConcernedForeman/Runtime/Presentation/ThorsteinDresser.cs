using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Presentation;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Presentation;

/// <summary>Puts the owner's Thorstein on an extracted figure (#298): Mullet,
/// Mustache, his colouring, and working leathers.
///
/// <b>The pattern is Hulgi's, transcribed rather than referenced</b> (products
/// never reference each other, D2), from Concerned Cartographer's
/// <c>CompanionActor</c>, where it has been seen working in game for hair and
/// beard. Every rule that made it work there is kept:
///
/// <list type="bullet">
/// <item>A preset is the item's <c>attach</c> or <c>attach_skin</c> child,
/// never the item, which carries a network view. It is instantiated under an
/// <b>inactive</b> holder and inspected before it may wake. A forbidden
/// component, or a script this build will not let us destroy, refuses the
/// piece. Nothing is stripped from something that has run.</item>
/// <item>A skinned piece takes the body's own bone array verbatim, the way
/// the game binds one. Matching bones by name is what drew Hulgi's braid 0.99 m
/// from his head (#305).</item>
/// <item>Colour goes through a property block on <c>_SkinColor</c>, so the
/// vanilla material every character shares is never written.</item>
/// <item>A moment after he is first drawn, every piece is measured against his
/// head or body, and one that is not where it was put is taken off with a log
/// line rather than left floating.</item>
/// </list>
///
/// <b>Local only.</b> Nothing here is networked, saved, given to anybody or
/// taken from anybody. A garment is drawn on the figure; it is not an item he
/// owns, and its armour value is never read. Every engine write is in
/// <see cref="FigureParts"/>, where the repository's audit can pin it.</summary>
internal sealed class ThorsteinDresser
{
    private static readonly string[] ForbiddenComponents =
    {
        "ZNetView", "ZSyncAnimation", "Player", "Character", "Humanoid",
        "BaseAI", "MonsterAI", "AnimalAI", "NpcTalk", "Tameable", "ItemDrop",
    };

    private static readonly string[] HeadBoneNames = { "head", "neck" };

    private static readonly int SkinColourProperty = Shader.PropertyToID("_SkinColor");
    private static readonly int ChestTexProperty = Shader.PropertyToID("_ChestTex");
    private static readonly int ChestBumpProperty = Shader.PropertyToID("_ChestBumpMap");
    private static readonly int ChestMetalProperty = Shader.PropertyToID("_ChestMetal");
    private static readonly int LegsTexProperty = Shader.PropertyToID("_LegsTex");
    private static readonly int LegsBumpProperty = Shader.PropertyToID("_LegsBumpMap");
    private static readonly int LegsMetalProperty = Shader.PropertyToID("_LegsMetal");

    private readonly GameObject _root;
    private readonly SkinnedMeshRenderer? _bodyModel;
    private readonly Transform? _helmetJoint;
    private readonly ThorsteinSettings _settings;
    private readonly Action<string> _log;
    private readonly Dictionary<string, GameObject> _attached = new Dictionary<string, GameObject>(StringComparer.Ordinal);

    private Material? _bodyMaterial;
    private bool _verified;

    internal ThorsteinDresser(in NpcPresentationFigure figure, ThorsteinSettings settings, Action<string> log)
    {
        _root = figure.Root ?? throw new ArgumentException("A built figure is required.", nameof(figure));
        _bodyModel = figure.BodyModel;
        _helmetJoint = figure.HelmetJoint;
        _settings = settings;
        _log = log;
    }

    internal LookChoice Hair { get; private set; } = LookChoice.None;

    internal LookChoice Beard { get; private set; } = LookChoice.None;

    internal LookChoice Chest { get; private set; } = LookChoice.None;

    internal LookChoice Legs { get; private set; } = LookChoice.None;

    internal bool HairWorn { get; private set; }

    internal bool BeardWorn { get; private set; }

    internal bool ChestWorn { get; private set; }

    internal bool LegsWorn { get; private set; }

    internal bool SkinApplied { get; private set; }

    internal bool PaletteObserved { get; private set; }

    internal string Fit { get; private set; } = "not measured yet";

    /// <summary>One line for <c>cf_worker status</c>: what he is wearing, and
    /// whether it is the owner's reference or a fallback.</summary>
    internal string Describe() =>
        "hair " + Hair + (HairWorn ? string.Empty : " (not worn)") +
        ", beard " + Beard + (BeardWorn ? string.Empty : " (not worn)") +
        ", chest " + Chest + (ChestWorn ? string.Empty : " (not worn)") +
        ", legs " + Legs + (LegsWorn ? string.Empty : " (not worn)") +
        ", colour from " + (PaletteObserved ? "the game's palette" : "the stated fallback, palette not read") +
        (SkinApplied ? string.Empty : ", skin is the model's own") +
        ", fit [" + Fit + "]";

    /// <summary>Dresses the figure. Every part fails on its own and none of them
    /// can stop the figure existing: the worst case is the source model's own
    /// look, said in the log.</summary>
    internal void Apply()
    {
        try
        {
            List<LookOption> hair = ReadItems(ItemDrop.ItemData.ItemType.Customization, "Hair", skipVariants: true);
            List<LookOption> beards = ReadItems(ItemDrop.ItemData.ItemType.Customization, "Beard", skipVariants: true);
            List<LookOption> chest = ReadItems(ItemDrop.ItemData.ItemType.Chest, string.Empty, skipVariants: false);
            List<LookOption> legs = ReadItems(ItemDrop.ItemData.ItemType.Legs, string.Empty, skipVariants: false);

            Hair = ThorsteinLookPlan.Choose(hair, ThorsteinLookPlan.Hair, _settings.Hair.Value);
            Beard = ThorsteinLookPlan.Choose(beards, ThorsteinLookPlan.Beard, _settings.Beard.Value);
            Chest = ThorsteinLookPlan.Choose(chest, ThorsteinLookPlan.Chest, _settings.ChestGarment.Value);
            Legs = ThorsteinLookPlan.Choose(legs, ThorsteinLookPlan.Legs, _settings.LegsGarment.Value);

            if (!Hair.MatchesReference || !Beard.MatchesReference)
            {
                _log(
                    "Thorstein's reference look (Mullet, Mustache) could not be matched exactly on this build " +
                    "(hair " + Hair + ", beard " + Beard + "). He wears the closest available presets.");
            }

            LookPalette palette = ThorsteinPalette.Read(_log);
            PaletteObserved = palette.Observed;
            LookRgb hairColour = ThorsteinLookColour.ThorsteinHair(palette);

            Transform? joint = _helmetJoint ?? FindHeadBone();
            if (joint == null)
            {
                _log("Thorstein's head attachment point could not be found, so his hair and beard were not attached.");
            }
            else
            {
                HairWorn = AttachPreset(Hair.PrefabName, joint, "hair", hairColour);
                BeardWorn = AttachPreset(Beard.PrefabName, joint, "beard", hairColour);
            }

            LookRgb? skin = ThorsteinLookColour.ThorsteinSkin(palette);
            SkinApplied = skin.HasValue && TintBody(0, skin.Value, "skin tone");
            TintBody(1, hairColour, "hair tint");

            ChestWorn = AttachGarment(Chest.PrefabName, "chest");
            LegsWorn = AttachGarment(Legs.PrefabName, "legs");
        }
        catch (Exception exception)
        {
            _log("Thorstein's look could not be applied on this build; he keeps the model's own: " +
                SafeFailure.Brief(exception));
        }
    }

    /// <summary>Measures every piece once, after the figure has been drawn for
    /// at least one frame. Before that a skinned mesh's bones are unposed and
    /// every piece looks right however it is bound.</summary>
    internal void VerifyOnce()
    {
        if (_verified || !_root.activeInHierarchy)
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

            float tolerance = garment ? LookFit.GarmentToleranceMetres : LookFit.HeadToleranceMetres;
            float distance = DistanceFrom(piece.Value, reference.Value);
            bool fits = LookFit.Fits(distance, tolerance);
            measured.Add(piece.Key + " " + distance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                + "m" + (fits ? string.Empty : " REMOVED"));
            if (fits)
            {
                continue;
            }

            _log("Thorstein's " + piece.Key + " piece is not drawn where it was put on this build, so it was " +
                "taken off rather than left floating.");
            MarkRemoved(piece.Key);
            FigureParts.Drop(piece.Value);
        }

        Fit = string.Join(", ", measured.ToArray());
    }

    /// <summary>Releases what this dresser created that is not a child of the
    /// figure: the body's own material instance.</summary>
    internal void Dispose()
    {
        if (_bodyMaterial != null)
        {
            FigureParts.Drop(_bodyMaterial);
            _bodyMaterial = null;
        }
    }

    // --- hair and beard ------------------------------------------------------------------------------

    private bool AttachPreset(string? prefabName, Transform joint, string slot, LookRgb colour)
    {
        GameObject? prefab = FindItem(prefabName);
        if (prefab == null)
        {
            return false;
        }

        GameObject? attach = FindAttachChild(prefab, out bool namedForSkin);
        if (attach == null)
        {
            _log("Thorstein's " + slot + " preset \"" + prefabName + "\" has no attachment mesh on this build.");
            return false;
        }

        GameObject holder = FigureParts.Holder();
        GameObject? piece = null;
        try
        {
            piece = FigureParts.Harvest(attach, holder);
            if (!Quieten(piece, slot, prefabName!))
            {
                return false;
            }

            bool skinned = namedForSkin || piece.GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true) != null;
            if (skinned && _bodyModel != null)
            {
                FigureParts.Seat(piece.transform, _bodyModel.transform.parent);
                BindToBody(piece);
            }
            else
            {
                // worldPositionStays defaults to true, which is what keeps the
                // piece at its own world scale under a joint that is not at
                // unit scale. Hulgi's braid hovered a metre up without it.
                FigureParts.Seat(piece.transform, joint);
            }

            FigureParts.Offset(prefab, piece.transform);
            Tint(piece, colour);

            GameObject attached = piece;
            piece = null;
            attached.SetActive(true);
            _attached[slot] = attached;
            return true;
        }
        catch (Exception exception)
        {
            _log("Thorstein's " + slot + " preset could not be attached: " + SafeFailure.Brief(exception));
            return false;
        }
        finally
        {
            FigureParts.Discard(piece);
            FigureParts.Discard(holder);
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
        GameObject holder = FigureParts.Holder();
        GameObject? piece = null;
        try
        {
            piece = FigureParts.Harvest(source, holder);
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

                FigureParts.Seat(piece.transform, _bodyModel.transform.parent);
                BindToBody(piece);
            }
            else
            {
                Transform? joint = FindBoneNamed(jointName);
                if (joint == null)
                {
                    return false;
                }

                FigureParts.Seat(piece.transform, joint);
            }

            GameObject attached = piece;
            piece = null;
            attached.SetActive(true);
            _attached[slot + ":" + jointName] = attached;
            return true;
        }
        catch (Exception exception)
        {
            _log("Thorstein's " + slot + " garment could not be attached: " + SafeFailure.Brief(exception));
            return false;
        }
        finally
        {
            FigureParts.Discard(piece);
            FigureParts.Discard(holder);
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
            _log("Thorstein's " + slot + " garment texture could not be applied: " + SafeFailure.Brief(exception));
            return false;
        }
    }

    // --- refusing a piece rather than cleaning it -----------------------------------------------------

    /// <summary>Takes physics, cloth and scripts out of a piece that has not
    /// woken. False when the piece must be refused: a forbidden component is
    /// inside it, or a script survives two passes.</summary>
    private bool Quieten(GameObject piece, string slot, string prefabName)
    {
        foreach (Component component in piece.GetComponentsInChildren<Component>(includeInactive: true))
        {
            if (component != null && Array.IndexOf(ForbiddenComponents, component.GetType().Name) >= 0)
            {
                _log("Thorstein's " + slot + " \"" + prefabName + "\" carries " + component.GetType().Name +
                    ", so it was refused. Nothing was stripped.");
                return false;
            }
        }

        foreach (Collider collider in piece.GetComponentsInChildren<Collider>(includeInactive: true))
        {
            FigureParts.Discard(collider);
        }

        foreach (Rigidbody body in piece.GetComponentsInChildren<Rigidbody>(includeInactive: true))
        {
            FigureParts.Discard(body);
        }

        foreach (Cloth cloth in piece.GetComponentsInChildren<Cloth>(includeInactive: true))
        {
            FigureParts.Discard(cloth);
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

                FigureParts.Discard(script);
                if (script != null)
                {
                    script.enabled = false;
                    refused.Add(name);
                }
            }
        }

        if (refused.Count > 0)
        {
            _log("Thorstein's " + slot + " \"" + prefabName + "\" keeps " + string.Join(", ", refused.ToArray()) +
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
            switch (LookSkinBindings.Decide(bindposes, bodyBones.Length))
            {
                case LookSkinBinding.AdoptBodySkeleton:
                    mesh.rootBone = _bodyModel.rootBone;
                    mesh.bones = bodyBones;
                    mesh.updateWhenOffscreen = true;
                    break;

                case LookSkinBinding.MatchByName:
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

    private static void Tint(GameObject piece, LookRgb colour)
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
    private bool TintBody(int materialIndex, LookRgb colour, string what)
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
            _log("Thorstein's " + what + " could not be applied: " + SafeFailure.Brief(exception));
            return false;
        }
    }

    // --- reading the build ----------------------------------------------------------------------------

    /// <summary>What this build offers, asked of the item table exactly as the
    /// game's own customization screen asks, with each label localized.
    /// Read-only: names and labels are copied out and nothing is created.</summary>
    private static List<LookOption> ReadItems(ItemDrop.ItemData.ItemType type, string prefix, bool skipVariants)
    {
        var options = new List<LookOption>();
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

                options.Add(new LookOption(name, Localize(item)));
            }
        }
        catch (Exception)
        {
            // An empty list is an ordinary "no options": he keeps his own look.
        }

        return options;
    }

    private static string? Localize(ItemDrop item)
    {
        try
        {
            string token = item.m_itemData?.m_shared?.m_name ?? string.Empty;
            if (token.Length == 0 || Localization.instance == null)
            {
                return null;
            }

            string label = Localization.instance.Localize(token);

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
                _log("\"" + prefabName + "\" is not in this build's item table, so Thorstein is not wearing it.");
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

    private Transform? FindHeadBone()
    {
        if (_bodyModel != null && _bodyModel.bones != null)
        {
            foreach (string wanted in HeadBoneNames)
            {
                foreach (Transform bone in _bodyModel.bones)
                {
                    if (bone != null && string.Equals(bone.name, wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        return bone;
                    }
                }
            }
        }

        foreach (string wanted in HeadBoneNames)
        {
            foreach (Transform bone in _root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (bone != null && string.Equals(bone.name, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return bone;
                }
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
