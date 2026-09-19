using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Jotunn.Entities;
using TheConcernedCat.ConcernedForeman.Domain.Ladders;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Ladders;

/// <summary>`cf_ladders`: the read-only audit the ladder feature is measured
/// from (CF-LAD-001, `docs/mods/concerned-foreman/LADDERS.md` §1).
///
/// The decompiled game says vanilla ladders teleport and that the pieces exist;
/// it cannot say how tall they are, where their rungs sit, which way they face
/// or whether they snap to each other. Only the loaded game knows that, and
/// this command asks it. It reads: no piece is placed, moved, damaged or
/// changed, and nothing is written to the world.
///
/// It is the lead's tool, not part of the feature. It stays useful afterwards
/// as the way to re-measure after a game update.</summary>
internal sealed class LadderAuditCommand : ConsoleCommand
{
    private readonly Action<string> _log;

    internal LadderAuditCommand(Action<string> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public override string Name => "cf_ladders";

    public override string Help =>
        "Concerned Foreman ladder audit (read-only). Subcommands: " +
        "list (every loaded prefab carrying a Ladder), " +
        "here [radius] (measure the ladders around you), " +
        "snaps [radius] (their snap points, which is what stacking depends on).";

    public override void Run(string[] args, Terminal context)
    {
        string output;
        try
        {
            output = Execute(args);
        }
        catch (Exception exception)
        {
            output = "Ladder audit failed: " + exception.Message;
        }

        // Into the log as well as the console: the lead reads the log
        // afterwards rather than photographing a console.
        _log(output);
        context?.AddString(output);
    }

    internal string Execute(string[] args)
    {
        // Both positions come from LadderAuditArguments, which is where the
        // reasoning about them lives and where they are tested: the console
        // has already taken this command's own name off the front, so the
        // subcommand is the first argument and the radius the second.
        string[] given = args ?? Array.Empty<string>();
        string subcommand = LadderAuditArguments.Subcommand(given);
        if (!LadderAuditArguments.IsKnown(subcommand))
        {
            return "Unknown subcommand. " + Help;
        }

        switch (subcommand)
        {
            case LadderAuditArguments.List:
                return ListPrefabs();
            case LadderAuditArguments.Here:
                return MeasureNearby(LadderAuditArguments.Radius(given), snapPoints: false);
            case LadderAuditArguments.Snaps:
                return MeasureNearby(LadderAuditArguments.Radius(given), snapPoints: true);
            default:
                // The guard above and this switch are the same three names. If
                // they ever stop agreeing, say so rather than pick one: a
                // measurement nobody asked for is what this whole fix is about.
                return "Unknown subcommand. " + Help;
        }
    }

    /// <summary>Every prefab the scene knows that carries a <c>Ladder</c>, with
    /// the facts the feature needs: is it buildable, how big is it, and where
    /// does vanilla's Use teleport you.</summary>
    private static string ListPrefabs()
    {
        if (ZNetScene.instance == null)
        {
            return "No world is loaded, so the prefabs are not there to read.";
        }

        var report = new StringBuilder();
        report.AppendLine("Prefabs carrying a Ladder component:");
        int found = 0;

        foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
        {
            if (prefab == null)
            {
                continue;
            }

            Ladder ladder = prefab.GetComponent<Ladder>();
            if (ladder == null)
            {
                continue;
            }

            found++;
            Piece piece = prefab.GetComponent<Piece>();
            Bounds bounds = LocalBounds(prefab);
            Transform target = ladder.m_targetPos;
            string teleport = target == null
                ? "no target"
                : Format(prefab.transform.InverseTransformPoint(target.position));

            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0}: {1}, size {2:0.##} x {3:0.##} x {4:0.##} m, use distance {5:0.##}, teleport target {6}{7}",
                prefab.name,
                piece == null ? "not buildable" : "buildable (" + piece.m_name + ")",
                bounds.size.x,
                bounds.size.y,
                bounds.size.z,
                ladder.m_useDistance,
                teleport,
                piece != null && piece.m_comfort > 0 ? ", comfort " + piece.m_comfort : string.Empty));
        }

        if (found == 0)
        {
            report.AppendLine("  none. Either the scene is not ready or this build has no ladder pieces.");
        }

