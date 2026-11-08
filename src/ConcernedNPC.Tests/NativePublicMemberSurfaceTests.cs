using System.Reflection;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>Metadata only: no Valheim, Unity or plugin code is executed. The
/// controlled native gate sets CC_NPC_NATIVE_API=1 after building all products.
/// Game-free CI visibly skips this check instead of claiming native coverage.</summary>
public sealed class NativePublicMemberSurfaceTests
{
    private sealed class NativeFactAttribute : FactAttribute
    {
        public NativeFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("CC_NPC_NATIVE_API") != "1")
                Skip = "Requires the controlled product build; run scripts/check-npc-native-api.ps1.";
        }
    }

    // Publicizer output filenames differ from their assembly identities. Resolve
    // by metadata identity, never by renaming/copying a game binary.
    private sealed class NamedMetadataResolver : MetadataAssemblyResolver
    {
        private readonly Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);
        internal NamedMetadataResolver(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                string? name = AssemblyName.GetAssemblyName(path).Name;
                if (name != null && !_paths.ContainsKey(name)) _paths.Add(name, path);
            }
        }
        public override Assembly? Resolve(MetadataLoadContext context, AssemblyName name) =>
            name.Name != null && _paths.TryGetValue(name.Name, out string? path)
                ? context.LoadFromAssemblyPath(path) : null;
    }

    [NativeFact]
    public void Compiled_library_public_members_match_the_complete_contract()
    {
        string root = PublicMemberSurfaceTests.Repository();
        string configuration = Environment.GetEnvironmentVariable("CC_NPC_NATIVE_CONFIGURATION") ?? "Release";
        Assert.Contains(configuration, new[] { "Debug", "Release" });
        string directory = Path.Combine(root, "src", "ConcernedNPC", "bin", configuration, "net48");
        string assemblyPath = Path.Combine(directory, "TheConcernedCat.ConcernedNPC.dll");
        Assert.True(File.Exists(assemblyPath), "Native library missing: build the exact candidate first.");
        string[] platform = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "").Split(Path.PathSeparator);
        // Prefer the build's real game references; framework references come
        // from the test host. MetadataLoadContext never executes their code.
        var paths = Directory.GetFiles(directory, "*.dll").Concat(platform)
            .Where(File.Exists).GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToArray();
        using var context = new MetadataLoadContext(new NamedMetadataResolver(paths), "System.Private.CoreLib");
        Assembly library = context.LoadFromAssemblyPath(assemblyPath);
        Type[] types = library.GetExportedTypes(); // includes namespace escapes
        Assert.All(types, type => Assert.StartsWith("TheConcernedCat.ConcernedNPC.", type.Namespace + "."));
        string[] actual = PublicMemberSurfaceTests.Describe(types);
        string fixture = Path.Combine(root, "src", "ConcernedNPC.Tests", "Fixtures", "native-public-members.txt");
        string[] expected = File.Exists(fixture) ? File.ReadAllLines(fixture) : Array.Empty<string>();
        Assert.True(expected.SequenceEqual(actual, StringComparer.Ordinal),
            "Compiled native API changed; review every difference and consumer/version implications.\nACTUAL_BEGIN\n" +
            string.Join("\n", actual) + "\nACTUAL_END");
        Assert.Contains(actual, row => row.StartsWith("type TheConcernedCat.ConcernedNPC.Body.Dressing.NpcFigureDresser ", StringComparison.Ordinal));
    }
}
