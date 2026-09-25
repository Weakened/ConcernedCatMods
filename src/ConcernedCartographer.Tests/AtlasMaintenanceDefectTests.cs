using System.Globalization;
using System.IO;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Companions;
using TheConcernedCat.ConcernedCartographer.Reporting;
using TheConcernedCat.ConcernedCartographer.Roads;
using TheConcernedCat.ConcernedCartographer.Storage;

namespace ConcernedCartographer.Tests;

/// <summary>Issue #367: three defects in the atlas maintenance commands, none
/// of which announces itself to the player.
///
/// <b>(a)</b> <c>cc_atlas support</c> replied "Sanitized support report (no
/// positions/names/notes/world ids/paths) written to C:\…\support-report.log" —
/// a claim that there are no paths, followed by a path. Read at the one moment
/// somebody is already confused enough to be asking for help.
///
/// <b>(b)</b> The report was written with a bare <c>File.WriteAllLines</c>. On a
/// profile whose data directory is not there yet, the command that exists to
/// produce a bug-report attachment produced a failure message instead. And the
/// failure message printed the exception's raw text, which for a filesystem
/// exception is the full path and therefore this machine's user name — into the
/// reply a player screenshots, from the one command whose purpose is
/// shareability.
///
/// <b>(c)</b> The backup tools kept their own list of three of the five
/// per-world sidecars, and composed the backup folder name under the player's
/// own culture while the lister globbed invariantly. Two silent data losses: a
/// sidecar in no backup, and a backup under a name nothing can find.
///
/// All three are asserted against the code the plugin ships. That took moving
/// the decisions into the domain: the console reply, the directory-creating
/// write, the failure wording, the sidecar family and the on-disk naming all
/// lived in classes that need BepInEx and could not be compiled into this
/// assembly at all.</summary>
public sealed class AtlasMaintenanceDefectTests : IDisposable
{
    /// <summary>A world uid shaped like the ones that break: negative, so its
    /// leading character is the culture's negative sign.</summary>
    private const long NegativeWorldUid = -731284559L;

    private const long PositiveWorldUid = 731284559L;

    private static readonly DateTime Stamped =
        new DateTime(2026, 9, 20, 14, 5, 6, DateTimeKind.Utc);

    private readonly string _directory;

    public AtlasMaintenanceDefectTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "cc-367-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A culture built to break the old code rather than chosen and
    /// hoped over: U+2212 MINUS SIGN as the negative sign, which several real
    /// cultures use and which no invariant glob will ever match.</summary>
    private static CultureInfo Hostile()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = "\u2212";
        return culture;
    }

    private static T UnderCulture<T>(CultureInfo culture, Func<T> work)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            return work();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ------------------------------------------------------------------
    // (c) the sidecar family: one list, all five
    // ------------------------------------------------------------------

    [Fact]
    public void EveryPerWorldSidecarIsInTheOneFamily()
    {
        // Three of these five were missing from the backup tools' own copy of
        // the list. `.survey-rejected.tsv` is a player's memory of what he
        // already said no to and `.terrain-intent.tsv` is his exclusion mask:
        // in no backup, unrestorable, and absent from the support report while
        // it read as complete.
        Assert.Equal(
            new[]
            {
                ".roads.tsv",
                ".pins.tsv",
                ".routes-atlas.tsv",
                ".survey-rejected.tsv",
                ".terrain-intent.tsv",
            },
            CartographerWorldSidecars.Suffixes);
    }

    [Fact]
    public void TheFamilyIsSuffixesOnlyAndCarriesNoWorldUid()
    {
        // A name in this list rather than a suffix would be world-independent
        // and would make every world's backup pick up another world's file.
        Assert.All(
            CartographerWorldSidecars.Suffixes,
            suffix => Assert.StartsWith(".", suffix));
        Assert.All(
            CartographerWorldSidecars.Suffixes,
            suffix => Assert.EndsWith(".tsv", suffix));
    }

    // ------------------------------------------------------------------
    // (c) naming: the writer and the lister must agree, in any culture
    // ------------------------------------------------------------------

    [Fact]
    public void TheHostileCultureReallyDoesBreakInterpolation()
    {
        // Without this the culture tests below could be vacuous: they would
        // pass under a culture that formats exactly like the invariant one and
        // prove nothing. This is the assertion that makes them mean something.
        long uid = NegativeWorldUid;
        Assert.NotEqual(
            uid.ToString(CultureInfo.InvariantCulture),
            UnderCulture(Hostile(), () => $"{uid}"));
    }

    [Theory]
    [InlineData(NegativeWorldUid)]
    [InlineData(PositiveWorldUid)]
    public void ABackupFolderNameAlwaysMatchesTheGlobThatLooksForIt(long worldUid)
    {
        // The defect, stated as the property that was violated: `Backup` writes
        // the folder, `ListBackups` globs for it, and if the two spell the uid
        // differently the backup exists and is unreachable. Nothing warns,
        // because both halves succeed.
        string folder = UnderCulture(
            Hostile(), () => AtlasBackupNaming.FolderName(worldUid, Stamped, "backup"));
        string pattern = UnderCulture(Hostile(), () => AtlasBackupNaming.SearchPattern(worldUid));
        string prefix = pattern.TrimEnd('*');

        Assert.StartsWith(prefix, folder, StringComparison.Ordinal);

        // And the real matcher, not only a prefix comparison: a directory of
        // that name is actually found by that glob.
        Directory.CreateDirectory(Path.Combine(_directory, folder));
        Assert.Single(Directory.GetDirectories(_directory, pattern));
    }

    [Theory]
    [InlineData(NegativeWorldUid)]
    [InlineData(PositiveWorldUid)]
    public void EveryNameOnDiskIsTheSameUnderAnyCulture(long worldUid)
    {
        string invariantFolder = AtlasBackupNaming.FolderName(worldUid, Stamped, "pre-restore");
        string invariantSidecar = AtlasBackupNaming.SidecarName(worldUid, ".pins.tsv");
        string invariantJournal = AtlasBackupNaming.JournalName(worldUid, ".pins.tsv");

        foreach (CultureInfo culture in new[] { Hostile(), new CultureInfo("sv-SE"), new CultureInfo("th-TH") })
        {
            Assert.Equal(
                invariantFolder,
                UnderCulture(culture, () => AtlasBackupNaming.FolderName(worldUid, Stamped, "pre-restore")));
            Assert.Equal(
                invariantSidecar,
                UnderCulture(culture, () => AtlasBackupNaming.SidecarName(worldUid, ".pins.tsv")));
            Assert.Equal(
                invariantJournal,
                UnderCulture(culture, () => AtlasBackupNaming.JournalName(worldUid, ".pins.tsv")));
        }
    }

    [Fact]
    public void TheStampIsGregorianAndSortable()
    {
        // The lister sorts folder names as text and reverses them to get
        // "newest first", so the stamp must be a sortable Gregorian one in
        // every culture: `yyyy` under a culture whose default calendar is not
        // Gregorian is a different era's year, and it would sort wrong against
        // every folder already on disk.
        Assert.Equal("20260920-140506", AtlasBackupNaming.Stamp(Stamped));
        foreach (CultureInfo culture in new[] { Hostile(), new CultureInfo("th-TH"), new CultureInfo("ar-SA") })
        {
            Assert.Equal("20260920-140506", UnderCulture(culture, () => AtlasBackupNaming.Stamp(Stamped)));
        }
    }

    [Fact]
    public void JournalNamesAreTheSidecarNamePlusTheSuffixTheWritersUse()
    {
        // Restore deletes these so a stale journal cannot replay over the
        // snapshot just restored. A name that does not match what
        // PinPersistence and RoutePersistence write would leave the journal in
        // place and silently undo the restore on the next load.
        Assert.Equal(
            AtlasBackupNaming.SidecarName(PositiveWorldUid, ".pins.tsv") + ".journal",
            AtlasBackupNaming.JournalName(PositiveWorldUid, ".pins.tsv"));
    }

    // ------------------------------------------------------------------
    // (c) the support report now describes all five kinds, truthfully
    // ------------------------------------------------------------------

    [Fact]
    public void TheRejectedSurveySidecarIsCountedByItsOwnCodec()
    {
        string status = SupportReportComposer.DescribeSidecar(
            ".survey-rejected.tsv", new[] { SurveyRejectedCodec.Header }, 100L);

        Assert.Equal("1 KB, 0 rejected, 0 malformed", status);
    }

    [Fact]
    public void TheTerrainIntentSidecarIsCountedByItsOwnCodec()
    {
        var mask = new TerrainIntentMask(new[] { (1, 2), (3, 4) });
        string status = SupportReportComposer.DescribeSidecar(
            ".terrain-intent.tsv", TerrainIntentCodec.Serialize(mask).ToList(), 100L);

        Assert.Equal("1 KB, 2 cells, 0 malformed", status);
    }

    [Fact]
    public void ASidecarKindWithNoCodec_IsDescribedAsRowsAndNotGuessedAt()
    {
        // The fallback used to hand anything unrecognised to the route codec,
        // which was harmless only while routes were the one suffix reaching it.
        // With the family at five, that fallback would have reported a
        // confident, wrong "0 routes" for two real sidecars in the one document
        // a player sends us when something is already wrong.
        string status = SupportReportComposer.DescribeSidecar(
            ".something-new.tsv", new[] { "a", "b", "c" }, 100L);

        Assert.Equal("1 KB, 3 rows", status);
        Assert.DoesNotContain("routes", status);
    }

    [Fact]
    public void EveryFamilyMemberIsDescribedWithoutClaimingToBeRoutes()
    {
        // The whole family through the composer, so adding a sixth sidecar
        // without teaching the composer about it cannot silently produce a
        // route count for it.
        foreach (string suffix in CartographerWorldSidecars.Suffixes)
        {
            string status = SupportReportComposer.DescribeSidecar(suffix, new[] { "#" }, 10L);
            if (suffix != ".routes-atlas.tsv")
            {
                Assert.DoesNotContain("routes", status);
            }
        }
    }

    [Fact]
    public void TheReportsOwnTimestampIsGregorianInEveryCulture()
    {
        // The same defect as the folder stamp, one file over, and found by
        // looking for it rather than by a report: the report's generated-utc
        // line was interpolated, so under a culture whose default calendar is
        // not Gregorian it named a different era's year. A support report is
        // read off a bug report by a human; a date wrong by 543 years is worse
        // than no date. SupportReportPrivacyTests already asserted this line's
        // exact text, which means that assertion was itself only true in some
        // locales.
        foreach (CultureInfo culture in new[] { new CultureInfo("th-TH"), new CultureInfo("ar-SA"), Hostile() })
        {
            List<string> lines = UnderCulture(culture, () => SupportReportComposer.Compose(
                new DateTime(2026, 9, 3, 12, 34, 56, DateTimeKind.Utc),
                "1.3.0",
                "enabled=True",
                new List<(string, string)> { (".pins.tsv", SupportReportComposer.AbsentStatus) },
                backupCount: 0));

            Assert.Contains("generated-utc: 2026-09-03 12:34:56Z", string.Join("\n", lines));
        }
    }

    // ------------------------------------------------------------------
    // (a) the reply that contradicted itself
    // ------------------------------------------------------------------

    [Fact]
    public void TheSupportReplySaysWhereTheFileIsBeforeClaimingAnythingAboutIt()
    {
        const string path = @"C:\Users\erenc\AppData\Roaming\Thunderstore Mod Manager\profiles\TCC\BepInEx\config\ConcernedCatMods\ConcernedCartographer\support-report.log";
        string reply = SupportReportComposer.DescribeWrittenReport(path);

        // The path stays: this is the one file the troubleshooting docs ask a
        // player to go and find.
        Assert.StartsWith("Support report written to " + path, reply, StringComparison.Ordinal);

        // And the sanitization claim now has a subject, stated before it, so
        // the reply no longer reads as a claim about itself.
        int subject = reply.IndexOf("The report's contents are sanitized", StringComparison.Ordinal);
        int claim = reply.IndexOf("no positions", StringComparison.Ordinal);
        Assert.True(subject > 0, "the claim must name what it is about");
        Assert.True(claim > subject, "the subject must come before the claim it qualifies");
    }

    [Fact]
    public void TheSupportReplyNeverMakesTheUnattributedClaimAgain()
    {
        string reply = SupportReportComposer.DescribeWrittenReport(@"C:\x\support-report.log");

        // The exact shape of the defect: a parenthesised bare denial of paths,
        // and then a path.
        Assert.DoesNotContain("(no positions/names/notes/world ids/paths)", reply);
    }

    [Fact]
    public void TheReplyAndTheReportsOwnHeaderClaimTheSameThing()
    {
        // Two statements of one audit. If the header's promise is narrowed the
        // reply must not go on promising more than the file delivers.
        string reply = SupportReportComposer.DescribeWrittenReport(@"C:\x\support-report.log");

        Assert.Contains("no positions, names, notes, world identifiers or file paths", reply);
        Assert.Contains("no positions, names, notes, world identifiers, or file paths", SupportReportComposer.Header);
    }

    // ------------------------------------------------------------------
    // (b) the missing directory, and the failure text
    // ------------------------------------------------------------------

    [Fact]
    public void WritingAReportCreatesTheDirectoryItNeeds()
    {
        // A profile whose data directory is not there yet, asking for help as
        // its first act. The bare write threw; the command that exists to
        // produce a bug-report attachment produced a failure instead.
        string path = Path.Combine(_directory, "not", "there", "yet", "support-report.log");

        AtlasTextFile.WriteLines(path, new[] { SupportReportComposer.Header, "backups: 0" });

        Assert.True(File.Exists(path));
        Assert.Equal(
            new[] { SupportReportComposer.Header, "backups: 0" },
            File.ReadAllLines(path));
    }

    [Fact]
    public void WritingAReportTwiceReplacesItRatherThanFailing()
    {
        string path = Path.Combine(_directory, "reports", "support-report.log");

        AtlasTextFile.WriteLines(path, new[] { "first" });
        AtlasTextFile.WriteLines(path, new[] { "second" });

        Assert.Equal(new[] { "second" }, File.ReadAllLines(path));
    }

    [Fact]
    public void AFailedSubcommandNamesItselfAndLosesTheMachinesUserName()
    {
        // The realistic message: what DirectoryNotFoundException says when the
        // product's data directory is not there, on a real Thunderstore profile.
        var exception = new IOException(
            @"Could not find a part of the path 'C:\Users\erenc\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\TCC\BepInEx\config\ConcernedCatMods\ConcernedCartographer\support-report.log'.");

        string reply = ConsoleFailure.Describe("cc_atlas", "support", exception);

        // Which of fourteen subcommands failed, and what class of failure.
        Assert.Contains("cc_atlas support", reply);
        Assert.Contains("IOException", reply);

        // The part that matters: the account name, and the rooted head of the
        // path it sits in.
        Assert.DoesNotContain("erenc", reply);
        Assert.DoesNotContain(@"C:\", reply);
        Assert.DoesNotContain("AppData", reply);
        Assert.DoesNotContain("Roaming", reply);

        // This is where the gap used to be written down instead of asserted:
        // CrashReportSanitizer's path pattern forbade whitespace inside a
        // segment, so the match stopped at the space in "Thunderstore Mod
        // Manager" and everything after it survived. The user name was before
        // that point, so the privacy-critical part always went; the tail did
        // not, and asserting it here would have pinned the defect in place
        // while closing it was a change to a CC-098 audit surface owned by its
        // own issue. #388 closed it, so the tail is asserted now.
        Assert.DoesNotContain("Mod Manager", reply);
        Assert.DoesNotContain("DataFolder", reply);
        Assert.DoesNotContain("profiles", reply);
        Assert.DoesNotContain("BepInEx", reply);
        Assert.DoesNotContain(@"\", reply);

        // And the file name still survives, which is the whole point of
        // keeping the last component: the failure stays diagnosable.
        Assert.Contains("support-report.log", reply);
    }

    [Fact]
    public void APathWithoutSpacesIsScrubbedWholeAndKeepsOnlyTheFileName()
    {
        var exception = new IOException(
            @"Access to the path 'C:\Users\erenc\Valheim\BepInEx\config\ConcernedCatMods\ConcernedCartographer\support-report.log' is denied.");

        string reply = ConsoleFailure.Describe("cc_atlas", "support", exception);

        Assert.DoesNotContain("erenc", reply);
        Assert.DoesNotContain(@"C:\", reply);
        Assert.DoesNotContain("BepInEx", reply);
        Assert.DoesNotContain("ConcernedCatMods", reply);

        // The file name survives on purpose: it is what makes the failure
        // diagnosable, and it carries nothing about the machine.
        Assert.Contains("support-report.log", reply);
        Assert.Contains("IOException", reply);
    }

    // ------------------------------------------------------------------
    // (b) the arguments the guard made it necessary to have an answer for
    // ------------------------------------------------------------------

    [Fact]
    public void NoArgumentsMeansStatus()
    {
        Assert.Equal("status", ConsoleArguments.Subcommand(null));
        Assert.Equal("status", ConsoleArguments.Subcommand(Array.Empty<string>()));
        Assert.Equal("", ConsoleArguments.Remainder(null));
        Assert.Equal("", ConsoleArguments.Remainder(Array.Empty<string>()));
    }

    [Fact]
    public void AnEmptyFirstArgumentIsNotStatusAndFallsToTheUsageReply()
    {
        // Deliberate, and the deliberateness is the point: `cc_atlas ""` gave
        // the unknown-subcommand usage reply before the try/catch was added,
        // and it still does. Mapping it to "status" would be a behaviour change
        // smuggled in by a refactor, and would answer a typo with a status line
        // rather than saying the argument was not understood. No switch in the
        // handler has a case for "", so this reaches `default`.
        Assert.Equal("", ConsoleArguments.Subcommand(new[] { "" }));
        Assert.Equal("", ConsoleArguments.Subcommand(new string[] { null! }));
        Assert.NotEqual(ConsoleArguments.DefaultSubcommand, ConsoleArguments.Subcommand(new[] { "" }));
    }

    [Fact]
    public void TheSubcommandIsLowercasedAndTheRemainderIsNot()
    {
        // The subcommand is matched against literals; the remainder is the
        // player's own text - a view name, a query, a pattern - and lowercasing
        // it would quietly rename things.
        var args = new[] { "VIEW", "Save", "My Favourite Spot" };

        Assert.Equal("view", ConsoleArguments.Subcommand(args));
        Assert.Equal("Save My Favourite Spot", ConsoleArguments.Remainder(args));
    }

    [Fact]
    public void ArgumentHandlingNeverThrows()
    {
        // This runs before the guard that would catch it. A null array used to
        // become "could not finish: NullReferenceException" - caught rather
        // than crashing, and still a meaningless thing to tell somebody.
        Assert.Equal("status", ConsoleArguments.Subcommand(null));
        Assert.Equal("", ConsoleArguments.Remainder(new string[] { "backup", null! }));
    }

    [Fact]
    public void AFailureReportNeverAddsASecondFailure()
    {
        // This runs in a catch block. Anything it throws replaces a reported
        // failure with an unreported one.
        Assert.Equal("cc_atlas could not finish.", ConsoleFailure.Describe("cc_atlas", "", null!));
        Assert.Equal("cc_atlas could not finish.", ConsoleFailure.Describe("cc_atlas", "   ", null!));
        Assert.Contains("cc_atlas backup", ConsoleFailure.Describe("cc_atlas", "backup", null!));
    }

    // ------------------------------------------------------------------
    // The other six commands (#389)
    //
    // #367 fixed cc_atlas and left Pin, Road, Route, Survey, Sync and
    // Companion replying `"<X> tool failed: " + exception.Message`. One row
    // per command, because the defect WAS six copies of one line and the
    // fix is that each of them now names itself and scrubs.
    //
    // These prove the reply. That each wrapper actually produces it is a
    // different claim and a different check: the wrappers need BepInEx and
    // compile into no test assembly, so validate_repo.py's `#389 console
    // failure audit` is what holds the wiring — it refuses a `.Message` in
    // any `*ToolsCommand.cs`, a reply naming another command, and an entry
    // point that reaches its work around the single guard.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("cc_pins", "merge")]
    [InlineData("cc_roads", "paint")]
    [InlineData("cc_routes", "save")]
    [InlineData("cc_survey", "commit")]
    [InlineData("cc_sync", "apply")]
    [InlineData("cc_companion", "toolsonly")]
    public void EveryConsoleCommandNamesItsSubcommandAndScrubsTheFailure(
        string command, string subcommand)
    {
        // The realistic message: what the runtime throws when the product's
        // own data directory has gone, on a real mod-manager profile.
        var exception = new IOException(
            @"Could not find a part of the path 'C:\Users\erenc\AppData\Roaming\Thunderstore Mod " +
            @"Manager\DataFolder\Valheim\profiles\TCC\BepInEx\config\ConcernedCatMods\ConcernedCartographer\x.tsv'.");

        string reply = ConsoleFailure.Describe(command, subcommand, exception);

        // Which command and which of its subcommands, so the reply is worth
        // pasting into a bug report at all.
        Assert.Contains(command + " " + subcommand, reply);
        Assert.Contains("IOException", reply);

        // And nothing about this machine: not the account name, not the
        // profile, not the folder layout. The tail is asserted because #388
        // closed it; before that, only the head of the path went.
        Assert.DoesNotContain("erenc", reply);
        Assert.DoesNotContain("AppData", reply);
        Assert.DoesNotContain("Mod Manager", reply);
        Assert.DoesNotContain("profiles", reply);
        Assert.DoesNotContain("BepInEx", reply);
        Assert.DoesNotContain(@"\", reply);

        // The file name survives, which is what makes the failure diagnosable.
        Assert.Contains("x.tsv", reply);
    }

    [Fact]
    public void TheOldRawReplyIsWhatTheseTestsRuleOut()
    {
        // Written as an explicit contrast because "it is scrubbed now" is
        // only meaningful against what it replaced. This is the exact string
        // six wrappers produced, and every assertion above fails against it.
        var exception = new IOException(
            @"Could not find a part of the path 'C:\Users\erenc\AppData\Roaming\Thunderstore Mod Manager\x.tsv'.");
        string raw = "Pin tool failed: " + exception.Message;

        Assert.Contains("erenc", raw);
        Assert.DoesNotContain("merge", raw);

        string scrubbed = ConsoleFailure.Describe("cc_pins", "merge", exception);

        Assert.DoesNotContain("erenc", scrubbed);
        Assert.Contains("cc_pins merge", scrubbed);
    }
}
