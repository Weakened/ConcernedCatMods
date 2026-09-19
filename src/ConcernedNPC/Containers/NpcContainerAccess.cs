namespace TheConcernedCat.ConcernedNPC.Containers;

/// <summary>Why an NPC may not use a container right now. One value, named
/// precisely enough that the sentence a player reads tells them what to change.
///
/// <b>Why ward and privacy are separate values.</b> The shipped gate collapses
/// them into one "access denied", and the fix for each is different: one is a
/// guard stone, the other is the chest's own privacy setting and the character
/// who built it. Adopting the stricter gate across the products will make some
/// already-working containers start refusing, so the refusal has to name the
/// thing to go and change, or a player is told only that it does not work.
/// </summary>
internal enum NpcContainerRefusal
{
    /// <summary>Nothing refuses it. Paired with a use the player allowed, this
    /// is a yes.</summary>
    None = 0,

    /// <summary>The container is not there any more, or its inventory could not
    /// be read.</summary>
    Gone = 1,

    /// <summary>Something is there, and it is not the container that was
    /// designated. The id was reused by a different object after a world load,
    /// which is the ordinary case rather than the exotic one.</summary>
    NotThisContainer = 2,

    /// <summary>The player has not enabled this container for NPCs. The
    /// default, and never an error.</summary>
    NotEnabled = 3,

    /// <summary>Enabled, but not for this - taking from a deposit-only
    /// container, or the reverse.</summary>
    UseNotAllowed = 4,

    /// <summary>This process does not own the container, and a write by a
    /// non-owner is discarded without telling anyone.</summary>
    NotOwnedHere = 5,

    /// <summary>Somebody has it open, or it rides a cart that is in use. What
    /// is written now is overwritten when their window closes.</summary>
    InUse = 6,

    /// <summary>A guard stone refuses. The player's fix is the ward.</summary>
    WardDenied = 7,

    /// <summary>The container's own privacy setting refuses. The player's fix is
    /// the chest.</summary>
    PrivacyDenied = 8,

    /// <summary>Too far away for the NPC to reach from where it is standing.
    /// Not a fault: walk closer and ask again.</summary>
    OutOfReach = 9,
}

/// <summary>What an NPC may do with one container right now: what the player
/// allowed, and what the world allows.
///
/// <b>What it guarantees.</b> That both halves are answered together, and that
/// the defaulted struct is a refusal. <see cref="Allowed"/> is
/// <see cref="NpcContainerUse.Off"/> and <see cref="Refusal"/> is
/// <see cref="NpcContainerRefusal.None"/> in a <c>default</c> value, and
/// <see cref="CanTake"/> and <see cref="CanDeposit"/> are both false, because
/// they require an allowance rather than the absence of a refusal.</summary>
internal readonly struct NpcContainerAccess
{
    internal NpcContainerAccess(NpcContainerUse allowed, NpcContainerRefusal refusal)
    {
        Allowed = allowed;
        Refusal = refusal;
    }

    /// <summary>What the player allowed for this container.</summary>
    internal NpcContainerUse Allowed { get; }

    /// <summary>What the world says about it now, or
    /// <see cref="NpcContainerRefusal.None"/>.</summary>
    internal NpcContainerRefusal Refusal { get; }

    /// <summary>May an NPC take from it, right now. Requires the allowance
    /// <b>and</b> no refusal: neither half alone is a yes.</summary>
    internal bool CanTake =>
        Refusal == NpcContainerRefusal.None && (Allowed & NpcContainerUse.Take) == NpcContainerUse.Take;

    /// <summary>May an NPC put things into it, right now.</summary>
    internal bool CanDeposit =>
        Refusal == NpcContainerRefusal.None && (Allowed & NpcContainerUse.Deposit) == NpcContainerUse.Deposit;

    /// <summary>Whether <paramref name="use"/> - one flag or both - is permitted
    /// right now.</summary>
    internal bool Permits(NpcContainerUse use) =>
        use != NpcContainerUse.Off && Refusal == NpcContainerRefusal.None && (Allowed & use) == use;
}
