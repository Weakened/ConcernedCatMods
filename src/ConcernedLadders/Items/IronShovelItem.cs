using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.ConcernedLadders.Domain.Excavation;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Items;

/// <summary>The Iron Shovel item (#487 slice 3), registered only when <c>[Excavation] Enabled</c> is on: with the switch
/// off (the default) this class is never constructed and nothing is added to the game.
///
/// <para>An inactive Jötunn clone of the vanilla <c>PickaxeIron</c> prefab (<see cref="IronShovel.DonorPrefab"/>), read
/// from the installed game at start-up: no game asset ships, and no live Player or BaseAI is ever instantiated. Since
/// #550 the clone shows its own shovel (<see cref="ShovelModel"/>, built in code from the donor's own texture sheet) in
/// place of the pickaxe's model; if the donor is not as measured it falls back to the tinted pickaxe and says so. It
/// keeps the donor's equip slot and hand, weight,
/// durability and repair behaviour; the game's normal item and recipe lifecycle carries it through world changes and
/// reloads. It is crafted and repaired at the Forge from refined Iron and Wood, so the game's own known-material
/// discovery unlocks the recipe once the player has held Iron, never on Iron Scrap alone
/// (<c>Player.HaveRequirements(recipe, discover: true)</c> requires every material to be known).</para>
///
/// <para><b>It has no vanilla attack.</b> Both attack animations are cleared, so the game's own attack never starts
/// with it: no stamina or durability is taken before the excavation gate has decided, nothing is hit, and the donor's
/// own terrain op (also cleared) is never spawned. Its damage is zero and it has one quality level (no upgrade
/// recipe). The press is read by <c>ExcavationShovelInput</c>; no animation plays (see ExcavationRuntime.Charge). The
/// registration verifies all of this on the clone and refuses the item rather than register one that could attack.</para>
///
/// <para>When the shovel could not dig this session (its input patch or the excavation probe did not match the installed
/// game), the item is still registered, so shovels already crafted survive, but its recipe is disabled, so no one spends
/// Iron on a tool that cannot work. Since slice 4 the same happens when excavation stops later in the session after a
/// fault (<see cref="DisableRecipe"/>): the recipe is switched off on the one Recipe object Jötunn adds to every
/// ObjectDB, which the game's crafting list skips (<c>Player.GetAvailableRecipes</c> reads <c>m_enabled</c>; repair does
/// not, so crafted shovels can still be repaired).</para>
///
/// <para><b>Not observed in game.</b> Recipe visibility, crafting, equip, repair and the look are OWNER GO-AROUND
/// PENDING.</para></summary>
internal sealed class IronShovelItem : IDisposable
{
    private readonly Action<string> _log;
    private bool _craftable;
    private Recipe? _recipe;
    private bool _subscribed;
    private bool _done;

    /// <param name="log">Startup log.</param>
    /// <param name="craftable">The shovel can dig this session (input installed, excavation probe available). When
    /// false the recipe is registered disabled.</param>
    internal IronShovelItem(Action<string> log, bool craftable)
    {
        _log = log;
        _craftable = craftable;
    }

    internal void Install()
    {
        if (_subscribed || _done) return;
        LocalizationManager.Instance.GetLocalization().AddTranslation("English", new Dictionary<string, string>
        {
            [IronShovel.NameToken.TrimStart('$')] = "Iron Shovel",
            [IronShovel.DescriptionToken.TrimStart('$')] = IronShovel.Description(),
            [IronShovel.DigHintToken.TrimStart('$')] = IronShovel.DigHintText,
            [IronShovel.SizeHintToken.TrimStart('$')] = IronShovel.SizeHintText,
        });
        PrefabManager.OnVanillaPrefabsAvailable += Register;
        _subscribed = true;
    }

