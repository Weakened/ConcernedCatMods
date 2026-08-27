using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Sharing policy for incoming sync entities. Structural
/// protections (revision monotonicity, tombstone no-resurrection) live in
/// the stores; this layer adds sharing semantics: only Table/Server-scoped
/// entities travel, and a non-owner cannot delete someone else's entity.</summary>
internal static class SyncPolicy
{
    public enum Verdict
    {
        Apply,
        SupersededByLocal,
        NotShared,
        NonOwnerDeleteRejected,
        Conflict,
    }

    public static Verdict EvaluatePin(AtlasPin? local, AtlasPin incoming)
    {
        return Evaluate(
            local?.Revision, local?.Deleted, local?.OwnerAuthor, local is null ? null : PinCodec.SerializeRow(local),
            incoming.Revision, incoming.Deleted, incoming.Scope, incoming.LastAuthor, PinCodec.SerializeRow(incoming));
    }

    public static Verdict EvaluateRoute(AtlasRoute? local, AtlasRoute incoming)
    {
        string? localRow = null;
        if (local is not null)
        {
            localRow = string.Join("\n", RouteCodec.SerializeRoute(local));
        }

        return Evaluate(
            local?.Revision, local?.Deleted, local?.OwnerAuthor, localRow,
            incoming.Revision, incoming.Deleted, incoming.Scope, incoming.LastAuthor,
            string.Join("\n", RouteCodec.SerializeRoute(incoming)));
    }

    private static Verdict Evaluate(
        long? localRevision,
        bool? localDeleted,
        string? localOwner,
        string? localRow,
        long incomingRevision,
        bool incomingDeleted,
        AtlasScope incomingScope,
        string incomingLastAuthor,
        string incomingRow)
    {
        if (incomingScope == AtlasScope.Private)
        {
            return Verdict.NotShared;
        }

        if (localRevision is null)
        {
            return Verdict.Apply;
        }

        if (incomingRevision < localRevision.Value)
        {
            return Verdict.SupersededByLocal;
        }

        if (incomingRevision == localRevision.Value)
        {
            return string.Equals(localRow, incomingRow, StringComparison.Ordinal)
                ? Verdict.SupersededByLocal
                : Verdict.Conflict;
        }

        if (incomingDeleted && localDeleted == false &&
            !string.IsNullOrEmpty(localOwner) &&
            !string.Equals(incomingLastAuthor, localOwner, StringComparison.Ordinal))
        {
            return Verdict.NonOwnerDeleteRejected;
        }

        return Verdict.Apply;
    }
}

/// <summary>A reviewed synchronization plan: what an incoming share would
/// do, computed without touching the stores. Apply is explicit and
/// selective; conflicts default to keep-local, and taking the remote side
/// lands as a NEW local revision, preserving the convergence contract.</summary>
internal sealed class SyncPlan
{
    public List<AtlasPin> NewPins { get; } = new();
    public List<AtlasPin> UpdatedPins { get; } = new();
    public List<AtlasPin> TombstonePins { get; } = new();
    public List<(AtlasPin Local, AtlasPin Remote)> PinConflicts { get; } = new();
    public int RejectedPins { get; set; }
    public int SupersededPins { get; set; }

    public List<AtlasRoute> NewRoutes { get; } = new();
    public List<AtlasRoute> UpdatedRoutes { get; } = new();
    public List<AtlasRoute> TombstoneRoutes { get; } = new();
    public List<(AtlasRoute Local, AtlasRoute Remote)> RouteConflicts { get; } = new();
    public int RejectedRoutes { get; set; }
    public int SupersededRoutes { get; set; }

    public bool IsEmpty =>
        NewPins.Count == 0 && UpdatedPins.Count == 0 && TombstonePins.Count == 0 && PinConflicts.Count == 0 &&
        NewRoutes.Count == 0 && UpdatedRoutes.Count == 0 && TombstoneRoutes.Count == 0 && RouteConflicts.Count == 0;

    public string Summary()
    {
        return $"pins: +{NewPins.Count} new, {UpdatedPins.Count} updated, {TombstonePins.Count} deletions, " +
            $"{PinConflicts.Count} conflicts, {RejectedPins} rejected, {SupersededPins} already-newer · " +
            $"routes: +{NewRoutes.Count} new, {UpdatedRoutes.Count} updated, {TombstoneRoutes.Count} deletions, " +
            $"{RouteConflicts.Count} conflicts, {RejectedRoutes} rejected, {SupersededRoutes} already-newer";
    }

