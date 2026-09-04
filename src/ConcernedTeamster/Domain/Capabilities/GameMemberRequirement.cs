using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Capabilities;

/// <summary>One game member an adapter depends on (CT-002). The owner type is
/// passed as a <see cref="Type"/> so adapters can resolve real game types by
/// name at runtime while tests substitute fake surfaces; a null owner means
/// the type itself was not found and the requirement can never verify.</summary>
public sealed class GameMemberRequirement
{
    public GameMemberRequirement(
        string ownerLabel,
        Type? ownerType,
        string memberName,
        GameMemberKind kind,
        Type? expectedType = null,
        IReadOnlyList<Type?>? parameterTypes = null)
    {
        OwnerLabel = ownerLabel;
        OwnerType = ownerType;
        MemberName = memberName;
        Kind = kind;
        ExpectedType = expectedType;
        ParameterTypes = parameterTypes;
    }

    /// <summary>Display name of the owning type for log lines; supplied by
    /// the adapter so this assembly-neutral core never names game types.</summary>
    public string OwnerLabel { get; }

    /// <summary>The type to inspect, or null when it could not be resolved.</summary>
    public Type? OwnerType { get; }

    public string MemberName { get; }

    public GameMemberKind Kind { get; }

    /// <summary>Field type for fields, return type for methods; null skips
    /// the type check (existence only).</summary>
    public Type? ExpectedType { get; }

    /// <summary>Exact parameter types for methods (null means parameterless).
    /// A null entry marks a parameter type that could not be resolved, which
    /// fails the requirement.</summary>
    public IReadOnlyList<Type?>? ParameterTypes { get; }

    /// <summary>The identity used in verified/missing reports: the caller's
    /// owner label joined to the member name, "SomeType.m_someMember".</summary>
    public string DisplayName => OwnerLabel + "." + MemberName;
}
