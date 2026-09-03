namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>The player-facing terrain action a captured TerrainOp belongs
/// to, resolved from the operation's actual prefab identity (and the piece
/// name token as fallback) — never from its paint or level/raise settings.
///
/// VERIFIED AGAINST THE LIVE GAME (RC10): Valheim's prefab names do not
/// match their hoe menu labels. The hoe's "Level ground" action places
/// <c>mud_road_v2</c> (a historical misnomer) and the actual "Pathen"
/// action places <c>path_v2</c>. Both smooth-and-paint Dirt with
/// near-identical <c>TerrainOp.Settings</c>, which is why every
/// settings-flag heuristic (m_level/m_raise, RC8) misclassified Level
/// Ground as road building.</summary>
internal enum TerrainActionCategory
{
    /// <summary>Not a recognized vanilla terrain action. Never creates
    /// road data.</summary>
    Unknown = 0,

    /// <summary>Hoe "Pathen" (<c>path_v2</c>, $piece_pathen). The ONLY
    /// action that may create Dirt road data.</summary>
    Pathen = 1,

    /// <summary>Hoe "Paved road" (<c>paved_road_v2</c>, $piece_pavedroad).
    /// The ONLY action that may create Paved road data.</summary>
    PavedRoad = 2,

    /// <summary>Hoe "Level ground" (<c>mud_road_v2</c>, $piece_level).
    /// Terraforming; paints Dirt as a side effect. Never a road.</summary>
    LevelGround = 3,

    /// <summary>Hoe "Raise ground" (<c>raise_v2</c>, $piece_raise).
    /// Terraforming. Never a road.</summary>
    RaiseGround = 4,

    /// <summary>Cultivator "Cultivate" (<c>cultivate_v2</c>,
    /// $piece_cultivate). Erases road paint. Never a road.</summary>
    Cultivate = 5,

    /// <summary>Cultivator "Grass" (<c>replant_v2</c>, $piece_replant).
    /// Never a road.</summary>
    Replant = 6,

    /// <summary>Pickaxe digging (<c>digg_v2</c>). Never a road.</summary>
    Digging = 7,
}
