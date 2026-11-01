# Gunnar real-cart recruitment (#460): source handoff

GunnarRecruitment owns one in-memory, scope/epoch-bound selection and a random
single-use confirmation token. Prepare requires IntroPending (or a later explicit
welcome after Skipped), the existing strict hauling authority and a positively
usable, named, explicitly selected CartKey. Confirm checks the exact key, current
scope/epoch and token, then rechecks CURRENT cart/authority facts. Only a durable
recorded recruitment returns Recruited. A save refusal consumes the confirmation
but grants nothing; the player selects and confirms again. No nearest-cart guess,
ZDOID sidecar, cart write, lease, job start or fabricated tutorial cargo exists.

Owner decision, 2026-11-01: "Require hauling opt-in and host authority."
Recruitment requires both at selection AND confirmation. The existing
existingHaulingAuthorityGranted input must come from the real worker-authority
verdict, including its existing no-peers rule, not UI visibility or companion
presence. Recruitment still starts no job and bypasses no custody/lease check.
The implementation already enforces this rule; the owner decision confirms it.

Lead-owned native/panel wiring remains required: use the existing pointed-cart
selection and CartKey world-load epoch. Build a GunnarCartChoice from actual
kind/load/reach/occupancy/ownership/lease facts. Unknown means Unknown, never Usable.
Show prompt.Label on the confirmation surface and retain prompt.Token only in
memory. At confirmation resolve THAT key freshly; do not use a newly hovered or
nearest cart. Call Forget on cancel, logout, world/character switch. No cart
assignment or hauling mutation is authorized by this story policy.

On Recruited show gunnar.complete AFTER the save. An optional first-delivery offer
is intentionally omitted: no trustworthy player-owned cargo signal is supplied
by this leaf. All actual jobs stay under the existing cart-lease, authority,
worker-body and custody checks. Missing/destroyed/unloaded/in-use/unauthorized
carts retain exact work NeedsAttention handling there and never undo recruitment.

No active worker/runtime/panel/localization/validator files were edited. The
existing #317 assign/confirm prompt integration and real-game evidence remain
OPEN. The recruitment authority decision is resolved as stated above. The flow is not yet reachable in the installed game.

Automated: exact named cart confirmation, every unavailable check, post-prompt
in-use/unauthorized/leased refusal, two pending assignments, changed identity,
world epoch, character switch, both authority checks, durable completion, cart
loss after recruitment, real save refusal and consumed duplicate confirmations.
OWNER GO-AROUND PENDING: native selection/labels, loaded and occupied carts,
multiplayer authority, relog reselection, no cart mutation and preserved cargo.
