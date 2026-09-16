using System;
using System.Reflection;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Reporting;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>Puts a line of speech over the companion's head, the way the game
/// puts one over a trader's.
///
/// The game's own route is <c>Talker.Say</c>, and it is closed to us on
/// purpose: <c>Talker</c> requires a <c>ZNetView</c> and registers an RPC, and
/// this companion is forbidden both — he is a local presentation, not a
/// networked creature. But <c>Say</c> only <i>sends</i>. What actually draws
/// the bubble is the far end, <c>Chat.AddInworldText</c>, and that is pure
/// local UI: it instantiates the world-text widget, gives it a GameObject to
/// follow, and adds it to the list <c>Chat</c> already positions every frame.
/// No network, no permission check, no chat-window line — <c>AddInworldText</c>
/// is reached directly, so the chat log stays the player's own.
///
/// Two details from reading that code decide the shape of this:
/// <list type="bullet">
/// <item>The widget follows its GameObject by asking for a <c>Character</c> and
/// using <c>GetHeadPoint()</c>, falling back to the transform's own position
/// when there is none. Our companion has no <c>Character</c> — that component
/// is on the forbidden list — so the object handed over is his <b>head
/// joint</b>, not his root, and the bubble sits over his head rather than at
/// his feet.</item>
/// <item>For <c>Talker.Type.Normal</c> the speaker's name is never read; only
/// Shout and Ping prefix it. So the <c>UserInfo</c> passed in is never
/// dereferenced, and nothing here touches platform identity.</item>
/// </list>
///
/// Reached by reflection, because the method is private and because a build
/// that renames or reshapes it must quietly fall back to the old centre
/// message rather than throw in a player's face. Which path was taken is
/// logged once — silence is what let a whole console command set ship
/// broken.</summary>
internal static class OverheadSpeech
{
    /// <summary>Identifies the bubble so that a second line replaces the first
    /// rather than stacking. Deliberately a small constant: the field is
    /// otherwise a platform user id, and colliding with a real one would move
    /// somebody's own chat bubble onto Hulgi.</summary>
    private const long TalkerId = 0x0000_4343_4855_4C47L;

    private static bool _probed;
    private static MethodInfo? _addInworldText;
    private static Type? _userInfoType;
    private static FieldInfo? _userInfoName;

    /// <summary>Says <paramref name="text"/> over <paramref name="head"/>.
    /// Returns false when this build has no such widget, and the caller should
    /// fall back to an ordinary message.</summary>
    public static bool TrySay(
        GameObject head, Vector3 position, string speaker, string text, ManualLogSource log)
    {
        if (head == null || string.IsNullOrEmpty(text))
        {
            return false;
        }

        Probe(log);
        if (_addInworldText == null || _userInfoType == null)
        {
            return false;
        }

        try
        {
            if (Chat.instance == null)
            {
                return false;
            }

            object user = Activator.CreateInstance(_userInfoType);
            _userInfoName?.SetValue(user, speaker);

            _addInworldText.Invoke(
                Chat.instance,
                new object[] { head, TalkerId, position, Talker.Type.Normal, user, text });
            return true;
        }
        catch (Exception exception)
        {
            // One failure is enough to stop trying: whatever is wrong with this
            // build's chat widget will still be wrong on the next line, and a
            // companion who logs an exception every time he speaks is worse
            // company than one who quietly uses the old message.
            _addInworldText = null;
            log.LogInfo(
                "The companion's speech could not be drawn over his head on this build, so his " +
                $"lines will appear as ordinary messages instead: {SafeLogText.Brief(exception)}");
            return false;
        }
    }

    private static void Probe(ManualLogSource log)
    {
        if (_probed)
        {
            return;
        }

        _probed = true;

        try
        {
            _userInfoType = typeof(Talker).Assembly.GetType("UserInfo");
            _userInfoName = _userInfoType?.GetField("Name", BindingFlags.Instance | BindingFlags.Public);

            // Matched by SHAPE rather than by an exact signature. Every time a
            // pinned signature has been assumed in this codebase the game has
            // eventually changed it out from under the assumption, quietly.
            foreach (MethodInfo candidate in typeof(Chat).GetMethods(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (!string.Equals(candidate.Name, "AddInworldText", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length != 6 ||
                    parameters[0].ParameterType != typeof(GameObject) ||
                    parameters[1].ParameterType != typeof(long) ||
                    parameters[2].ParameterType != typeof(Vector3) ||
                    parameters[3].ParameterType != typeof(Talker.Type) ||
                    parameters[5].ParameterType != typeof(string))
                {
                    continue;
                }

                if (_userInfoType != null && parameters[4].ParameterType != _userInfoType)
                {
                    continue;
                }

                _addInworldText = candidate;
                break;
            }

            log.LogInfo(
                _addInworldText != null
                    ? "Companion speech will appear over his head, the way a trader's does."
                    : "This build has no in-world chat text of the expected shape, so the " +
                      "companion's lines will appear as ordinary messages.");
        }
        catch (Exception exception)
        {
            _addInworldText = null;
            log.LogInfo(
                "The companion's speech widget could not be looked up on this build; his lines " +
                $"will appear as ordinary messages: {SafeLogText.Brief(exception)}");
        }
    }

    /// <summary>Forgets what was found, so a world change re-probes. The widget
    /// lives on <c>Chat.instance</c>, which does not survive a return to the
    /// main menu.</summary>
    public static void Forget()
    {
        _probed = false;
        _addInworldText = null;
        _userInfoType = null;
        _userInfoName = null;
    }
}
