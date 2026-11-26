# AnimalTown multiplayer implementation

The [26 November online play PoC backlog](online-play-poc.md) is the current
execution index. It brings chat, eligible guest jobs and resident mail into
the PoC, selects Cloudflare HTTPS/WSS, and excludes cloned-card reconciliation,
online attachments and guest tool actions. The broader release requirements
below remain context; the PoC document defines which apply to this milestone.

The local memory-card save is the source of truth. AnimalTown's hosted service
runs a synchronized copy for online visits while the console is on or off.
The first release supports one local villager and up to two guests. Player-run
servers are outside the agreed design. This document records the current
implementation and broader release work. It updates the multiplayer decisions
in the [earlier networking brief](https://github.com/capz/AnimalTown/pull/162).
See the [technical design](multiplayer-technical-design.md) for the proposed
transport, security requirements, and DOL-015 performance targets.

## Implemented and available for staging

- Gate Travel can start short-code linking in a GameCube staging build.
  Build the ISO and Depot candidates with `./tools/build-game.ps1 -OnlinePairing`,
  or set `AnimalTownOnlinePairing=true` for a direct project build. Standard
  builds leave this path disabled so the TLS and broadband stack can be
  omitted from the game executable while DOL-015 memory use is qualified.
  Pairing leaves the regular town renderer before configuring HTTPS. Device
  access is bound to the account and town, expires quickly, and can be rotated
  or revoked.
  Two verified memory-card records preserve the refresh credential and pending
  rotation ID across interrupted writes. The browser approval flow uses a
  separate account credential.
- The server validates an initial split-save checkpoint, preserves inventory
  and balances, and keeps an immutable copy. A resumable upload writes the
  remaining data in one HTTP request and saves verified offsets at bounded
  intervals. The device, account, town, fingerprint, and expected length are
  checked before accepting the checkpoint. A subsequent offline upload can
  advance an unchanged hosted town while its gate is closed. The server
  refuses it if hosted activity or open travel requires reconciliation.
  The card repository can export the verified canonical checkpoint with one
  record buffer at a time. The console still needs to connect that export to
  the upload endpoint and publish a downloaded checkpoint back to the card.
- Gate endpoints keep up to two guest reservations, enforce mutual friendship
  and blocks, and renew each guest seat for 60 seconds after a heartbeat.
  Closing the gate revokes its generation and invalidates old gameplay tickets.
  Removing one guest reserves the removal until the gate closes and reopens.
- The experimental TLS listener accepts short-lived, single-use tickets and
  negotiates `animaltown/1`. It validates and relays outdoor movement for
  up to three humans. It applies close, block, and review checks during the
  session. The client protocol pump uses caller-owned TLS buffers and fixed
  per-update I/O limits. The GameCube adapter builds with the experimental
  SDK property `AnimalTownExperimentalOnlineTls=true` and local SDK packages
  containing `TlsStream`. Normal builds keep the released package pins.
- Staging placement commands are bound to the current live connection, visit,
  and villager. A guest can place from their own pocket, then move or pick up
  the same pending placement. The review service persists each keep or koban
  decision and uses a renewable pause lease. Imported towns are protected from
  older guest item and shop paths while the card settlement path is incomplete.

The gameplay listener is disabled unless configured with
`OnlineGameplay__Port`, `OnlineGameplay__Certificate`, and
`OnlineGameplay__CertificateKey`. These are server settings. The console's
optional deployment files are `online-origin.txt`, containing an HTTPS origin,
and `online-roots.pem`, containing its certificate trust roots. Staging placement
commands also require `Multiplayer__EnablePlacements=true`. Keep this flag off
for player saves until card inventory settlement is complete.

The streamed upload starts with
`POST /v1/towns/{account}/checkpoint-uploads`. It accepts the length and SHA-256
of the canonical wire data. Include `parentRevision` for a later offline
checkpoint over the current shared revision. Send the remaining binary data to the returned
upload ID with one `PUT` at the verified offset. The wire data consists of the
native split-save commit and seven records in codec order. Each record has a
four-byte little-endian length prefix. The aggregate limit is 4 MiB.

## Remaining in the first playable release

1. Integrate the experimental TLS stream into a playable visit screen. Qualify
   Nintendo's DOL-015 with the released SDK. Measure frame time, memory, and
   recovery before setting shipping transport rates and resource budgets.
   Connect gate controls, friend selection, public snapshots, remote actor
   rendering, room transitions, and reconnect with replay.
2. Route guest gameplay through the hosted session. A guest can place only their
   own item and can move, rotate, or pick up only their own placements. Picking
   up a placement returns the item to their pockets and cancels its review
   entry. Keep stable identities as the town's item arrays compact. The
   hosted session must enforce these permissions across commands and reconnects.
3. Wire the console review screen to the durable placement decisions. Show
   each guest item's final placement. Keeping it transfers it to the town. Undoing it forfeits the
   guest's ownership and sends it to the koban. Clear all holes dug by one
   selected guest during a visit with one decision. Game-spawned pickups,
   game-buried item recoveries, and purchases remain permanent.
4. Trigger review when a local villager starts after unattended visits or as
   they leave after shared play. Guests wait during active review. If interrupted,
   they resume play immediately and committed decisions stand. The next local
   villager sees the remaining placements at their latest positions. Entries
   picked back up disappear. Guests can keep visiting after the local leaves.
5. Reconcile the selected memory-card copy and the hosted copy against the last
   shared checkpoint. Trust validated offline gains. Commit compatible edits
   and let the local player decide conflicts. Send displaced local placements
   to the koban if the guest placement wins. If two local card copies diverge,
   let the player select the one to continue from.
6. Integrate shop receipts and physical sale stands with sync. Both a local and
   a guest can keep a purchase from the same game-stocked slot across diverged
   copies, and the slot remains sold out after sync. Place a listed player item
   in escrow. Keep it unavailable offline until sale or cancellation is settled.
7. Run a complete visit with one local and two guests. Exercise removal, gate
   closure, a dropped connection, interrupted review, offline conflict choice,
   and card save and reload. Verify that retries preserve purchases, transfers,
   and review decisions without duplicating their effects.

## Features included in the agreed PoC

These were previously deferred to a subsequent phase. They are now open work
in the [PoC index](online-play-poc.md#indexed-implementation-work):

- **Live text chat.** Reuse the GameCube keyboard and deliver across the whole
  town, with rate limits, mute/block enforcement and report evidence.
- **Guest villager jobs.** Run eligible visited-town jobs in the hosted session.
  Hide jobs requiring excluded actions. Earned items and Bells travel home;
  simultaneous claims, reconnect and settlement must not duplicate rewards.
- **Guest mail.** Reuse the letter composer for visited-town residents only,
  without attachments. Preserve drafts and make delivery safe to retry.
