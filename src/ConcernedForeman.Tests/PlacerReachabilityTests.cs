using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using TheConcernedCat.ConcernedForeman.Runtime.Construction;

namespace ConcernedForeman.Tests;

/// <summary>The defect #380 actually fixed: a fully built, fully tested
/// placement chain with nothing calling it.
///
/// <b>Why an ordinary test could not catch it, and this one can.</b> At
/// <c>49bb361</c> every piece of the build order worked in isolation and every
/// suite was green. <c>WorldPiecePlacer</c> - the placement gate followed by the
/// host player's own <c>PlacePiece</c>, with nothing in between - had two dozen
/// tests and <b>no production caller at all</b>:
/// <c>grep -rn "WorldPiecePlacer" --include=*.cs src/</c> returned its own
/// definition and test files. A player could mark a site, price it, confirm it
/// and read a progress line while nothing ever happened, and no test in the
/// repository was unhappy. Behavioural tests cannot see that, because a
/// behavioural test is itself a caller.
///
/// <b>So this reads the compiled product instead.</b> It walks the IL of every
/// shipped Concerned Foreman type in this assembly (the project links the real
/// sources) and asks whether any of them calls
/// <see cref="WorldPiecePlacer.Place"/> and whether any of them constructs one.
/// Both were false at <c>49bb361</c> and both are true now. IL rather than source
/// text on purpose: a comment, a string or an XML doc reference mentioning the
/// name would satisfy a text search and satisfies nothing here.
///
/// <b>And the hop IL cannot see is checked separately.</b> The last link in the
/// chain is <c>Plugin.cs</c> constructing the runtime and ticking it, and
/// <c>Plugin.cs</c> is a BepInEx entry point this project cannot link. So it is
/// read off disk as text, which is weaker and is the honest best available: a
/// green IL check with the plugin hookup deleted would be exactly the original
/// defect one level up.</summary>
public sealed class PlacerReachabilityTests
{
    private const string ProductNamespace = "TheConcernedCat.ConcernedForeman.";

    // ---- the pinned reachability check -----------------------------------

    [Fact]
    public void Shipped_code_outside_the_placer_itself_calls_the_placer()
    {
        List<string> callers = CallersOf(typeof(WorldPiecePlacer), nameof(WorldPiecePlacer.Place));

        // This is the assertion that was RED at 49bb361, where the list was
        // empty: nothing in the shipped product called the placer.
        Assert.NotEmpty(callers);

        // And named, so a failure says what is missing rather than that a set is
        // empty. By NAME rather than by `typeof`, so this whole file still
        // COMPILES against a tree where the join does not exist - a reachability
        // test that cannot be run against the unfixed tree cannot be shown to
        // have caught anything.
        Assert.Contains("GatedPiecePlacer.Place", Lines(callers));
    }

    [Fact]
    public void Shipped_code_builds_a_placer_to_call()
    {
        // A caller is not enough on its own: a method that calls Place inside a
        // type nobody ever constructs is the same defect wearing a hat.
        List<string> builders = CallersOf(typeof(WorldPiecePlacer), ".ctor");

        Assert.NotEmpty(builders);
        Assert.Contains("ShelterConstructionRuntime..ctor", Lines(builders));
    }

    [Fact]
    public void The_runtime_that_builds_the_placer_is_driven_by_a_loop_that_reads_the_world()
    {
        // The middle of the chain, also in IL: the composition root builds the
        // loop, and the loop is what a tick runs.
        List<string> callers = CallersOf(
            "TheConcernedCat.ConcernedForeman.Domain.Construction.ShelterBuildLoop", "Tick");

        Assert.Contains("ShelterConstructionRuntime.Tick", Lines(callers));
    }

