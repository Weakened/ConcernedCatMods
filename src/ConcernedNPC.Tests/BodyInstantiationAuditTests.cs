using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>Every way a body can come into existence, and the two files
/// allowed to do it.
///
/// <b>Why this audit exists.</b> The arbiter hands out a lease on a grant and
/// only on a grant, and the body factory takes one and re-checks it
/// immediately before it builds. That closes the bypass only for code that
/// goes through the factory. Nothing in the type system stops a future leaf
/// reaching for the engine's own instantiation on a prefab it happens to hold
/// and never speaking to the registry at all - which is precisely what the
/// three copies this package replaces each did, because they were conventions.
///
/// So the capability is confined by file, the way
/// <c>check_teamster_integration_readonly</c> confines the cart calls to one
/// folder and proves it with a mutation. <b>The same rule is wanted in
/// <c>tools/validate_repo.py</c></b>, which is the shared, cross-language
/// backstop; this is the half that can be written from inside the package
/// today, and the two are not redundant - one fails a test run and the other
/// fails the gate even when no test project is built.</summary>
public sealed class BodyInstantiationAuditTests
{
    /// <summary>The tokens that create a live object out of a prefab, or put a
    /// component on one. Each is the start of a body.</summary>
    private static IReadOnlyList<string> CreationTokens => new[]
    {
        "Instantiate(",
        "CreateClonedPrefab(",
        "AddComponent<",
        "new GameObject(",
    };

