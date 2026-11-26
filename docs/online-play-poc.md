# Online play PoC: agreed scope and implementation backlog

Agreed on 26 November 2026. Source audit: `main` at
`e85171855f7fa6eb33c1773ef56aa74a37a3ce41`.

This is the execution index for the online play proof of concept. It records
the user's scope decisions and the work still needed; it does not claim that
the PoC is implemented or that hardware tests have passed. Its scope takes
precedence over the earlier phase labels in the
[networking brief](networking-implementation-plan.md),
[first-slice status](multiplayer-first-slice.md), and
[technical design](multiplayer-technical-design.md).

## Agreed player behavior

| Area | PoC requirement |
| --- | --- |
| Targets | Internet play on Dolphin and physical GameCube with Nintendo DOL-015. The user can run physical candidates and supply results. |
| Capacity | Retain the current design: one local player and up to two guests. Saved NPC visitors are separate from live human participants. |
| Hosting | Reuse AnimalTown's backend. Run it locally, either on the user's computer or in the development workspace, exposed through free Cloudflare Quick Tunnels. |
| Availability | Guests can visit and continue playing while the owner's console is off. The backend computer and tunnel must remain running. |
| Visit | Joining, movement, entering supported rooms, guest item placement, shopping, jobs, mail, and chat are included. |
| Placements | Retain the existing ownership rules: guests place from their own inventory and move, rotate, or retrieve their own pending placements. Existing town possessions remain protected. |
| Review | Support Keep in town and Send to koban. Resolve conflicts between normal offline progress and hosted changes through review. |
| Saves | Support existing player cards and safe return, synchronization, and card reload. Cloned or divergent copies of a card are outside this PoC. |
| Jobs | Offer only jobs available in the visited town and completable with the PoC's supported actions. Hide offers requiring excluded actions. Earned items and Bells travel home with the guest. |
| Mail | Address residents of the visited town using its resident directory. No friend/cross-town addressing and no attachments in online PoC mail. Reuse the composer and preserve drafts on failed sends. |
| Chat | Deliver live text to everyone participating in the town, including players in different rooms. Reuse the keyboard and existing text limits. |
| Portable items | Earned and legitimately gathered/caught items may travel home. This ownership rule does not add gathering or catching actions to the PoC. |

Guest fishing, bug catching, gathering, planting, digging and other tool actions
are excluded. Jobs cannot indirectly enable those actions. Hole creation and
grouped hole review therefore belong to later work. Keep existing offline
tools, mail attachments and memory-card travel working.

Existing review and security behavior remains the implementation baseline:
review on local entry after unattended visits and on exit after shared play;
pause guest commands during active review; resume guests after interruption;
retain committed decisions and review each remaining placement at its latest
position. Confirmed purchases and earned rewards survive review and retries.
Mutual-friend admission, blocks, removal, device revocation and selected-card
ownership remain enforced.

## Existing foundation to reuse

| Foundation | Source | Integration still needed |
| --- | --- | --- |
| Pairing, revocable device credentials and credential card banks | [Console pairing](../game/AnimalTown.GameCube/OnlinePairing.cs), [card credentials](../game/AnimalTown.GameCube/OnlineCredentialCard.cs), [server credentials](../server/AnimalTown.Server/DeviceCredentials.cs) | Playable online lifecycle, endpoint changes and recovery. |
| Verified canonical card export and bounded resumable imports | [Checkpoint export](../shared/AnimalTown.Domain/Online/TownCheckpointExport.cs), [upload endpoints](../server/AnimalTown.Server/CheckpointTransferEndpoints.cs) | Connect console export to upload; download, verify and publish reconciled checkpoints to cards. |
| Gate generation, two guest seats, friendship, blocks and heartbeats | [Visits](../server/AnimalTown.Server/Visits.cs) | Console controls, live availability and reconnect flow. |
| Binary framing, bounded pose pump and experimental TLS adapter | [Wire codec](../shared/AnimalTown.Domain/Online/OnlineWire.cs), [pose pump](../shared/AnimalTown.Domain/Online/OnlinePosePump.cs), [console connection](../game/AnimalTown.GameCube/OnlineGameplayConnection.cs) | WebSocket transport, reliable commands/events, snapshots, roster and remote rendering. |
| Staging movement listener and single-use gameplay tickets | [Live transport](../server/AnimalTown.Server/LiveTownTransport.cs) | WebSocket endpoint, persistent session fencing and authoritative in-scope simulation. |
| Stable guest-placement records and durable Keep/koban decisions | [Placement review](../server/AnimalTown.Server/PlacementReview.cs) | Live replication, console review, broader conflict handling and card settlement. |
| Atomic stock/listing purchases, receipts, grants and custody | [Town shop](../server/AnimalTown.Server/TownShop.cs), [API](../server/AnimalTown.Server/Program.cs), [town items](../server/AnimalTown.Server/TownItems.cs) | Route managed towns through the hosted command boundary and settle the home card. |
| Local chat, jobs, letters and save recovery | [Chat](../game/AnimalTown.GameCube/TownGame.Chat.cs), [job service](../shared/AnimalTown.Domain/Jobs/VillagerJobService.cs), [mail](../shared/AnimalTown.Domain/Jobs/VillagerJobService.Mail.cs), [save repository](../shared/AnimalTown.Domain/SaveRepository.cs) | Authenticated remote commands, per-participant state, delivery and receipts. Local mail currently rejects visitors. |

