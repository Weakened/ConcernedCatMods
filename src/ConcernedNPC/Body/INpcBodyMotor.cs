using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Body;

/// <summary>Everything a body can be asked to do with itself, and deliberately
/// nothing else.
///
/// <b>Why the seam is this narrow.</b> Every member here is a call into
/// vanilla's own motor. There is no member that sets a position, a velocity, a
/// force or a rotation, and that absence is the safety property: a planner
/// written against this interface cannot teleport a body or shove it through a
/// wall, because there is no method that would let it. The two shipped minds
/// have the same property today and keep it by everyone remembering; here it is
/// the type.
///
/// <b>Why a seam at all, when there is one implementation.</b> Because the
/// planner is injected rather than baked in. Two of the three minds this
/// replaces each owned a movement planner as a field, which is why the same
/// planner exists twice and why neither could be exercised without a live
/// creature. With the planner outside, the mind is the game contact and the
/// planning is a decision about points and budgets - and the leaf that writes
/// the route planner can be proved against this interface with no game at
/// all.
///
/// <b>How a role gets one.</b> Only from
/// <see cref="NpcBodyMind.TryDrive"/>, against the <c>BodyLease</c> the arbiter
/// granted for that body's identity - which is what makes "one identity, one
/// body, driven by its holder" a refusal a role reads rather than a rule it is
/// asked to keep. The mind itself does not implement this interface, so there
/// is no cast to a motor either.</summary>
public interface INpcBodyMotor
{
    /// <summary>The body is owned by this peer and its network object is valid.
    /// False means it must not be commanded at all: another peer owns it.
    /// </summary>
    bool IsOwnedAndValid { get; }

    /// <summary>The mind latched a fault and is permanently inert this session.
    /// </summary>
    bool IsFaulted { get; }

    /// <summary>The motor was commanded to move since the last
    /// <see cref="Halt"/>. Vanilla never clears a direction by itself, so a
    /// body nobody is commanding keeps walking; this is what a runtime reads to
    /// notice that.</summary>
    bool MotorCommanded { get; }

    /// <summary>The last path search succeeded, read from the field vanilla
    /// already wrote.
    ///
    /// Deliberately not "does a path exist": the question that sounds cheap
    /// runs a full path search, so asking it every tick would be twenty
    /// searches a second inside a runtime whose whole claim is that its costs
    /// are bounded.</summary>
    bool HasFoundPath { get; }

    /// <summary>Asks vanilla for a path to a point. Returns what vanilla
    /// answered. Call it on a budget: this is the expensive one.</summary>
    bool TryFindPath(Vector3 point);

    /// <summary>One step of walking toward a point, along the path vanilla
    /// found.
    ///
    /// Returns nothing on purpose. Vanilla's own move answers "stopped", not
    /// "arrived" - it says the same thing when the point is close, when the
    /// path search failed and when the path ran out - so a caller that read it
    /// as arrival would report a failed path as a completed order. Arrival is
    /// decided by the caller, from distance.</summary>
    void WalkTo(Vector3 point, float arrivalRadiusMetres);

    /// <summary>Walks straight toward a point without a path, for a caller that
    /// has already vouched for the segment. Halts instead when the point is
    /// where the body already is.</summary>
    void SteerToward(Vector3 point);

    /// <summary>Turns to face a direction without moving.</summary>
    void Face(Vector3 direction);

    /// <summary>Stops. Idempotent, and safe to call on a body that was never
    /// moving.</summary>
    void Halt();
}