        return report.ToString().TrimEnd();
    }

    /// <summary>The ladders actually standing around the player, measured the
    /// way a climb needs them: the ends, the face a climber stands on, the rung
    /// spacing, and optionally the snap points that decide stacking.</summary>
    private string MeasureNearby(float radius, bool snapPoints)
    {
        Player player = Player.m_localPlayer;
        if (player == null)
        {
            return "No player, so there is nothing to measure from.";
        }

        // Unsorted: this is a manual audit, and the sort would be the most
        // expensive part of it.
        Ladder[] ladders = UnityEngine.Object.FindObjectsByType<Ladder>(FindObjectsSortMode.None);
        if (ladders == null || ladders.Length == 0)
        {
            return "No ladder is loaded anywhere near you.";
        }

        Vector3 from = player.transform.position;
        var report = new StringBuilder();
        report.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "Ladders within {0:0.#} m of {1}:",
            radius,
            Format(from)));

        int found = 0;
        foreach (Ladder ladder in ladders)
        {
            if (ladder == null)
            {
                continue;
            }

            Vector3 position = ladder.transform.position;
            float distance = Vector3.Distance(from, position);
            if (distance > radius)
            {
                continue;
            }

            found++;
            Bounds bounds = WorldBounds(ladder.gameObject);
            Piece piece = ladder.GetComponentInParent<Piece>();
            WearNTear wear = ladder.GetComponentInParent<WearNTear>();

            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0} at {1}, {2:0.#} m away",
                piece == null ? ladder.name : piece.gameObject.name,
                Format(position),
                distance));
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "    foot y {0:0.##}, head y {1:0.##}, height {2:0.##} m, width {3:0.##} m, depth {4:0.##} m",
                bounds.min.y,
                bounds.max.y,
                bounds.size.y,
                bounds.size.x,
                bounds.size.z));
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "    facing (forward) {0}, right {1}, health {2}",
                Format(ladder.transform.forward),
                Format(ladder.transform.right),
                wear == null ? "no WearNTear" : wear.GetHealthPercentage().ToString("0.##", CultureInfo.InvariantCulture)));

            Transform target = ladder.m_targetPos;
            if (target != null)
            {
                report.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "    vanilla Use would put you at {0} (that is the teleport this feature replaces)",
                    Format(target.position)));
            }

            AppendRungs(report, ladder.gameObject, bounds);

            if (snapPoints && piece != null)
            {
                AppendSnapPoints(report, piece);
            }
        }

        if (found == 0)
        {
            report.AppendLine("  none in range. Stand next to one, or pass a bigger radius.");
        }

        return report.ToString().TrimEnd();
    }

    /// <summary>Rung spacing, guessed from the mesh: the child transforms of a
    /// ladder are usually its rungs, so their vertical spacing is the pitch a
    /// climber's hands and feet follow. Reported as a measurement, never as a
    /// certainty: the lead confirms it by eye.</summary>
    private static void AppendRungs(StringBuilder report, GameObject ladder, Bounds bounds)
    {
        var heights = new List<float>();
        foreach (Transform child in ladder.GetComponentsInChildren<Transform>())
        {
            if (child == null || child.gameObject == ladder)
            {
                continue;
            }

            string name = child.name.ToLowerInvariant();
            if (name.Contains("rung") || name.Contains("step") || name.Contains("bar"))
            {
                heights.Add(child.position.y);
            }
        }

        if (heights.Count < 2)
        {
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "    rungs: not named in the mesh; the climb falls back to {0:0.##} m spacing",
                0.35f));
            return;
        }

        heights.Sort();
        float total = 0f;
        for (int index = 1; index < heights.Count; index++)
        {
            total += heights[index] - heights[index - 1];
        }

        report.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "    rungs: {0} found, average spacing {1:0.###} m over {2:0.##} m",
            heights.Count,
            total / (heights.Count - 1),
            bounds.size.y));
    }

    /// <summary>The snap points are the whole stacking question: a ladder with a
    /// point at its foot and one at its head stacks into a run, and one without
    /// does not.</summary>
    private static void AppendSnapPoints(StringBuilder report, Piece piece)
    {
        var points = new List<Transform>();
        piece.GetSnapPoints(points);
        if (points.Count == 0)
        {
            report.AppendLine("    snap points: none - this piece cannot be snapped end to end (decision L3 applies)");
            return;
        }

        report.AppendLine("    snap points: " + points.Count);
        foreach (Transform point in points)
        {
            if (point == null)
            {
                continue;
            }

            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "      {0} at local {1}",
                point.name,
                Format(piece.transform.InverseTransformPoint(point.position))));
        }
    }

    private static Bounds WorldBounds(GameObject root)
    {
        var bounds = new Bounds(root.transform.position, Vector3.zero);
        bool any = false;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
        {
            if (renderer == null)
            {
                continue;
            }

            if (!any)
            {
                bounds = renderer.bounds;
                any = true;
                continue;
            }

            bounds.Encapsulate(renderer.bounds);
        }

        if (!any)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>())
            {
                if (collider == null)
                {
                    continue;
                }

                if (!any)
                {
                    bounds = collider.bounds;
                    any = true;
                    continue;
                }

                bounds.Encapsulate(collider.bounds);
            }
        }

        return bounds;
    }

    private static Bounds LocalBounds(GameObject prefab)
    {
        var bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool any = false;
        foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>())
        {
            if (filter == null || filter.sharedMesh == null)
            {
                continue;
            }

            Bounds local = filter.sharedMesh.bounds;
            local.center = prefab.transform.InverseTransformPoint(filter.transform.TransformPoint(local.center));
            if (!any)
            {
                bounds = local;
                any = true;
                continue;
            }

            bounds.Encapsulate(local);
        }

        return bounds;
    }

    private static string Format(Vector3 value) => string.Format(
        CultureInfo.InvariantCulture,
        "({0:0.##}, {1:0.##}, {2:0.##})",
        value.x,
        value.y,
        value.z);
}
