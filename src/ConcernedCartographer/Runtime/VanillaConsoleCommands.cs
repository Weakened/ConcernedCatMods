using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>Puts this mod's console commands into the game's own command
/// table.
///
/// Jötunn 2.29.2 registers console commands by reflecting for a
/// <c>Terminal.ConsoleCommand</c> constructor with an exact twelve-parameter
/// signature. Valheim 1.0.12 has thirteen — <c>hideBehindDevCommands</c> was
/// inserted before the options fetcher and <c>onlyAdmin</c> added at the end —
/// so the match returns null, Jötunn logs "No suitable constructor for
/// Terminal.ConsoleCommand found", and <b>every</b> command from every mod
/// using that path silently does not exist. Observed on this machine: all
/// seven of ours were missing in game, including <c>cc_companion toolsonly</c>,
/// which the design relies on as the way to reach the map tools without ever
/// finding the compass.
///
/// So the constructor is found by shape rather than by exact signature: the
/// first three parameters must be (name, description, handler), and every
/// parameter after them is filled with its own declared default. A build that
/// adds, removes or reorders trailing options keeps working, which is the point
/// — this is the second time a trailing-parameter change in this game has
/// broken a call that looked pinned.
///
/// Jötunn stays as the fallback. If the shape ever stops matching too, its
/// path is tried, and a failure there is reported rather than swallowed: a
/// missing diagnostic surface that says nothing is how this went unnoticed in
/// the first place.</summary>
internal static class VanillaConsoleCommands
{
    /// <summary>Registers one command. Returns false only when neither path
    /// worked, which is a notice, never an exception: a mod that will not load
    /// because a console command is unavailable would be a far worse
    /// failure.</summary>
    public static bool Register(Jotunn.Entities.ConsoleCommand command, ManualLogSource log)
    {
        if (command == null)
        {
            return false;
        }

        try
        {
            if (TryRegisterVanilla(command))
            {
                return true;
            }

            log.LogWarning(
                $"This build's console does not take commands the usual way, so \"{command.Name}\" " +
                "is being registered through Jötunn instead.");
        }
        catch (Exception exception)
        {
            log.LogWarning(
                $"Could not add \"{command.Name}\" to the console directly, falling back to Jötunn: " +
                SafeLogText.Brief(exception));
        }

        try
        {
            CommandManager.Instance.AddConsoleCommand(command);
            return true;
        }
        catch (Exception exception)
        {
            log.LogWarning(
                $"The console command \"{command.Name}\" is unavailable on this build: " +
                SafeLogText.Brief(exception) +
                ". Everything it reports is also written to this log, and no feature depends on it.");
            return false;
        }
    }

    private static bool TryRegisterVanilla(Jotunn.Entities.ConsoleCommand command)
    {
        ConstructorInfo? constructor = FindConstructor(out ParameterInfo[]? parameters);
        if (constructor == null || parameters == null)
        {
            return false;
        }

        var arguments = new object?[parameters.Length];
        arguments[0] = command.Name;
        arguments[1] = command.Help;
        arguments[2] = new Terminal.ConsoleEvent(args =>
        {
            // The game hands the command its own name as args[0]; every
            // command in this mod is written against the arguments after it,
            // which is also what Jötunn passes.
            string[] rest = args.Args == null || args.Args.Length <= 1
                ? new string[0]
                : Skip(args.Args);
            command.Run(rest, args.Context);
        });

        for (int index = 3; index < parameters.Length; index++)
        {
            arguments[index] = DefaultFor(parameters[index], command);
        }

        // Constructing it IS registering it: the constructor writes itself into
        // Terminal's static command table. Nothing else is touched, and no
        // existing command is replaced - a name collision would overwrite, so
        // the prefix every command here carries is what keeps that impossible.
        constructor.Invoke(arguments);
        return true;
    }

    /// <summary>The constructor whose first three parameters are the name, the
    /// description and a <c>ConsoleEvent</c> handler. The shortest match wins,
    /// so a build offering several overloads gets the least surprising one.</summary>
    private static ConstructorInfo? FindConstructor(out ParameterInfo[]? matched)
    {
        matched = null;
        ConstructorInfo? best = null;

        foreach (ConstructorInfo candidate in typeof(Terminal.ConsoleCommand).GetConstructors())
        {
            ParameterInfo[] parameters = candidate.GetParameters();
            if (parameters.Length < 3 ||
                parameters[0].ParameterType != typeof(string) ||
                parameters[1].ParameterType != typeof(string) ||
                parameters[2].ParameterType != typeof(Terminal.ConsoleEvent))
            {
                continue;
            }

            if (best == null || parameters.Length < best.GetParameters().Length)
            {
                best = candidate;
                matched = parameters;
            }
        }

        return best;
    }

    /// <summary>What to pass for one trailing parameter: the tab-completion
    /// fetcher where the build wants one, otherwise the parameter's own
    /// declared default. Reading the default off the parameter rather than
    /// assuming <c>false</c> means a build that defaults one of these to true
    /// still behaves the way its own author intended.</summary>
    private static object? DefaultFor(ParameterInfo parameter, Jotunn.Entities.ConsoleCommand command)
    {
        if (parameter.ParameterType == typeof(Terminal.ConsoleOptionsFetcher))
        {
            return new Terminal.ConsoleOptionsFetcher(command.CommandOptionList);
        }

        if (parameter.HasDefaultValue)
        {
            return parameter.DefaultValue;
        }

        return parameter.ParameterType.IsValueType
            ? Activator.CreateInstance(parameter.ParameterType)
            : null;
    }

    private static string[] Skip(string[] args)
    {
        var rest = new string[args.Length - 1];
        Array.Copy(args, 1, rest, 0, rest.Length);
        return rest;
    }

    /// <summary>A one-line report of what the console actually accepted, for
    /// the startup log. Console commands that quietly do not exist is the
    /// failure this whole file is about, so their presence is stated rather
    /// than assumed.</summary>
    public static string Describe(IReadOnlyList<string> names)
    {
        var present = new List<string>();
        var missing = new List<string>();

        foreach (string name in names)
        {
            if (IsRegistered(name))
            {
                present.Add(name);
            }
            else
            {
                missing.Add(name);
            }
        }

        string report = "Console commands registered: " +
            (present.Count == 0 ? "<none>" : string.Join(", ", present.ToArray()));
        if (missing.Count > 0)
        {
            report += ". NOT available on this build: " + string.Join(", ", missing.ToArray()) +
                " - every feature still works, and this log carries the same information.";
        }

        return report;
    }

    private static bool IsRegistered(string name)
    {
        try
        {
            FieldInfo? field = typeof(Terminal).GetField(
                "commands", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (field?.GetValue(null) is System.Collections.IDictionary table)
            {
                return table.Contains(name.ToLowerInvariant());
            }
        }
        catch
        {
            // Not knowing is not the same as not being there.
        }

        return false;
    }
}
