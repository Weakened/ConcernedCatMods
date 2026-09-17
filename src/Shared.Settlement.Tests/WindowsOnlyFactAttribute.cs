using System.Runtime.InteropServices;

namespace Shared.Settlement.Tests;

/// <summary>A fact that exercises a Windows-only operating-system behaviour,
/// such as a sharing violation from a file held open with
/// <c>FileShare.None</c>. On other systems it is reported as <b>skipped</b>,
/// with the reason, rather than passing without having tested anything.
///
/// The products ship for the Windows game client. CI runs these suites on
/// Linux, where file sharing modes are advisory and a replace of a file held
/// open simply succeeds, so the fault the test injects cannot happen there.
/// </summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute(string reason)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Skip = "Windows-only: " + reason;
        }
    }
}
