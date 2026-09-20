using System.Collections.Generic;
using System.IO;

namespace TheConcernedCat.ConcernedCartographer.Storage;

/// <summary>Writing a text file this product owns, with the directory created
/// first.
///
/// <b>Why this exists (#367).</b> The support report was written with a bare
/// <c>File.WriteAllLines</c>. Every other writer in the product creates its
/// directory first; that one did not, so on a profile where the product's data
/// directory is not yet there — a fresh install whose first act is asking for
/// help, or a directory a player tidied away — <c>cc_atlas support</c> threw
/// instead of producing the file whose entire purpose is to be attached to a bug
/// report. The console wrapper did catch it, so it was never a crash; what the
/// player got was a failure message where the diagnostic should have been, at
/// exactly the moment they were already asking for help.
///
/// One line of logic, in the domain, because a test can then create a path
/// several directories deep and watch it work — which no test could do while
/// the write lived in a class that needs BepInEx.</summary>
internal static class AtlasTextFile
{
    /// <summary>Writes <paramref name="lines"/> to <paramref name="path"/>,
    /// creating the containing directory if it is missing. Exceptions are the
    /// caller's: this promises the directory, not the disk.</summary>
    public static void WriteLines(string path, IEnumerable<string> lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
    }
}
