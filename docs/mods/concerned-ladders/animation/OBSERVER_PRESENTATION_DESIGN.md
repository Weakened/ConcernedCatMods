# Observer presentation signal — design candidate, not implemented

2026-10-26. Prepared during mandatory native-baseline capture; no replacement animation or transport code is authored here. This proposal is for independent design review and lead ownership agreement. It does not establish multiplayer support or approve a protocol.

## Why a signal is needed

Claude's accepted-state presentation interface currently produces local frames only. Native replicated position/facing does not identify accepted attachment, hold versus ordinary standing/falling, slide, transition completion or hammer ownership. The rejected proximity-inference presentation is not an accepted observer implementation. Native ZSyncAnimation syncs configured parameters, speed and triggers; it does not replicate original clips, graphs or procedural bones.

## API findings from read-only installed assembly inspection

Installed assembly_valheim.dll SHA256:
94998885b103d6e5f5480123eaee0a093e82af2ddeeee37536c15bf555f24f3a.

- ZNetView.Register<T>(string, Action<long,T>), InvokeRPC and Unregister exist.
- ZRoutedRpc.RPC_RoutedRPC reads senderPeerID from its received package and forwards it; the inspected method does not authenticate that field against its incoming ZRpc. Therefore comparing the callback's long sender to ZDO.GetOwner alone does not establish an actual actor-owner connection.
- ZRpc.Register<ZPackage>(string, Action<ZRpc,ZPackage>) passes the actual receiving connection. ZNet.GetPeers(), peer.m_rpc, m_uid, m_characterID, IsReady(), GetServerPeer() and ZDO.GetOwner are available.
- ZNet.GetPeer(ZRpc) is private; do not invent a public call.
- Object-targeted routed messages can be dropped before the scene instance exists; they are not persistent late-join state.
- The client binary's IsDedicated implementation is not evidence about the server binary.

These are API/source facts, not tested network behavior.

## Candidate design

Use unique, versioned, product-local direct per-peer ZRpc methods for a tiny presentation handshake, owner Submit and server Relay. Server binds actual incoming ZRpc by reference to a ready connected peer, then requires peer character ID and current ZDO owner to match the actor. Client accepts relay only from its actual server connection. Local host uses a separately checked local-owner path; never fabricate a peer identity.

Submit is incapable of invoking gameplay: no inputs, forces/velocities to apply, collision requests, costs or XP. Its bounded self-contained snapshot carries session/generation/sequence, accepted sample/travel, motion/face, rung basis/geometry, discontinuity and geometry revisions, accepted transition progress and action ownership. End closes the session immediately. No persistent ZDO keys, game saves or per-frame motor RPC.

Provisional limits to finalize against the actual struct: fixed schema <=512 bytes, no strings/variable lists, exact version/size and no trailing data, enum/flag/finite and geometry bounds before allocating state. Reuse existing run/dimension limits; reject teleport-sized changes, invalid basis, impossible transition values and stale sequences. Session/generation are scoped to actual connection/world lifetime, not trusted from a previous peer.

Coalesce ordinary presentation updates to at most 10Hz; hold heartbeat provisionally 2Hz. State/action changes have a bounded burst budget and End has an immediate reserved path. Local animation still consumes every accepted local sample. These rates are proposed, not measured smoothness: a fast two-rung cycle can be .274s, so contact-preserving interpolation must be tested.

Use receiver monotonic time and original owner-update age. Provisional expiry 1.5s; clear earlier on End, death, unload, disconnect, changed owner or world shutdown. Ended sessions cannot reopen from delayed packets. Keep bounded tombstones and latest snapshot cache only in memory.

On late-join handshake, relay only fresh cached snapshots with remaining lifetime, not a new lease for stale owner data. Periodic complete snapshots recover scene-instantiation races without unbounded queues. Reconnect gets a new server-assigned connection generation.

Dedicated relay code must not load graphics, bind avatars or construct a presenter. Unsupported protocol, absent actor, stale data or changed ownership yields native presentation and cleanup. It does not change compatibility policy, kick players or acquire movement authority.

## Unresolved design and verification work

- Agree transport/schema ownership with Claude and obtain uninvolved review before integration.
- Finalize exact payload and numeric bounds, connection generation, End ordering and bounded registration lifecycle.
- Test malformed/truncated/oversized/nonfinite/unknown/reordered/replayed packets, stale caches, missing actor and actor-owner mismatch.
- Define synchronization with native transform interpolation. Owner travel can be ahead of the displayed remote body; presentation must never move the remote actor to compensate or extrapolate climbing indefinitely. Tolerances need observed latency/rig evidence.
- Test real host/client, both directions, fast/slide/hold, catch/top-exit/Jump/Drop/hammer, late join/reconnect, removal/ownership change and stale cleanup.
- Inspect/test the actual dedicated server binary.
- No network source-only claim becomes P03/R27/R28/R43 PASS. Until a reviewed implementation and real endpoints exist, observer rows remain BLOCKED.

This is a narrow presentation option for review, not permission to build a generic networking framework or delay unrelated gameplay repairs.