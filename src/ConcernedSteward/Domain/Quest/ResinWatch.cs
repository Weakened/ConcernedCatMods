namespace TheConcernedCat.ConcernedSteward.Domain.Quest;

/// <summary>What one look at a player's pack concluded.</summary>
internal enum ResinSighting
{
    /// <summary>Nothing worth reporting: the count did not rise, or this is the
    /// first look and there is nothing to compare it with. Zero, so an
    /// unanswered reading is never a pickup.</summary>
    Nothing = 0,

    /// <summary>More resin than a moment ago, with no window open through which
    /// it could have been moved. Treated as picked up off the ground.</summary>
    PickedUp = 1,

    /// <summary>The pack could not be counted. <b>Not "none"</b>: the next
    /// reading starts a fresh comparison instead of being read as a jump from
    /// zero.</summary>
    Unreadable = 2,

    /// <summary>A window is open through which items move between containers,
    /// so a rise proves nothing. The count is taken as the new baseline and
    /// nothing is reported.</summary>
    Moved = 3,
}

/// <summary>Noticing that somebody picked resin up, without patching the game.
///
/// <b>Why this exists at all.</b> #382 wants the flint and steel to turn up the
/// first time a player picks up resin. The precise way to see that is a Harmony
/// postfix on <c>Humanoid.Pickup</c> - and Concerned Steward's own audit,
/// <c>SourceAuditTests.The_product_patches_nothing</c>, is an acceptance
/// criterion of #340 and says this product patches nothing, so that with the
/// runtime off it is genuinely inert. Weakening that test to add a quest hook
/// would be trading a safety property a player was promised for a convenience
/// this feature does not need.
///
/// <b>So the question is answered from what vanilla already exposes.</b> Two
/// readings, both of them plain public reads:
///
/// <list type="bullet">
/// <item>how much resin is in the player's own pack, counted with the same
/// predicate vanilla's own fuel path uses - the item's shared name, and nothing
/// else;</item>
/// <item>whether an inventory window is open, which is the one thing that tells
/// a pickup apart from a withdrawal. Taking resin out of a chest needs the
/// window; picking it up off the ground, by hand or by auto-pickup, does
/// not.</item>
/// </list>
///
/// <b>What that costs, said rather than hidden.</b> This is an inference and not
/// an observation. Anything that raises the count with no window open would read
/// as a pickup: a mod that grants items, a console command, a trade. Every one
/// of those is a rise in the player's own resin with nobody's window open, which
/// is what "picked up" means from outside, and the consequence of being wrong is
/// that a story beat arrives a little early. It is never a duplicated object -
/// that is guaranteed one layer up, by the quest's own high-water mark - and it
/// never moves an item.
///
/// <b>It does not fire on the first look.</b> A watch that reported the player's
/// existing resin as a pickup would hand the flint and steel to anybody who
/// loaded a save with resin already in their pack, which is not what "picks up
/// resin" means.</summary>
internal sealed class ResinWatch
{
    private bool _primed;
    private int _seen;

    /// <summary>How much resin the last usable reading found. Meaningless until
    /// <see cref="IsPrimed"/>.</summary>
    internal int LastSeen => _seen;

    /// <summary>Whether there is a reading to compare against. False before the
    /// first look and after any look that could not count.</summary>
    internal bool IsPrimed => _primed;

    /// <summary>Looks once.</summary>
    /// <param name="carried">Units of resin in the player's own pack, or a
    /// negative number when it could not be counted.</param>
    /// <param name="aWindowIsOpen">Whether an inventory or container window is
    /// open right now.</param>
    internal ResinSighting Observe(int carried, bool aWindowIsOpen)
    {
        if (carried < 0)
        {
            // Forget rather than keep. Keeping the old baseline and comparing
            // the next good reading against it would turn "I could not see for a
            // moment" into "and then twenty appeared".
            _primed = false;
            _seen = 0;
            return ResinSighting.Unreadable;
        }

        if (aWindowIsOpen)
        {
            // Re-baselined on EVERY frame the window is open, not once when it
            // closes. That is what makes a chest withdrawal invisible here: by
            // the time the window shuts, the baseline already includes what was
            // taken.
            _primed = true;
            _seen = carried;
            return ResinSighting.Moved;
        }

        if (!_primed)
        {
            _primed = true;
            _seen = carried;
            return ResinSighting.Nothing;
        }

        bool rose = carried > _seen;
        _seen = carried;
        return rose ? ResinSighting.PickedUp : ResinSighting.Nothing;
    }

    /// <summary>Forgets the baseline, because the pack it was about has gone.
    /// Called when a world unloads: the next world's first reading must be a
    /// baseline and not a comparison against somebody else's pack.</summary>
    internal void Forget()
    {
        _primed = false;
        _seen = 0;
    }
}
