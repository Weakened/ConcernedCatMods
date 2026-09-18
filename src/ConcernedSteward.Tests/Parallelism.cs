// The suite is pure computation over its own fixtures: no shared file, no
// static game state, no clock. Running the collections in parallel is
// therefore safe, and it is the difference between a suite you run and one you
// remember to run.
[assembly: CollectionBehavior(MaxParallelThreads = -1)]