    [Fact]
    public void The_plugin_builds_the_construction_runtime_and_ticks_it()
    {
        string plugin = ReadShippedFile(Path.Combine("src", "ConcernedForeman", "Plugin.cs"));

        // The one hop IL cannot reach, because BepInEx's entry point is not
        // linked into any test project. Text, therefore, and deliberately narrow:
        // the construction and the tick, both of which have to be there for a
        // confirmed order to do anything at all.
        Assert.Contains("new Runtime.Construction.ShelterConstructionRuntime(", plugin);
        Assert.Contains("_construction?.Tick()", plugin);
        Assert.Contains("_construction?.OnWorldUnloaded()", plugin);
    }

    // ---- the gate is never gone round ------------------------------------

    [Fact]
    public void Nothing_shipped_calls_the_installer_except_the_placer()
    {
        // The other half of the same property. A caller that reached
        // IPieceInstaller.Install - or Player.PlacePiece - directly would place a
        // piece with no gate, which is the failure the placer exists to make
        // impossible.
        List<string> callers = CallersOf(
            typeof(IPieceInstaller), nameof(IPieceInstaller.Install));

        Assert.NotEmpty(callers);
        foreach (string caller in callers)
        {
            Assert.StartsWith(typeof(WorldPiecePlacer).FullName + ".", caller);
        }
    }

    [Fact]
    public void Only_the_host_player_installer_calls_the_games_own_place_piece()
    {
        List<string> callers = CallersOf(typeof(Player), nameof(Player.PlacePiece));

        Assert.NotEmpty(callers);
        foreach (string caller in callers)
        {
            Assert.StartsWith(typeof(HostPlayerPieceInstaller).FullName + ".", caller);
        }
    }

    // ---- the IL walk -----------------------------------------------------

    /// <summary>Every shipped Concerned Foreman method whose compiled body calls
    /// <paramref name="member"/> on <paramref name="owner"/>, as
    /// "Namespace.Type.Method".
    ///
    /// The assembly under test contains both the linked product sources and this
    /// project's own tests, so the walk is restricted to the product's namespaces
    /// - a test calling the placer is not a production caller and must not be
    /// able to satisfy this.</summary>
    private static List<string> CallersOf(Type owner, string member) =>
        CallersOf(owner.FullName!, member, owner);

    /// <summary>The same question about a type named only as text, so a test can
    /// ask it about a type that does not exist in every tree it is run
    /// against.</summary>
    private static List<string> CallersOf(string ownerFullName, string member) =>
        CallersOf(ownerFullName, member, owner: null);

    private static string Lines(List<string> callers) => string.Join(System.Environment.NewLine, callers);