    private void Register()
    {
        // Once per session: Jötunn keeps the registered item for every later ObjectDB (world change, reload).
        if (_done) return;
        _done = true;
        Unsubscribe();
        try
        {
            var config = new ItemConfig
            {
                Name = IronShovel.NameToken,
                Description = IronShovel.DescriptionToken,
                CraftingStation = IronShovel.Station,
                RepairStation = IronShovel.Station,
                MinStationLevel = IronShovel.StationLevel,
                Enabled = _craftable,
                Requirements = new[]
                {
                    new RequirementConfig(IronShovel.IronItem, IronShovel.IronAmount, 0, true),
                    new RequirementConfig(IronShovel.WoodItem, IronShovel.WoodAmount, 0, true),
                },
            };
            var custom = new CustomItem(IronShovel.PrefabName, IronShovel.DonorPrefab, config);
            GameObject? prefab = custom.ItemPrefab;
            ItemDrop? drop = custom.ItemDrop;
            if (prefab == null || drop == null || drop.m_itemData?.m_shared == null)
                throw new InvalidOperationException("the donor " + IronShovel.DonorPrefab + " was not found or has no ItemDrop");
            if (prefab.activeInHierarchy)
                throw new InvalidOperationException("expected an inactive-parent clone");
            ItemDrop.ItemData item = drop.m_itemData;
            ItemDrop.ItemData.SharedData shared = item.m_shared;
            if (!item.IsWeapon())
                throw new InvalidOperationException("the donor is not a weapon type, so the attack input would never see it");
            if (shared.m_attack == null || shared.m_secondaryAttack == null)
                throw new InvalidOperationException("the donor has no attack data");

            // No vanilla attack at all (see the class summary). Cleared on the clone only; the donor is untouched.
            shared.m_attack.m_attackAnimation = "";
            shared.m_secondaryAttack.m_attackAnimation = "";
            shared.m_attack.m_attackStamina = 0f;
            shared.m_secondaryAttack.m_attackStamina = 0f;
            shared.m_spawnOnHitTerrain = null;
            shared.m_spawnOnHit = null;
            shared.m_damages = default(HitData.DamageTypes);
            shared.m_damagesPerLevel = default(HitData.DamageTypes);
            shared.m_buildPieces = null;
            shared.m_maxQuality = 1;
            if (item.HavePrimaryAttack() || item.HaveSecondaryAttack())
                throw new InvalidOperationException("the clone still has a vanilla attack");

            if (!ReplaceModel(prefab)) Tint(prefab);
            RenderIcon(prefab, shared);
            if (!ItemManager.Instance.AddItem(custom))
                throw new InvalidOperationException("Jötunn rejected the item registration");
            _recipe = custom.Recipe?.Recipe;
            // A fault between construction and registration already turned the recipe off; keep it off.
            if (!_craftable && _recipe != null) _recipe.m_enabled = false;
            _log("Registered " + IronShovel.PrefabName + " (Iron Shovel, from " + IronShovel.DonorPrefab + ": " + shared.m_itemType +
                 ", durability " + F(shared.m_maxDurability) + (shared.m_useDurability ? "" : " (unused)") + ", repairable " +
                 shared.m_canBeReparied + "). Recipe" + (_craftable ? ": " : " DISABLED (the shovel cannot dig this session): ") + IronShovel.IronAmount.ToString(CultureInfo.InvariantCulture) +
                 " " + IronShovel.IronItem + " + " + IronShovel.WoodAmount.ToString(CultureInfo.InvariantCulture) + " " + IronShovel.WoodItem +
                 " at " + IronShovel.Station + " level " + IronShovel.StationLevel.ToString(CultureInfo.InvariantCulture) + ".");
        }
        catch (Exception exception)
        {
            // Jötunn remembers a clone before AddItem; retrying under the same id would return a half-edited asset, so
            // this session stays without the shovel. Nothing else depends on it: the ladders and the terrain ops are
            // unaffected, and with no shovel nothing can dig.
            _log("Iron Shovel NOT registered: " + SafeFailure.Brief(exception) + ". Excavation cannot be used this session.");
        }
    }

