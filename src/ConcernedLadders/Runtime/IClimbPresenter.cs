using System;
using TheConcernedCat.ConcernedLadders.Domain;

namespace TheConcernedCat.ConcernedLadders.Runtime;

/// <summary>The read-only presentation seam (agreed with Codex, 2026-10-26). The ladder runtime is the only
/// producer; a presenter only poses the local climber's own body and restores what it owns.
///
/// <para>Producer guarantees: frames only for the locally owned player, finite values or no frame, one frame per
/// motor step, never a frame after <see cref="End"/> in the same session; <see cref="End"/> is synchronous and
/// precedes any Jump/Drop impulse or other release write, and is also sent for death, teleport, logout, world
/// unload, a disabled setting, a destroyed ladder and an external takeover.</para>
///
/// <para>Presenter obligations: no input reads, no rigidbody/capsule/gravity/stamina/skill/collision writes, no
/// landing selection; restore only owned presentation state; a fault disables presentation only.</para></summary>
internal interface IClimbPresenter : IDisposable
{
    /// <summary>False means unavailable: the runtime keeps its walking-pose fallback.</summary>
    bool Install(string harmonyId);

    /// <summary>After each motor step, in physics order.</summary>
    void Publish(Player player, in ClimbPresentationFrame frame);

    /// <summary>The attachment ended; <paramref name="player"/> may already be destroyed.</summary>
    void End(Player? player, ClimbEnd reason);

    /// <summary>Once per rendered frame.</summary>
    void Update(float deltaTime);
}
