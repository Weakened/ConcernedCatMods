using TheConcernedCat.Settlement.Storage;

namespace Shared.Settlement.Tests;

/// <summary>The file-writing both settlement records depend on.
///
/// This was extracted from <c>JournalStore</c> when CF-SET-004 added a second
/// file with the same requirement, and extracting it doubled how much depends on
/// it — so it gets its own tests rather than being covered only incidentally
/// through two stores. The two commit paths are genuinely different code
/// (<c>File.Move</c> for a new file, <c>File.Replace</c> for an existing one)
/// and only one of them was ever reachable in a fresh temp directory.
///
/// <b>What these tests do not cover, and cannot here.</b> The
/// <c>PlatformNotSupportedException</c> and <c>IOException</c> fallbacks to
/// <c>CopyOver</c> exist because <c>File.Replace</c>'s semantics differ across
/// runtimes — and these tests run on <c>net10.0</c> while the plugins ship
/// <c>net48</c>. The fallback is therefore exercised on a runtime that is not the
/// one it was written for. That gap is a known, recorded debt of this
/// repository, not something this file closes.</summary>
public sealed class AtomicTextFileTests : IDisposable
{
    private readonly string _root;

    public AtomicTextFileTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "cc-atomic-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // A locked temp file must never fail the suite.
        }
    }

    private string Path_(string name) => Path.Combine(_root, name);

    [Fact]
    public void CommittingToAPathWithNoFileThereLandsTheWholeFile()
    {
        string target = Path_("record.tsv");
        string temporary = target + ".tmp";

        Assert.Null(AtomicTextFile.TryWriteTemporary(_root, temporary, new[] { "a", "b" }));
        AtomicTextFile.Commit(temporary, target);

        Assert.Equal(new[] { "a", "b" }, File.ReadAllLines(target));

        // The temporary must not survive: a leftover .tmp beside a live record
        // is indistinguishable from an interrupted write that still needs
        // recovering.
        Assert.False(File.Exists(temporary));
    }

    [Fact]
    public void CommittingOverAnExistingFileReplacesItWhole()
    {
        string target = Path_("record.tsv");
        File.WriteAllLines(target, new[] { "old-1", "old-2", "old-3" });

        string temporary = target + ".tmp";
        Assert.Null(AtomicTextFile.TryWriteTemporary(_root, temporary, new[] { "new-1" }));
        AtomicTextFile.Commit(temporary, target);

        // Whole, not merged: one line, not one line followed by the old second
        // and third.
        Assert.Equal(new[] { "new-1" }, File.ReadAllLines(target));
        Assert.False(File.Exists(temporary));
    }

    [Fact]
    public void AFailedWriteReportsTheReasonAndChangesNothing()
    {
        string target = Path_("record.tsv");
        File.WriteAllLines(target, new[] { "untouched" });

        // A directory where the temporary file wants to be. What matters is
        // that a write failure is returned rather than thrown, and that the
        // live file is not touched on the way.
        string temporary = target + ".tmp";
        Directory.CreateDirectory(temporary);

        Exception? failure = AtomicTextFile.TryWriteTemporary(_root, temporary, new[] { "new" });

        Assert.NotNull(failure);
        Assert.Equal(new[] { "untouched" }, File.ReadAllLines(target));
    }

    [Fact]
    public void WritingCreatesTheDirectoryWhenItIsNotThereYet()
    {
        string nested = Path.Combine(_root, "settlements");
        string target = Path.Combine(nested, "record.tsv");

        Assert.Null(AtomicTextFile.TryWriteTemporary(nested, target + ".tmp", new[] { "a" }));
        AtomicTextFile.Commit(target + ".tmp", target);

        Assert.True(File.Exists(target));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("with\ttab")]
    [InlineData("with\r\nnewlines")]
    [InlineData("with*star")]
    [InlineData("with%percent")]
    [InlineData("100%")]
    [InlineData("")]
    public void EveryFieldSurvivesTheRoundTrip(string value)
    {
        Assert.Equal(value, AtomicTextFile.Unescape(AtomicTextFile.Escape(value)));
    }

    [Fact]
    public void AnEscapeSequenceTypedByAPlayerIsNotMistakenForOne()
    {
        // The ordering trap: '%' is escaped FIRST on the way out and unescaped
        // LAST on the way back. Get that backwards and a chest a player named
        // "row%09two" comes back with a real tab in it, which then splits the
        // line and reads as damage.
        const string Typed = "row%09two";

        string escaped = AtomicTextFile.Escape(Typed);

        Assert.DoesNotContain("\t", escaped);
        Assert.Equal(Typed, AtomicTextFile.Unescape(escaped));
    }

    [Fact]
    public void AnEscapedFieldNeverContainsASeparator()
    {
        string escaped = AtomicTextFile.Escape("a\tb\r\nc*d%e");

        Assert.DoesNotContain("\t", escaped);
        Assert.DoesNotContain("\r", escaped);
        Assert.DoesNotContain("\n", escaped);
    }
}
