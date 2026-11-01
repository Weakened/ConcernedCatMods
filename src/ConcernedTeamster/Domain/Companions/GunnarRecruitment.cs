using System;
using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;

internal enum GunnarCartCheck { Unknown, Usable, WrongKind, Unloaded, Destroyed, InUse, Unauthorized, OutOfReach, Leased }
internal readonly struct GunnarCartChoice
{
    public GunnarCartChoice(CartKey key, string label, GunnarCartCheck check)
    { Key=key; Label=label; Check=check; }
    public CartKey Key { get; }
    public string Label { get; }
    public GunnarCartCheck Check { get; }
}
internal sealed class GunnarRecruitmentPrompt
{
    internal GunnarRecruitmentPrompt(Guid token, CartKey cart, CompanionScope scope, string label)
    { Token=token; Cart=cart; Scope=scope; Label=label; }
    public Guid Token { get; }
    public string Label { get; }
    internal CartKey Cart { get; }
    internal CompanionScope Scope { get; }
}
internal enum GunnarRecruitmentResult { Unavailable, NeedsIntroduction, AuthorityRequired, NeedsSelection,
    PendingConfirmation, Busy, Stale, CartUnavailable, NotRecorded, Recruited, AlreadyRecruited }

/// <summary>One explicitly selected in-session cart and one named confirmation.
/// No cart lease, mutation, persisted native identity, worker start or tutorial cargo.
/// The owner/lead authority decision is pending: require the existing strict
/// hauling opt-in + host authority until a wider read-only rule is approved.</summary>
internal sealed class GunnarRecruitment
{
    private readonly object _gate=new();
    private GunnarRecruitmentPrompt? _pending;
    public GunnarRecruitmentResult Prepare(GunnarCompanionRecord? record, Guid epoch,
        GunnarCartChoice choice, bool existingHaulingAuthorityGranted, out GunnarRecruitmentPrompt? prompt)
    {
        lock (_gate)
        {
            prompt=null;
            GunnarRecruitmentResult eligibility=Eligibility(record,epoch,choice,existingHaulingAuthorityGranted);
            if (eligibility!=GunnarRecruitmentResult.PendingConfirmation) return eligibility;
            if (_pending!=null) return GunnarRecruitmentResult.Busy;
            _pending=new GunnarRecruitmentPrompt(Guid.NewGuid(),choice.Key,record!.Scope,choice.Label);
            prompt=_pending; return GunnarRecruitmentResult.PendingConfirmation;
        }
    }
    public GunnarRecruitmentResult Confirm(GunnarCompanionRecord? record, Guid epoch, Guid token,
        GunnarCartChoice freshExactCart, bool existingHaulingAuthorityGranted)
    {
        lock (_gate)
        {
            if (_pending==null || token==Guid.Empty || _pending.Token!=token || record==null ||
                !_pending.Scope.Equals(record.Scope) || !_pending.Cart.IsFromEpoch(epoch) ||
                !_pending.Cart.Equals(freshExactCart.Key))
            { _pending=null; return GunnarRecruitmentResult.Stale; }
            GunnarRecruitmentResult eligible=Eligibility(record,epoch,freshExactCart,existingHaulingAuthorityGranted);
            if (eligible!=GunnarRecruitmentResult.PendingConfirmation) { _pending=null; return eligible; }
            _pending=null; // consume before saving/announcing; failure requires explicit selection again
            return record.TryRecruitAfterAcceptedCartAssignment(true)
                ? GunnarRecruitmentResult.Recruited : GunnarRecruitmentResult.NotRecorded;
        }
    }
    public void Forget() { lock (_gate) { _pending=null; } }
    private static GunnarRecruitmentResult Eligibility(GunnarCompanionRecord? record,Guid epoch,
        GunnarCartChoice choice,bool authority)
    {
        if (record==null) return GunnarRecruitmentResult.Unavailable;
        if (record.IsRecruited) return GunnarRecruitmentResult.AlreadyRecruited;
        if (record.State!=QuestState.IntroPending && record.State!=QuestState.Skipped)
            return GunnarRecruitmentResult.NeedsIntroduction;
        if (!authority) return GunnarRecruitmentResult.AuthorityRequired;
        if (epoch==Guid.Empty || !choice.Key.IsFromEpoch(epoch) || string.IsNullOrWhiteSpace(choice.Label))
            return GunnarRecruitmentResult.NeedsSelection;
        if (choice.Check!=GunnarCartCheck.Usable) return GunnarRecruitmentResult.CartUnavailable;
        return GunnarRecruitmentResult.PendingConfirmation;
    }
}
