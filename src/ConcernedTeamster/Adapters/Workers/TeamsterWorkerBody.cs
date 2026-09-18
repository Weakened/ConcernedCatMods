using System;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>Gunnar's body as the executor uses it (<see cref="IPullerBody"/>),
/// and the one place his pulling strength is set (DECISIONS.md D5).
///
/// <b>Calibration.</b> The vanilla motor restores velocity whatever the body
/// weighs, so a puller's mass is its strength. The installed 1.0.12 cart attach
/// calls <c>Character.SetExtraMass(m_playerExtraPullMass)</c> on any puller that
/// has a <c>Character</c>, which sets <c>m_body.mass = m_originalMass + extra</c>,
/// and resets it on detach. Gunnar's body is a character, so vanilla gives him
/// the cart's extra pull mass exactly as it gives the player. Calibration
/// therefore sets his <b>base</b> mass, both the rigidbody's mass and
/// <c>m_originalMass</c>, to the local player's own <c>m_originalMass</c>, and
/// never adds the extra itself (vanilla's attach would overwrite it anyway).
/// Only Gunnar's own body is ever written; a cart's mass is never touched.
///
/// <b>Upright.</b> His rigidbody's rotation constraints must match the local
/// player's, measured at the same time, because the joint holds him 0.8 m above
/// his pivot with free angular motion.</summary>
internal sealed class TeamsterWorkerBody : IPullerBody
{
    private const RigidbodyConstraints RotationMask =
        RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;

    private readonly HaulExecutionLimits _execution;
    private TeamsterWorkerAI? _ai;
    private float _calibratedMass = float.NaN;
    private RigidbodyConstraints _playerRotation;
    private bool _playerRotationMeasured;

    public TeamsterWorkerBody(HaulExecutionLimits execution)
    {
        _execution = execution ?? throw new ArgumentNullException(nameof(execution));
    }

    internal TeamsterWorkerAI? Bound => _ai;

    internal GameObject? GameObject => _ai != null ? _ai.gameObject : null;

    internal Rigidbody? Rigidbody => _ai != null ? _ai.GetComponent<Rigidbody>() : null;

    internal float CalibratedMassKg => _calibratedMass;

    /// <summary>Set by the runtime's census: more than one body carries Gunnar's
    /// identity, so none is bound.</summary>
    internal bool Duplicated { get; set; }

    internal string PlayerRotationConstraints => _playerRotationMeasured ? _playerRotation.ToString() : "not measured";

    /// <summary>Binds (or unbinds) the one body the runtime found for Gunnar,
    /// calibrating it at once when a player can be measured.</summary>
    internal void Bind(TeamsterWorkerAI? ai)
    {
        if (_ai == ai)
        {
            return;
        }

        _ai = ai;
        _calibratedMass = float.NaN;
        _playerRotationMeasured = false;
        if (ai != null)
        {
            Calibrate();
        }
    }

    public PullerBodyFacts Read()
    {
        var facts = new PullerBodyFacts { CalibratedMassKg = _calibratedMass, Duplicated = Duplicated };
        TeamsterWorkerAI? ai = _ai;
        if (ai == null)
        {
            return facts;
        }

        try
        {
            ReadCore(ai, ref facts);
        }
        catch
        {
            facts.Present = false;
        }

        return facts;
    }

    private void ReadCore(TeamsterWorkerAI ai, ref PullerBodyFacts facts)
    {
        ZNetView view = ai.GetComponent<ZNetView>();
        facts.Present = view != null && view.IsValid() && view.IsOwner() && ai.isActiveAndEnabled;
        facts.Faulted = ai.IsFaulted;
        Character character = ai.GetComponent<Character>();
        facts.Dead = character == null || character.IsDead();

        Transform transform = ai.transform;
        Vector3 position = transform.position;
        facts.Position = new WorkPoint(position.x, position.y, position.z);
        Vector3 forward = transform.forward;
        facts.ForwardX = forward.x;
        facts.ForwardZ = forward.z;
        Vector3 scale = transform.lossyScale;
        facts.UnitScale =
            Math.Abs(scale.x - 1f) <= _execution.ScaleTolerance &&
            Math.Abs(scale.y - 1f) <= _execution.ScaleTolerance &&
            Math.Abs(scale.z - 1f) <= _execution.ScaleTolerance;

        Rigidbody body = ai.GetComponent<Rigidbody>();
        facts.HasRigidbody = body != null;
        if (body != null)
        {
            facts.IsKinematic = body.isKinematic;
            facts.UsesGravity = body.useGravity;
            facts.DetectsCollisions = body.detectCollisions;
            facts.RotationLockedUpright = _playerRotationMeasured && (body.constraints & RotationMask) == _playerRotation;
            facts.BodyMassKg = body.mass;
            Vector3 velocity = body.linearVelocity;
            facts.SpeedMetresPerSecond = (float)Math.Sqrt((velocity.x * velocity.x) + (velocity.z * velocity.z));
        }

        facts.BaseMassKg = character != null ? character.m_originalMass : float.NaN;
        facts.HasPath = ai.HasFoundPath;
        facts.MotorCommanded = ai.MotorCommanded;
    }

    public void WalkTo(WorkPoint target, float arrivalRadiusMetres)
    {
        _ai?.WalkTo(new Vector3(target.X, target.Y, target.Z), arrivalRadiusMetres);
    }

    public void SteerToward(WorkPoint target)
    {
        _ai?.SteerToward(new Vector3(target.X, target.Y, target.Z));
    }

    public void Face(float directionX, float directionZ)
    {
        _ai?.Face(new Vector3(directionX, 0f, directionZ));
    }

    public void Stop()
    {
        TeamsterWorkerAI? ai = _ai;
        if (ai != null)
        {
            ai.Halt();
        }
    }

    /// <summary>D5: Gunnar's base mass and upright constraints become the local
    /// player's, measured now.</summary>
    public bool Calibrate()
    {
        TeamsterWorkerAI? ai = _ai;
        Player player = Player.m_localPlayer;
        if (ai == null || player == null)
        {
            return false;
        }

        Rigidbody playerBody = player.GetComponent<Rigidbody>();
        Rigidbody body = ai.GetComponent<Rigidbody>();
        Character character = ai.GetComponent<Character>();
        float playerBase = player.m_originalMass;
        if (playerBody == null || body == null || character == null || !(playerBase > 0f) || !HaulExecutionLimits.IsFinite(playerBase))
        {
            return false;
        }

        character.m_originalMass = playerBase;
        body.mass = playerBase;
        _calibratedMass = playerBase;
        _playerRotation = playerBody.constraints & RotationMask;
        _playerRotationMeasured = true;
        return true;
    }

    public void Retire()
    {
        TeamsterWorkerAI? ai = _ai;
        _ai = null;
        _calibratedMass = float.NaN;
        if (ai == null)
        {
            return;
        }

        ZNetView view = ai.GetComponent<ZNetView>();
        if (view != null && view.IsValid() && view.IsOwner())
        {
            view.Destroy();
        }
    }
}
