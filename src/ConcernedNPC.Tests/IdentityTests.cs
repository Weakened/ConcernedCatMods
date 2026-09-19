using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>That <see cref="NpcIdentity"/> and the shipped <c>WorkerKey</c>
/// agree on every input.
///
/// <b>Why this test exists.</b> <c>WorkerKey</c> is <c>internal</c> source
/// compiled separately into three shipped products, so it cannot appear in this
/// library's public surface and this library cannot reuse it. The two types are
/// therefore separate, and the entire safety of that split is that their <i>text
/// forms</i> are interchangeable: a role converts at the boundary and its
/// journal rows, its capability messages and its saved bodies keep working with
/// no migration.
///
/// If the rules ever drifted apart, the break would be silent and one-sided. A
/// looser rule here lets a role register an identity its own product cannot
/// round-trip; a tighter one stops an already shipped worker being addressable
/// at all. So the shipped file is linked into this test assembly and the two
/// validators are compared directly.</summary>
public class SlugAgreementTests
{
    private static IEnumerable<string?> Corpus()
    {
        yield return null;
        yield return string.Empty;
        yield return "a";
        yield return "thorstein";
        yield return "gunnar";
        yield return "steward";
        yield return "hulgi";
        yield return "broken-compass";
        yield return "a-b-c-1-2-3";
        yield return "9";
        yield return "-leading";
        yield return "trailing-";
        yield return "double--dash";
        yield return "Upper";
        yield return "with space";
        yield return "with_underscore";
        yield return "with/slash";
        yield return "with.dot";
        yield return "unicode-å";
        yield return new string('a', 48);
        yield return new string('a', 49);
        yield return "-";
        yield return "--";
    }

    [Fact]
    public void The_two_slug_validators_agree_on_every_input()
    {
        foreach (string? value in Corpus())
        {
            Assert.True(
                NpcSlug.IsValid(value) == WorkSlug.IsValid(value),
                $"NpcSlug and the shipped WorkSlug disagree about '{value ?? "<null>"}'. They must not: a role " +
                "converts between NpcIdentity and WorkerKey at the boundary, and a disagreement makes that " +
                "conversion lossy in one direction.");
        }
    }

    [Fact]
    public void The_two_ceilings_are_the_same()
    {
        Assert.Equal(WorkSlug.MaxLength, NpcSlug.MaxLength);
    }

    [Theory]
    [InlineData("foreman", "thorstein")]
    [InlineData("teamster", "gunnar")]
    [InlineData("steward", "steward")]
    [InlineData("cartographer", "hulgi")]
    public void An_identity_round_trips_through_the_shipped_key(string product, string role)
    {
        var identity = new NpcIdentity(product, role);

        Assert.True(WorkerKey.TryParse(identity.Value, out WorkerKey key));
        Assert.Equal(identity.Value, key.Value);
        Assert.True(NpcIdentity.TryParse(key.Value, out NpcIdentity back));
        Assert.Equal(identity, back);
    }

    [Fact]
    public void Every_shipped_worker_key_is_an_identity()
    {
        foreach (WorkerKey key in new[] { WorkerKey.Thorstein, WorkerKey.Gunnar, WorkerKey.Steward })
        {
            Assert.True(NpcIdentity.TryParse(key.Value, out NpcIdentity identity), key.Value);
            Assert.Equal(key.Value, identity.Value);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("noslash")]
    [InlineData("/leading")]
    [InlineData("trailing/")]
    [InlineData("two/slashes/here")]
    [InlineData("Upper/case")]
    [InlineData("ok/--bad")]
    public void Text_that_is_not_an_identity_is_refused_by_both(string? text)
    {
        Assert.False(NpcIdentity.TryParse(text, out _));
        Assert.False(WorkerKey.TryParse(text, out _));
    }

    [Fact]
    public void A_defaulted_identity_is_empty_and_never_equal_to_a_real_one()
    {
        NpcIdentity empty = default;

        Assert.True(empty.IsEmpty);
        Assert.Equal(string.Empty, empty.Value);
        Assert.NotEqual(Identities.Thorstein, empty);
        Assert.False(Identities.Thorstein == empty);
        Assert.True(Identities.Thorstein != empty);
    }

    [Fact]
    public void The_longest_legal_identity_round_trips()
    {
        // Each half may be MaxLength, so the composed form can be 97 characters.
        // Nothing anywhere states a ceiling on it, and nothing needs to - but
        // that is only true while it round-trips, so it is asserted rather than
        // assumed.
        string longest = new string('a', NpcSlug.MaxLength);
        var identity = new NpcIdentity(longest, longest);

        Assert.Equal((NpcSlug.MaxLength * 2) + 1, identity.Value.Length);
        Assert.True(NpcIdentity.TryParse(identity.Value, out NpcIdentity back));
        Assert.Equal(identity, back);
        Assert.True(WorkerKey.TryParse(identity.Value, out WorkerKey key));
        Assert.Equal(identity.Value, key.Value);
    }

    [Fact]
    public void Identities_are_keyed_by_product_so_two_products_may_share_a_slug()
    {
        Assert.NotEqual(new NpcIdentity("foreman", "helper"), new NpcIdentity("teamster", "helper"));
        Assert.Equal(new NpcIdentity("foreman", "helper"), new NpcIdentity("foreman", "helper"));
    }

    [Theory]
    [InlineData("", "worker")]
    [InlineData("product", "")]
    [InlineData("Product", "worker")]
    [InlineData("product", "-worker")]
    public void A_malformed_identity_throws_where_a_role_declares_a_constant(string product, string role)
    {
        // Roles declare their identity as a constant, so a bad one is a
        // programming error found on the first run rather than a runtime
        // condition. Text from outside goes through TryParse, which never
        // throws - and registration answers an outcome for the empty identity
        // the caller is left with.
        Assert.Throws<ArgumentException>(() => new NpcIdentity(product, role));
    }
}
