using System.Collections.Generic;
using TheConcernedCat.Companions.Dialogue;

namespace TheConcernedCat.ConcernedCartographer.Companions;

/// <summary>Hulgi's lines.
///
/// Everything here is a localization key; the text lives in
/// <c>AtlasStrings</c> with the rest of the product's strings, so a translator
/// replaces the companion in the same file as the buttons.
///
/// Three rules shaped the writing.
///
/// <b>He is not a joke delivery system.</b> A cat gag every second sentence
/// wears out inside one evening. Roughly a third of the catalogue is sincere,
/// and the cat is a person he misses rather than a punchline he owns.
///
/// <b>He knows nothing you have not seen.</b> Every biome line is gated behind
/// that biome being known to <i>this</i> character. No line names a boss, a
/// weakness, an item or a location, because the failure mode of guessing there
/// is a spoiler, and a spoiler cannot be taken back. A player who has been
/// nowhere hears the ungated lines and nothing is missing.
///
/// <b>He is drowned, not omniscient.</b> The advice is the kind a careful
/// person gives: mark the way home, two short trips beat one you do not
/// finish. Where he would have to know the future, he says so.</summary>
internal static class HulgiDialogue
{
    /// <summary>Biome names as <c>Heightmap.Biome</c> spells them. The adapter
    /// compares case-insensitively against whatever this build's
    /// <c>Player.m_knownBiome</c> actually holds — the audit could confirm the
    /// field is a <c>HashSet&lt;string&gt;</c> but not its spellings, so a
    /// mismatch must fail closed. It does: no biome line is eligible, Hulgi
    /// says something ungated, and nothing is spoiled.</summary>
    public const string Meadows = "Meadows";
    public const string BlackForest = "BlackForest";
    public const string Swamp = "Swamp";
    public const string Mountain = "Mountain";
    public const string Plains = "Plains";
    public const string Ocean = "Ocean";
    public const string Mistlands = "Mistlands";
    public const string AshLands = "AshLands";
    public const string DeepNorth = "DeepNorth";

    public static readonly IReadOnlyList<DialogueLine> Lines = new[]
    {
        // ---- Greetings. Short, warm, and glad you are alive.
        new DialogueLine("hulgi.greet.back", DialogueCategory.Greeting),
        new DialogueLine("hulgi.greet.speech", DialogueCategory.Greeting),
        new DialogueLine("hulgi.greet.upright", DialogueCategory.Greeting),
        new DialogueLine("hulgi.greet.sit", DialogueCategory.Greeting),
        new DialogueLine("hulgi.greet.quiet", DialogueCategory.Greeting),

        // ---- The cat, and home. The sincere half of the catalogue.
        new DialogueLine("hulgi.cat.weather", DialogueCategory.CatAndHome),
        new DialogueLine("hulgi.cat.essential", DialogueCategory.CatAndHome),
        new DialogueLine("hulgi.cat.wind", DialogueCategory.CatAndHome),
        new DialogueLine("hulgi.cat.ship", DialogueCategory.CatAndHome),
        new DialogueLine("hulgi.cat.noticed", DialogueCategory.CatAndHome),
        new DialogueLine("hulgi.cat.tidier", DialogueCategory.CatAndHome),
        new DialogueLine("hulgi.cat.late", DialogueCategory.CatAndHome),
        new DialogueLine("hulgi.cat.supper", DialogueCategory.CatAndHome),

        // ---- Travel and caution. Advice a careful person gives.
        new DialogueLine("hulgi.travel.markhome", DialogueCategory.Travel),
        new DialogueLine("hulgi.travel.turnback", DialogueCategory.Travel),
        new DialogueLine("hulgi.travel.wayout", DialogueCategory.Travel),
        new DialogueLine("hulgi.travel.compass", DialogueCategory.Travel),
        new DialogueLine("hulgi.travel.twoshort", DialogueCategory.Travel),
        new DialogueLine("hulgi.travel.shortest", DialogueCategory.Travel),
        new DialogueLine("hulgi.travel.morning", DialogueCategory.Travel),
        new DialogueLine("hulgi.travel.weight", DialogueCategory.Travel),

        // ---- Weather. He was a sailor; this is the subject he lost to.
        new DialogueLine("hulgi.weather.turned", DialogueCategory.Weather),
        new DialogueLine("hulgi.weather.cold", DialogueCategory.Weather),
        new DialogueLine("hulgi.weather.loan", DialogueCategory.Weather),
        new DialogueLine("hulgi.weather.calm", DialogueCategory.Weather),
        new DialogueLine("hulgi.weather.prophet", DialogueCategory.Weather),
        new DialogueLine("hulgi.weather.fog", DialogueCategory.Weather),

        // ---- Biome tips. Every one gated behind this character having been
        // there. Tone only: what a place is like to travel, never what lives
        // in it, what it drops, or what to bring to kill something.
        new DialogueLine("hulgi.biome.meadows", DialogueCategory.BiomeTip, Meadows),
        new DialogueLine("hulgi.biome.blackforest", DialogueCategory.BiomeTip, BlackForest),
        new DialogueLine("hulgi.biome.swamp", DialogueCategory.BiomeTip, Swamp),
        new DialogueLine("hulgi.biome.mountain", DialogueCategory.BiomeTip, Mountain),
        new DialogueLine("hulgi.biome.plains", DialogueCategory.BiomeTip, Plains),
        new DialogueLine("hulgi.biome.ocean", DialogueCategory.BiomeTip, Ocean),
        new DialogueLine("hulgi.biome.mistlands", DialogueCategory.BiomeTip, Mistlands),
        new DialogueLine("hulgi.biome.ashlands", DialogueCategory.BiomeTip, AshLands),
        new DialogueLine("hulgi.biome.deepnorth", DialogueCategory.BiomeTip, DeepNorth),
    };

    /// <summary>Categories offered on an ordinary interaction, in the order a
    /// conversation naturally goes: hello first, then something to say.
    ///
    /// <see cref="DialogueCategory.BiomeTip"/> is deliberately absent. Biome
    /// lines are drawn from the whole-catalogue pass instead, so a player who
    /// has been everywhere does not get nothing but travel advisories, and a
    /// player who has been nowhere never notices a category quietly failing.</summary>
    public static readonly IReadOnlyList<DialogueCategory> ConversationOrder = new[]
    {
        DialogueCategory.Greeting,
    };

    public static DialogueCatalog BuildCatalog()
    {
        return new DialogueCatalog(Lines);
    }
}
