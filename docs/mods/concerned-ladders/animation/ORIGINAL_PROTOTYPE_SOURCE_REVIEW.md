# Independent prototype source review — 2026-10-26

Status: source corrections reviewed; licensed compilation and game evidence pending. This is an internal original-animation checkpoint, not a fitted loop or finished build approval.

Read-only reviewers: `/root/final_piece_review` (contact policy), `/root/final_runtime_review` (native API/lifecycle). Neither edited source, built or operated the game in this review.

Resolved findings:
- Repeated partial-stroke holds could ratchet contacts ahead/behind completed travel. Actual-body support thresholds and endpoint settlement now preserve planted contacts while waiting for motor travel. New repeated-hold, partial-settle/reverse, exact-boundary and partition-equivalence regressions passed.
- Cached Player.InAttack could miss hammer onset between Publish and IK. The IK boundary now reads current AND transitioning next Animator states directly for inspected native tags. No cached fields or input are changed.
- Dispose could retain its presenter/global hook when no later IK arrived. Pending numeric leases now transfer to a rig-local cleanup continuation; the presenter clears global registration immediately. The cleanup continuation is released by a legal IK pass or rig destruction; a disabled rig does not block a new Install.
- First cleanup scheduling could leave a zero-count hook after Patch/AddComponent failure. Scheduling now publishes the hook after successful Patch and rolls back zero-count failure while retaining existing continuations. The reviewer verified this correction in source.

Review confirmed no player-root, rigidbody, capsule, gravity, input, stamina, XP or equipment writes. IK/body getters/setters occur only in the native IK path. Established pose/contact paths have no obvious per-frame collection allocation. Required native APIs were inspected; GetRightItem accessibility is not used by the final onset check.

Remaining native verification: actual layer0 callback/order, rig/palm/sole fit, state-onset race, End and disabled/destroyed/re-enabled-rig cleanup, failed-schedule/retry, every transition/action/equipment variant, observer endpoints and exact package identity. Source review cannot pass those rows.
