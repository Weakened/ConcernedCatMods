namespace TheConcernedCat.ConcernedCartographer.Companions;

/// <summary>How often a settled companion is allowed to look around his camp
/// for somewhere better to sit (#306).
///
/// The interval is the whole of the anti-oscillation rule, so it lives in one
/// object rather than in a float field the next feature can poke. What the
/// director used to do - and what #306's third acceptance criterion forbids -
/// was arm the clock from the camp-fingerprint handler, and that fingerprint
/// includes seats, fires, beds and doors. Every one of those is a thing that
/// flickers: a fire burns down and is fed, a player sits in a chair and gets up
/// again. Each flicker changed the fingerprint, each fingerprint change set the
/// clock to full, and so the "half a minute, not every tick" rule was defeated
/// by exactly the events it existed to survive.
///
/// The distinction this type draws is not "world versus player". It is whether
/// the thing asking can change his <i>rank</i>:
///
/// <list type="bullet">
/// <item><see cref="CampChanged"/> - something in camp moved, lit, was claimed
/// or was built. These change what the sweep scores, so they can hand the
/// companion a strictly better spot over and over, and they arrive as often as
/// the world likes. They ask for a look; they never shorten the wait.</item>
/// <item><see cref="PlayerAsked"/> - the local player issued a command that
/// changed his situation directly: summoned him, or changed which doors
/// companions may use. Being asked is the one case where waiting half a minute
/// looks like a bug rather than like patience.</item>
/// </list>
///
/// Two different reasons hold that bypass up, and they are not interchangeable:
///
/// <list type="bullet">
/// <item>A door-permission change cannot re-score him at all. The camp snapshot
/// the wish list is built from carries home, the hour, the weather, fires and
/// beds - no doors - and his current rank is read from that snapshot and from
/// geometry, never from what he can reach. So a toggle can only widen or narrow
/// the candidate set, and narrowing never invents a strictly better spot:
/// repeat it all day and the planner has nothing new to offer. Bounded by the
/// ratchet, and needs no clock.</item>
/// <item><c>cc_companion summon</c> is different and must not be justified the
/// same way. It sets him down two metres in front of the player and forgets
/// what stopped his last walks, which <i>does</i> lower his current rank, so
/// the ratchet is re-cocked every time and the bypass is what makes him walk
/// back. What bounds it is one walk-back per invocation, at the pace a person
/// can type: it is a testing command whose whole purpose is to see him find his
/// way home again.</item>
/// </list>
///
/// A refused look is not a lost one. The request is remembered and taken the
/// first moment the interval allows, so "a seat built after he settles is never
/// taken" cannot come back as "a seat built just after a sweep is never
/// taken".</summary>
internal sealed class SeatSweepGate
{
    private readonly float _interval;

    /// <summary>Seconds waited since the last look, never counted past the
    /// interval. Capping it is what stops a long quiet spell banking two looks
    /// back to back.</summary>
    private float _waited;

    /// <summary>A look has been asked for and not yet taken. It survives a
    /// refusal, so the wait delays the look rather than dropping it.</summary>
    private bool _asked;

    public SeatSweepGate(float intervalSeconds)
    {
        _interval = intervalSeconds > 0f ? intervalSeconds : 0f;
    }

    /// <summary>The floor between two looks.</summary>
    public float IntervalSeconds => _interval;

    /// <summary>Whether a look has been asked for and is still owed.</summary>
    public bool LookPending => _asked;

    /// <summary>How long he has waited, for logs and tests. Never more than
    /// <see cref="IntervalSeconds"/>.</summary>
    public float WaitedSeconds => _waited;

    /// <summary>Whether the wait is over, so a look asked for now would be
    /// allowed rather than deferred.</summary>
    public bool WaitIsOver => _waited >= _interval;

    public void Tick(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        _waited += deltaTime;
        if (_waited > _interval)
        {
            _waited = _interval;
        }
    }

    /// <summary>Something in camp changed. Asks for a look at the next
    /// opportunity - even mid-stroll or mid-drink - without shortening the
    /// wait by one second.</summary>
    public void CampChanged()
    {
        _asked = true;
    }

    /// <summary>The player asked for one, by summoning him or by changing which
    /// doors companions may use. The single bypass; the two reasons it is safe
    /// are on the type, and they are different reasons.</summary>
    public void PlayerAsked()
    {
        _asked = true;
        _waited = _interval;
    }

    /// <summary>Whether a sweep may run this pass, and books it if so.
    ///
    /// Not while he is on his feet or in the middle of a drink, unless a look
    /// was asked for; and never before the interval is up, whoever asked.
    /// </summary>
    public bool TryTakeLook(bool settled, bool drinking)
    {
        if (!settled && !_asked)
        {
            return false;
        }

        if (_waited < _interval)
        {
            return false;
        }

        if (drinking && !_asked)
        {
            return false;
        }

        _waited = 0f;
        _asked = false;
        return true;
    }
}
