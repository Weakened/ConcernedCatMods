using System.IO;
using System.Reflection;

namespace TheConcernedCat.ConcernedTeamster.Domain.Risk;

/// <summary>Loads the embedded descent calibration data (CT-011); same
/// dual-embed pattern as the load data so tests prove the shipped bytes.
/// Fails closed to null — descent risk stays Unknown.</summary>
public static class DescentCalibrationSource
{
    public const string ResourceName = "CT.Data.CartDescentCalibration";

    public static DescentCalibrationData? TryLoadEmbedded()
    {
        try
        {
            Assembly assembly = typeof(DescentCalibrationSource).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            return DescentCalibrationData.Parse(reader.ReadToEnd());
        }
        catch
        {
            return null;
        }
    }
}
