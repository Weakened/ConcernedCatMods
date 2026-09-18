# Gunnar and Thorstein: the player's guide

How to have Thorstein collect loose stones and branches, have Gunnar pull a cart, and have the two of them work one
order together. Written for the build described in `SPEC.md`; the contract behind it is `CONTRACTS.md` (revision C4).

**What this slice does:** natural loose stones and branches only, one order for Thorstein and one haul for Gunnar, on
your own world, as the host, with nobody else connected. Everything else — mining, felling, crops, chests you did not
choose — is out of scope and is refused rather than guessed at.

---

## 1. Turning it on

Both halves are off until you turn them on.

| Mod | Setting | Default | What it does |
|---|---|---|---|
| Concerned Foreman | `Settlement / SettlementRuntimeEnabled` | off | Lets Thorstein work: orders, pickups, deliveries. |
| Concerned Teamster | `Workers / GunnarHaulingEnabled` | off | Lets Gunnar take a cart you assign him. |

Work runs only when **all** of these are true, and says so when they are not: you are in a loaded world, you are the
host (single player counts), it is not a dedicated server, and no other player is connected. If somebody joins, the
work stops where it is — Gunnar keeps holding the cart if letting go there would let it roll.

Thorstein also needs to be recruited and to be holding the axe and hammer he was issued.

## 2. Give Gunnar a cart

1. Stand near the cart and **look at it**.
2. Open Teamster's **Gunnar** panel (the button at the right edge) and press **Assign the cart I am looking at**.
3. The panel names the cart it means and what is in it. Press the button again to confirm.

A cart is never chosen for you by being nearest. Assigning is refused, with the reason, for a cart somebody is using,
one with the parking brake on, one this game is not in charge of, one that is not upright, one already assigned, or
one you are too far from. **Anything already in the cart stays yours**: it is written down as the cart's existing
cargo before the order puts anything in, and it is never counted, delivered or refunded as gathered material.

**Release the cart** gives it back to you. **Stop here** stops Gunnar where he is; **Stop, detach and park** also has
him let go, on ground flat enough to leave a loaded cart on.

## 3. Give Thorstein an order

1. Open Foreman's **Thorstein** panel (the button at the left edge).
2. Type how much **Stone** and **Wood** you want. These are *newly delivered* amounts: what is already in the chest
   does not count toward them.
3. Press **Preview area** to see where he will work — your harvest area if you have marked one, otherwise a 30 m
   circle around your respawn point. It never follows you around.
4. Choose where it goes: **look at a chest**, or tick **Hold it for me instead of a chest**.
5. Tick **With Gunnar and his cart** if you want them to work together. The panel tells you when Gunnar cannot join
   (not installed, too old, no cart assigned, busy, or not allowed here). A hold-for-player order is always solo.
6. Press **Survey and start**. He looks the area over first, and only then starts.

## 4. Watching it

The panel shows, for each resource: how much is delivered, in the cart, carried, or still lying where he picked it,
and how much is still to collect. Every unit is in exactly one of those. An estimate from the sources he picked out is
shown as an estimate and never counts as progress. In hold-for-player mode the amount reads **held for you**, not
delivered, until you take it.

It also shows one line for what is happening, and at most one thing that needs you. **Pause** holds everything where
it is (Gunnar stops too). **Resume** picks it up from where it stopped. **Cancel** ends the order: delivered material
stays delivered, what is in the cart stays in the cart, and what Thorstein carries stays with him — cancelling is not
a refund. **Release the cart** ends the haul and has Gunnar park the cart; the cart stays assigned to him.

Together they work like this: Gunnar brings the cart to a meeting point in the work area while Thorstein collects;
Thorstein walks to the cart and loads it while Gunnar holds it still; Gunnar hauls it to your chest with Thorstein
alongside; the load goes into the chest (Thorstein carries it the last stretch when the cart cannot get close); and
they come back for more until the order is done.

## 5. Confirming a delivery

Open the chest and count. What the panel calls delivered is what actually went in, once. If the chest fills up, the
order pauses and the rest waits in the cart or in Thorstein's pack — nothing is thrown away, and no other chest is
used instead.

If something could not be confirmed — a transfer whose outcome was unclear, or a cart whose contents no longer match
the record — the order stops and says so rather than guessing. Nothing is credited, replayed or written off on its
own; `cf_settle` and the panel tell you what is waiting for your answer.