    /// <summary>The two files allowed to create anything, and the reason each
    /// one is.
    ///
    /// <b>Asserted to be exactly these two.</b> Adding a third is how this rule
    /// gets weakened one quiet entry at a time, so the list itself is pinned by
    /// <see cref="The_allowed_list_is_exactly_the_two_factories"/> and a new
    /// entry is a visible edit with a reason attached rather than a line in a
    /// diff.</summary>
    private static IReadOnlyDictionary<string, string> Allowed =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NpcWorkerPrefabFactory.cs"] =
                "the worker body factory: it takes a BodyLease and re-checks it immediately before "
                + "the one instantiation in this package that creates a saved body",
            ["NpcPresentationBody.cs"] =
                "the presentation extraction: it instantiates a game prefab under an INACTIVE holder "
                + "so nothing wakes, and destroys every part of it again before it returns",
        };

    [Fact]
    public void Only_the_two_factories_can_bring_a_body_into_existence()
    {
        var offences = new List<string>();
        foreach (string path in LibrarySources.Files())
        {
            string fileName = Path.GetFileName(path);
            if (Allowed.ContainsKey(fileName))
            {
                continue;
            }

            offences.AddRange(Creations(LibrarySources.Relative(path), File.ReadAllText(path)));
        }

        Assert.True(
            offences.Count == 0,
            "A body may be created only inside the two factories, because that is where a BodyLease is "
            + "required and re-checked. Found:" + Environment.NewLine
            + string.Join(Environment.NewLine, offences));
    }

    [Fact]
    public void The_allowed_list_is_exactly_the_two_factories()
    {
        Assert.Equal(
            new[] { "NpcPresentationBody.cs", "NpcWorkerPrefabFactory.cs" },
            Allowed.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());

        foreach (KeyValuePair<string, string> allowed in Allowed)
        {
            Assert.NotEqual(string.Empty, allowed.Value);
            Assert.True(
                LibrarySources.Files().Any(path =>
                    string.Equals(Path.GetFileName(path), allowed.Key, StringComparison.OrdinalIgnoreCase)),
                allowed.Key + " is exempted from the instantiation audit but no longer exists, so the "
                + "exemption covers nothing and hides the next file that takes its name");
        }
    }

    [Fact]
    public void Each_creation_token_is_load_bearing()
    {
        // The plant. An audit nobody has watched fail is an audit that might be
        // matching nothing, so every token gets a candidate that only it
        // catches: deleting any one of them fails here.
        AssertCaught("Instantiate(", "class X { void M() { UnityEngine.Object.Instantiate(prefab, at, how); } }");
        AssertCaught("CreateClonedPrefab(", "class X { void M() { prefabs.CreateClonedPrefab(name, source); } }");
        AssertCaught("AddComponent<", "class X { void M() { clone.AddComponent<NpcBody>(); } }");
        AssertCaught("new GameObject(", "class X { void M() { var holder = new GameObject(name); } }");
    }

    [Fact]
    public void A_role_that_instantiates_its_own_prefab_is_caught()
    {
        // The exact bypass the re-review named: a role holds a prefab, calls
        // the engine directly, and never speaks to the registry at all. The
        // lease cannot stop it, because nothing asked for one.
        IReadOnlyList<string> found = Creations(
            "Roles/SomeRole.cs",
            "class SomeRole { void Spawn() { var body = Object.Instantiate(_prefab, where, how); } }");

        Assert.Single(found);
        Assert.Contains("Instantiate(", found[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_comment_may_still_explain_what_the_rest_of_the_package_may_not_do()
    {
        // The doc comments in this package talk about instantiation constantly,
        // because explaining why a rule exists is most of what they are for. An
        // audit that could not tell a sentence from a call would have made them
        // unwritable.
        Assert.Empty(Creations(
            "Body/Something.cs",
            "/// <summary>Never call Instantiate( here; the factory does it.</summary>\n"
            + "class X { }"));
    }

    [Fact]
    public void A_call_hidden_in_a_string_is_still_not_a_call()
    {
        Assert.Empty(Creations("Body/Something.cs", "class X { string a = \"Instantiate(\"; }"));
    }

    [Fact]
    public void The_factory_asks_whether_its_lease_is_still_held_and_asks_it_last()
    {
        // The other acceptance criterion, as a structural fact rather than only
        // as behaviour: the gate's final refusal is the lease check, and
        // nothing has been added below it. The behaviour itself is pinned by
        // WorkerPrefabFactoryTests; this is what fails if somebody moves the
        // check up the list, where an earlier refusal would return before it.
        string gate = File.ReadAllText(Path.Combine(LibrarySources.Library, "Body", "NpcBodyBuildGate.cs"));
        string code = CsharpSource.Read(gate).Code;

        int lastActive = code.LastIndexOf("IsActive", StringComparison.Ordinal);
        int lastRefusal = code.LastIndexOf("NpcBuildPermission.Refused", StringComparison.Ordinal);
        int granted = code.LastIndexOf("NpcBuildPermission.Granted", StringComparison.Ordinal);

        Assert.True(lastActive > 0, "the build gate no longer asks whether the lease is active at all");
        Assert.True(lastActive < lastRefusal, "the lease check no longer guards a refusal");
        Assert.True(lastRefusal < granted, "the gate grants before its final refusal");

        // Exactly one refusal after the lease check: its own. Any more means a
        // later question can return before the lease is asked about, which is
        // the whole failure the re-check exists to prevent - the answer has to
        // be the freshest thing the gate knows.
        int refusalsAfterTheCheck =
            Regex.Matches(code.Substring(lastActive), Regex.Escape("NpcBuildPermission.Refused")).Count;
        Assert.True(
            refusalsAfterTheCheck == 1,
            "the lease check is no longer the last thing the gate asks: " + refusalsAfterTheCheck
            + " refusals follow it, so a stale lease can slip past one of them");
    }

    private static void AssertCaught(string token, string source)
    {
        IReadOnlyList<string> found = Creations("Body/Planted.cs", source);
        Assert.True(
            found.Any(offence => offence.Contains(token, StringComparison.Ordinal)),
            "the audit no longer catches " + token + ", so that way of creating a body is unguarded");
    }

    /// <summary>Every creation token in one file, with comments and string
    /// literals already removed by the same scanner the literal audit uses.
    /// </summary>
    private static IReadOnlyList<string> Creations(string fileName, string source)
    {
        string code = CsharpSource.Read(source).Code;
        var found = new List<string>();
        foreach (string token in CreationTokens)
        {
            foreach (Match match in Regex.Matches(code, Regex.Escape(token)))
            {
                int line = code.Take(match.Index).Count(character => character == '\n') + 1;
                found.Add(fileName + ":" + line + ": " + token);
            }
        }

        return found;
    }
}