    private static List<string> CallersOf(string ownerFullName, string member, Type? owner)
    {
        var callers = new List<string>();
        foreach (Type type in typeof(WorldPiecePlacer).Assembly.GetTypes())
        {
            string? space = type.Namespace;
            if (space == null || !space.StartsWith(ProductNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(type.FullName, ownerFullName, StringComparison.Ordinal))
            {
                // A type calling itself proves nothing about being reachable.
                continue;
            }

            if (owner != null && owner.IsInterface && owner.IsAssignableFrom(type))
            {
                // An implementation of the interface is not a caller going round
                // anything, and the compiler emits an interface bridge inside one
                // when a parameter is `in` - which shows up here as the installer
                // calling itself. Excluding implementations keeps the question
                // "who reaches the installer from outside" answerable.
                continue;
            }

            foreach (MethodBase method in Methods(type))
            {
                if (Calls(method, ownerFullName, member, owner))
                {
                    callers.Add(type.FullName + "." + method.Name);
                }
            }
        }

        return callers;
    }

    private static IEnumerable<MethodBase> Methods(Type type)
    {
        const BindingFlags Everything =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.DeclaredOnly;
        foreach (MethodInfo method in type.GetMethods(Everything))
        {
            yield return method;
        }

        foreach (ConstructorInfo constructor in type.GetConstructors(Everything))
        {
            yield return constructor;
        }
    }

    private static bool Calls(MethodBase method, string ownerFullName, string member, Type? owner)
    {
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception)
        {
            // A body that cannot be read is not evidence of a call. Abstract and
            // extern methods land here, and so would a runtime that refused to
            // hand the bytes over - in which case this test would fail loudly by
            // finding nothing, which is the right direction.
            return false;
        }

        if (il == null)
        {
            return false;
        }

        Type[]? typeArguments = method.DeclaringType != null && method.DeclaringType.IsGenericType
            ? method.DeclaringType.GetGenericArguments()
            : null;
        Type[]? methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

        foreach (int token in MethodTokens(il))
        {
            MethodBase? called;
            try
            {
                called = method.Module.ResolveMethod(token, typeArguments, methodArguments);
            }
            catch (Exception)
            {
                continue;
            }

            if (called == null)
            {
                continue;
            }

            if (!string.Equals(called.Name, member, StringComparison.Ordinal))
            {
                continue;
            }

            // An interface call counts against the interface it is made on, which
            // is what makes "nothing calls the installer except the placer"
            // answerable: the placer holds an IPieceInstaller.
            Type? declaring = called.DeclaringType;
            if (declaring == null)
            {
                continue;
            }

            if (string.Equals(declaring.FullName, ownerFullName, StringComparison.Ordinal) ||
                (owner != null && owner.IsAssignableFrom(declaring)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The metadata tokens of every method-taking instruction in one
    /// body.
    ///
    /// <b>A real opcode walk, not a scan for a byte pattern.</b> Searching for
    /// 0x28 followed by four bytes finds tokens inside other instructions'
    /// operands, and a false positive here would report a call that is not there
    /// - which in a reachability test means reporting the defect as
    /// fixed.</summary>
    private static IEnumerable<int> MethodTokens(byte[] il)
    {
        Dictionary<short, OpCode> opcodes = Opcodes();
        int at = 0;
        while (at < il.Length)
        {
            short code = il[at];
            if (il[at] == 0xFE && at + 1 < il.Length)
            {
                code = unchecked((short)(0xFE00 | il[at + 1]));
                at += 2;
            }
            else
            {
                at += 1;
            }

            if (!opcodes.TryGetValue(code, out OpCode opcode))
            {
                // An unknown opcode means the walk has lost the instruction
                // boundary, and guessing from there is worse than stopping.
                yield break;
            }

            if (opcode.OperandType == OperandType.InlineSwitch)
            {
                if (at + 4 > il.Length)
                {
                    yield break;
                }

                int cases = BitConverter.ToInt32(il, at);
                at += 4 + (4 * cases);
                continue;
            }

            int size = OperandSize(opcode.OperandType);
            if (at + size > il.Length)
            {
                yield break;
            }

            if (size == 4 &&
                (opcode.OperandType == OperandType.InlineMethod || opcode.OperandType == OperandType.InlineTok))
            {
                yield return BitConverter.ToInt32(il, at);
            }

            at += size;
        }
    }

    private static int OperandSize(OperandType operand)
    {
        switch (operand)
        {
            case OperandType.InlineNone:
                return 0;
            case OperandType.ShortInlineBrTarget:
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar:
                return 1;
            case OperandType.InlineVar:
                return 2;
            case OperandType.InlineI8:
            case OperandType.InlineR:
                return 8;
            default:
                // InlineBrTarget, InlineField, InlineI, InlineMethod, InlineSig,
                // InlineString, InlineTok, InlineType, ShortInlineR.
                return 4;
        }
    }

    private static Dictionary<short, OpCode> Opcodes()
    {
        var table = new Dictionary<short, OpCode>();
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(OpCode))
            {
                continue;
            }

            var opcode = (OpCode)field.GetValue(null)!;
            table[opcode.Value] = opcode;
        }

        return table;
    }

    /// <summary>A shipped source file, found by walking up from the test
    /// assembly. Used only for the one hop that cannot be linked.</summary>
    private static string ReadShippedFile(string relative)
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here != null)
        {
            string candidate = Path.Combine(here.FullName, relative);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            here = here.Parent;
        }

        throw new FileNotFoundException(
            "Could not find " + relative + " above " + AppContext.BaseDirectory +
            ". This test reads the shipped plugin entry point, which no test project links.");
    }
}
