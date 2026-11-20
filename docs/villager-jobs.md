# Villager jobs: implementation plan and first slice

The best fit is a deterministic domain service inside `AnimalTown.Domain`, with
thin adapters to the existing inventory, profiles, relationships, clock, world
events, mail and committed save repository. Jobs must never own a second wallet
or copy of the inventory. The supplied [v1 specification](specs/villager-jobs-v1.md)
is the behavioral contract, including its explicit engineering adaptations.
The archived spec normalizes four Markdown hard breaks; its wording is preserved.

This PR starts the implementation. It does **not** expose the new jobs in the
game, replace the existing daily requests, or claim the specification's full
definition of done. In particular, it cannot yet accept, complete or pay a job.
The existing tutorial and shop-owner work remain separate systems.

## Host mapping and necessary additions

| Concern | AnimalTown today | Implementation decision |
| --- | --- | --- |
| Profiles | Four `VillagerProfile` records; profile slots can be reused | Keep four profiles; add durable player identity/generation before job ownership. Never identify an owner solely by slot. |
| Residents | Six saved residents with fixed roster positions, names and home coordinates | Keep six residents. The foundation stores availability alongside this roster; add persisted NPC identities before creating targets or supporting migration. Names, species, homes and array indices cannot be portable identity. |
| Inventory | `GameState.Pockets`, item-definition IDs, capacity 10/20/30 | Preserve host capacity. Add an instance/binding sidecar for unique job objects and route **every** inventory mutation through its restrictions. Do not enlarge ordinary item definitions into per-item heap objects. |
| Relationships | Personal profile relationship records, clamped 0–255 | Keep the host range and apply deltas through one transaction path, exactly once. |
| Time | 30 Hz fixed simulation; dated RTC clock and 06:00 town rollover | Derive two eligible job ticks per simulation step. Add dated-second timestamps and an event watermark for deadlines; do not derive deadlines from town rollover or rendered frames. |
| Save | Mirrored records and reference manifest; legacy combined import/export | Extend the existing atomic snapshot. Co-publish job, inventory, relationship and wallet revisions before celebration. Do not introduce another save file or journal. |
| Casual dialogue | `GameState.Talk` can generate simple requests and pay immediately | Keep this legacy behavior until the replacement's transactions and approved presentation are ready. The new service will generate **only** on explicit Ask for work, and previews will be read-only. |
| Catalog | One fruit, one shirt, furniture, fish/insect and generic parcel/letter IDs | Add original non-native fruit, clothing and borrowable property plus stationery/flooring/wallpaper tags and pools. Validate every enabled category; missing content must never become a silent money fallback. |
| Ball and snowman | No qualifying physical ball-collision or snowman-combination events | Add small world-object adapters with stable IDs and attributable, persisted events. Visual snow alone cannot satisfy a snowman objective. |
| Flowers | Planted ground items and town cells | Provide acre plantable-cell and seedling/mature counts; snapshot baseline at acceptance, check net live count at reporting. |
| Mail | A bounded item-ID mailbox and tutorial letter placeholder | Implement real composed text, addressed delivery events and reply content before enabling letter jobs. Verify NFC, Unicode scalars and classification on the console compiler; ASCII-only grading is insufficient. |
| Online/travel | Server accounts, visits and transactions exist | These do not establish a console job/parcel transfer or remembered NPC destination. Keep both remote templates gated until real travel adapters can transfer ownership atomically; build no new networking platform for them. |

The six-resident roster, upgradeable pockets, four profiles and 0–255 relationship
range are existing host requirements that supersede the spec's fallback limits.
No friendship gate, daily quota, new profession/rank or generic success cooldown
is introduced. The initial roster's six availability personalities are assigned
Normal, Peppy, Lazy, Jock, Cranky and Snooty in saved resident order; this mapping
is not a portable identity and is persisted after the first job-state checkpoint.

## Implemented in this PR

* `Jobs/JobRulesV1.cs` is the single typed, immutable configuration asset for the
  implemented rules. It is compiled into the binary, avoiding a runtime JSON
  parser, reflection or disc read. It contains all twelve template definitions,
  their base deadlines, family, parcel/travel flags and seasonal policy tags;
  personality settings; generation weights; ordinary/retrieval reward tables;
  capacity defaults; friendship deltas; and constants used by the foundation.
  Actual season windows, letter-rank content and their runtime enforcement belong
  to subsequent slices; a seasonal tag alone does not enable an objective.
* Three independently seeded, saved 32-bit RNG streams implement the specified
  LCG and unbiased rejection sampling, including valid zero states. Pure helpers
  implement the gate/family/subtype rolls, reward category rolls, and exact
  integer MoneyPower/jitter calculation. These helpers cannot issue offers or
  modify the economy; a future transaction must freeze their results.
* Per-resident cooldown, acre progress budget, patience and fresh-generation
  flag follow section 6. Locked failed attempts cannot reset a timer. Acre entry
  replaces the budget; standing still exhausts it. Mild annoyance restarts the
  timer without independently disabling fresh generation.
