using System;
using System.Globalization;
using System.IO;
using System.Text;
using TheConcernedCat.ConcernedLadders.Domain;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Runtime.Animation;

/// <summary>Development-only per-frame contact trace (ClimbTuning.TraceContacts, off in normal play). One row per limb
/// per frame to BepInEx/ladder_contact_trace.csv: motion, target and solved patch, error, gap, weights, reach, state.
/// Bounded to 40 000 rows per game session; any write fault turns it off.</summary>
internal static class ContactTrace
{
    private const int MaximumRows = 40000;
    private static StreamWriter? _writer;
    private static int _rows;
    private static bool _failed;
    private static readonly string[] Names = { "LH", "RH", "LF", "RF" };

    internal static void Row(in ClimbPresentationFrame frame, int limb, OriginalClimbPresenter.ContactRecord record, Vector3 patch, float error, float gap)
    {
        if (_failed || _rows >= MaximumRows) { return; }
        try
        {
            if (_writer == null)
            {
                string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "ladder_contact_trace.csv");
                _writer = new StreamWriter(path, false, new UTF8Encoding(false));
                _writer.WriteLine("time,motion,face,feetY,limb,planted,skipped,native,requested,effective,reach,targetX,targetY,targetZ,patchX,patchY,patchZ,error,gap,rootX,rootY,rootZ,chain,progress");
            }

            var inv = CultureInfo.InvariantCulture;
            _writer.WriteLine(string.Join(",", new[]
            {
                Time.time.ToString("0.0000", inv), frame.Motion.ToString(), frame.Face.ToString(), frame.Feet.Y.ToString("0.0000", inv),
                Names[limb], record.Planted ? "1" : "0", record.Skipped ? "1" : "0", record.Native ? "1" : "0",
                record.Requested.ToString("0.000", inv), record.Effective.ToString("0.000", inv), record.Ratio.ToString("0.000", inv),
                record.Target.x.ToString("0.0000", inv), record.Target.y.ToString("0.0000", inv), record.Target.z.ToString("0.0000", inv),
                patch.x.ToString("0.0000", inv), patch.y.ToString("0.0000", inv), patch.z.ToString("0.0000", inv),
                error.ToString("0.0000", inv), gap.ToString("0.0000", inv),
                record.Root.x.ToString("0.0000", inv), record.Root.y.ToString("0.0000", inv), record.Root.z.ToString("0.0000", inv),
                record.Chain.ToString("0.000", inv), record.Progress.ToString("0.000", inv),
            }));
            if (++_rows % 200 == 0) { _writer.Flush(); }
        }
        catch (Exception)
        {
            _failed = true;
            try { _writer?.Dispose(); } catch { /* Diagnostics only. */ }
            _writer = null;
        }
    }

    internal static void Flush()
    {
        try { _writer?.Flush(); } catch { /* Diagnostics only. */ }
    }
}
