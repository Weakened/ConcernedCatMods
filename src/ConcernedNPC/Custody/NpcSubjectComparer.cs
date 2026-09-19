using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>How a reservation book tells two subjects apart.
///
/// <b>Why this exists at all.</b> A reservation book is a dictionary, and a
/// dictionary over a reference type that does not override equality compares
/// references. Almost every subject a role will reserve is a wrapper it re-reads
/// each tick - a container's own permission doc requires exactly that ("re-read
/// before every use; never stored and acted on later") - so two ticks produce
/// two objects for one chest, and reference equality grants both of them. Two
/// NPCs then withdraw from the same chest, which is the one thing the book
/// promises cannot happen, and nothing looks wrong afterwards because releasing
/// still clears both.
///
/// So the book refuses to be built without an answer to this question, and
/// <see cref="ByKey{TSubject}"/> is the answer almost every role wants.</summary>
internal static class NpcSubjectComparer
{
    /// <summary>Compares subjects by a name the role gives them - a container's
    /// key, a source's id - ordinally, with a null or empty name treated as its
    /// own value rather than as a match for every other blank.
    ///
    /// The key must be stable for as long as the reservation is held, which for
    /// a world object means stable within one world load. That is what the
    /// book's epoch is for; this only has to be stable, not durable.</summary>
    internal static IEqualityComparer<TSubject> ByKey<TSubject>(Func<TSubject, string?> key)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        return new KeyComparer<TSubject>(key);
    }

    private sealed class KeyComparer<TSubject> : IEqualityComparer<TSubject>
    {
        private readonly Func<TSubject, string?> _key;

        internal KeyComparer(Func<TSubject, string?> key)
        {
            _key = key;
        }

        public bool Equals(TSubject? left, TSubject? right)
        {
            if (left == null || right == null)
            {
                return left == null && right == null;
            }

            // Both must be nameable, and by the same name. A subject that
            // cannot say what it is matches nothing - including another subject
            // that also cannot. Falling back to a shared blank would make every
            // broken wrapper the same chest, which is the failure this whole
            // type exists to prevent, arrived at from the other side.
            return TryName(left, out string leftName)
                && TryName(right, out string rightName)
                && string.Equals(leftName, rightName, StringComparison.Ordinal);
        }

        public int GetHashCode(TSubject subject) =>
            subject != null && TryName(subject, out string name)
                ? StringComparer.Ordinal.GetHashCode(name)
                : 0;

        private bool TryName(TSubject subject, out string name)
        {
            try
            {
                name = _key(subject) ?? string.Empty;
            }
            catch (Exception)
            {
                name = string.Empty;
            }

            return name.Length != 0;
        }
    }
}
