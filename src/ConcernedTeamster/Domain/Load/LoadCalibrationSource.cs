using System.IO;
using System.Reflection;

namespace TheConcernedCat.ConcernedTeamster.Domain.Load;

/// <summary>Loads the embedded calibration data (CT-008). The same file is
/// embedded into both the plugin and the test assembly under one logical
/// name, so tests prove the shipped bytes and the game runs them. Fails
/// closed to null — the caller logs once and load advice stays off.</summary>
public static class LoadCalibrationSource
{
    public const string ResourceName = "CT.Data.CartLoadCalibration";

    public static LoadCalibrationData? TryLoadEmbedded()
    {
        try
        {
            Assembly assembly = typeof(LoadCalibrationSource).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            return LoadCalibrationData.Parse(reader.ReadToEnd());
        }
        catch
        {
            return null;
        }
    }
}
