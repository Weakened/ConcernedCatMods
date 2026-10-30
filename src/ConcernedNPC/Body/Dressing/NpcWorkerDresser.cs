using System;
using TheConcernedCat.ConcernedNPC.Appearance;
using TheConcernedCat.ConcernedNPC.Roles;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Body.Dressing;

/// <summary>Puts a character's own hair and beard pieces on that role's
/// <b>worker</b> body (owner decision 2026-10-29, #295 S6, option (b)).
///
/// <b>What the owner decided.</b> Thorstein's worker body stays the clone it
/// always was (Foreman's is the <c>Dverger</c> creature), under the same prefab
/// name and keys, so no saved body changes and nothing migrates. On top of it,
/// locally, go the same player hair and beard pieces his presentation figure
/// wears. If a piece will not attach or is not drawn where it was put on that
/// rig, it is taken off and he keeps the plain look. A role offers this behind
/// its own setting, on by default so the playtest judges it.
///
/// <b>What it touches.</b> Children it adds under the body's own head joint,
/// and nothing else: not the body's renderer, its material, its equipment, its
/// network object, its inventory or its saved data. The pieces are never
/// networked or saved, so they leave with the body when it unloads and come back
/// only when the role dresses it again. <see cref="TakeOff"/> drops exactly the
/// pieces it attached; there is no member here that destroys or moves the body.
///
/// <b>Why it takes a body at all, and how that is kept honest.</b> A worker body
/// is the role's own runtime object, so the role has to say which one. A member
/// that dressed whatever it was handed could put pieces on the player, or on
/// anyone. So <see cref="TryDress"/> refuses unless the object is <b>that
/// registered role's own worker body</b>: its saved prefab is the role's
/// contracted worker prefab, and its saved identity key is the role's identity.
/// The repository validator pins that the guard runs before anything is built.
/// </summary>
public sealed class NpcWorkerDresser
{
    private readonly NpcDressing _dressing;

    private NpcWorkerDresser(NpcDressing dressing)
    {
        _dressing = dressing;
    }

    public NpcLookChoice Hair => _dressing.Hair;

    public NpcLookChoice Beard => _dressing.Beard;

    public bool HairWorn => _dressing.HairWorn;

    public bool BeardWorn => _dressing.BeardWorn;

    public bool PaletteObserved => _dressing.PaletteObserved;

    public string Fit => _dressing.Fit;

    /// <summary>One line for a status command.</summary>
    public string Describe() => _dressing.Describe();

    /// <summary>Dresses <paramref name="body"/> with the hair and beard of
    /// <paramref name="spec"/>, or refuses and says why. The spec's body model,
    /// skin, garments and their overrides are ignored here: a worker body's
    /// renderer belongs to the game.</summary>
    /// <param name="identity">The role the body belongs to. Must be registered
    /// with Concerned NPC under a worker contract.</param>
    /// <param name="body">The worker body, as the role's runtime holds it.</param>
    public static NpcWorkerDresser? TryDress(
        NpcIdentity identity, Humanoid body, NpcLookSpec spec, string holderName, Action<string>? log,
        out string refusal)
    {
        if (!IsThatRolesWorkerBody(identity, body, out refusal))
        {
            return null;
        }

        try
        {
            VisEquipment? equipment = body.GetComponentInChildren<VisEquipment>(includeInactive: true);
            Transform? helmet = equipment != null ? equipment.m_helmet : null;
            SkinnedMeshRenderer? model = equipment != null ? equipment.m_bodyModel : null;
            var dressing = new NpcDressing(body.gameObject, model, helmet, spec, holderName, log);
            dressing.Apply();
            if (!dressing.HairWorn && !dressing.BeardWorn)
            {
                refusal = "no hair or beard piece would attach to this body, so it keeps its own look";
                dressing.TakeOff();
                return null;
            }

            refusal = string.Empty;
            return new NpcWorkerDresser(dressing);
        }
        catch (Exception exception)
        {
            refusal = "dressing the worker body threw " + exception.GetType().Name + "; it keeps its own look";
            return null;
        }
    }

    /// <summary>Measures every piece once after the body has been drawn, and
    /// takes off any that is not where it was put.</summary>
    public void VerifyOnce() => _dressing.VerifyOnce();

    /// <summary>Takes off the pieces this dresser attached, and nothing else.
    /// Safe after the body is gone.</summary>
    public void TakeOff() => _dressing.TakeOff();

    /// <summary>The guard. True only for the registered role's own saved
    /// worker body: the role is registered under a worker contract, the
    /// object's saved prefab is that contract's prefab, and its saved identity
    /// is this identity. Anything unknown refuses.</summary>
    private static bool IsThatRolesWorkerBody(NpcIdentity identity, Humanoid? body, out string refusal)
    {
        refusal = string.Empty;
        if (body == null)
        {
            refusal = "there is no body";
            return false;
        }

        if (!NpcRoleRegistry.Shared.TryGetRole(identity, out INpcRole role) || role == null)
        {
            refusal = "this identity is not registered with Concerned NPC";
            return false;
        }

        if (!role.Body.Admits(NpcBodyKind.Worker)
            || !NpcBodyFields.TryFor(role.Body, out NpcBodyFields fields, out _))
        {
            refusal = "this role has no worker body contract";
            return false;
        }

        ZNetView view = body.GetComponent<ZNetView>();
        if (view == null || !view.IsValid())
        {
            refusal = "the body is not a saved object yet";
            return false;
        }

        ZDO zdo = view.GetZDO();
        ZNetScene scene = ZNetScene.instance;
        GameObject? prefab = scene != null ? scene.GetPrefab(zdo.GetPrefab()) : null;
        if (prefab == null || !string.Equals(prefab.name, role.Body.PrefabName, StringComparison.Ordinal))
        {
            refusal = "the body is not this role's worker prefab";
            return false;
        }

        if (!NpcIdentity.TryParse(zdo.GetString(fields.Key, string.Empty), out NpcIdentity stored)
            || stored != identity)
        {
            refusal = "the body does not carry this role's identity";
            return false;
        }

        return true;
    }
}
