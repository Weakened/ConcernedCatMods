namespace TheConcernedCat.ConcernedNPC.Roles;

/// <summary>What a product hands to this library about one NPC. The whole
/// extension contract, and deliberately three lines long.
///
/// <b>What it guarantees.</b> That everything durable about an NPC stays in the
/// product that ships it. The identity a saved body is found by, the prefab name
/// the world stores, the key prefix the inventory lives under and every path on
/// disk are all supplied here, by the role, and none of them has a default in
/// this library. That is the whole reason the program that moves four NPCs onto
/// one runtime needs no data migration: nothing on disk is derived from a type,
/// a namespace or an assembly name, and this interface is where that stays true.
///
/// <b>What is deliberately absent.</b> No display name, no dialogue, no
/// appearance, no lore, no config, no gameplay of any kind. This library has no
/// idea what a cart is, what resin is for, or why anyone wants a shelter. A
/// member here that only one role could answer is a defect; so is a type in this
/// library that names a product.
///
/// <b>What a role still does for itself, forever.</b> Registering its own prefab
/// with the game, from its own plugin start, as soon as vanilla prefabs can be
/// cloned. This library never registers a prefab and never asks a role to hand
/// one over, because a prefab registered late - or under a name of this
/// library's choosing - deletes every existing body of that role on the next
/// load.</summary>
public interface INpcRole
{
    /// <summary>Which NPC this is. Unique across every role registered in one
    /// process: two roles claiming one identity is refused, because the
    /// never-coexist rule is stated per identity and two owners of one identity
    /// each believe they are the arbiter.</summary>
    NpcIdentity Identity { get; }

    /// <summary>The durable facts of this role's body, or the presentation
    /// contract that declares it has none.</summary>
    NpcBodyContract Body { get; }

    /// <summary>Where this role's data lives. Never null: a role with nothing to
    /// persist still says where its root is, and registration refuses one that
    /// does not.</summary>
    INpcDataPaths Paths { get; }
}