Standard game builds currently omit inactive networking. Pairing and raw TLS
are staging paths. Their existence is not evidence of a playable online visit.
Staging placement writes must stay disabled for player saves until inventory
and card settlement are complete.

## Cloudflare transport contract

Use one local ASP.NET service for HTTPS account/control requests and a secure
WebSocket gameplay endpoint, with PostgreSQL retaining state across restarts.
The development database must use persistent storage; the CI database in
`build/compose.yml` uses temporary storage and is not the PoC deployment.

Cloudflare Quick Tunnels provide a temporary `trycloudflare.com` hostname
without a purchased domain or Cloudflare account. Their hostnames change on
restart and they have no uptime guarantee. They do not support Server-Sent
Events. WebSockets are supported. See the official
[Quick Tunnel documentation](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/),
[WebSocket documentation](https://developers.cloudflare.com/network/websockets/),
and [protocol routing table](https://developers.cloudflare.com/tunnel/concepts/routing/).

The existing `animaltown/1` custom ALPN listener is not a connection that a
GameCube can send directly through a public HTTP tunnel. Cloudflare's arbitrary
TCP routes require a client-side connector. For this PoC, add HTTP/1.1 WebSocket
upgrade over certificate-validated TLS and carry the existing bounded binary
protocol in binary WebSocket messages. Use a versioned WebSocket subprotocol
such as `animaltown.v1`; do not negotiate the raw listener's ALPN with the edge.
Reuse backend rules rather than moving gameplay authority into Cloudflare.

Preserve hostname, certificate-chain and date checks. Trust the appropriate
public roots for the edge certificate. The local HTTP origin may bind to
loopback behind `cloudflared`; the public game connection is HTTPS/WSS.
Use the game's device authentication and single-use gameplay tickets; a
browser-only email challenge cannot authenticate the GameCube connection.

Keep the endpoint configurable for both ISO and standalone SD candidates.
Changing a tunnel hostname must not silently retarget stored credentials.
The initial PoC may require explicit reconfiguration and relinking, preserving
pending saves and settlements; seamless credential migration is not an exit
requirement. Ordinary reconnects to a still-running endpoint are required.

Qualify the WebSocket path on Dolphin and physical DOL-015 before treating it as
the selected working transport. Verify TLS compatibility with the actual SDK
pin and Cloudflare edge, upgrade handling, cancellation and long-lived traffic.

## Indexed implementation work

All rows are open. "Foundation" means source exists for part of the row, not
that its completion gate has passed. IDs are local backlog references, not
GitHub issue numbers.

| ID | Deliverable | Current state | Depends on | Completion gate |
| --- | --- | --- | --- | --- |
| POC-01 | Reproducible local backend and tunnel | API/container foundation | — | Start persistent PostgreSQL, backend and Quick Tunnel on either supported computer; document configuration, stop/restart, endpoint changes, secret handling and restore. Verify authenticated access through the public URL. |
| POC-02 | Bounded WSS transport | Raw TLS/pose foundation | POC-01 | Add console WebSocket framing and server upgrade endpoint. Validate upgrade/subprotocol, masking, fragmented messages, ping/pong, close and malformed/oversized input. Keep frame work, queues, message sizes and allocation bounded; preserve ticket and epoch checks. |
| POC-03 | Dolphin and DOL-015 transport spike | Hardware qualification missing | POC-02 | Prove API pairing, WSS authentication, movement samples, bulk transfer and recovery through Cloudflare on the actual target SDK. Record matched memory and frame-time measurements before enabling networking beside a dense town. |
| POC-04 | Playable visit lifecycle | Pairing/admission foundation | POC-03, POC-13 approval | Add gate/friend selection, connection states, owner plus two guests, unattended availability, removal and safe exit. Preserve the existing indefinitely open hosted gate without owner-console heartbeats and complete the playable client lifecycle. Keep destination state separate from the guest's home save. |
| POC-05 | Public snapshots, reliable events and reconnect | Codec/pose foundation | POC-02 | Add bounded recipient projections, stable snapshot watermarks, ordered durable events, replay cursors and resnapshot on gaps. Never expose another player's inventory, mail, credentials or raw card records. |
| POC-06 | Hosted command and simulation boundary | Movement and placement foundation | POC-05 | Route every enabled action through one town writer. Fence stale workers/connections; bind commands to authenticated participants and stable entity IDs. Commit mutation, receipt and replayable event together. Reject excluded commands server-side. Advance required NPC/job/mail state with server time. |
| POC-07 | Three live human actors and rooms | Outdoor pose foundation | POC-04, POC-05, POC-06 | Replicate appearance, roster, room and position; predict/reconcile local movement and interpolate remote actors. Validate portals and room visibility. Keep human slots separate from NPCs and card visitors; reuse models, textures and residency budgets. |
| POC-08 | Existing-card sync and guest return | Import/export/recovery foundation | POC-05, POC-06 | Reserve travel inventory/value before online use; reconcile one selected card lineage against the shared checkpoint. Merge compatible offline/hosted edits and send conflicts to review. Apply permanent receipts and portable rewards once. Publish/read back the new card bank before acknowledging settlement; never save the destination as the guest's home. |
| POC-09 | Placement and offline-conflict review | Durable placement decisions | POC-06, POC-08, POC-13 approval | Wire startup/exit triggers and Keep/koban choices. Guests wait during review, then resume on end/disconnect/lease expiry. Preserve decisions, latest positions, picked-up entry removal and custody capacity. Resolve overlapping offline/hosted placements without losing items. |
| POC-10 | Shopping and inventory settlement | Atomic commerce foundation | POC-06, POC-08 | Integrate existing stock/listing purchase paths with the active session. Keep confirmed purchases permanent, reserve listed inventory before online use, and settle sale/cancellation exactly once. Do not reopen legacy writes behind the hosted writer. |
| POC-11 | Guest jobs and portable earnings | Offline job rules/receipts | POC-06, POC-08, POC-10 | Bind conversations/progress to each guest, expose only visited-town jobs supported by enabled actions, and enforce the same filter server-side. Handle concurrent claims and reconnect. Deliver earned items and Bells to the correct home profile once, including full pockets/deferred delivery. |
| POC-12 | Town-wide chat and resident mail | Local keyboard/composer | POC-05, POC-06, POC-08, POC-13 approval | Deliver chat across rooms with identity, length/rate limits, mute/block enforcement and bounded report evidence. Add resident mail commands/delivery with stable IDs and retry receipts. Reject online attachments and non-resident recipients; preserve failed-send drafts and offline mail behavior. |
| POC-13 | Approved online UI mockups and implementation | Existing panels/assets/input | — | Prepare linking/endpoint, gate/join, loading/reconnect, guest wait, review/conflict and return/save mockups. Obtain user approval before implementing new UI. Reuse existing chat/letter input, assets, design language and controller hints. Bind screens to real lifecycle states. |
| POC-14 | Integrated acceptance and candidate delivery | Host/backend/offline fixtures | POC-01–POC-13 | Pass the acceptance matrix below with reproducible reports; deliver matching ISO, Depot app, backend and tools. Record actual physical results supplied by the user. Leave unqualified gates open. |

POC-08 includes normal offline-versus-hosted conflict review. It does not include
a chooser/merger for multiple cloned card branches. An unexpected or stale
lineage must fail without replacing the saved town or minting another copy of
an online receipt. Excluding clone support does not disable retry protection,
identity validation or interrupted-save recovery.

The guest job reward rule above supersedes the older blanket assumption that
destination-issued ordinary rewards must stay in destination custody. It does
not authorize taking pre-existing town possessions or undoing a reviewed koban
transfer. Mail is text-only for this PoC; stationery still follows the existing
letter rules.

## Execution order

1. POC-01–POC-03: prove the local Cloudflare/SDK route first. Prepare POC-13
   mockups during this work; approval gates new UI, not independent server,
   transport, protocol or persistence work.
2. POC-05–POC-08 and approved POC-04/POC-07 screens: build one complete visit,
   reliable reconnect and safe card return before enabling player item writes.
3. POC-09–POC-12: complete review, commerce, eligible jobs, chat and resident
   mail against the same command and settlement boundary.
4. POC-14: run full mixed-client acceptance, then qualify on physical hardware.

These are dependencies, not permission to mark a later feature complete because
its endpoint or offline equivalent exists. Update each backlog row with its PR,
commit and evidence as implementation lands.

## Acceptance matrix

| ID | Scenario | Required result |
| --- | --- | --- |
| POC-A01 | Local backend through a free tunnel | Public HTTPS pairing and WSS gameplay work without a purchased domain or router forwarding. PostgreSQL survives backend restart. |
| POC-A02 | Tunnel loss and endpoint change | Same-endpoint reconnect recovers membership and committed outcomes. Restarting the tunnel has a documented reconfigure/relink path; pending card progress and receipts remain intact. |
| POC-A03 | Existing cards | Import a valid existing save without resetting inventory, balances or resident identity; return and reload the correct home card. Exercise selected slot and unsupported/invalid saves without destructive writes. |
| POC-A04 | Three humans and unattended visits | One local plus two guests move and change rooms; capacity is enforced. Guests continue with the owner's console off while the backend stays running. Close/kick/block revoke access. |
| POC-A05 | Permissions and privacy | Guessed identities, stale epochs, replayed tickets, other guests' placements and private inventory/mail are rejected. Excluded gameplay and unsupported job offers cannot be enabled by a forged command. |
| POC-A06 | Placement review and interruption | Keep transfers ownership; koban preserves exactly one item. Full custody leaves a decision pending. Disconnect after each committed decision, resume guests, then review the latest remaining state. |
| POC-A07 | Normal offline/hosted conflicts | With one card lineage, combine compatible edits and review actual conflicts against the shared baseline. Both choices preserve a valid world; displaced placements reach custody. No cloned-card feature is claimed. |
| POC-A08 | Permanent commerce and earnings | Concurrent purchases/claims have valid winners. Retry after commit-before-reply, server restart or card publication failure cannot duplicate or erase purchases, portable job rewards or sale/cancellation receipts. |
| POC-A09 | Eligible guest jobs | Jobs belong to the visited town and current participant. Hide and reject jobs requiring excluded tools/actions. Concurrent progress, reconnect and full pockets preserve the correct claimant and owed reward. |
| POC-A10 | Town-wide chat | All authorized participants receive messages across rooms. Sender identity, limits, mute/block behavior and report evidence work without logging credentials or unbounded private text. |
| POC-A11 | Resident mail | Resident-only text mail is delivered once on retry/reconnect and normal scheduled processing. Attachments/cross-town recipients are rejected; failed sends retain the draft and do not consume stationery twice. |
| POC-A12 | Save/power failures | Interrupt before/after card publication and server acknowledgment. The old or new complete card state survives and settlement remains retryable, including card full/removal. |
| POC-A13 | Target memory and timing | Compare networking-disabled and enabled candidates on the same dense town. Include both remote actors, rooms, maximum checkpoint transfer and cable loss; record native/managed peaks, largest free block and frame-time percentiles. Preserve the existing heap/save headroom targets. |
| POC-A14 | Reproducible release candidate | Reports identify exact source, SDK, protocol, scenario and artifact hashes. Matching ISO/Depot/backend/tools are published through the release workflow. The user runs physical DOL-015 acceptance; emulator-only evidence never closes that gate. |

Use [change validation](change-validation.md) for focused checks during each
change and complete release validation for the final candidate. Existing
offline job/mail/card fixtures remain regressions, not substitutes for the
online scenarios. The [technical resource targets](multiplayer-technical-design.md#dol-015-resource-and-acceptance-budgets)
remain provisional until measured through the selected WSS route.

## Outside this PoC

- Cloned-card branch selection and reconciliation.
- Guest fishing, catching, gathering, planting, digging and other tool actions,
  and jobs which require those actions.
- Online mail attachments and friend/cross-town addressing.
- LAN-only play, peer hosting, NAT punching and player-run production servers.
- Paid hosting/domains, stable production endpoints and production uptime.
- Replacing the existing backend rules or changing unrelated offline gameplay.

The older brief's NET IDs remain useful component references. Its literal P2
(server simulation and durable commands) is part of POC-05/POC-06/POC-08/POC-10,
not a separate promise that chat, jobs and mail can be postponed beyond this PoC.

## Implementation checkpoint

The active implementation adds a persistent development stack and isolated
backup/restore workflow in [running the PoC](online-poc-running.md), a bounded
WSS adapter, fenced backend connections, ordered recipient events, explicit
public projections, and staged home-card publication. Its SDK dependency is
[DolphinDotNet PR 195](https://github.com/capz/DolphinDotNet/pull/195), published
as `0.1.0-preview.387.1`. Hypercube remains pinned to 55 with an explicit,
reviewable additive runtime override; normal game builds still omit networking.

The local three-client fixture exercises device-linked existing-save imports,
distinct seats, movement, paged snapshots, room reach checks, cross-room chat,
mail restrictions/retries, and immutable return/acknowledgment. Fault injection
checks every travel-bank byte boundary and home-card publication interruptions.
The actual connection path compiles through AOT; portable protocol code executes
under the native runtime with GC stress. These results do not close the public
Cloudflare, complete lifecycle, review, guest job, renderer, or physical gates.

The [connection mockups](concepts/online-poc/connection.svg) and
[review/social mockups](concepts/online-poc/review-and-social.svg) are awaiting
explicit approval under the user's UI rule. No new UI has been implemented.
The workspace's scoped Cloudflare network change was not approved; no network
settings changed. Public tunnel qualification therefore remains open here.
