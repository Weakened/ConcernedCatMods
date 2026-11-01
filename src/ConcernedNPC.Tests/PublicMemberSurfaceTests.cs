using System.Globalization;
using System.Reflection;
using TheConcernedCat.ConcernedNPC.Roles;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>#372: a type-only list misses edits to an existing type. This pins
/// the members of the REAL linked library surface, including parameter names,
/// defaults, accessor visibility, enum values and generic constraints. The
/// existing type test still refuses foreign/global namespace escapes.
/// Rendering-only sources excluded by the test project remain a separate
/// native-assembly obligation; this test does not pretend to compile them.</summary>
public sealed class PublicMemberSurfaceTests
{
    private const BindingFlags PublicDeclared = BindingFlags.Public | BindingFlags.Instance |
        BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void Linked_public_members_match_the_reviewed_contract()
    {
        var library = typeof(NpcRoleRegistry).Assembly.GetExportedTypes().Where(type =>
            type.Namespace != null && type.Namespace.StartsWith("TheConcernedCat.ConcernedNPC", StringComparison.Ordinal) &&
            type.Namespace != "TheConcernedCat.ConcernedNPC.Tests" &&
            !type.Namespace.StartsWith("TheConcernedCat.ConcernedNPC.Tests.", StringComparison.Ordinal));
        string[] actual = Describe(library);
        string fixture = Path.Combine(Repository(), "src", "ConcernedNPC.Tests", "Fixtures", "public-members.txt");
        string[] expected = File.Exists(fixture) ? File.ReadAllLines(fixture) : Array.Empty<string>();
        Assert.True(expected.SequenceEqual(actual, StringComparer.Ordinal),
            "Public members changed. Review the API/version and consumer implications before updating the contract.\nACTUAL_BEGIN\n" +
            string.Join("\n", actual) + "\nACTUAL_END");
    }

    private static string[] Describe(IEnumerable<Type> types)
    {
        var rows = new List<string>();
        foreach (Type type in types.OrderBy(Name, StringComparer.Ordinal))
        {
            string owner = Name(type);
            rows.Add("type " + owner + " " + type.Attributes + " : " + (type.BaseType == null ? "-" : Name(type.BaseType)) +
                " interfaces=" + string.Join(",", type.GetInterfaces().Select(Name).OrderBy(x => x, StringComparer.Ordinal)) + Constraints(type.GetGenericArguments()));
            foreach (FieldInfo field in type.GetFields(PublicDeclared))
                rows.Add(owner + " field " + field.Name + " : " + Name(field.FieldType) + " " + field.Attributes +
                    (field.IsLiteral ? " = " + Literal(field.GetRawConstantValue()) : ""));
            foreach (ConstructorInfo ctor in type.GetConstructors(PublicDeclared))
                rows.Add(owner + " ctor(" + Parameters(ctor.GetParameters()) + ") " + ctor.Attributes);
            foreach (MethodInfo method in type.GetMethods(PublicDeclared).Where(method => !method.IsSpecialName))
                rows.Add(owner + " method " + method.Name + "(" + Parameters(method.GetParameters()) + ") : " + Name(method.ReturnType) +
                    " " + method.Attributes + Constraints(method.GetGenericArguments()));
            foreach (PropertyInfo property in type.GetProperties(PublicDeclared))
                rows.Add(owner + " property " + property.Name + "(" + Parameters(property.GetIndexParameters()) + ") : " + Name(property.PropertyType) +
                    " get=" + (property.GetGetMethod()?.Attributes.ToString() ?? "-") + " set=" + (property.GetSetMethod()?.Attributes.ToString() ?? "-"));
            foreach (EventInfo item in type.GetEvents(PublicDeclared))
                rows.Add(owner + " event " + item.Name + " : " + Name(item.EventHandlerType!) +
                    " add=" + item.GetAddMethod()?.Attributes + " remove=" + item.GetRemoveMethod()?.Attributes);
            // Operators and conversions are special methods, unlike accessors.
            foreach (MethodInfo method in type.GetMethods(PublicDeclared).Where(method => method.IsSpecialName && method.Name.StartsWith("op_", StringComparison.Ordinal)))
                rows.Add(owner + " operator " + method.Name + "(" + Parameters(method.GetParameters()) + ") : " + Name(method.ReturnType));
        }
        return rows.OrderBy(row => row, StringComparer.Ordinal).ToArray();
    }
    private static string Parameters(ParameterInfo[] parameters) => string.Join(",", parameters.Select(parameter =>
        parameter.Name + ":" + Name(parameter.ParameterType) + " " + parameter.Attributes +
        (parameter.HasDefaultValue ? "=" + Literal(parameter.RawDefaultValue) : "")));
    private static string Constraints(Type[] parameters) => string.Concat(parameters.Where(parameter => parameter.IsGenericParameter).Select(parameter =>
        " generic " + parameter.Name + " " + parameter.GenericParameterAttributes + "[" +
        string.Join(",", parameter.GetGenericParameterConstraints().Select(Name).OrderBy(x => x, StringComparer.Ordinal)) + "]"));
    private static string Name(Type type) => type.IsByRef ? Name(type.GetElementType()!) + "&" :
        type.IsPointer ? Name(type.GetElementType()!) + "*" : type.IsArray ? Name(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]" :
        type.IsGenericParameter ? "!" + type.Name : type.IsGenericType ? type.GetGenericTypeDefinition().FullName + "<" +
        string.Join(",", type.GetGenericArguments().Select(Name)) + ">" : type.FullName ?? type.Name;
    private static string Literal(object? value) => value == null ? "null" : value is string text ?
        "string:" + text.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") :
        value.GetType().FullName + ":" + Convert.ToString(value, CultureInfo.InvariantCulture);
    private static string Repository()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln"))) return directory.FullName;
        throw new InvalidOperationException("Repository root not found for the reviewed public contract.");
    }
    private sealed class Before { public string Call(int amount = 1) => ""; public int Count { get; private set; } }
    private sealed class After { public string Call(long amount = 2) => ""; public int Count { get; set; } }
    [Fact]
    public void Existing_type_parameter_default_and_accessor_changes_do_not_escape()
    {
        string before = string.Join("\n", Describe(new[] { typeof(Before) })).Replace(typeof(Before).FullName!, "same-type");
        string after = string.Join("\n", Describe(new[] { typeof(After) })).Replace(typeof(After).FullName!, "same-type");
        Assert.NotEqual(before, after);
        Assert.Contains("amount:System.Int32 Optional, HasDefault=System.Int32:1", before);
        Assert.Contains("set=-", before);
        Assert.DoesNotContain("set=-", after);
    }
}
