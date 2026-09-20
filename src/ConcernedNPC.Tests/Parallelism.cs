using Xunit;

// The game is global, and so is the part of this package that touches it: the
// prefab manager's vanilla-prefabs event, the world object index, the live body
// list and the prefab-to-contract table are all static, exactly as their real
// counterparts are. Tests therefore run one at a time in this assembly, each
// setting up the world it needs, rather than pretending the game can be
// instantiated twice.
//
// This costs nothing: the contract half of this suite is pure and fast, and the
// body half is milliseconds.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