* The actual console fixed step observes logical 20×20 outdoor acres and advances
  the separate cooldown clock at 60 eligible ticks/second. Menus, dialogue,
  onboarding, initial loading and doorway transitions contribute no active job
  time. Town journeys, animations, calendar and traffic continue behind menus.
  An initial observation, repeated same-acre observation or same-acre house
  entry/exit grants no new budget. Each local profile has its own saved acre
  cursor, cleared on deletion. Camera movement cannot change that cursor.
* Combined schema 13 and shared-town schema 9 add a bounded versioned job-state
  extension. Older supported saves initialize the foundation without RNG draws
  or offline ticks. Save readback validates a private candidate. Old single-player
  exports explicitly reject a request that would discard non-pristine job state.
* Incremental saves project this extension into `AT-Residents`, alongside the
  existing NPC runtime data. The town projection contains zeroes and is restored
  from the **same committed** manifest before semantic validation. RNG, cooldowns
  and patience can change without rewriting terrain. Pristine states retain the
  previous wire formats; no new memory-card filename or record bank is required.

The conversation-completion API is domain-only for now. Connecting it once per
authoritative dialogue session, and enforcing refusal without blocking existing
handoffs/payouts, is part of the presentation/transaction slice. There is no new
menu or dialogue text in this PR, so there is no UI change awaiting approval.

## Next implementation slices

1. **Identity, ledger and inventory transactions.** Persist town/NPC/player/item
   identities and monotonic IDs; bounded per-profile delivery/retrieval records;
   one offer per player/requester; one shared favor per NPC/subtype; and 128 reward
   obligation slots. Reserve job, obligation and parcel capacity atomically.
   Include cleanup on profile deletion, unknown-rule migration and corrupt
   binding quarantine. No accepted job may exist without its reservation.
2. **Offers and personal jobs.** Add authoritative conversation sessions,
   revisions and bounded command receipts. Roll one candidate before filtering,
   persist the complete offer, and preserve it on capacity failures. Implement
   clothing/property delivery and retrieval's saved borrower decisions, exact
   bound parcels, stage deadlines and timeout/abandon cleanup. Permanent NPC
   removal differs from sleep or an unloaded model. Remote definitions remain
   present but their real-travel capability requirements stay false.
3. **Rewards and time reconciliation.** Earned entitlements outlive job occupancy.
   Freeze category, concrete item/currency/reply and clothing snapshots exactly
   once. Partial wallet credits retain a durable remainder. Item rewards can use
   the slot just freed by turn-in. Apply dated-second expiry strictly at
   `now > due`, process timestamped events in chronological/priority order before
   the final watermark, and shift live times on a backwards clock change.
   Never store an unbounded receipt/event-ID set or write the card every tick.
4. **All local favors and mail.** Connect fruit/fish/insect selectors, ball
   collision, snowman construction/destruction, flower counts and delivered
   letters to actual host actions. Test all seasonal start/end boundaries,
   cross-player claims, exact evidence identity and NPC home-acre moves. Implement
   Unicode mail scoring and frozen replies. Ordinary completed favors retain
   occupancy until **original deadline + three days**; letter occupancy ends on
   reply insertion or its original deadline and its earned reply remains owed.
5. **Approved presentation and console verification.** Present Ask for work,
   stable offer/Accept/Decline, delivery/report/reward options and job log using
   the existing paper panels, IPL text, dialogue voices and item art. Produce a
   mockup for approval before changing those screens. Connect completed sessions
   once, preserve handoff/reward access when patience blocks casual conversation,
   and replace the legacy request loop only once all ten local templates have
   real completion hooks. Keep tutorial wages/debt work separate. Capture actual
   console/emulator scenarios, performance and save-reload/clock/capacity cases.

No build flag should advertise the full job feature until those slices meet the
supplied specification's definition of done. The steps above are implementation
boundaries, not permission gates for independent domain work.

## Cost and validation

The foundation has six fixed availability records, four acre cursors and three
32-bit RNG states. It scans six records per fixed tick, allocates no managed
memory while ticking, and does not load any model or texture. Fixed storage does
not grow with elapsed time, completed conversations or the number of RNG draws.
No hardware FPS or total console heap measurement is claimed by the host tests.

The encoded extension is **164 bytes** (length plus 40 words); a job-bearing
combined/shared-town snapshot also stores the daylight field when Classic is
selected, adding four bytes compared with the oldest current Classic schema.
An incremental NPC runtime record grows from 300 to **464 bytes**. A clock-only
checkpoint writes its two mirrors and two 192-byte commit manifests: **1,312
physical bytes**, with no terrain rewrite. This is checkpoint cost, not per-tick
card traffic. The committed split export includes the extension once in its town
record; the memory-card incremental projection moves it into the runtime record.

Deterministic tests enumerate the gate/family/subtype branches and all 100 reward
category draws, exercise rejection sampling and MoneyPower boundaries, and check
cooldown/patience/acre/pause behavior through the actual linked console fixed
step. Save tests cover old formats, zero RNG state, exact stream continuation,
unknown or malformed job fields and every truncated extension tail. Fault
injection interrupts every phase of a wallet-plus-job update before writing,
during a truncated write and after writing: recovery must yield the complete old
or new economic/RNG/cooldown revision, never a mixture.
