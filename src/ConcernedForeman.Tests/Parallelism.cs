using Xunit;

// The game is global: ObjectDB.instance, Player.m_localPlayer, the worker body's
// live list and the inventory trace are static, exactly as they are in Valheim.
// Tests therefore run one at a time in this assembly, each setting up the world
// it needs, rather than pretending the game can be instantiated twice.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
