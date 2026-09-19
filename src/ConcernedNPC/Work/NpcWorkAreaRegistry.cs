using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedNPC.Work;

/// <summary>What happened when a provider offered itself.</summary>
public enum ProviderRegistration
{
    /// <summary>Nobody asked.</summary>
    Unspecified = 0,

    /// <summary>Registered. Areas naming this id resolve from now on.</summary>
    Registered = 1,

    /// <summary>Nothing was offered.</summary>
    NoProvider = 2,

    /// <summary>The provider answers to no id, or threw when asked for one. It
    /// could never be found, so registering it would be a silent no-op that
    /// looks like success.</summary>
    Unnamed = 3,

    /// <summary>Something else is already registered under that id. Refused,
    /// never replaced: two mods claiming one id is a conflict a person has to
    /// see, and last-one-wins means which shape a player's area comes back as
    /// depends on plugin load order.</summary>
    DuplicateId = 4,
}

/// <summary>Who knows how to rebuild which shapes.
///
/// <b>What it guarantees, and it is one thing.</b> That there is no fallback.
/// An area whose provider is not registered resolves to
/// <see cref="WorkAreaResolution.ProviderMissing"/> and no area at all - never
/// to a circle around the same coordinates, never to the last area that worked,
/// never to some default radius. The circle that ships here is registered
/// through the same call as anything else and has no privileged path, precisely
/// so there is no code in this file that could become one.
///
/// This is the failure the leaf exists to prevent, and it is the worse of the
/// two named in the issue, because it looks like it worked: an NPC standing in
/// a plausible circle, harvesting ground the player never marked, until somebody
/// notices their trees are gone.
///
/// <b>What it is not: durable.</b> Registration happens at start-up, every
/// session, from whichever mods are installed that session. Nothing here is
/// written down, and the set of providers is allowed to differ between two loads
/// of the same world - which is exactly the case
/// <see cref="WorkAreaResolution.ProviderMissing"/> exists to report honestly.
///
/// <b>Not thread-safe, deliberately.</b> Registration is start-up work on the
/// main thread and resolution is tick work on the main thread, like everything
/// else that touches the world here. A lock would suggest otherwise.</summary>
public sealed class NpcWorkAreaRegistry
{
    private readonly Dictionary<string, INpcWorkAreaProvider> _providers =
        new Dictionary<string, INpcWorkAreaProvider>(StringComparer.Ordinal);

    /// <summary>How many providers are registered. For a diagnostic line, and
    /// for a test proving the built-in one is not special.</summary>
    internal int Count => _providers.Count;

    /// <summary>Whether anything answers to this id right now.</summary>
    internal bool Knows(string providerId) =>
        providerId != null && _providers.ContainsKey(providerId);

    /// <summary>Offers a provider. A provider whose own <c>ProviderId</c> getter
    /// throws is <see cref="ProviderRegistration.Unnamed"/> rather than an
    /// exception: a badly written mod must not take the registry down with
    /// it.</summary>
    internal ProviderRegistration Register(INpcWorkAreaProvider? provider)
    {
        if (provider == null)
        {
            return ProviderRegistration.NoProvider;
        }

        string id;
        try
        {
            id = provider.ProviderId ?? string.Empty;
        }
        catch (Exception)
        {
            return ProviderRegistration.Unnamed;
        }

        if (id.Length == 0)
        {
            return ProviderRegistration.Unnamed;
        }

        if (_providers.ContainsKey(id))
        {
            return ProviderRegistration.DuplicateId;
        }

        _providers.Add(id, provider);
        return ProviderRegistration.Registered;
    }

    /// <summary>Rebuilds one area, or says why it could not be - and hands back
    /// no area whenever it could not be.</summary>
    internal NpcWorkAreaResult Resolve(NpcWorkAreaDescriptor descriptor)
    {
        if (!descriptor.Id.IsNamed)
        {
            return NpcWorkAreaResult.ProviderMissing("an area with no provider and no key names nothing");
        }

        if (!descriptor.IsWellFormed)
        {
            return NpcWorkAreaResult.ShapeUnreadable(
                "the saved shape for " + descriptor.Id + " is missing or is not made of real numbers");
        }

        if (!_providers.TryGetValue(descriptor.Id.ProviderId, out INpcWorkAreaProvider? provider) || provider == null)
        {
            return NpcWorkAreaResult.ProviderMissing(
                "no provider is registered for " + descriptor.Id.ProviderId +
                ", so the area it drew cannot be rebuilt");
        }

        NpcWorkAreaResult result;
        try
        {
            result = provider.Rebuild(descriptor);
        }
        catch (Exception failure)
        {
            return NpcWorkAreaResult.Refused(
                "the provider for " + descriptor.Id.ProviderId + " failed while rebuilding the area: " +
                failure.Message);
        }

        if (result.Resolution == WorkAreaResolution.Resolved && result.Area == null)
        {
            return NpcWorkAreaResult.Refused(
                "the provider for " + descriptor.Id.ProviderId + " reported success with no area");
        }

        if (result.Resolution == WorkAreaResolution.Unspecified)
        {
            return NpcWorkAreaResult.Refused(
                "the provider for " + descriptor.Id.ProviderId + " answered nothing at all");
        }

        return result;
    }
}
