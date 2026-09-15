using System;
using BepInEx.Logging;
using TheConcernedCat.Companions.Identity;
using TheConcernedCat.ConcernedCartographer.Companions;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>Resolves the world + character a companion sidecar belongs to.
///
/// Both halves are Verified surfaces on the audited build (1.0.12):
/// <c>ZNet.GetWorldUID()</c> and <c>PlayerProfile.GetPlayerID()</c>. The
/// profile id is used rather than the character's <i>name</i> on purpose —
/// two characters can share a name, and a rename must not read as a different
/// person and lose their progress.
///
/// Failure here is never evidence about the player. When the scope cannot be
/// resolved the caller leaves the feature gate open, so an unidentifiable
/// world produces a player with all their tools and no companion, rather than
/// a player with no tools.</summary>
internal sealed class CompanionScopeSource
{
    private readonly ManualLogSource _log;
    private readonly RateLimitedLog _rateLimited;

    public CompanionScopeSource(ManualLogSource log)
    {
        _log = log;

        // Resolution is attempted on a timer while a world loads; one line a
        // minute is enough to diagnose without flooding a log.
        _rateLimited = new RateLimitedLog(log, 60f);
    }

    /// <summary>True when the last attempt failed for a reason that is about
    /// this build's APIs rather than about the world not being ready yet.</summary>
    public bool CapabilityMissing { get; private set; }

    public bool TryResolve(out CompanionScope scope)
    {
        scope = default;

        try
        {
            if (!WorldContext.TryGetWorldUid(out long worldUid))
            {
                return false;
            }

            if (Game.instance == null)
            {
                return false;
            }

            PlayerProfile profile = Game.instance.GetPlayerProfile();
            if (profile == null)
            {
                return false;
            }

            long playerId = profile.GetPlayerID();
            if (playerId == 0L)
            {
                // A profile that has never been saved has no id yet. Normal
                // during the first seconds of a session; not a capability
                // problem.
                return false;
            }

            scope = new CompanionScope(
                CartographerCompanions.Product,
                new WorldId(worldUid),
                new CharacterId(playerId));
            CapabilityMissing = false;
            return scope.IsComplete;
        }
        catch (MissingMethodException exception)
        {
            MarkCapabilityMissing(exception);
            return false;
        }
        catch (MissingMemberException exception)
        {
            MarkCapabilityMissing(exception);
            return false;
        }
        catch (TypeLoadException exception)
        {
            MarkCapabilityMissing(exception);
            return false;
        }
        catch (Exception exception)
        {
            _rateLimited.Error(
                "companion-scope",
                "Could not identify this world and character for companion progress; " +
                $"companion features stay open and nothing is locked: {SafeLogText.Describe(exception)}");
            return false;
        }
    }

    private void MarkCapabilityMissing(Exception exception)
    {
        if (!CapabilityMissing)
        {
            CapabilityMissing = true;
            _log.LogWarning(
                "This Valheim build does not expose the world/character identity this mod's companion " +
                "features need, so companions are disabled for this session. Every map tool stays " +
                $"available and nothing is locked: {SafeLogText.Brief(exception)}");
        }
    }
}