## 6. After a reload

- **Cart assignments do not survive a reload.** Assign the cart to Gunnar again.
- **An order comes back paused.** The area and the chest were noted for the world load that has gone, so confirm them
  again (look at the chest, check the area) before it continues.
- **What was in the cart is still in the cart.** If the record cannot be matched to it any more, the order asks you to
  confirm what happened instead of adjusting itself.
- Thorstein's own pack — carried Stone and Wood, and his tools — is saved with the world, so it survives a reload.

## 7. Before you uninstall

- **Concerned Foreman:** release everything first. Have Thorstein deliver or hand over what he carries and return his
  tools. A worker body still holding items is deleted by the game when the mod that defines it is gone, and those
  items go with it.
- **Concerned Teamster:** release the cart from Gunnar. Orders that were running with him carry on solo after you
  restart them; nothing is stranded in the cart except what is physically in it, which is yours.
- Neither mod writes anything into your carts, chests or the world save; Foreman's record is its own file under
  `BepInEx/config/ConcernedCatMods/ConcernedForeman/`.

## 8. When it stops and says something

| What it says | What to do |
|---|---|
| No cart is assigned to Gunnar | Assign one in Teamster's Gunnar panel. |
| Gunnar needs your attention | Open his panel: it names the one thing (a brake, a tipped cart, no cart-safe route). |
| The two of them waited too long to meet | The way between them is blocked; move the cart or pick a clearer spot and resume. |
| The chest is full | Empty it; what is waiting goes in when you resume. |
| The chest cannot be used / was chosen in an earlier world load | Look at it and choose it again. |
| The record and what is actually there disagree | Answer the question it asks (`cf_settle resolve …`); nothing moves until you do. |
| Someone else is connected | Work waits until you are alone again. |

---

## 9. Gate D live checklist (for the lead)

**Status: pending — no part of this has been run in game.** Each row is observed in one disposable world, on a
disposable character, in the dedicated profile, with the build and profile recorded in `EVIDENCE.md`.

Set-up:
- [ ] pending — a disposable world; Foreman `SettlementRuntimeEnabled` on, Teamster `GunnarHaulingEnabled` on.
- [ ] pending — Thorstein recruited and holding his issued axe and hammer; Gunnar's body present.
- [ ] pending — a documented patch of loose stones and branches inside the work area (count them before starting).
- [ ] pending — a cart with **some cargo already in it** (write down what), assigned to Gunnar.
- [ ] pending — a chest with room, chosen by looking at it; note what is already in it.

The order (20 Stone + 30 Wood, with Gunnar):
- [ ] pending — the panel previews the area, accepts the order, and both of them are named as working (and Hulgi only
      if he is really there).
- [ ] pending — Gunnar brings the cart to a meeting point in the area; Thorstein collects and walks to the cart.
- [ ] pending — the cart is held still while it is loaded; the cart does not move during a transfer.
- [ ] pending — the loaded cart is hauled to the chest and unloaded; the chest gains exactly what the panel says.
- [ ] pending — **a second trip** happens when one load cannot finish the order (use a smaller cart or a bigger order).
- [ ] pending — the order completes; Gunnar detaches and parks; the panel reads done.
- [ ] pending — the cart's **pre-existing cargo is untouched** and was never counted as delivered.
- [ ] pending — the chest's delivered counts equal the order, counted once.

Negative cases, each with nothing duplicated, nothing lost and nothing teleported:
- [ ] pending — **partial collection:** stop the sources short; the order pauses honestly with what it has.
- [ ] pending — **provider loss:** disable Teamster's hauling (or unload Gunnar) mid-haul; the order reconciles the
      cart and pauses. Material in the cart stays in the cart.
- [ ] pending — **cart loss:** destroy or take the cart mid-haul; the order asks for your confirmation and credits
      nothing.
- [ ] pending — **rendezvous timeout:** block the way between them; after the timeout Gunnar stops and the order says
      why.
- [ ] pending — **full destination:** fill the chest; the order pauses with the rest retained.
- [ ] pending — **player takeover:** grab the cart yourself mid-haul; Gunnar lets go and the order stops.
- [ ] pending — **reload:** save and reload mid-order; the order comes back paused and asks for the cart and chest
      again.
- [ ] pending — a second player joins: work stops with that reason, and no cart is left rolling.