    /// <summary>Names of the local entities this plan would delete, for the
    /// preview: author identity is labeling, not authentication, so a
    /// deletion must be reviewable by NAME before it is applied
    /// (SEC-1.0-001). Empty when the plan deletes nothing.</summary>
    public List<string> DeletionNames(int max)
    {
        var names = new List<string>();
        foreach (AtlasPin pin in TombstonePins)
        {
            if (names.Count >= max)
            {
                return names;
            }

            names.Add($"pin \"{(pin.Name.Length > 0 ? pin.Name : pin.Id.ToString())}\"");
        }

        foreach (AtlasRoute route in TombstoneRoutes)
        {
            if (names.Count >= max)
            {
                return names;
            }

            names.Add($"route \"{(route.Name.Length > 0 ? route.Name : route.Id.ToString())}\"");
        }

        return names;
    }
}

internal static class SyncPlanner
{
    /// <summary>Everything shareable in the local stores: Table/Server
    /// scoped entities INCLUDING their tombstones, so deletions propagate
    /// and can never resurrect on peers.</summary>
    public static (List<AtlasPin> Pins, List<AtlasRoute> Routes) CollectShared(PinStore pins, RouteStore routes)
    {
        var sharedPins = new List<AtlasPin>();
        foreach (AtlasPin pin in pins.All)
        {
            if (pin.Scope != AtlasScope.Private)
            {
                sharedPins.Add(pin.Clone());
            }
        }

        var sharedRoutes = new List<AtlasRoute>();
        foreach (AtlasRoute route in routes.All)
        {
            if (route.Scope != AtlasScope.Private)
            {
                sharedRoutes.Add(route.Clone());
            }
        }

        return (sharedPins, sharedRoutes);
    }

    public static SyncPlan Plan(
        PinStore pins,
        RouteStore routes,
        IReadOnlyList<AtlasPin> remotePins,
        IReadOnlyList<AtlasRoute> remoteRoutes)
    {
        var plan = new SyncPlan();

        foreach (AtlasPin incoming in remotePins)
        {
            pins.TryGet(incoming.Id, out AtlasPin local);
            switch (SyncPolicy.EvaluatePin(local, incoming))
            {
                case SyncPolicy.Verdict.Apply:
                    if (local is null)
                    {
                        plan.NewPins.Add(incoming);
                    }
                    else if (incoming.Deleted && !local.Deleted)
                    {
                        plan.TombstonePins.Add(incoming);
                    }
                    else
                    {
                        plan.UpdatedPins.Add(incoming);
                    }

                    break;
                case SyncPolicy.Verdict.Conflict:
                    plan.PinConflicts.Add((local!, incoming));
                    break;
                case SyncPolicy.Verdict.NonOwnerDeleteRejected:
                    plan.RejectedPins++;
                    break;
                case SyncPolicy.Verdict.SupersededByLocal:
                    plan.SupersededPins++;
                    break;
                case SyncPolicy.Verdict.NotShared:
                    plan.RejectedPins++;
                    break;
            }
        }

        foreach (AtlasRoute incoming in remoteRoutes)
        {
            routes.TryGet(incoming.Id, out AtlasRoute local);
            switch (SyncPolicy.EvaluateRoute(local, incoming))
            {
                case SyncPolicy.Verdict.Apply:
                    if (local is null)
                    {
                        plan.NewRoutes.Add(incoming);
                    }
                    else if (incoming.Deleted && !local.Deleted)
                    {
                        plan.TombstoneRoutes.Add(incoming);
                    }
                    else
                    {
                        plan.UpdatedRoutes.Add(incoming);
                    }

                    break;
                case SyncPolicy.Verdict.Conflict:
                    plan.RouteConflicts.Add((local!, incoming));
                    break;
                case SyncPolicy.Verdict.NonOwnerDeleteRejected:
                    plan.RejectedRoutes++;
                    break;
                case SyncPolicy.Verdict.SupersededByLocal:
                    plan.SupersededRoutes++;
                    break;
                case SyncPolicy.Verdict.NotShared:
                    plan.RejectedRoutes++;
                    break;
            }
        }

        return plan;
    }

