using TheConcernedCat.Companions.Identity;

namespace ConcernedCartographer.Tests.Companions;

public class CompanionIdentityTests
{
    [Theory]
    [InlineData("hulgi")]
    [InlineData("broken-compass")]
    [InlineData("a")]
    [InlineData("mod2")]
    public void AcceptsWellFormedSlugs(string slug)
    {
        Assert.True(IdentitySlug.IsValid(slug));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Hulgi")]              // case would make two identities look alike
    [InlineData("broken compass")]     // a space breaks the tab-separated row
    [InlineData("broken\tcompass")]    // the field separator itself
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--dash")]
    [InlineData("path/traversal")]
    [InlineData("..")]
    [InlineData("a.b")]
    public void RejectsMalformedSlugs(string? slug)
    {
        Assert.False(IdentitySlug.IsValid(slug));
    }

    [Fact]
    public void RejectsOverlongSlugs()
    {
        Assert.False(IdentitySlug.IsValid(new string('a', IdentitySlug.MaxLength + 1)));
        Assert.True(IdentitySlug.IsValid(new string('a', IdentitySlug.MaxLength)));
    }

    [Fact]
    public void ProductIdRejectsMalformedValueAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new ProductId("Not A Slug"));
        Assert.False(ProductId.TryCreate("Not A Slug", out _));
    }

    [Fact]
    public void DefaultIdentitiesAreEmptyRatherThanNull()
    {
        Assert.True(default(ProductId).IsEmpty);
        Assert.Equal(string.Empty, default(ProductId).Value);
        Assert.True(default(CompanionId).IsEmpty);
        Assert.True(default(QuestId).IsEmpty);
    }

    [Fact]
    public void NumericIdentitiesTreatZeroAsUnresolved()
    {
        Assert.False(new WorldId(0).IsValid);
        Assert.False(new CharacterId(0).IsValid);
        Assert.True(new WorldId(1).IsValid);
        Assert.True(new CharacterId(-1).IsValid);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(4611686018427387904L)]
    public void NumericIdentitiesRoundTripThroughStorageTokens(long value)
    {
        var world = new WorldId(value);
        Assert.True(WorldId.TryParseStorageToken(world.ToStorageToken(), out WorldId parsedWorld));
        Assert.Equal(world, parsedWorld);

        var character = new CharacterId(value);
        Assert.True(CharacterId.TryParseStorageToken(character.ToStorageToken(), out CharacterId parsedCharacter));
        Assert.Equal(character, parsedCharacter);
    }

    [Fact]
    public void NegativeIdentifiersDoNotProduceASignInStorageTokens()
    {
        // A '-' in a file name is legal, but a sign would also make two
        // different identifiers render at different widths, which is how
        // token parsing starts guessing.
        string token = new WorldId(-1L).ToStorageToken();
        Assert.Equal(16, token.Length);
        Assert.DoesNotContain("-", token);
    }

    [Fact]
    public void StorageTokenParsingRejectsWrongLengthAndGarbage()
    {
        Assert.False(WorldId.TryParseStorageToken("1", out _));
        Assert.False(WorldId.TryParseStorageToken("zzzzzzzzzzzzzzzz", out _));
        Assert.False(WorldId.TryParseStorageToken(null, out _));
        Assert.False(CharacterId.TryParseStorageToken("00000000000000000", out _));
    }

    [Fact]
    public void ScopeRequiresEveryComponent()
    {
        var product = new ProductId("concerned-cartographer");
        Assert.False(new CompanionScope(product, WorldId.None, new CharacterId(7)).IsComplete);
        Assert.False(new CompanionScope(product, new WorldId(7), CharacterId.None).IsComplete);
        Assert.False(new CompanionScope(default, new WorldId(7), new CharacterId(7)).IsComplete);
        Assert.True(new CompanionScope(product, new WorldId(7), new CharacterId(7)).IsComplete);
    }

    [Fact]
    public void IncompleteScopeRefusesToProduceAStorageKey()
    {
        // Falling back to a default bucket here would silently merge every
        // character that ever failed to resolve.
        var scope = new CompanionScope(new ProductId("p"), WorldId.None, new CharacterId(1));
        Assert.Throws<InvalidOperationException>(() => scope.ToStorageKey());
    }

    [Fact]
    public void ScopeKeysDifferAcrossProductWorldAndCharacter()
    {
        var alpha = new ProductId("synthetic-alpha");
        var beta = new ProductId("synthetic-beta");

        string baseline = new CompanionScope(alpha, new WorldId(10), new CharacterId(20)).ToStorageKey();
        string otherProduct = new CompanionScope(beta, new WorldId(10), new CharacterId(20)).ToStorageKey();
        string otherWorld = new CompanionScope(alpha, new WorldId(11), new CharacterId(20)).ToStorageKey();
        string otherCharacter = new CompanionScope(alpha, new WorldId(10), new CharacterId(21)).ToStorageKey();

        Assert.Equal(4, new HashSet<string>(
            new[] { baseline, otherProduct, otherWorld, otherCharacter }).Count);
    }

    [Fact]
    public void ScopeKeyContainsNoPathSeparators()
    {
        string key = new CompanionScope(
            new ProductId("concerned-cartographer"),
            new WorldId(long.MinValue),
            new CharacterId(long.MaxValue)).ToStorageKey();

        Assert.DoesNotContain("/", key);
        Assert.DoesNotContain(Path.DirectorySeparatorChar.ToString(), key);
        Assert.DoesNotContain("..", key);
        Assert.Equal(key, Path.GetFileName(key));
    }
}
