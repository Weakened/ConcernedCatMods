using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TheConcernedCat.ConcernedNPC.Persistence;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>The sidecar file and the atomic write, against a real temporary
/// directory.
///
/// <b>The golden fixture is the important one.</b> A pre-refactor sidecar,
/// written here as the literal bytes a shipped build produced, is read and
/// written back without a byte changing. That is the whole of what this half of
/// the move had to preserve: the row tags, the field order and the schema
/// number never reach this library at all, and these tests are what say so
/// rather than assume it.</summary>
public sealed class SidecarPersistenceTests : IDisposable
{
    private readonly string _root;

    public SidecarPersistenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cnpc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // A temporary directory a scanner is holding is not a test failure.
        }
    }

    // -----------------------------------------------------------------
    // The escaping pair. Five sequences, in an order that is load-bearing.
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("has\ta tab", "has%09a tab")]
    [InlineData("has*a star", "has%2Aa star")]
    [InlineData("has\r\na break", "has%0D%0Aa break")]
    [InlineData("100% sure", "100%25 sure")]
    public void One_field_is_escaped_exactly_as_it_always_was(string raw, string escaped)
    {
        Assert.Equal(escaped, NpcAtomicText.Escape(raw));
        Assert.Equal(raw, NpcAtomicText.Unescape(escaped));
    }

    [Fact]
    public void A_player_who_typed_an_escape_sequence_gets_it_back_rather_than_a_tab()
    {
        // The per-cent sign is escaped first on the way out and unescaped last
        // on the way back. Reverse either and this text comes back as a tab,
        // and every existing file containing one reads differently.
        string typed = "%09";

        string escaped = NpcAtomicText.Escape(typed);

        Assert.Equal("%2509", escaped);
        Assert.Equal(typed, NpcAtomicText.Unescape(escaped));
    }

    [Fact]
    public void Empty_and_null_both_escape_to_empty()
    {
        Assert.Equal(string.Empty, NpcAtomicText.Escape(null));
        Assert.Equal(string.Empty, NpcAtomicText.Escape(string.Empty));
        Assert.Equal(string.Empty, NpcAtomicText.Unescape(null));
    }

    // -----------------------------------------------------------------
    // The file.
    // -----------------------------------------------------------------

    [Fact]
    public void This_library_refuses_to_resolve_a_path_of_its_own()
    {
        Assert.False(NpcSidecarFile.TryAt(null, out _, out string reason));
        Assert.Contains("composes none", reason, StringComparison.Ordinal);

        Assert.False(NpcSidecarFile.TryAt("companions.tsv", out _, out reason));
        Assert.Contains("must be absolute", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_file_is_a_first_run_and_not_an_error()
    {
        NpcSidecarFile file = At("nothing-here.tsv");

        NpcSidecarRead read = file.Read();

        Assert.Equal(NpcSidecarOutcome.Missing, read.Outcome);
        Assert.Empty(read.Lines);
        Assert.False(file.Exists);
    }

    [Fact]
    public void A_pre_refactor_sidecar_reads_back_byte_for_byte_and_writes_back_the_same()
    {
        // The literal bytes a shipped build wrote: a header comment, a schema
        // row, a scope row naming the product, world and character, an unlock
        // row and a quest row. Not one of those tags, numbers or field
        // positions is known to this library, and that is the point.
        string[] shipped =
        {
            "# ConcernedCatMods companions v1",
            "s\t1",
            "k\tconcerned-cartographer\tx16:0123456789ABCDEF\tx16:FEDCBA9876543210",
            "u\t2",
            "q\thulgi.compass\t4\t7\t0\t1",
        };

        string path = Path.Combine(_root, "concerned-cartographer.0123456789ABCDEF.FEDCBA9876543210.companions.tsv");
        File.WriteAllLines(path, shipped, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        byte[] before = File.ReadAllBytes(path);

        NpcSidecarFile file = At(Path.GetFileName(path));
        NpcSidecarRead read = file.Read();

        Assert.Equal(NpcSidecarOutcome.Read, read.Outcome);
        Assert.Equal(shipped, read.Lines);

        NpcSidecarWrite written = file.Write(read.Lines);

        Assert.True(written.IsSaved, written.Failure);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_write_leaves_no_temporary_file_behind_when_it_succeeds()
    {
        NpcSidecarFile file = At("data.tsv");

        file.Write(new[] { "s\t1" });

        Assert.True(File.Exists(file.Path));
        Assert.False(File.Exists(file.TemporaryPath));
    }

    [Fact]
    public void A_write_replaces_the_live_file_whole()
    {
        NpcSidecarFile file = At("data.tsv");
        file.Write(new[] { "s\t1", "old", "rows", "everywhere" });

        file.Write(new[] { "s\t1", "new" });

        Assert.Equal(new[] { "s\t1", "new" }, File.ReadAllLines(file.Path));
    }

    [Fact]
    public void A_failed_commit_keeps_the_complete_copy_and_names_it()
    {
        // The live path is a directory, so the swap cannot happen. Three of the
        // five writers this replaces deleted the temporary file in a finally at
        // exactly this moment - throwing away the only intact copy of the new
        // content there was.
        string path = Path.Combine(_root, "data.tsv");
        Directory.CreateDirectory(path);
        NpcSidecarFile file = At("data.tsv");

        NpcSidecarWrite written = file.Write(new[] { "s\t1", "the new content" });

        Assert.False(written.IsSaved);
        Assert.Equal(file.TemporaryPath, written.CompleteCopyPath);
        Assert.True(File.Exists(file.TemporaryPath));
        Assert.Equal(new[] { "s\t1", "the new content" }, File.ReadAllLines(file.TemporaryPath));
    }

    [Fact]
    public void A_write_of_nothing_at_all_is_refused_rather_than_emptying_the_file()
    {
        NpcSidecarFile file = At("data.tsv");
        file.Write(new[] { "s\t1" });

        NpcSidecarWrite written = file.Write(null);

        Assert.False(written.IsSaved);
        Assert.Equal(new[] { "s\t1" }, File.ReadAllLines(file.Path));
    }

    [Fact]
    public void An_unreadable_file_is_moved_aside_rather_than_deleted_and_the_next_one_gets_its_own_name()
    {
        NpcSidecarFile file = At("data.tsv");
        File.WriteAllText(file.Path, "damaged");

        string? first = file.TryQuarantine();

        Assert.NotNull(first);
        Assert.EndsWith(".corrupt", first!, StringComparison.Ordinal);
        Assert.Equal("damaged", File.ReadAllText(first));
        Assert.False(File.Exists(file.Path));

        File.WriteAllText(file.Path, "damaged again");
        string? second = file.TryQuarantine();

        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.Equal("damaged", File.ReadAllText(first));
    }

    [Fact]
    public void Quarantining_a_file_that_is_not_there_answers_null_rather_than_inventing_one()
    {
        NpcSidecarFile file = At("data.tsv");

        Assert.Null(file.TryQuarantine());
    }

    [Fact]
    public void Existence_is_asked_about_one_file_and_never_about_the_directory()
    {
        // Counting files in a role's data root answers "has this player ever
        // played anything", which is a different question and has already
        // shipped twice as a permanent wrong grant.
        File.WriteAllText(Path.Combine(_root, "somebody-elses.companions.tsv"), "s\t1");
        NpcSidecarFile file = At("mine.companions.tsv");

        Assert.False(file.Exists);
    }

    [Fact]
    public void The_write_creates_the_roles_directory_but_never_names_it()
    {
        string nested = Path.Combine(_root, "deeper");
        Assert.True(NpcSidecarFile.TryAt(Path.Combine(nested, "data.tsv"), out NpcSidecarFile? file, out string why), why);

        Assert.True(file!.Write(new[] { "s\t1" }).IsSaved);
        Assert.True(Directory.Exists(nested));
    }

    private NpcSidecarFile At(string fileName)
    {
        Assert.True(
            NpcSidecarFile.TryAt(Path.Combine(_root, fileName), out NpcSidecarFile? file, out string reason), reason);
        return file!;
    }
}