    /// <summary>Applies a plan. Non-conflicting entries land through the
    /// stores' revision-guarded upserts; conflicts keep the local side
    /// unless <paramref name="takeRemoteOnConflict"/> — taking the remote
    /// side copies its fields under a NEW local revision so both peers
    /// converge on it.</summary>
    public static int Apply(SyncPlan plan, PinStore pins, RouteStore routes, bool takeRemoteOnConflict)
    {
        int applied = 0;
        foreach (AtlasPin incoming in plan.NewPins)
        {
            applied += pins.Upsert(incoming.Clone()) ? 1 : 0;
        }

        foreach (AtlasPin incoming in plan.UpdatedPins)
        {
            applied += pins.Upsert(incoming.Clone()) ? 1 : 0;
        }

        foreach (AtlasPin incoming in plan.TombstonePins)
        {
            applied += pins.Upsert(incoming.Clone()) ? 1 : 0;
        }

        foreach ((AtlasPin local, AtlasPin remote) in plan.PinConflicts)
        {
            if (takeRemoteOnConflict)
            {
                AtlasPin captured = remote;
                pins.Mutate(local.Id, pin =>
                {
                    long revision = pin.Revision;
                    DateTime created = pin.CreatedUtc;
                    pin.CopyFrom(captured);
                    pin.Revision = revision;
                    pin.CreatedUtc = created;
                });
                applied++;
            }
        }

        foreach (AtlasRoute incoming in plan.NewRoutes)
        {
            applied += routes.Upsert(incoming.Clone()) ? 1 : 0;
        }

        foreach (AtlasRoute incoming in plan.UpdatedRoutes)
        {
            applied += routes.Upsert(incoming.Clone()) ? 1 : 0;
        }

        foreach (AtlasRoute incoming in plan.TombstoneRoutes)
        {
            applied += routes.Upsert(incoming.Clone()) ? 1 : 0;
        }

        foreach ((AtlasRoute local, AtlasRoute remote) in plan.RouteConflicts)
        {
            if (takeRemoteOnConflict)
            {
                AtlasRoute captured = remote;
                routes.Mutate(local.Id, route =>
                {
                    long revision = route.Revision;
                    DateTime created = route.CreatedUtc;
                    route.CopyFrom(captured);
                    route.Revision = revision;
                    route.CreatedUtc = created;
                });
                applied++;
            }
        }

        return applied;
    }
}

/// <summary>Received shares awaiting review, newest per author. Nothing in
/// the inbox touches the stores until the player applies it.</summary>
internal sealed class SyncInbox
{
    public const int MaxAuthors = 8;

    public sealed class Envelope
    {
        public Envelope(string authorId, string authorName, List<AtlasPin> pins, List<AtlasRoute> routes, DateTime receivedUtc)
        {
            AuthorId = authorId;
            AuthorName = authorName;
            Pins = pins;
            Routes = routes;
            ReceivedUtc = receivedUtc;
        }

        public string AuthorId { get; }
        public string AuthorName { get; }
        public List<AtlasPin> Pins { get; }
        public List<AtlasRoute> Routes { get; }
        public DateTime ReceivedUtc { get; }
    }

    private readonly List<Envelope> _envelopes = new();

    public IReadOnlyList<Envelope> Envelopes => _envelopes;

    public void Add(Envelope envelope)
    {
        _envelopes.RemoveAll(existing => existing.AuthorId == envelope.AuthorId);
        _envelopes.Add(envelope);
        while (_envelopes.Count > MaxAuthors)
        {
            _envelopes.RemoveAt(0);
        }
    }

    public bool TryPeek(string authorIdOrName, out Envelope envelope)
    {
        foreach (Envelope candidate in _envelopes)
        {
            if (string.Equals(candidate.AuthorId, authorIdOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.AuthorName, authorIdOrName, StringComparison.OrdinalIgnoreCase))
            {
                envelope = candidate;
                return true;
            }
        }

        envelope = null!;
        return false;
    }

    public bool TryTake(string authorIdOrName, out Envelope envelope)
    {
        foreach (Envelope candidate in _envelopes)
        {
            if (string.Equals(candidate.AuthorId, authorIdOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.AuthorName, authorIdOrName, StringComparison.OrdinalIgnoreCase))
            {
                envelope = candidate;
                _envelopes.Remove(candidate);
                return true;
            }
        }

        envelope = null!;
        return false;
    }

    public void Clear()
    {
        _envelopes.Clear();
    }
}
