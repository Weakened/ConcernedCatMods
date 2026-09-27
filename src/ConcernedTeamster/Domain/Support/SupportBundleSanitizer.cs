using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedTeamster.Domain.Support;

/// <summary>Scrubs every line of a support bundle before it is composed
/// (CT-039, #410, #411).
///
/// <b>This is now a delegation, and that is the point.</b> The patterns used to
/// live here, written independently of Concerned Cartographer's because the two
/// products share no compile-time reference. #388 then fixed a defect in that
/// one and #410 had to find and fix the identical defect here, separately; and
/// Concerned Foreman, which had no scrubber at all, was about to need a third
/// copy. The patterns are shared source now
/// (<see cref="TheConcernedCat.Diagnostics.PathScrubber"/>), which is what
/// <c>AGENTS.md</c> prescribes for code two products need and which contains no
/// Unity, BepInEx or Jötunn type.
///
/// <b>What stays this product's decision.</b> A matched path is replaced by
/// <c>&lt;path&gt;</c> with <b>no terminal segment kept</b>. Cartographer keeps a
/// file name because its composer only ever builds strings from fixed
/// components; this sanitizer's input is the raw log tail, gathered by
/// <c>LogTailRecorder</c> from lines this mod writes for a developer — and the
/// class comment there records that they embed full paths. A leaf file name can
/// itself be the identifying content, so none is kept. That is the one
/// parameter the shared scrubber takes, and this is the caller that says
/// <c>false</c>.
///
/// The limits that remain are listed in
/// <c>docs/mods/concerned-teamster/PRIVACY_INVENTORY.md</c> and asserted in
/// <c>SupportBundleTests.Sanitizer_TheseAreTheStatedLimits</c>.</summary>
public static class SupportBundleSanitizer
{
    public const int MaxLineLength = PathScrubber.DefaultMaxLength;

    /// <summary>One line, scrubbed and length-capped.</summary>
    public static string Sanitize(string? line) =>
        PathScrubber.Scrub(line, keepFileName: false, maxLength: MaxLineLength);
}