    /// <summary>Replaces the pickaxe's model under the clone's <c>attach</c> (shown in the hand and on the ground) with
    /// the shovel (#550): the pickaxe's own child, on the inactive clone only, is given <see cref="ShovelModel.Mesh"/>
    /// and the donor's own <c>weapons1</c> material (shared, never edited) and its transform reset to the attach's space. False, changing
    /// nothing, when the donor is not as measured (no <c>attach/pickaxe</c>, no such material, or another renderer
    /// under <c>attach</c>), so the caller keeps the tinted pickaxe rather than a half-built model.</summary>
    private bool ReplaceModel(GameObject prefab)
    {
        try
        {
            Transform? attach = prefab.transform.Find(ShovelModel.DonorAttach);
            Transform? model = attach != null ? attach.Find(ShovelModel.DonorModel) : null;
            MeshRenderer? renderer = model != null ? model.GetComponent<MeshRenderer>() : null;
            Material? material = renderer != null ? Array.Find(renderer.sharedMaterials, m => m != null && m.name == ShovelModel.DonorMaterial) : null;
            if (attach == null || model == null || material == null)
                throw new InvalidOperationException("the donor has no " + ShovelModel.DonorAttach + "/" + ShovelModel.DonorModel + " drawn with " + ShovelModel.DonorMaterial);
            foreach (Renderer other in attach.GetComponentsInChildren<Renderer>(true))
            {
                if (other != renderer && !(other is ParticleSystemRenderer))
                    throw new InvalidOperationException("the donor's attach draws more than the pickaxe (" + other.name + ")");
            }

            CellarMesh shape = ShovelModel.Mesh();
            var mesh = new Mesh { name = "tcc_iron_shovel", hideFlags = HideFlags.DontUnloadUnusedAsset };
            mesh.vertices = shape.Vertices.Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray();
            mesh.normals = shape.Normals.Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray();
            mesh.uv = shape.Uvs.Select(uv => new Vector2(uv.U, uv.V)).ToArray();
            mesh.triangles = shape.Triangles.ToArray();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            MeshFilter? filter = model.GetComponent<MeshFilter>();
            if (filter == null) throw new InvalidOperationException("the donor's " + ShovelModel.DonorModel + " has no mesh filter");
            // The pickaxe's own child carries the shovel: its mesh and materials are replaced and its transform reset
            // to the attach's space, which the shovel is built in. Nothing is created or destroyed.
            model.name = ShovelModel.Child;
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            model.localScale = Vector3.one;
            filter.sharedMesh = mesh;
            renderer!.sharedMaterials = new[] { material };
            return true;
        }
        catch (Exception exception)
        {
            _log("Iron Shovel model fallback: " + SafeFailure.Brief(exception) + ". The tinted pickaxe model is shown.");
            return false;
        }
    }

    /// <summary>The fallback when <see cref="ReplaceModel"/> refuses: gives every renderer of the clone (the dropped model and the held "attach" model) its own tinted copy of
    /// each material, so the shovel reads apart from the donor pickaxe. The donor's materials are never edited.</summary>
    private static void Tint(GameObject prefab)
    {
        var tint = new Color(0.72f, 0.76f, 0.84f, 1f);
        var copies = new Dictionary<Material, Material>();
        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material original = materials[i];
                if (original == null) continue;
                if (!copies.TryGetValue(original, out Material copy))
                {
                    copy = new Material(original) { name = original.name + "_tcc_shovel" };
                    if (copy.HasProperty("_Color")) copy.color *= tint;
                    copies[original] = copy;
                }
                materials[i] = copy;
            }
            renderer.sharedMaterials = materials;
        }
    }

    /// <summary>Renders the tinted clone as its icon on graphical clients. A dedicated server cannot render and keeps the
    /// donor's icon, as does any rendering fault (reported).</summary>
    private void RenderIcon(GameObject prefab, ItemDrop.ItemData.SharedData shared)
    {
        if (Application.isBatchMode) return;
        try
        {
            Sprite rendered = RenderManager.Instance.Render(prefab, RenderManager.IsometricRotation);
            if (rendered == null || rendered.texture == null || rendered.rect.width <= 0f || rendered.rect.height <= 0f)
                throw new InvalidOperationException("Jötunn returned no usable icon");
            shared.m_icons = new[] { rendered };
        }
        catch (Exception exception)
        {
            _log("Iron Shovel icon fallback: " + SafeFailure.Brief(exception) + ". The donor's icon is kept.");
        }
    }

    /// <summary>Excavation stopped for the rest of the session (a fault after start-up): the recipe is switched off so no one
    /// crafts a tool that can no longer dig. Only ever turns it off; nothing turns it back on before a restart.</summary>
    internal void DisableRecipe()
    {
        bool wasCraftable = _craftable;
        _craftable = false;
        Recipe? recipe = _recipe;
        if (recipe != null) recipe.m_enabled = false;
        if (wasCraftable) _log("Iron Shovel recipe disabled for this session: excavation stopped after a fault (see above). Crafted shovels can still be repaired.");
    }

    private static string F(float value) => value.ToString("0", CultureInfo.InvariantCulture);

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        try { PrefabManager.OnVanillaPrefabsAvailable -= Register; } catch { /* Teardown only. */ }
        _subscribed = false;
    }

    public void Dispose() => Unsubscribe();
}
