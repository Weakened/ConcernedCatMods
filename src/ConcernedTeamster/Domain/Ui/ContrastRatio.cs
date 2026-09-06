using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>WCAG 2.1 contrast ratio between two sRGB colors — the public
/// W3C formula (https://www.w3.org/TR/WCAG21/#dfn-contrast-ratio), used to
/// audit Teamster's panel text against its background (CT-033). Returns a
/// value from 1 (no contrast) to 21 (black on white); 4.5 is the AA target
/// for normal-size text. Pure color math, not a Valheim API.</summary>
public static class ContrastRatio
{
    public const double AaNormalTextMinimum = 4.5;

    public static double Compute((float R, float G, float B) colorOne, (float R, float G, float B) colorTwo)
    {
        double luminanceOne = RelativeLuminance(colorOne);
        double luminanceTwo = RelativeLuminance(colorTwo);
        double lighter = Math.Max(luminanceOne, luminanceTwo);
        double darker = Math.Min(luminanceOne, luminanceTwo);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance((float R, float G, float B) color)
    {
        return 0.2126 * Linearize(color.R) + 0.7152 * Linearize(color.G) + 0.0722 * Linearize(color.B);
    }

    private static double Linearize(float channel)
    {
        double c = Math.Min(1.0, Math.Max(0.0, channel));
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
