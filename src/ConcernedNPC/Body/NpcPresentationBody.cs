using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Body;

/// <summary>Builds a local-only figure out of a game character prefab, by
/// <b>extraction and refusal</b> - never by instantiating one and stripping it
/// afterwards.
///
/// <b>What "extraction" means here, exactly.</b> The source prefab is
/// instantiated under an inactive holder, so nothing in it wakes. Its animated
/// visual subtree is re-parented out into a new root that is also created
/// inactive, and the remainder - which is where the player component, the
/// network view, the character and the AI live - is destroyed without ever
/// having been enabled.
///
/// <b>What "refusal" means, and why it is not cleanup.</b> If a networking or
/// AI component turns out to be inside the subtree on some build, the whole
/// attempt is abandoned and the caller tries the next candidate; it is never
/// removed and carried on with. If one of the source character's own scripts
/// cannot be destroyed on some build, the candidate is refused in the same way,
/// and the root is destroyed while it is still dark. Disabling such a script is
/// not safety: the engine runs its <c>Awake</c> on activation whether it is
/// enabled or not. That keeps one property true and auditable: a finished
/// figure has never contained one of those components.
///
/// <b>Why the light goes on last.</b> Re-parenting is what wakes a subtree. The
/// moment a transform lands under an active parent, the engine runs
/// <c>Awake</c> on everything inside it - and the character animation event
/// component's <c>Awake</c> dereferences the character this figure deliberately
/// does not have, then puts itself in a static list the engine walks every
/// physics and late update for the rest of the session. So the root is born
/// dark, the scripts are taken out while it is dark, and it is switched on only
/// after they are gone. That is removal, not stripping: not one of them has
/// run.
///
/// <b>Physics is removed rather than refused, and the difference is
/// deliberate.</b> A collider carries no registration - nothing is aware of it
/// until something touches it - so removing one cannot leave a trace the way
/// waking a network view would. Leaving one in place would mean a player
/// walking into an invisible wall where their companion stands, which is also
/// the safety rule that says a companion never blocks anybody.
///
/// <b>What this type does not do.</b> It does not dress the figure, pose it,
/// name it, or decide which candidate to try. Appearance, props and the
/// candidate list are the role's: the figure comes back with the joints and the
/// renderer the source rig offered, and the role goes on from there.</summary>
public static class NpcPresentationBody
{
    /// <summary>Extracts one figure, or refuses this candidate.</summary>
    /// <param name="source">The game prefab to take a visual from.</param>
    /// <param name="candidateName">What to call it in the log line - the
    /// caller's word for this candidate, so a player reading the log sees the
    /// name they would recognise.</param>
    /// <param name="figureName">The name to give the new root. Supplied by the
    /// role: this library names nothing that the game or a player will see.
    /// </param>
    /// <param name="holderName">The name of the inactive holder the source is
    /// instantiated under. Also the role's, and destroyed before this method
    /// returns either way.</param>
    /// <param name="ownNamespacePrefix">Scripts whose type namespace starts
    /// with this are left alone, because they were put on the figure by the
    /// caller on purpose. Empty means "take every script out", which is the
    /// safe answer for a caller that adds none.</param>
    /// <param name="log">Where the refusal and the summary lines go.</param>
    public static NpcPresentationFigure TryExtract(
        GameObject? source,
        string candidateName,
        string figureName,
        string holderName,
        string ownNamespacePrefix,
        Action<string>? log)
    {
        if (source == null)
        {
            return NpcPresentationFigure.Refused("there was no prefab to take a visual from");
        }

        GameObject holder = new GameObject(holderName);
        holder.SetActive(false);

        GameObject? clone = null;
        GameObject? root = null;
        try
        {
            clone = UnityEngine.Object.Instantiate(source, holder.transform);

            Animator? animator = clone.GetComponentInChildren<Animator>(includeInactive: true);
            if (animator == null)
            {
                return Refuse(log, candidateName, "it has no animator, so there is no animated body to take");
            }

            GameObject visual = animator.gameObject;
            if (visual == clone)
            {
                // The animator sits on the same object as the networking and AI
                // components. Extracting it would mean extracting them, so this
                // candidate is refused rather than cleaned up.
                return Refuse(
                    log,
                    candidateName,
                    "it keeps its animator on the same object as its game components, so it cannot be used "
                    + "as a local visual source");
            }

            if (ContainsForbiddenComponent(visual, out string offender))
            {
                return Refuse(
                    log,
                    candidateName,
                    "it carries " + offender + " inside its visual subtree. Nothing was stripped; the "
                    + "candidate was refused");
            }

            // Read the source's own attachment points and body renderer BEFORE
            // anything is destroyed. All of them live inside the visual
            // subtree, so the references stay valid once it is re-parented out,
            // and the component they were read from never wakes.
            Transform? helmet = null;
            Transform? rightHand = null;
            SkinnedMeshRenderer? body = null;
            VisEquipment equipment = clone.GetComponentInChildren<VisEquipment>(includeInactive: true);
            if (equipment != null)
            {
                helmet = InsideOrNull(equipment.m_helmet, visual);
                rightHand = InsideOrNull(equipment.m_rightHand, visual);
                body = equipment.m_bodyModel != null
                    && equipment.m_bodyModel.transform.IsChildOf(visual.transform)
                        ? equipment.m_bodyModel
                        : null;
            }

            // Born dark, and it stays dark until the source's own scripts are
            // out of it.
            root = new GameObject(figureName);
            root.SetActive(false);

            visual.transform.SetParent(root.transform, worldPositionStays: false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            RemovePhysics(root);
            int quieted = RemoveBehaviours(root, ownNamespacePrefix, out string quietedNames, out string survivors);

            if (survivors.Length > 0)
            {
                // Fail closed. A script this build will not let us destroy is a
                // script that wakes the instant the figure is switched on, and
                // disabling it is not safety. So the candidate is REFUSED
                // exactly as a forbidden component is - the root is never
                // enabled, the finally below destroys it while it is still
                // dark, and the caller tries the next candidate. A companion
                // who does not appear is a disappointment; a companion who
                // wakes the game's own code inside himself is a defect.
                return Refuse(
                    log,
                    candidateName,
                    "it carries " + survivors + " inside its visual subtree and this build will not let it "
                    + "be destroyed, so the candidate was refused rather than switched on. Nothing was "
                    + "enabled");
            }

            // Nothing inside can run any more, so it is safe to switch on.
            root.SetActive(true);

            if (quieted > 0)
            {
                log?.Invoke(quieted
                    + " of the source character's own script(s) were taken out of the body before it was "
                    + "ever enabled: " + quietedNames + ".");
            }

            GameObject built = root;
            root = null;
            return NpcPresentationFigure.Built(built, animator, helmet, rightHand, body, quieted, quietedNames);
        }
        catch (Exception exception)
        {
            return Refuse(
                log, candidateName, "building a local visual from it threw " + exception.GetType().Name);
        }
        finally
        {
            if (root != null)
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            if (clone != null)
            {
                // Whatever is left holds the player, network view, character
                // and AI components. It never woke, and now it never will.
                UnityEngine.Object.DestroyImmediate(clone);
            }

            UnityEngine.Object.DestroyImmediate(holder);
        }
    }

    private static Transform? InsideOrNull(Transform? joint, GameObject visual) =>
        joint != null && joint.IsChildOf(visual.transform) ? joint : null;

    private static NpcPresentationFigure Refuse(Action<string>? log, string candidateName, string reason)
    {
        string sentence = "\"" + candidateName + "\": " + reason + ". Trying the next candidate.";
        log?.Invoke(sentence);
        return NpcPresentationFigure.Refused(sentence);
    }

    /// <summary>Whether a component's type name is one whose presence inside an
    /// extracted subtree invalidates the whole extraction.
    ///
    /// Matched by name rather than by type so a build that does not have one of
    /// these cannot fail the check by its absence, and written as a method
    /// rather than a list so the set is in one place with no way to add to it
    /// quietly.</summary>
    private static bool IsForbiddenComponentName(string name)
    {
        switch (name)
        {
            case "ZNetView":
            case "ZSyncAnimation":
            case "Player":
            case "Character":
            case "Humanoid":
            case "BaseAI":
            case "MonsterAI":
            case "AnimalAI":
            case "NpcTalk":
            case "Tameable":
                return true;
            default:
                return false;
        }
    }

    private static bool ContainsForbiddenComponent(GameObject subtree, out string offender)
    {
        offender = string.Empty;
        foreach (Component component in subtree.GetComponentsInChildren<Component>(includeInactive: true))
        {
            if (component == null)
            {
                continue;
            }

            string name = component.GetType().Name;
            if (IsForbiddenComponentName(name))
            {
                offender = name;
                return true;
            }
        }

        return false;
    }

    private static void RemovePhysics(GameObject subtree)
    {
        foreach (Collider collider in subtree.GetComponentsInChildren<Collider>(includeInactive: true))
        {
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        foreach (Rigidbody body in subtree.GetComponentsInChildren<Rigidbody>(includeInactive: true))
        {
            if (body != null)
            {
                UnityEngine.Object.DestroyImmediate(body);
            }
        }
    }

    /// <summary>Takes the source character's own scripts out of a subtree that
    /// has not been switched on yet, and says what went.
    ///
    /// Every component here is destroyed while its object is still inactive, so
    /// none of it has run and none of it can have registered itself anywhere.
    /// Stripping removes something that already woke; this removes something
    /// that never will.
    ///
    /// The rule is a type test rather than a name list, because the problem is
    /// not any one class: a script inside a character's visual subtree is game
    /// code written for a live character, and the refusal above has just
    /// guaranteed this figure has no character, no network view and no AI
    /// anywhere above it, so that code has nothing to be written for.
    ///
    /// Nothing that draws the figure is one of these: the animator, the
    /// renderers, the level-of-detail group and the transforms are all built-in
    /// components, so this cannot take away the body, the rig, the mesh or the
    /// animation.
    ///
    /// Two passes, because a destroy is refused while a component declaring a
    /// dependency on it is still present, and the enumeration order is the
    /// prefab's rather than the dependency's. Anything the engine refuses twice
    /// - which it does by writing to the log and carrying on rather than by
    /// throwing - is disabled and named in <paramref name="survivors"/>, and
    /// every caller refuses on that rather than carrying on over the top. The
    /// count covers both halves, so a build that refused every one still says
    /// so out loud rather than looking like a build that found nothing.
    /// </summary>
    private static int RemoveBehaviours(
        GameObject subtree, string ownNamespacePrefix, out string names, out string survivors)
    {
        var removed = new List<string>();
        var refused = new List<string>();
        bool skipOwn = !string.IsNullOrEmpty(ownNamespacePrefix);

        for (int pass = 0; pass < 2; pass++)
        {
            foreach (MonoBehaviour script in subtree.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
            {
                if (script == null)
                {
                    continue;
                }

                Type type = script.GetType();
                if (skipOwn && type.Namespace != null
                    && type.Namespace.StartsWith(ownNamespacePrefix, StringComparison.Ordinal))
                {
                    // The caller's own, put on a figure it owns, on purpose.
                    continue;
                }

                string name = type.Name;
                UnityEngine.Object.DestroyImmediate(script);
                if (script == null)
                {
                    if (!removed.Contains(name))
                    {
                        removed.Add(name);
                    }

                    refused.Remove(name);
                    continue;
                }

                script.enabled = false;
                if (!refused.Contains(name) && !removed.Contains(name))
                {
                    refused.Add(name);
                }
            }
        }

        names = Join(removed, refused);
        survivors = refused.Count == 0 ? string.Empty : string.Join(", ", refused.ToArray());
        return removed.Count + refused.Count;
    }

    private static string Join(List<string> removed, List<string> refused)
    {
        if (removed.Count == 0 && refused.Count == 0)
        {
            return string.Empty;
        }

        var all = new List<string>(removed);
        foreach (string name in refused)
        {
            all.Add(name + " (could not be destroyed)");
        }

        return string.Join(", ", all.ToArray());
    }
}
