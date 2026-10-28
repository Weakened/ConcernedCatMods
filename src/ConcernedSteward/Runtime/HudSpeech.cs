using System;
using System.Reflection;
using TheConcernedCat.ConcernedSteward.Domain.Speech;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary>Puts a line Sunniva says on the player's screen, through vanilla's
/// own message display.
///
/// <b>Reads and draws; changes nothing.</b> This is the screen the game already
/// shows "Your skill increased" on. Nothing here touches the world, an
/// inventory, a network object or an RPC, and nothing here is a patch.
///
/// <b>Resolved by name, not compiled against.</b> C# bakes optional arguments
/// into the call site, so a game update that merely appends one to
/// <c>MessageHud.ShowMessage</c> would turn a direct call into a
/// <c>MissingMethodException</c> raised while the CALLING method is compiled -
/// which is how Valheim 1.0's change to <c>Character.Message</c> stopped Concerned
/// Cartographer 0.10.1 from running at all. So the overload is found by name and
/// its optional tail is filled from the live parameter list, and a build that
/// has no recognisable overload loses the line and nothing else. Same shape as
/// Cartographer's <c>VanillaMessage</c>, without the Harmony dependency this
/// product does not take.
///
/// <b>Localized by the game.</b> Vanilla's <c>ShowMessage</c> runs every line
/// through its own localization before drawing it, so a line that names
/// <c>$item_resin</c> reads "Resin" in the player's language.
///
/// Never throws.</summary>
internal static class HudSpeech
{
    private static readonly MethodInfo? Show;
    private static readonly object?[] Tail = Array.Empty<object?>();
    private static readonly object? TopLeft;
    private static readonly object? Centre;

    static HudSpeech()
    {
        try
        {
            Type messageType = typeof(MessageHud.MessageType);
            MethodInfo? best = null;
            int bestLength = int.MaxValue;
            foreach (MethodInfo candidate in typeof(MessageHud).GetMethods(
                         BindingFlags.Instance | BindingFlags.Public))
            {
                if (candidate.Name != "ShowMessage" || candidate.IsGenericMethod)
                {
                    continue;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length < 2
                    || parameters[0].ParameterType != messageType
                    || parameters[1].ParameterType != typeof(string))
                {
                    continue;
                }

                bool fillable = true;
                for (int index = 2; index < parameters.Length; index++)
                {
                    if (parameters[index].ParameterType.IsByRef
                        || parameters[index].ParameterType.IsPointer
                        || !parameters[index].IsOptional
                        || !parameters[index].HasDefaultValue)
                    {
                        fillable = false;
                        break;
                    }
                }

                if (fillable && parameters.Length < bestLength)
                {
                    best = candidate;
                    bestLength = parameters.Length;
                }
            }

            if (best == null)
            {
                return;
            }

            ParameterInfo[] chosen = best.GetParameters();
            var tail = new object?[chosen.Length - 2];
            for (int index = 0; index < tail.Length; index++)
            {
                tail[index] = chosen[index + 2].DefaultValue;
            }

            TopLeft = Enum.Parse(messageType, "TopLeft");
            Centre = Enum.Parse(messageType, "Center");
            Tail = tail;
            Show = best;
        }
        catch (Exception)
        {
            // A display that cannot be bound loses lines, never the mod.
            Show = null;
        }
    }

    /// <summary>False when this game build has no recognisable display.
    /// Reported once at load so a player told "she will say so" can be told
    /// why she does not.</summary>
    internal static bool Available => Show != null;

    /// <summary>Shows one line, or does nothing. The voice's sink.</summary>
    internal static void Say(string line, SpeechWeight weight)
    {
        if (Show == null || string.IsNullOrEmpty(line))
        {
            return;
        }

        try
        {
            MessageHud? hud = MessageHud.instance;
            if (hud == null)
            {
                return;
            }

            var arguments = new object?[2 + Tail.Length];
            arguments[0] = weight == SpeechWeight.Important ? Centre : TopLeft;
            arguments[1] = line;
            Array.Copy(Tail, 0, arguments, 2, Tail.Length);
            Show.Invoke(hud, arguments);
        }
        catch (Exception)
        {
            // A line that could not be shown is lost, and only the line.
        }
    }
}
