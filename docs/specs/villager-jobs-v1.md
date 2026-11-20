# Villager Job System — Autonomous Implementation Specification

**Document version:** 1.0\
**Purpose:** Implement a complete Animal Crossing–inspired villager job system without relying on the surrounding conversation.\
**Target:** Engine agnostic; suitable for a small town simulation, including a resource-constrained C# or GameCube project.\
**Scope:** Repeatable resident jobs, job availability, rewards, relationships, inventory, world events, mail, persistence, and validation.\
**Reference snapshot:** ACreTeam/ac-decomp, commit 986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0. This is a community reconstruction of the GameCube game, not Nintendo-published source.

## Contents

- [0. Instructions to the implementing agent](#0-instructions-to-the-implementing-agent)
- [1. Player-facing model and terminology](#1-player-facing-model-and-terminology)
- [2. Configuration and immutable definitions](#2-configuration-and-immutable-definitions)
- [3. Authoritative state model](#3-authoritative-state-model)
- [4. Domain APIs and host-game integration](#4-domain-apis-and-host-game-integration)
- [5. Conversation priority and job generation](#5-conversation-priority-and-job-generation)
- [6. NPC cooldown and conversational patience](#6-npc-cooldown-and-conversational-patience)
- [7. Common objective, item, and time rules](#7-common-objective-item-and-time-rules)
- [8. Complete template behavior](#8-complete-template-behavior)
- [9. Reward selection and delivery](#9-reward-selection-and-delivery)
- [10. Letter scoring, reply rewards, and mail integration](#10-letter-scoring-reply-rewards-and-mail-integration)
- [11. Failure, cancellation, migration, and recovery](#11-failure-cancellation-migration-and-recovery)
- [12. Persistence, deterministic randomness, and transactions](#12-persistence-deterministic-randomness-and-transactions)
- [13. Dialogue and presentation contract](#13-dialogue-and-presentation-contract)
- [14. Implementation sequence and acceptance tests](#14-implementation-sequence-and-acceptance-tests)
- [15. Relationship to the opening employment tutorial](#15-relationship-to-the-opening-employment-tutorial)
- [16. Reference behavior that must not be confused with the default](#16-reference-behavior-that-must-not-be-confused-with-the-default)
- [17. Source register and traceability](#17-source-register-and-traceability)
- [18. Definition of done](#18-definition-of-done)

## 0. Instructions to the implementing agent

Implement the **specified default** throughout this document. The default preserves the reference game's task families, category probabilities, reward tables, seasonal rules, exploration-dependent cooldown, and completed-contest retention. It makes a small, explicit set of engineering adaptations so that offers, saves, deadlines, and rewards behave consistently.

Labels have the following meanings:

- **Reference:** Behavior established by the reconstructed source linked in section 17.
- **Default:** A binding requirement for the inspired implementation. Where it differs from the reference, the difference is identified.
- **Optional:** An extension; do not make it a dependency of the default implementation.

If prose, examples, and pseudocode appear inconsistent, the numbered rules and configuration values take precedence. Resolve genuine contradictions before implementation; do not silently invent a third behavior. There are no unresolved design questions that require the original conversation.

### 0.1 Execution contract

1. Read the repository's instructions and inspect its existing inventory, NPC identity, dialogue, clock, save, mail, relationship, and world-event systems.
2. Reuse those systems, visual language, and existing assets. Add adapters rather than duplicating ownership of inventory, time, money, or NPC data.
3. Implement a testable domain layer before presentation. Preserve this document's behavior even if the host engine uses different class names.
4. Author new dialogue text and use project-owned item definitions. Original Animal Crossing dialogue, character models, and textures are not implementation dependencies.
5. Integrate with approved existing UI where possible. If new UI is necessary, produce a mockup and obtain the user's required design confirmation before building it. Domain implementation and tests can proceed independently.
6. Follow repository requirements for commits, pull requests, builds, and approvals. This document does not override those requirements.
7. Do not report the feature complete with missing normal job types, unconnected completion events, disposable save state, or a reward system that can duplicate or lose earned rewards.
8. Cross-town travel is conditional: implement the two remote-delivery definitions and adapter contract, but keep their prerequisites false in a project without travel. Do not build a networking platform solely for these two templates.
9. Use the defaults below when the host project has no equivalent rule. Record any necessary project-specific departure in the implementation report and tests.

### 0.2 Deliberate adaptations

These are required defaults, not claims about the original:

| Area | Specified default | Reference distinction |
| --- | --- | --- |
| Offer timing | Generate only when the player explicitly asks for work. | The reference may generate while preparing ordinary conversation choices. |
| Offer identity | Store a complete stable offer and revalidate it before acceptance. | Queued reference offers retain subtype metadata and can regenerate the target/item. |
| Acceptance | All templates become active on explicit acceptance. | Reference contests can become active before the accept/reject choice. |
| Persistence | Save offers, cooldowns, patience, RNG, obligations, and active jobs. | Reference offers/cooldowns/patience reset during session initialization. |
| Recipient/fruit sampling | Uniformly choose from explicit eligible candidate lists. | Reconstructed helper code contains selection biases. |
| Timing | Use a specified 60 Hz active simulation clock and strict deadlines. | A tick-to-seconds conversion was not established by this review; some reference handoffs test completion before expiry. |
| Rewards | Resolve each entitlement once and retain it until fully delivered. | Deferred reference payouts can reroll or be cleared with their quest. |
| Letter quality | Use the explicit Unicode-friendly validator in section 10. | The original uses separate byte/prefix/repetition heuristics, including a documented unsafe table traversal. |
| Multiplayer | Shared contests have one winning claimant; no other player can clear that person's earned reward or retention record. | Some reference other-player/deferred-payment paths clear completed records early. |
| Unreachable objectives | Fail or cancel deterministically with a reason, without penalizing the player for world removal. | Not all such reference combinations were fully traced. |
| Physical events | Persist qualifying ball/snowman evidence until it can be handled, subject to the stated destruction rules. | A reference ball event depends on successfully requesting an immediate forced conversation. |

Do not add a second full compatibility mode unless separately requested. Section 16 records historical quirks for future fidelity work.

## 1. Player-facing model and terminology

A resident may offer a small favor when asked. The player carries an object, follows a borrowing trail, supplies a collectible, changes the world, or writes a letter. Rewards are items or currency; there is no permanent employment rank, salary, XP level, profession tree, or daily task quota in this system.

Three families contain twelve normal templates:

| Family | Template IDs | Count |
| --- | --- | ---: |
| Delivery | DELIVERY_CLOTHING, DELIVERY_FOREIGN, DELIVERY_MOVED, DELIVERY_PROPERTY | 4 |
| Retrieval | RETRIEVAL_BORROWED | 1 |
| Favor, called “contest” in the reference code | FAVOR_FRUIT, FAVOR_BALL, FAVOR_SNOWMAN, FAVOR_FLOWERS, FAVOR_FISH, FAVOR_INSECT, FAVOR_LETTER | 7 |

“Contest” is an internal grouping. It does not imply a race, a timer displayed on screen, or competition against another NPC.

Definitions:

- **Requester:** NPC who initially asks for the job.
- **Current target:** NPC the next interaction must involve.
- **Reward giver:** NPC who owes the reward. For deliveries this is the receiving NPC; for retrieval it is the original owner; for favors it is the requester.
- **Personal job:** Delivery or retrieval, owned by one player profile.
- **Shared favor:** One town-level favor record attached to its requester. Another local player can complete it, subject to claim rules.
- **Home acre:** A stable map region containing an NPC's home, independent of where the NPC is currently walking.
- **Quest object:** An inventory instance reserved for one job. A parcel is not a freely openable gift.
- **Retention:** A completed non-letter favor continues occupying its requester and subtype slot until the original due time plus three days.
- **Active tick:** One domain update during ordinary, unpaused world simulation. Menus, dialogue, loading, and offline time do not advance this clock.
- **Game calendar time:** The host game's authoritative dated clock. It supplies seasonal checks and quest deadlines.

The opening shop-owner employment tutorial is separate from repeatable resident jobs. It must not accidentally enter the random resident pool. See section 15.

## 2. Configuration and immutable definitions

Keep balance values in one versioned configuration asset. The following values are mandatory defaults unless an existing project requirement explicitly supersedes them.

~~~yaml
rulesVersion: 1

limits:
  localPlayerProfiles: 4
  townResidents: 15
  inventorySlotsPerPlayer: 15
  personalDeliveryRecordsPerPlayer: 15
  personalRetrievalRecordsPerPlayer: 5
  favorRecordsPerResident: 1
  activeOrRetainedFavorsPerSubtypeTownWide: 1
  pendingOffersPerPlayerResidentPair: 1
  rewardObligationSlotsTownWide: 128

generation:
  freshAttemptGateNumerator: 3
  freshAttemptGateDenominator: 4
  familyWeights:
    DELIVERY: 1
    RETRIEVAL: 1
    FAVOR: 1
  deliverySubtypeWeights: [1, 1, 1, 1]
  favorSubtypeWeights: [1, 1, 1, 1, 1, 1, 1]
  retryInvalidCandidate: false
  gateOnFriendship: false
  dailyJobQuota: null

cooldown:
  activeTicksPerSecond: 60
  progressBudgetPerAcreEntryTicks: 1000
  advanceDuringDialogue: false
  advanceDuringMenus: false
  advanceOffline: false
  persistAcrossSessions: true

retrieval:
  maximumBorrowers: 4
  continueProbabilityNumerator: 1
  continueProbabilityDenominator: 2
  deadlineDaysPerStage: 2

favor:
  completedRetentionAddedToOriginalDeadlineDays: 3
  flowersToAdd: 4
  maxMatureFlowersWhenOffering: 20
  minimumEmptyCellsWhenOffering: 4
  localPlayersMayCompleteSharedFavor: true
  letterBodyMaximumNormalizedScalars: 1000

time:
  secondsPerDay: 86400
  expiryComparison: now_greater_than_due
  expireSeasonalJobsAtSeasonEnd: true
  backwardClockPolicy: shift_live_deadlines_and_recorded_event_times_by_delta

rewards:
  freezeOnEntitlementCreation: true
  ordinaryFurnitureHouseCandidateChanceNumerator: 1
  ordinaryFurnitureHouseCandidateChanceDenominator: 10
  defaultMoneyPower: 0
  walletLimitFallback: 99999
  preserveUndeliveredCurrencyRemainder: true

friendship:
  defaultMinimum: 0
  defaultMaximum: 127
  ordinarySuccess: 3
  explicitOfferRefusal: -3
  voluntaryPersonalJobAbandonment: -3
  personalJobTimeout:
    DELIVERY_CLOTHING: -5
    DELIVERY_PROPERTY: -5
    DELIVERY_FOREIGN: -1
    DELIVERY_MOVED: -1
    RETRIEVAL_SEEKING: 0
    RETRIEVAL_RETURNING: -2
  supplySelectionCancelled: -1
  worldInvalidation: 0
  uncompletedFavorExpiry: 0
~~~

The reference friendship helper clamps to 0–127. Apply deltas through the existing relationship service; an established host-game range may supersede these fallback bounds. [S3]

### 2.1 Template definitions

| Template | Family | Base deadline | Needs a parcel slot on acceptance? | Objective scope |
| --- | --- | ---: | --- | --- |
| DELIVERY_CLOTHING | Delivery | 2 days | Yes | Personal |
| DELIVERY_FOREIGN | Delivery | 2 days | Yes | Personal, remote target |
| DELIVERY_MOVED | Delivery | 2 days | Yes | Personal, moved target |
| DELIVERY_PROPERTY | Delivery | 2 days | Yes | Personal |
| RETRIEVAL_BORROWED | Retrieval | 2 days per stage | No; one required at pickup | Personal |
| FAVOR_FRUIT | Favor | 1 day | No | Shared |
| FAVOR_BALL | Favor | 1 day | No | Shared |
| FAVOR_SNOWMAN | Favor | 1 day | No | Shared |
| FAVOR_FLOWERS | Favor | 3 days | No | Shared |
| FAVOR_FISH | Favor | 3 days | No | Shared |
| FAVOR_INSECT | Favor | 3 days | No | Shared |
| FAVOR_LETTER | Favor | 2 days | No | Shared, local player only |

Deadlines are elapsed dated intervals, not “end of today.” A two-day job accepted at 14:35 is due two days later at 14:35. Retrieval refreshes the deadline only on a committed stage transition.

### 2.2 Content requirements

Every enabled template must resolve its content before it can be offered:

- A nonempty ordinary furniture, stationery, clothing, flooring, and wallpaper pool.
- At least one ordinary clothing item suitable for a delivery.
- A small “borrowable property” pool: for example a notebook, comic, camera, handheld game, glasses case, or handkerchief.
- A native fruit and at least one different fruit in the town's normal fruit set.
- Fish and insect category tags.
- One portable quest-object icon/presentation; type-specific parcel or item icons may reuse existing assets.
- Letter reply text families for every reward rank; they can share structure.
- NPC personality-to-dialogue mappings for the six personalities in section 6.

Validation must fail visibly in development when an enabled reward category has no fallback items. Never return an invalid item ID or silently turn missing furniture into money.

A rare pool may intentionally equal its ordinary pool when the project has no rarity taxonomy. Record that choice in configuration. Do not invent inaccessible “rare” objects.

## 3. Authoritative state model

Use stable IDs rather than display names, actor pointers, scene indices, or inventory slot numbers. An NPC identity must survive moving between towns and unloaded scenes. A home-acre coordinate is location metadata, not identity.

All example types are language-neutral. Use integers for persisted identifiers and durations where practical. Serialize explicit fields; do not serialize raw memory, object pointers, engine entities, or platform-dependent bitfields.

### 3.1 Required records

| Record | Required fields |
| --- | --- |
| JobDefinition | templateId; family; deadlineDays; generationWeight; targetPolicy; objectivePolicy; rewardTableId; contentTags; capabilityRequirements |
| JobInstance | jobId; rulesVersion; templateId; requesterNpcId; originalRequesterTownId; ownerPlayerId or SHARED; acceptedByPlayerId; claimantPlayerId or NONE; currentTargetNpcId; state; acceptedAt; stageStartedAt; originalDueAt; dueAt; completedAt or NONE; retainedUntil or NONE; occupiesJobSlot; obligationSlotReservationId; revision; templatePayload; terminalReason; transactionId |
| PendingOffer | offerId; playerId; requesterNpcId; templateId; full selected payload; createdAt; revision; explicitRefusalRecorded; validityContext |
| NpcJobAvailability | npcId; freshGenerationEnabled; conversationCount; cooldownRemainingTicks; cooldownProgressBudgetTicks; personalityId |
| PlayerJobState | playerId; personal job references; rememberedForeignNpcId; pending offer references; lastProcessedCommandSequence |
| TownJobState | townId; lastMovedAwayNpcId; favorByRequester; favorBySubtype; jobSerial; offerSerial; RNG states; lastObservedGameTime; saveSchemaVersion |
| QuestItemBinding | itemInstanceId; jobId; ownerPlayerId; itemDefinitionId; senderNpcId; intendedRecipientNpcId; condition=QUEST_BOUND |
| RewardEntitlement | entitlementId; jobId; claimantPlayerId; rewardGiverNpcId; resolvedAt; category; itemDefinitionId or totalCurrency; deliveredCurrency; deliveryState; friendshipApplied; catalogSnapshotVersion |
| CompletionEvidence | evidenceId; eventId; jobId; playerId; eventTime; relevantWorldObjectId; homeAcreId; evidenceKind; invalidated |
| ConversationContext | conversationSessionId; playerId; npcId; activeInteractionKind; authoritativeSessionRevision |
| ReplyEntitlement | replyId; jobId; playerId; npcId; acceptedMailId; resolvedAt; rank; resolvedPresentDefinitionId or NONE; replyTextKey; state |
| TerminalJobReceipt | jobId; owner/claimant; finalState; terminalReason; completedOrEndedAt; appliedEffectIds |

Implementation may combine records, but their invariants must remain explicit.

### 3.2 Template payloads

~~~text
DeliveryPayload:
    recipientNpcId
    recipientTownIdAtOffer
    parcelItemDefinitionId
    parcelInstanceId        // assigned on acceptance
    requestedClothingId?    // clothing delivery only

RetrievalPayload:
    rootOwnerNpcId
    currentBorrowerNpcId
    propertyItemDefinitionId
    visitedBorrowerIds[]    // up to 4, excluding original owner
    excludedParticipantIds[] // root + borrowers already committed
    borrowerCount          // starts at 1; maximum 4
    stageDecision          // UNDECIDED, STOP_AND_PICK_UP, or CONTINUE_TO(npcId)
    carriedItemInstanceId? // present only after actual pickup

FruitPayload:
    requestedFruitDefinitionId

BallPayload:
    qualifyingCollisionEvidenceId?
    claimantPlayerId?

SnowmanPayload:
    qualifyingSnowmanId?
    builderPlayerId?
    homeAcreId

FlowerPayload:
    homeAcreId
    baselineSeedAndFlowerCount
    targetSeedAndFlowerCount = baseline + 4

CollectiblePayload:
    requiredCategory       // FISH or INSECT

LetterPayload:
    acceptedMailId?
    senderPlayerId?
    receivedAt?
    nonWhitespaceLength?
    qualityPass?
    hasPresent?
    presentDefinitionId?
    rewardRank?
~~~

Persist a retrieval stage decision once it is revealed. Reopening dialogue must not reroll which borrower comes next or whether the current borrower has the object.

### 3.3 State enums

Personal deliveries:

~~~text
ACTIVE_CARRYING
  -> REWARD_OWED
  -> CLOSED_SUCCESS

ACTIVE_CARRYING
  -> FAILED_EXPIRED | CANCELLED_PLAYER | CANCELLED_WORLD
~~~

Personal retrieval:

~~~text
ACTIVE_SEEKING_BORROWER
  -> ACTIVE_SEEKING_BORROWER   // committed referral
  -> ACTIVE_RETURNING_OBJECT  // committed pickup
  -> REWARD_OWED
  -> CLOSED_SUCCESS

Either ACTIVE state
  -> FAILED_EXPIRED | CANCELLED_PLAYER | CANCELLED_WORLD
~~~

Shared favors:

~~~text
ACTIVE
  -> OBJECTIVE_READY         // event evidence or accepted letter
  -> REWARD_OWED
  -> COMPLETED_RETAINED
  -> CLOSED_SUCCESS

ACTIVE or OBJECTIVE_READY
  -> EXPIRED | CANCELLED_WORLD

OBJECTIVE_READY
  -> ACTIVE                 // credited snowman destroyed before report

Letter special case:
OBJECTIVE_READY
  -> REPLY_OWED
  -> CLOSED_SUCCESS
~~~

Inventory-backed fruit/fish/insect and live-count flowers can validate and complete directly from ACTIVE during a handoff. OBJECTIVE_READY must not falsely reserve an item that the player can subsequently sell.

**Reward ownership is separate from quest occupancy.** A favor can leave its subtype/requester slot after retention or seasonal cleanup while an unpaid entitlement survives. Removing a job record must never delete its undelivered entitlement.

### 3.4 Core invariants

1. Every live personal job has exactly one owner.
2. An inventory instance can belong to at most one job.
3. At most one occupying favor exists for a given requester, and at most one for a given favor subtype in a town.
4. Occupying favor states include active, ready, reward-owed, reply-owed, and completed-retained until their explicit slot release rule fires. Reply occupancy follows section 10.5.
5. A job can create at most one ordinary reward entitlement or one letter reply entitlement.
6. A claimed shared favor has exactly one claimant.
7. A terminal result cannot transition back to active.
8. Rewards, currency, item consumption, clothing changes, friendship changes, and save revisions commit atomically.
9. Replaying a command or world event has no additional effect.
10. An uncommitted offer owns no parcel, active-job slot, deadline, or completed reward.
11. A declined offer retains its snapshot but does not activate its objective.
12. A save/load cycle does not change the next outcome solely because runtime objects were reconstructed.

## 4. Domain APIs and host-game integration

Expose intent-level commands. Presentation must not write directly into job records.

Every public NPC-interaction command (AskForWork, AcceptOffer, DeclineOffer, PresentQuestObject, ResolveBorrowerInteraction, SupplyRequestedItem, ReportWorldFavor, CollectReward, and ConfirmLetterFollowup) also receives a validated ConversationContext, in addition to the inputs in the table below. The host must verify an active conversation session for that player and the correct requester/current target/reward giver, its session revision, interaction availability, and the allowed action. Client-supplied NPC IDs or a visible button are not sufficient. This prevents remote turn-ins and reward collection. The patience exception permits an existing obligation within a real interaction; it does not bypass physical/world availability. AbandonPersonalJob may be invoked from an existing job-log flow without a conversation. Automatic unreachable-giver recovery is a trusted internal operation, not a public bypass flag.

### 4.1 Commands

| Command | Inputs | Expected responsibility |
| --- | --- | --- |
| GetConversationOptions | playerId, npcId | Read-only prioritized options; no RNG draw or generation. |
| AskForWork | commandId, playerId, npcId | Resolve existing obligations first; otherwise attempt generation. |
| AcceptOffer | commandId, playerId, offerId, expectedRevision | Revalidate and atomically activate. |
| DeclineOffer | commandId, playerId, offerId | Record explicit refusal and relationship effect once for that offer. |
| PresentQuestObject | commandId, playerId, npcId, itemInstanceId | Deliver the exact bound instance to its intended recipient. |
| ResolveBorrowerInteraction | commandId, playerId, jobId, npcId | Commit a referral or pick up the object if space permits. |
| SupplyRequestedItem | commandId, playerId, jobId, itemInstanceId | Validate and consume one eligible ordinary item. |
| ReportWorldFavor | commandId, playerId, jobId | Recheck evidence or flower count and establish entitlement. |
| CollectReward | commandId, playerId, entitlementId | Deliver owed item/currency exactly once or report blocked capacity. |
| ConfirmLetterFollowup | commandId, playerId, jobId | Insert the resolved reply in that player's mailbox when possible. |
| AbandonPersonalJob | commandId, playerId, jobId | Voluntary cancellation with the configured one-time effect. |
| OnConversationEnded | eventId, playerId, npcId | Increment patience once for a completed conversation session. |
| OnActiveSimulationTicks | tickCount, worldMode | Advance cooldown budgets only in eligible world mode. |
| OnAcreTransitionStarted | eventId, fromAcreId, toAcreId | Replenish cooldown progress budgets for all town NPCs. |
| OnWorldEvent | eventId, typedPayload | Record ball collision, snowman creation/destruction, mail receipt, NPC departure, etc. |
| ReconcileTime | newGameTime | Apply deadlines, seasonal closures, retention expiry, and clock-change policy. |

Return structured results: SUCCESS, OFFER_AVAILABLE, NO_JOB, COOLING_DOWN, NPC_UNAVAILABLE, REFUSED_CONVERSATION, NEED_INVENTORY_SPACE, NEED_WALLET_SPACE, NEED_MAILBOX_SPACE, JOB_CAPACITY_FULL, REWARD_BACKLOG_FULL, REMINDER, OBJECTIVE_INCOMPLETE, WRONG_RECIPIENT, WRONG_ITEM, OFFER_INVALIDATED, JOB_EXPIRED, TARGET_UNREACHABLE, ALREADY_PROCESSED, STALE_EVENT, and REVISION_CONFLICT.

A blocked-capacity result is recoverable. It must not be represented as job failure.

### 4.2 Services the domain consumes

| Service | Minimum contract |
| --- | --- |
| GameClock | Current dated game time; explicit clock-change delta; next seasonal boundary. |
| ActiveTickClock | Fixed active ticks independent of rendered frame count. |
| ResidentRegistry | Stable resident identities, personality, home acre, current town, migration state, current clothing, interaction availability. |
| Inventory | Query exact instances, category/condition, empty capacity; transactional add/remove/reserve operations. |
| ItemCatalog | Category tags, ordinary/rare pools, fruit set, clothing compatibility, furniture reward eligibility. |
| WorldRegions | Stable acre membership; empty plantable-cell count; mature flower count; seed-plus-flower count. |
| RelationshipService | Read/apply a bounded delta with a stable effect ID. |
| Wallet | Capacity, current balance, atomic credit. |
| Mail | Delivered-letter events, sender/recipient IDs, body, gift, mail ID; atomic mailbox insertion. |
| TravelRegistry | Resolve remembered/moved NPC identity to a reachable destination; transfer personal job and parcel together. |
| WorldEventSource | Ball collision and snowman lifecycle events, including player attribution. |
| SaveStore | Atomic domain transaction or journal, versioned serialization, recovery. |
| QuestRng | Deterministic independent random stream or streams with persisted state. |
| DialogueAdapter | Present options, item selectors, original text, animations, and result codes without owning gameplay state. |
| InteractionContextService | Validate active player/NPC conversation sessions and action permissions from authoritative host state. |

If an existing engine stores parcels by inventory position, the adapter must update bindings whenever positions change. The domain itself uses item-instance identity.

### 4.3 Event ordering and idempotency

Within one simulation update or load catch-up:

1. Apply an explicit backward-clock adjustment if present, using section 7.5.
2. Drain authoritative queued world/mail events through the new game time. Sort by event timestamp, then source priority, then source sequence. Use the fixed priority MAIL_DELIVERY, WORLD_OBJECT_CHANGE, NPC_MIGRATION for simultaneous events; other event types follow a configured stable order.
3. Before each event at time T, reconcile deadlines strictly earlier than T and season closures at or before T, then apply that event at T. Do not first expire every job at the final catch-up time.
4. After the queued events, reconcile to the new current time.
5. Execute player commands at that current time in command-sequence order.
6. Commit each logical transaction, then publish its presentation events.
7. Advance active cooldown ticks if the world mode permits it.

At exactly dueAt, a completion is valid. At any later instant it is expired. A season closes at 00:00 on its first disallowed date, before events at that timestamp.

The mail/world scheduler must enqueue events before the authoritative time watermark advances past them. A previously unseen event older than the committed watermark is STALE_EVENT and cannot resurrect a terminal job. Known duplicates return ALREADY_PROCESSED. This policy preserves a letter actually delivered before its deadline during offline catch-up while preventing arbitrary backdated claims.

Persist command outcomes until their owning job/entitlement is archived. Use a monotonic per-player command sequence and per-source world-event sequence to prevent unbounded “all events ever seen” sets. Terminal receipts and high-water marks must make late duplicates harmless.

The default assumes one active player in a town at a time, including local profile switching. A networked game must add server authority and compare-and-swap revision checks; client presentation alone cannot authorize rewards.

## 5. Conversation priority and job generation

### 5.1 Eligibility to interact

First apply the host game's ordinary interaction rules: the NPC exists, can converse now, and is a normal resident rather than a shopkeeper, visitor, special event character, or islander with a separate system.

Sleep, a running cutscene, or an unloaded actor can prevent starting conversation. Those are outer interaction restrictions, not friendship or probability modifiers.

Patience may suppress *new work and casual conversation*. The default still permits an existing handoff or reward collection so annoyance cannot strand a completed job. This is an explicit accessibility/reliability adaptation.

### 5.2 Conversation priority

Return the highest-priority relevant interaction:

1. An unpaid reward or reply owed to the current player by this NPC.
2. A ready handoff/completion involving this NPC and player.
3. A retrieval continuation/pickup at this NPC.
4. A reminder for a personal job involving this NPC as current target, requester, or original retrieval owner.
5. An active shared favor from this NPC: completion options if eligible, otherwise a reminder.
6. A completed-retained favor from this NPC: acknowledgment/no-new-work until retention ends.
7. A stored valid offer for this player and NPC.
8. A fresh-work option, subject to patience and the NPC's fresh-generation flag.
9. Ordinary no-work or cooling-down dialogue.

Among unpaid obligations at the same priority, use earliest resolvedAt, then the stable typed entitlement/reply ID. For other job interactions at the same priority, use earliest acceptedAt, then lowest jobId. Never depend on hash-map enumeration order.

Obligations involving another player are not exposed as actionable personal jobs. A shared favor claimed by another player produces an “already handled” explanation without clearing it.

### 5.3 Fresh-generation algorithm

~~~text
AskForWork(player, npc):
    CatchUpQueuedEventsAndReconcileTime(now)
    if npc cannot interact:
        return NPC_UNAVAILABLE
    existing = ResolveExistingInteraction(player, npc)
    if existing is an unpaid obligation, ready handoff, retrieval continuation,
       active-job reminder, or retained favor:
        return existing

    if npc.conversationCount >= personality.refusalThreshold:
        return REFUSED_CONVERSATION
    if existing is a stored offer:
        return ValidateOrInvalidateStoredOffer(existing)
    if not npc.freshGenerationEnabled:
        return COOLING_DOWN

    if NextInt(4) == 0:
        StartNoJobCooldown(npc)
        return NO_JOB

    family = UniformChoice([DELIVERY, RETRIEVAL, FAVOR])
    template = UniformChoice(all defined subtypes in that family)

    failure = CheckTemplatePrerequisites(player, npc, template)
    if failure:
        StartNoJobCooldown(npc)
        return NO_JOB

    payload = BuildValidPayload(player, npc, template)
    if payload cannot be built:
        StartNoJobCooldown(npc)
        return NO_JOB

    if no reward-obligation capacity can be reserved on acceptance:
        StoreStableOffer(player, npc, template, payload)
        return REWARD_BACKLOG_FULL

    if template is delivery and inventory has no free parcel slot:
        StoreStableOffer(player, npc, template, payload)
        return NEED_INVENTORY_SPACE

    if personal record capacity or favor slot is unavailable:
        StartNoJobCooldown(npc)
        return NO_JOB

    offer = StoreStableOffer(player, npc, template, payload)
    return OFFER_AVAILABLE(offer)
~~~

There is **one candidate per fresh attempt**. Do not filter to the currently eligible candidates before rolling, and do not retry an invalid choice. The no-job outcome is part of the behavior.

Before eligibility filtering, each fresh unlocked attempt has:

| Outcome | Probability |
| --- | ---: |
| Immediate no-job gate | 25% |
| Any delivery family | 25% |
| Retrieval | 25% |
| Any favor family | 25% |
| Each individual delivery subtype | 6.25% |
| Each individual favor subtype | 1/28, approximately 3.5714% |

Actual offered-job frequency is lower because prerequisite, target, and capacity checks can fail. Friendship does not change these probabilities. There is no universal success cooldown and no daily generation quota. [S1]

### 5.4 Stable offers and acceptance

The offer stores the chosen target, item, and objective parameters. It does not start a deadline.

On repeated requests, present the same stored offer without another RNG draw. GetConversationOptions may evaluate validity but is read-only: it never deletes an offer, starts a cooldown, or rolls RNG. AskForWork and AcceptOffer perform and commit any necessary invalidation. Recheck:

- NPC and target identity remain valid and reachable under the template's rules.
- Season/time prerequisites still permit starting this template.
- Required personal/favor slots are available.
- The same favor subtype is not already occupying another NPC.
- Foreign/moved duplicate checks exclude this very offer's own ID.
- Required content still exists.
- Delivery acceptance has one free inventory slot.
- Flower acceptance still has the required empty cells and mature-flower cap.

If a non-capacity prerequisite has become invalid, delete the offer, start the no-job cooldown, and return OFFER_INVALIDATED. Do not roll another candidate in that command. If only inventory or personal-record capacity is blocked, retain the offer and return NEED_INVENTORY_SPACE or JOB_CAPACITY_FULL respectively; do not punish the player. A competing occupied favor subtype invalidates the offer rather than granting a second favor.

On acceptance, perform one atomic transaction:

1. Revalidate the offer and expected revision.
2. Reserve the appropriate job slot, one reward-obligation slot, and any town-wide favor key; retain the offer if capacity is blocked.
3. Set acceptedAt and dueAt, and snapshot originalDueAt=dueAt. Retrieval stages can later update dueAt; originalDueAt remains the acceptance deadline.
4. Snapshot acceptance-dependent objective data, such as the flower baseline.
5. For a delivery, create its exact quest-bound parcel instance and inventory binding.
6. Mark the job active.
7. Remove the accepted offer.
8. Emit one “job accepted” presentation event.

A completed delivery/retrieval or an accepted offer does not automatically disable freshGenerationEnabled.

On explicit decline, apply -3 friendship once for that offer and retain the offer. Closing the dialogue, choosing casual chat, or backing out before the accept/decline choice is not a decline. Repeatedly declining the same stored offer does not stack penalties in the default; this is an adaptation to make command replay and retries safe.

### 5.5 Prerequisites by template

| Template | Additional prerequisite |
| --- | --- |
| Local clothing/property delivery | A valid local target in a different home acre from the requester. |
| Foreign delivery | Nonempty remembered foreign NPC identity; travel adapter resolves it; no same-subtype personal job or other stored same-subtype offer for this player. |
| Moved-away delivery | Nonempty town lastMovedAwayNpcId; travel adapter resolves it; same duplicate rule as foreign delivery. No 60-day rule. |
| Retrieval | A valid first borrower and a free retrieval record. |
| Fruit | At least one valid nonnative fruit in the configured normal fruit set. |
| Ball | Ball physics/collision integration is enabled; a particular spawned ball need not already be present. |
| Snowman | December 25–February 17 inclusive, and local game time from 08:00 through 16:59:59. |
| Flowers | February 25–August 31 inclusive; home acre has at least 4 empty plantable cells and at most 20 mature flowers. |
| Fish | Fish item category exists; no seasonal gate. |
| Insect | March 1–November 28 inclusive. |
| Letter | Current player is a local resident profile, not a visiting profile; mail integration enabled. |

Every favor additionally requires a free requester favor slot and no occupying favor of its subtype anywhere in this town. Offered-but-unaccepted favors do not hold that town-wide key; acceptance arbitrates a conflict.

Season checks use month/day and support intervals spanning New Year. Snowman's hour restriction affects **starting** a job only. A valid active snowman job may be completed after 17:00, until its deadline or season closure.

### 5.6 Target selection

For local deliveries and each retrieval borrower selection, build an explicit eligible resident list:

- Resident has a stable, nonempty identity.
- Resident is not the current sending NPC.
- Resident's home acre differs from the current sending NPC's home acre.
- Resident is not the original retrieval owner or an already visited borrower when selecting a new retrieval borrower.
- Resident is not permanently departing without a reachable destination.

Choose uniformly from this list. A sleeping, currently distant, or unloaded resident can still be a target. Dialogue availability is checked when the player reaches them.

For the first retrieval borrower, the sending NPC is the original owner. For later referrals, it is the current borrower. Thus acre exclusion changes with the current link; it is not always relative to the original owner's house.

Foreign/moved targets come from stored identity records, not a random town resident. Revalidate the destination on acceptance and on travel.

## 6. NPC cooldown and conversational patience

The cooldown is a combination of **active simulation time and movement between acres**. Standing still for enough real-world time is insufficient.

### 6.1 Personality table

| Personality | Cooldown ticks | Mild-annoyance count | Refusal count |
| --- | ---: | ---: | ---: |
| Normal | 4000 | 12 | 15 |
| Peppy | 3000 | 10 | 13 |
| Lazy | 4000 | 12 | 15 |
| Jock | 4000 | 10 | 13 |
| Cranky | 5000 | 9 | 12 |
| Snooty | 5000 | 9 | 12 |

The source table is indexed by personality at its call sites, despite misleading mood-oriented comments in the reconstruction. [S3]

### 6.2 Start and advance rules

~~~text
StartNoJobCooldown(npc):
    if npc.freshGenerationEnabled:
        npc.cooldownRemainingTicks = PersonalityDuration(npc)
        npc.cooldownProgressBudgetTicks = 1000
    npc.freshGenerationEnabled = false

OnAcreTransitionStarted(from, to):
    if from == to:
        return
    for each town NPC:
        npc.cooldownProgressBudgetTicks =
            min(1000, npc.cooldownRemainingTicks)

AdvanceActiveTicks(ticks):
    for each town NPC:
        previousRemaining = npc.cooldownRemainingTicks
        spend = min(ticks,
                    npc.cooldownRemainingTicks,
                    npc.cooldownProgressBudgetTicks)
        npc.cooldownRemainingTicks -= spend
        npc.cooldownProgressBudgetTicks -= spend

        if previousRemaining > 0 and npc.cooldownRemainingTicks == 0:
            npc.conversationCount = 0
            npc.freshGenerationEnabled = true
            npc.cooldownProgressBudgetTicks = 0
~~~

An acre transition replenishes the budget; it does not add an accumulating 1000 ticks and does not directly reduce remaining time. Trigger it only on an actual stable acre-ID change. Entering a house in the same acre, camera scrolling, and hovering on a region seam must not synthesize repeated transitions.

The fixed 60 Hz rate is a default for this new implementation. Do not describe 4000 source ticks as a historically verified number of seconds. Implement an accumulator if the host simulation runs at a different rate; do not advance by “one tick per rendered frame.”

Preserve active-tick event order. Batch ticks only when no acre transition, conversation-end reset, or pause boundary occurs inside the batch; otherwise split it at those events. Applying several acre crossings first and one combined tick batch afterward would produce the wrong budget consumption.

Example for a normal NPC:

1. A failed generation starts remaining=4000, budget=1000.
2. 1000 eligible ticks pass: remaining=3000, budget=0.
3. Another 10,000 ticks in the same acre do nothing.
4. Crossing an acre replenishes budget=1000.
5. Time plus further transitions eventually brings remaining to zero.
6. At zero, generation unlocks and conversational patience resets.

Save both counters. Offline time does not consume them.

### 6.3 Patience rules

On completion of one conversation session with the NPC:

~~~text
if conversationCount < refusalThreshold:
    conversationCount += 1
    if conversationCount >= mildThreshold:
        cooldownRemainingTicks = personalityDuration
        cooldownProgressBudgetTicks = 1000
~~~

This patience reset alone does **not** set freshGenerationEnabled=false. It shares the countdown mechanism with no-job locking.

At the mild threshold, use an annoyed dialogue tone. At the refusal threshold, disallow new work/casual talk until the countdown resets, while still allowing already-earned rewards and valid handoffs.

Do not claim the reference count is a verified rolling time window: the reviewed code increments after resetting its talk timer. The default therefore counts completed conversations until the shared countdown resets.

One long conversation containing multiple dialogue pages increments once. Inventory selection submenus and animations within that session do not increment again. A rejected attempt to start dialogue is not a completed conversation.

## 7. Common objective, item, and time rules

### 7.1 Quest objects

A quest parcel/property is a normal inventory instance with an additional binding:

- It occupies one slot.
- It can be rearranged within the inventory.
- It cannot be sold, donated, mailed, gifted to an unrelated NPC, equipped, opened, consumed, or merged into a stack.
- Default: it cannot be dropped or put into generic storage while the job is active. This restriction is an adaptation chosen to avoid an unbounded recovery problem.
- It is delivered only by exact instance ID, job ID, owner, and intended recipient.
- An ordinary item with the same item-definition ID is not a substitute.
- Finishing, abandoning, or invalidating the job removes its bound item and binding exactly once.
- Corruption recovery must not mint a second parcel. See section 12.

Reference inventory distinguishes quest-condition items from normal items and wrapped presents. A delivery parcel is not a wrapped gift the player can legitimately open for themselves. [S4], [S12], [S14]

### 7.2 Ordinary supplied items

Fruit, fish, and insect favors consume one ordinary-condition item selected by the player. The item may have been obtained before the favor was offered. There is no catch-after-acceptance timestamp requirement.

Never consume a quest-bound instance, a mailed attachment in transit, a protected/locked instance, or an item not owned by the acting player. A stack may contribute one unit if the host game supports stacks.

Recheck eligibility inside the final transaction, because the item selector is presentation, not a reservation.

### 7.3 Completion and reward boundary

A successful objective transaction:

1. Reconciles deadlines and verifies the actor/claimant.
2. Revalidates objective evidence or selected item.
3. Consumes the required item or records the world objective.
4. Creates exactly one resolved reward/reply entitlement.
5. Applies the configured friendship effect exactly once where applicable.
6. Transitions the job and its occupancy state.
7. For ordinary rewards only, attempts delivery using the freed inventory space if an item was consumed. A letter reply remains owed until its explicit follow-up; it is not inserted during receipt.
8. Saves atomically before a celebratory animation can be replayed.

Completing an objective earns a reward even when pockets, wallet, or mailbox are full. Capacity changes how it is delivered, not whether it was earned.

### 7.4 Deadline and season reconciliation

Use the host game's dated timeline with one authoritative value. Convert “days” to 86400 seconds on that game timeline; do not use the operating system's daylight-saving transitions.

Default rules:

- now <= dueAt: objective may still be completed.
- now > dueAt: active personal job fails; uncompleted shared favor expires.
- Successful objective commitment stops objective expiry. Pending earned rewards never expire.
- Snowman season ends at 00:00 February 18.
- Flower season ends at 00:00 September 1.
- Insect season ends at 00:00 November 29.
- Those season closures release the corresponding active or retained favor slot, even if its numeric deadline or retention time is later.
- A reward already earned before closure remains in the reward ledger.
- Each committed retrieval referral and the final pickup sets stageStartedAt=now and dueAt=now+2 days. Merely asking for a reminder does not refresh time.

For a non-letter favor completed at time T with original dueAt D, its retention endpoint is **D + 3 days**, not T + 3 days. Seasonal closure can release the slot earlier.

### 7.5 Clock adjustment policy

Forward adjustment: reconcile deadlines and seasons at the new time; apply each terminal effect once.

Backward adjustment: preserve elapsed progress rather than fail quests as punishment. Let delta=newTime-oldTime, which is negative. Shift all live acceptedAt, stageStartedAt, originalDueAt, dueAt, retainedUntil, and stored live evidence timestamps by delta. Do not reroll offers or rewards. The new calendar season still controls new offers and active seasonal cancellation.

This is a deliberate adaptation. The reference has a backward-clock validity check and a 28-day guard; see section 16.

### 7.6 Explicitly absent mechanics

Do not add job-level XP, reputation prerequisites, daily caps, paid shifts, worker skill levels, random reward quality based on friendship, or a generic cooldown after every success. These would materially change the loop and require a separate design decision.

---


## 8. Complete template behavior

The reference basis for these definitions is the template table, dialogue implementation, world-event hooks, and contest predicates. Exact source functions are listed in section 17. Labels such as “property delivery” are names for this inspired game; reconstructed internal names do not always prove the original dialogue's narrative meaning. [S1], [S2], [S4], [S5]

### 8.1 DELIVERY_CLOTHING — deliver clothing to a local resident

**Offer:** The requester supplies clothing for a specific local NPC in a different home acre. Choose an ordinary clothing definition other than the requester's currently worn outfit. If no eligible clothing or recipient exists, this candidate fails.

**Acceptance:** Create a personal delivery record and one quest-bound clothing parcel. Start a two-day deadline. The player needs an empty inventory slot.

**Completion:**

1. The owner talks to the intended receiving NPC.
2. The parcel selector exposes only the bound instance for that job.
3. Capture the recipient's old clothing definition before changing it.
4. Consume the parcel and make the recipient wear the delivered clothing.
5. Generate the clothing-delivery reward using the recipient as reward giver.
6. Apply the ordinary success relationship effect to the recipient.
7. Enter REWARD_OWED. The personal delivery slot remains occupied until the entitlement is fully settled; then enter CLOSED_SUCCESS and release that slot. This gameplay rule does not depend on the physical storage layout.

There is no required return trip to report success to the original sender. The recipient pays.

The “worn clothing” reward category gives the recipient's previous outfit, not the delivered shirt and not the player's outfit. An unsupported/custom-design outfit falls back as specified in section 9. [S1], [S2], [S13]

### 8.2 DELIVERY_FOREIGN — deliver property to a remembered remote resident

**Offer:** Target exactly the player's rememberedForeignNpcId. This record can be populated by the travel subsystem after visiting another town; it does not require a quest service to infer that the player personally conversed with that NPC.

**Additional gates:**

- A usable travel adapter and reachable destination must exist.
- The target must actually be outside the current town.
- No live foreign-delivery job or another stored foreign-delivery offer for this player exists.

**Acceptance:** Give one random quest-property parcel, not necessarily clothing. Personal ownership and a two-day deadline apply.

**Travel:** Transfer the player, parcel instance, job record, and stable identity mapping together. Do not clone the parcel into both source and destination inventories.

**Completion:** The identified remote NPC consumes the parcel and pays the foreign-delivery reward. No report to the sending NPC is required.

If the target moves again, follow the stable identity through the travel registry. If the destination becomes temporarily unloaded, retain the job. If it becomes permanently unreachable, cancel with no player-blame penalty and remove the parcel.

### 8.3 DELIVERY_MOVED — deliver property to the last departed resident

This uses the same transport and item flow as foreign delivery, but targets town.lastMovedAwayNpcId and uses its own reward row.

On a real NPC move-out, update lastMovedAwayNpcId to that character instance. Do not replace it with a newly generated character who happens to have the same display name.

There is no required “moved within 60 days” condition in this specification. The reviewed eligibility branch checks the stored identity, not a move-age threshold. The travel registry must still resolve a reachable destination. [S1], [S3]

An already accepted moved-resident job keeps its original target even if another resident subsequently leaves and overwrites the town's last-moved memory.

### 8.4 DELIVERY_PROPERTY — deliver or return a local possession

**Offer:** The requester gives the player a small property object to take to another local NPC in a different home acre.

**Acceptance:** Select an item from the generic quest-property pool, create its bound parcel, and set the two-day deadline.

**Completion:** The target consumes the object and pays the property-delivery reward. No report-back step exists.

This is mechanically distinct from retrieval: the player already has the object when leaving the requester, and there is no borrowing chain.

### 8.5 RETRIEVAL_BORROWED — recover a borrowed object

**Offer:** The original owner wants a specific property object returned and names the first borrower. No parcel is given at acceptance, so full pockets do not prevent starting this job.

**Initial state:**

~~~text
rootOwner = requester
currentBorrower = selected first borrower
currentTargetNpcId = currentBorrower
borrowerCount = 1
visitedBorrowerIds = [first borrower]
excludedParticipantIds = [root owner, first borrower]
item = one selected quest-property definition
state = ACTIVE_SEEKING_BORROWER
dueAt = acceptedAt + 2 days
~~~

**At the current borrower:**

1. Confirm the job is active, owned by this player, and within its current deadline.
2. If the stage has no revealed decision, decide once:
   - If borrowerCount is already 4, stop here.
   - Otherwise draw a fair stop/continue result.
   - A stop means this NPC still has the object.
   - A continue means select a new borrower, excluding the original owner, every visited borrower, and residents whose home acre equals the current borrower's home acre.
   - If a continue was chosen but no eligible new borrower exists, stop here instead. This fallback is an explicit adaptation preventing an impossible chain.
3. Persist the decision before presenting the continuation dialogue.
4. If continuing, commit the new current borrower and JobInstance.currentTargetNpcId to that same NPC, append the identity to the visited/excluded lists, increment borrowerCount, and refresh dueAt to now+2 days. Set the new stage's stageDecision=UNDECIDED; keep the prior command outcome separately for replay protection.
5. If stopping, require one empty inventory slot. If there is none, retain the same stop decision and current deadline; do not penalize, reroll, or mint the object.
6. Once space exists, create the bound object, set JobInstance.currentTargetNpcId to the original owner, enter ACTIVE_RETURNING_OBJECT, and refresh dueAt to now+2 days.

Referrals are mandatory progression once the player chooses the appropriate existing-job interaction; they are not new offers requiring a fresh generation roll.

**Return:** The original owner consumes the exact bound object and pays the reward for the final borrowerCount. Intermediaries neither pay nor receive ordinary completion friendship bonuses.

The chain has at most **four borrowers plus the original owner**, or five distinct NPCs. Examples:

| Route | Borrower count | Reward row |
| --- | ---: | ---: |
| Owner A → borrower B → owner A | 1 | 1 |
| A → B → C → A | 2 | 2 |
| A → B → C → D → A | 3 | 3 |
| A → B → C → D → E → A | 4 | 4 |

With enough valid candidates, the derived chain probabilities are 50%, 25%, 12.5%, and 12.5% for lengths 1–4. Candidate scarcity can shorten a chain under this default.

Do not increase the reward count for the final return trip, repeated reminders, or an inventory-full retry. [S1]

### 8.6 FAVOR_FRUIT — bring a specific nonnative fruit

At offer creation, uniformly select one fruit definition from the town's normal fruit set excluding its native fruit. Persist that exact request.

The player supplies one ordinary instance of that fruit. A different foreign fruit does not qualify. The fruit may already be in the player's pockets before the request.

The requester consumes one unit and pays the fruit reward. A one-day deadline applies. There is no gift-value multiplier and no requirement to prove where the fruit was harvested.

If the host catalog distinguishes special fruits such as coconuts, include them only if they belong to the configured normal fruit set; do not silently broaden the source game's normal-fruit pool. [S4], [S10]

### 8.7 FAVOR_BALL — bring a ball into contact with the requester

The objective is a **physics collision between a qualifying ball and the requesting NPC** while the favor is active.

- The ball may have existed before acceptance.
- Being near the NPC or merely entering the NPC's acre is insufficient.
- A decorative ball item in inventory is insufficient.
- Do not require the ball to be recently spawned or the active player to be its last recorded kicker.
- Credit the active local player at the qualifying collision, matching the reference event's attribution model.

On the first qualifying collision:

1. Store the event ID, ball ID, NPC ID, current player, and time.
2. Enter OBJECTIVE_READY and reserve that claimant.
3. Stop or settle the ball according to ordinary collision physics.
4. Request the NPC's acknowledgment conversation if interaction can begin.
5. If another interaction prevents it, keep the evidence and let the claimant report later.

The ready event alone does not create a reward: the claimant must acknowledge/report to the NPC before the objective's deadline. Once the report commits, reward and retention follow the common rules.

Destroying the ball after a successful recorded collision does not undo this favor. Destroying it before contact provides no credit. The world-ball system must make a usable ball obtainable; the quest system does not grant an inventory parcel. [S4], [S8]

### 8.8 FAVOR_SNOWMAN — build a snowman in the home acre

The objective is a **new snowman-combination event after acceptance**, in the requester's home acre.

- Any completed snowman quality/proportions qualify.
- A snowman that existed before acceptance does not qualify.
- The condition is acre membership, not an arbitrary distance from the house.
- No snowman is consumed on completion.
- A separate perfect-snowman reward system must remain independent.

On a qualifying combine event, store the exact snowman ID and builder player ID, then enter OBJECTIVE_READY. Only that builder may report it.

The first intact qualifying snowman owns the claim. Further builds do not overwrite it. If that specific snowman is destroyed before the report commits, invalidate the evidence, clear the claimant, and return the job to ACTIVE. Destruction of a different snowman does not clear credit.

That per-instance destruction rule is an adaptation; the reference stores player credit and home-acre information without an exact snowman binding.

Starting requires December 25–February 17 and 08:00–16:59:59. Completion is permitted outside that daily starting window, but still before the one-day deadline and season closure. [S4], [S5], [S9]

### 8.9 FAVOR_FLOWERS — increase the home acre's flower population

On acceptance, snapshot the count of planted flower objects in the requester's home acre, including seedlings and mature flowers:

~~~text
baseline = CountPlantedFlowersIncludingSeedlings(homeAcre)
target = baseline + 4
~~~

The player supplies planting resources; this template does not hand out free seeds.

At report time, count again. Complete if current count >= target. Count all supported species/colors equally. Count one planted object once regardless of growth stage; a seedling becoming mature does not add another plant. Inventory seed bags, decorative furniture, removed plants, and dead/nonexistent plants do not count.

Consequences:

- Adding four qualifying plants satisfies the goal.
- Planting four and destroying two may leave the goal incomplete.
- Replacing four existing plants without increasing the net count does not satisfy it.
- Planting farther away within the same acre still counts.
- Planting next to the house but across the acre boundary does not count.
- Contributions from different players can combine. The first eligible local player to report the reached total claims the reward; individual planting authors are not tracked.
- Recheck the current total in the completion transaction, even if an earlier UI preview said it was ready.
- Keep the flowers in the world after success.

The first-reporter rule replaces the reference's incidental save-time player attribution. If the house moves to a different acre before completion, cancel without penalty instead of silently moving the target or changing the baseline.

Offer eligibility checks at least four empty plantable cells and at most twenty mature flowers. The baseline count and mature-flower cap are different measurements. [S1], [S4], [S5]

### 8.10 FAVOR_FISH — provide any fish

The player chooses one ordinary fish-category item from their inventory. Any supported species, rarity, size, or sale value qualifies. Already-owned fish qualify.

Consume exactly the selected instance/unit. Pay the fish reward table; the fish's retail value does not affect the reward category or amount.

The picker may be cancelled. If the player explicitly began the “I have it” handoff and then provides nothing, apply the configured -1 effect once for that interaction command. Simply viewing a reminder or closing ordinary dialogue has no such effect.

This favor is available year-round and has a three-day deadline. [S5], [S12]

### 8.11 FAVOR_INSECT — provide any insect

Use the same item-selection, consumption, and cancellation rules as fish, but require the insect category.

Starting is permitted March 1–November 28 inclusive. The nominal deadline is three days, but November 29 at 00:00 closes the season and expires an unfinished job. The supplied insect may have been caught earlier; no post-offer capture condition applies. [S5], [S12]

### 8.12 FAVOR_LETTER — send the requester a letter

A two-day, shared, local-player-only favor. A drafted letter, an unsent letter in inventory, or an outgoing letter not yet delivered does not satisfy it.

The mail subsystem emits OnMailDeliveredToNpc. If the recipient is the active letter-favor requester and the record is still waiting:

1. Verify the sender is a local player profile and delivery time is within the deadline.
2. Accept the first eligible delivered mail ID.
3. Store its sender as claimant.
4. Compute and freeze rank and reply present using section 10.
5. Create a durable reply entitlement.
6. Apply ordinary mail relationship effects through the mail integration exactly once.
7. Enter REPLY_OWED.

A letter written before the favor began can qualify if its actual delivery occurs while the favor is waiting. Later letters cannot upgrade or replace the first accepted letter's reward.

The credited sender returns to the requester and selects the follow-up. Insert the reply directly into that sender's home mailbox when there is space. Do not route it through another ordinary postal delay. If the mailbox is full, retain the exact reply and retry on a later follow-up.

The default requires the claimant's explicit follow-up. The reference can attempt this during conversation preparation, including when another player initiates the conversation; that incidental behavior is not required here.

No generic handout reward table or extra ordinary +3 quest-success bonus applies. A rank with no present still produces a reply letter. Letter favors have no three-day completed retention. [S1], [S3], [S4]

## 9. Reward selection and delivery

### 9.1 Category weights

Draw one unbiased integer r in [0,99]. Traverse the nonzero category weights cumulatively in the displayed column order. Select the first category whose exclusive upper bound exceeds r.

Every row totals 100. “Currency base” is used only if Currency wins. Zero means the category cannot be selected, not that a zero-valued reward is handed out.

| Template | Furniture | Stationery | Clothing | Flooring | Wallpaper | Currency | Worn clothing | Currency base |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Clothing delivery | 40 | 0 | 0 | 0 | 0 | 30 | 30 | 200 |
| Foreign delivery | 40 | 0 | 0 | 10 | 10 | 40 | 0 | 1000 |
| Moved-away delivery | 20 | 0 | 0 | 20 | 20 | 40 | 0 | 1000 |
| Property delivery | 40 | 0 | 40 | 10 | 10 | 0 | 0 | 0 |
| Fruit | 0 | 0 | 0 | 30 | 30 | 40 | 0 | 500 |
| Ball | 40 | 0 | 0 | 30 | 30 | 0 | 0 | 0 |
| Snowman | 60 | 0 | 0 | 20 | 20 | 0 | 0 | 0 |
| Flowers | 60 | 0 | 0 | 20 | 20 | 0 | 0 | 0 |
| Fish | 80 | 0 | 0 | 10 | 10 | 0 | 0 | 0 |
| Insect | 80 | 0 | 0 | 10 | 10 | 0 | 0 | 0 |

These category weights are reference values. [S2]

### 9.2 Retrieval reward weights

The row is the number of borrowers visited, excluding the owner.

| Borrowers | Furniture | Stationery | Clothing | Flooring | Wallpaper | Currency | Currency base |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 0 | 75 | 25 | 0 | 0 | 0 | 0 |
| 2 | 25 | 25 | 25 | 0 | 0 | 25 | 500 |
| 3 | 50 | 0 | 25 | 0 | 0 | 25 | 750 |
| 4 | 65 | 0 | 0 | 5 | 5 | 25 | 1000 |

No worn-clothing outcome exists for retrieval. A short retrieval is usually rewarded with stationery; longer chains improve the table. Do not derive chain length from travel distance or elapsed time. [S1]

### 9.3 Item selection within a category

Default selection:

1. Determine the relevant category and ordinary/rare pool.
2. Remove missing, disabled, and otherwise nonrewardable entries.
3. Uniformly select among the remaining definition IDs.
4. If an optional rare pool is empty, use its configured ordinary fallback.
5. If an ordinary pool is empty, treat this as invalid content configuration, not a random runtime reward failure.

Uniform catalog selection is an adaptation; the reference delegates to stock-list selection whose full catalog-distribution logic is outside this job-system specification.

Duplicate possessions are allowed. Do not silently reroll because the player already owns an item. Category probabilities do not depend on friendship, item collection progress, or the supplied fish's value.

**Furniture special branch:** After selecting Furniture for an ordinary job, draw one integer in [0,9]. A zero attempts the reward giver's cached house-furniture candidate; all other values use the ordinary pool. If the cache is empty/invalid, fall back to the ordinary pool.

The cache contains one definition selected uniformly from eligible placements in the NPC's base house layout. Duplicate placements are separate candidates. Exclude clothing displays, umbrellas, fish, insects, gyroids, fossils, game-console/minigame collectibles, and any project item marked nonrewardable. Rebuild the cache when the relevant house-layout version changes, then persist it.

This reward creates a copy; it does not remove the NPC's actual furniture. The 10% branch is **conditional on Furniture being selected**, not a 10% chance on every job. Reply-mail presents do not use this branch. [S3], [S4]

**Worn clothing:** Use the reward giver's outfit captured before the successful clothing handoff. If that outfit is custom, disabled, or cannot be represented as an inventory item, choose ordinary clothing excluding the player's current outfit. If that exclusion leaves no candidate, use the configured ordinary-clothing fallback. Do not return a custom-design sentinel as an item.

### 9.4 Currency amount

Retain the reference money-power curve, with a deterministic integer jitter adaptation. Money power comes from an optional luck/feng-shui integration, not friendship. If those systems do not exist, return zero; do not make implementing feng shui a prerequisite for jobs.

For m <= 0, rate=0. Otherwise choose the row by m:

| Money power m | Rate before truncation |
| --- | --- |
| 1–99 | 0.3 × m |
| 100–199 | 0.7 × m − 40 |
| 200–299 | 2.7 × m − 440 |
| 300–399 | 1.3 × m − 20 |
| 400–499 | m + 100 |
| 500–599 | 0.6 × m + 300 |
| 600 or more | 0.0012 × m + 660 |

Floor the positive result, then cap rate at 700. Implement coefficients as rational integers so save replays do not depend on platform floating-point behavior.

Default jitterBasisPoints = 9000 + NextInt(2001), giving inclusive 9000–11000. Then:

~~~text
pay = floor(
    basePay * jitterBasisPoints * (100 + rate)
    / 1_000_000
)
~~~

Use at least 64-bit intermediate arithmetic. Freeze the resulting integer in the entitlement.

The reference instead uses a random sign and floating magnitude around ±10%; the discrete basis-point distribution above is the specified adaptation. “Base pay” is not a maximum.

At neutral jitter=10000:

| Base | Money power | Rate | Payment |
| --- | ---: | ---: | ---: |
| 200 | 0 | 0 | 200 |
| 1000 | 100 | 30 | 1300 |
| 1000 | 200 | 100 | 2000 |
| 1000 | 600 | 660 | 7600 |

If the host integrates the original-style fortune modifier, money luck contributes +100 and bad luck −50 to base money power, with a minimum of −80. Keep this policy in the money-power service and apply it once. [S1], [S7]

### 9.5 Exactly-once payout

Resolve category, item definition, money amount, and any relevant outfit/house snapshot once when entitlement is created. Retrying must not consume RNG or improve the outcome.

- Item reward: add exactly one item if capacity exists; otherwise retain ITEM_OWED.
- Currency reward: credit min(wallet free capacity, remaining amount), retain the rest, and mark complete only when remaining reaches zero.
- Default currency behavior uses a durable remainder rather than creating reference-style 30,000-unit money bags.
- A partial currency payment must not reroll the category, amount, or friendship effect.
- If handing over the objective item frees a slot, that freed slot can immediately receive the reward.
- A reward cannot be collected by another player profile.
- If a giver becomes permanently unreachable, use the recovery delivery route in section 11.

Apply ordinary success friendship once when the entitlement is earned, including when physical payout is deferred. This differs from the reference's immediate-versus-deferred relationship behavior and is intentional.

### 9.6 Completion retention

For every successful non-letter favor, set:

~~~text
retainedUntil = originalDueAt + 3 days
completedBy = claimant
~~~

The requester remains unavailable for fresh jobs and that favor subtype remains blocked town-wide until retainedUntil or its season closure. Settling an owed reward does not shorten retention.

If retention ends before a reward can be delivered, release the slot while keeping the independent entitlement. Do not lose the reward, and do not keep the subtype locked forever because someone has full pockets.

Personal deliveries and retrievals have no completed-favor retention. After their record and reward obligation are resolved, ordinary generation rules apply. [S1], [S5]

---


## 10. Letter scoring, reply rewards, and mail integration

### 10.1 Deterministic body evaluation

The default intentionally replaces the original byte-oriented language heuristics with a fully specified validator.

1. Evaluate only the message body. Address, recipient name, editor-generated salutation, and signature metadata are excluded unless the player typed them into the body itself.
2. Normalize the body to Unicode NFC.
3. Iterate Unicode scalar values, not UTF-8 bytes, UTF-16 code units, or visual grapheme clusters.
4. For counting/evaluation, remove Unicode White_Space characters and General_Category Cc/Cf control/format characters.
5. Let L be the remaining scalar count.
6. The letter passes quality only if:
   - L >= 5;
   - at least one remaining scalar is a Unicode letter or decimal digit;
   - there is no run of four or more identical consecutive letter/digit scalars in the filtered stream.
7. Repetition comparison is case-sensitive. A different scalar resets the run. Punctuation/emoji count toward length but do not independently trigger the repeated-letter/digit rejection.
8. The mail service's body limit applies before evaluation; default maximum is 1000 normalized scalar values. Reject an overlong submission at composition/send time rather than silently truncating a delivered letter for scoring.

Examples:

- Spaces/control characters only: L=0, fail.
- “aaaaa”: fail repetition.
- “a a a a a”: fail after whitespace removal.
- “aAaAb”: pass under case-sensitive comparison.
- “!!!!!”: fail because it has no letter or digit.
- “Hello!”: pass.
- A combined accented character normalized to one scalar counts as one; an emoji sequence may contain several scalars.

Do not insert a language model, spellchecker, dictionary download, sentiment analysis, or subjective “good letter” judgment into this system.

### 10.2 Rank formula

~~~text
lengthBucket =
    0 when L < 17
    1 when 17 <= L < 49
    2 when L >= 49

rank = lengthBucket
     + (qualityPass ? 3 : 0)
     + (hasActualAttachedPresent ? 6 : 0)
~~~

Rank is an integer from 0 through 11. A valuable gift does not score more than a cheap valid gift. The attachment must actually have been sent; a mention of a gift in text does not count.

The thresholds and rank bonuses are reference values; Unicode counting and quality rules are adaptations. [S4], [S6]

### 10.3 Reply-present table

| Rank | Reply present |
| ---: | --- |
| 0 | None |
| 1 | None |
| 2 | None |
| 3 | Ordinary clothing |
| 4 | Ordinary furniture |
| 5 | Ordinary flooring or wallpaper, 50/50 |
| 6 | Native fruit |
| 7 | Ordinary clothing |
| 8 | Rare-pool clothing |
| 9 | One uniformly selected nonnative fruit |
| 10 | Rare-pool furniture |
| 11 | Rare-pool flooring or wallpaper, 50/50 |

A “rare pool” is a catalog classification, not a guarantee of exclusive event furniture. Use the configured fallback if the host game has no rare pool.

Select the exact present definition at receipt, store it, and reuse it on every follow-up. These presents use catalog pools directly, without the ordinary job reward's 10% NPC-house branch. A no-present result still creates and delivers a reply letter. [S4]

### 10.4 Letter relationship effects

Use the same specified quality result for the default's letter relationship calculation:

~~~text
delta = 3
if not qualityPass:
    delta -= 5
if hasPresent:
    delta += 3
~~~

| Quality | Gift | Relationship delta |
| --- | --- | ---: |
| Pass | No | +3 |
| Pass | Yes | +6 |
| Fail | No | -2 |
| Fail | Yes | +1 |

Apply the effect to the sender's relationship with the receiving NPC. The original uses separate evaluators for quest reward rank and ordinary mail relationship effects; unifying them is an explicit adaptation.

If the existing mail service already owns relationship effects, have it apply this policy and report the effect ID. The job service must not apply it a second time. Do not add ordinary +3 quest-success friendship when the reply is collected. [S3]

### 10.5 First-letter and reply ownership

Only the first valid delivered letter while the favor awaits mail can claim it. “Valid” here means correct recipient, local sender, active waiting job, and delivery within its deadline; a low-quality letter still counts and receives the corresponding low rank.

The sender becomes claimant even if another local profile originally accepted the favor. Store the actual MailId and sender ID. The attached gift is consumed/transferred by ordinary mail delivery, not again by the quest service.

ConfirmLetterFollowup may only be invoked by the claimant in the default. Resolve its jobId through the durable reply ledger even if the live JobInstance has already been archived; a live quest slot is not a collection prerequisite. Check mailbox capacity inside the insertion transaction. Insert one reply immediately, mark the entitlement delivered, and close/release any still-live favor record. Do not require waiting for the next daily post round.

If there is no mailbox space, preserve the reply, its present, and its text key. Retry on the claimant's next follow-up; merely freeing a mailbox slot does not automatically insert the normal reply.

**Occupancy rule:** REPLY_OWED occupies the requester's favor slot and town-wide letter subtype until either:

- the reply is successfully inserted; or
- now passes the original letter dueAt.

On the latter path, archive the completed objective and release the slots, but retain the independent reply entitlement. Follow-up can still deliver it later. There is no three-day letter retention. The player's pending-reply conversation takes priority over new work from that NPC.

This durable reward and bounded occupancy policy is an adaptation to the reference's mailbox/deadline interaction.

## 11. Failure, cancellation, migration, and recovery

Every terminal transition has a reason and stable effect ID. A generic “failed” boolean is insufficient because world failures must not be treated as deliberate refusal.

### 11.1 Relationship effects on unsuccessful jobs

The default applies personal-job timeout effects once at automatic expiry, to the original requester's relationship with the owning player. This changes the reference's interaction-triggered timing.

| Situation | Default effect | Handling |
| --- | ---: | --- |
| Explicit offer refusal | -3 | Once per stored offer; no active job. |
| Voluntary abandonment of accepted personal job | -3 | Once; remove bound parcel/object. |
| Clothing-delivery timeout | -5 | Remove bound parcel; free personal record. |
| Property-delivery timeout | -5 | Remove bound parcel; free personal record. |
| Foreign/moved-delivery timeout | -1 | Remove bound parcel; free personal record. |
| Retrieval timeout before object pickup | 0 | Free record; no object to remove. |
| Retrieval timeout while returning object | -2 | Remove bound object; free record. |
| Uncompleted favor expires | 0 | Clear objective and release favor slots. |
| Cancel an explicitly begun supply handoff | -1 | Once per interaction command; job remains active. |
| Wrong item/recipient command | 0 | Reject without consuming anything. |
| Inventory/wallet/mailbox full | 0 | Preserve offer/job/reward as applicable. |
| NPC permanently removed, unsupported world change, corrupted objective binding | 0 | World cancellation/recovery, never blame the player. |

The -5/-1/-2 late-return values are drawn from reference failure paths; the automatic trigger, zero pre-pickup timeout, and uniform abandonment policy are specified adaptations. Do not also apply a second failure penalty when the player later asks what happened. [S1]

Friendship fallback bounds are 0–127. A host relationship service with an established different range may use that range, but must preserve once-only deltas.

### 11.2 NPC movement and removal

Use stable identity and a migration resolver.

- Delivery target moves to a reachable town: keep the same job and target; update its destination metadata.
- Delivery's original sender leaves, but destination still exists: the delivery remains completable.
- Retrieval owner moves and remains reachable: return the object to that same character at the new location.
- A retrieval borrower permanently disappears before pickup: cancel without penalty; do not fabricate a replacement borrowing link or secretly choose another NPC.
- A favor requester relocates its house to another acre: cancel unfinished geographically bound flower/snowman objectives without penalty.
- A favor requester permanently leaves the town: cancel unfinished favors and release retained slots.
- An NPC is temporarily unloaded/asleep: retain everything; neither condition is permanent removal.
- A different NPC receives the same species/name: it cannot inherit someone else's quest or unpaid reward.

Do not delete an earned entitlement when any participant moves or is removed.

### 11.3 Unreachable reward giver

When the reward giver is permanently unreachable, reroute that already-resolved obligation:

- Item reward or letter reply: create a recovery-mail entry for the claimant's home mailbox using the exact resolved item/present and one stable recovery-mail ID.
- If that mailbox is full, retain the recovery entry and retry on mailbox-capacity changes or load.
- Currency: automatically credit available wallet capacity and retain any remainder. Retry the same remainder on wallet-capacity changes or load.
- A recovery message may explain who sent the reward. Its wording is original project dialogue.
- No new reward RNG, second friendship gain, or new completed-retention interval occurs.

This automatic recovery applies only after permanent giver unreachability. Ordinary reachable-giver rewards still use conversation collection.

If a player profile is deliberately deleted, the host account/profile deletion transaction disposes of its owned jobs, items, and obligations together. An uncompleted shared favor claimed only by that removed profile must be cancelled/released, not left with an unresolvable claimant.

### 11.4 Content changes and corruption

On load or content-version migration:

1. Rebuild indexes from authoritative records.
2. Validate unique job IDs, instance bindings, ownership, slots, and entitlements.
3. A missing quest object with no committed handoff receipt is a broken active objective. Cancel it without penalty; do not spawn a replacement that might duplicate an object in another inventory.
4. A completed job with a valid entitlement remains payable even if its original parcel no longer exists.
5. A resolved reward item removed from the catalog is replaced by the explicit deterministic migration fallback for that category, and the replacement is recorded once.
6. An unknown template/rules version is migrated explicitly or cancelled without penalty. Do not reinterpret its payload as another template.
7. Duplicate bindings or conflicting entitlements are quarantined by stable ID and logged; never pay both copies.
8. Perform cleanup in one save transaction.

A failed load check must produce development diagnostics. Production handling should preserve earned value and recover the world without exposing internal IDs to the player.

## 12. Persistence, deterministic randomness, and transactions

### 12.1 What must be saved

Persist:

- All active/retained jobs, payloads, original deadlines, current-stage deadlines, claimant IDs, and pending decisions.
- Complete stable offers and whether their refusal effect has already been applied.
- NPC fresh-generation flags, patience counts, remaining ticks, and exploration budgets.
- Quest-item bindings and inventory instances in the same save revision.
- Unpaid ordinary entitlements, partial-currency remainder, reply entitlements, and recovery-mail entries.
- Per-effect delivery/friendship status.
- Stable counters for IDs and idempotent command/event processing.
- RNG states and catalog/rules versions used by live records.
- Remembered foreign NPC identity and last-moved NPC identity.
- House-furniture reward caches.
- Last reconciled game-time watermark.
- Reward-capacity reservations.

Do not reconstruct any of these by rolling again on load.

### 12.2 Slot lifetimes and bounded storage

The gameplay rules are independent of the physical storage layout:

- A personal job occupies its delivery/retrieval slot from acceptance until failure/cancellation or complete reward settlement.
- A delivery's emptied inventory slot is free after handoff even while its personal job slot remains occupied by REWARD_OWED.
- Favor occupancy follows sections 9.6 and 10.5.
- Paid closed jobs can become compact terminal receipts; retain enough identifiers/sequence high-water marks to reject replay.
- An offer uses an offer slot, not an active personal or favor slot.

For a constrained implementation, use a default **128-slot town-wide reward-obligation pool**, shared by ordinary entitlements and replies. Every accepted job reserves one slot in this pool before becoming active. A shared favor reserves one slot without yet knowing its winning player.

On objective completion, convert the reservation into the actual entitlement. On cancellation before earning, release it. After full payout/reply insertion, release it. A retained favor with an already-delivered reward needs no obligation slot.

If no slot is available, preserve a candidate offer and return REWARD_BACKLOG_FULL on acceptance; do not accept an objective whose future reward cannot be recorded. Do not drop older earned rewards to make space.

The default maximum personal job records is 4 × (15 + 5) = 80. There can be up to 15 requester-favor records by structure, although seven subtype locks limit simultaneous occupying favors to seven. Keep strings and catalog assets in shared definition data, not copied into each job.

### 12.3 Atomicity and saving

One logical transaction must include every linked mutation: job phase, inventory changes, currency, relationship delta, entitlement creation/delivery, RNG advancement, and idempotency result.

If the host save format supports transactions, use them. Otherwise:

1. Build a validated change set in memory.
2. Apply it to one authoritative snapshot.
3. Journal or save a new revision with a commit marker/checksum using the host's existing safe-write strategy.
4. Present success only after the logical commit.
5. Recover either the old or the new complete revision after an interrupted save.

Do not require a physical disk/card write every simulation tick. Cooldown ticking marks state dirty; save it at normal safe checkpoints. Critical economic changes must participate in the same safe save boundary as the host's inventory/money changes. The essential guarantee is that a saved revision cannot contain “item removed, reward forgotten” or “reward paid, claim still unpaid.”

Crash tests operate across these save boundaries. An unsaved reset may legitimately return to the previous complete save; it must not restore only half a transaction.

### 12.4 RNG contract

Use deterministic bounded draws. At minimum, separate:

- Offer/category/target/chain decisions.
- Reward category/item/currency decisions.
- House-furniture cache selection.

Persist each stream. GetConversationOptions and other previews consume no random values. Reward retries and repeated shown retrieval decisions consume no random values.

An existing stable engine PRNG is preferred. If there is none, this complete fallback is sufficient for gameplay:

~~~text
NextUInt32():
    state = (1664525 * state + 1013904223) mod 2^32
    return state

NextInt(n):
    require 1 <= n <= 2^31 - 1
    limit = floor(2^32 / n) * n   // use 64-bit arithmetic
    repeat:
        r = NextUInt32()
    until r < limit
    return r mod n
~~~

Initialize the three 32-bit states from the world's low 32 seed bits XOR 0x9E3779B9, 0x85EBCA6B, and 0xC2B2AE35 respectively. Zero is a valid state for this generator. This is a specified implementation fallback, not the reference game's RNG.

List candidate IDs in ascending stable-ID order before sampling, so container iteration order does not alter outcomes. Record RNG stream/version in the save schema if it may change in the future.

For deterministic tests, inject a queued-draw fake. Verify probability tables by enumerating all legal bounded draws rather than depending on a flaky random-frequency test.

### 12.5 Load sequence

1. Read and validate save schema/checksum.
2. Migrate records and catalog references.
3. Restore inventory, jobs, entitlements, RNG, and identities together.
4. Rebuild requester, subtype, ownership, and item-binding indexes.
5. Validate capacity reservations and reconcile broken references.
6. Ask the mail/world schedulers for queued authoritative events since the saved watermark.
7. Process those events chronologically before advancing expiry beyond them.
8. Reconcile forward elapsed calendar time and season closures.
9. Leave active-time cooldown counters unchanged by offline duration.
10. Retry permanent-unreachability recovery deliveries.
11. Expose normal dialogue options.

Do not reset cooldown/patience or reroll stored offers merely because a scene or session was recreated.

## 13. Dialogue and presentation contract

This is an integration contract, not a requirement to add a new quest-log UI.

Use the existing interaction style. At minimum, support original text for:

- Ask for work.
- Offer description, target name/location, object, and acceptance/refusal.
- No job today/right now, cooldown, and annoyed/refusal tone.
- Full pockets before acceptance or pickup.
- Delivery handoff and wrong-recipient feedback.
- Borrower referral and “I still have it” pickup.
- Reminder naming the current target, not a stale previous borrower.
- Supply-item selection and cancellation.
- World objective incomplete/ready.
- Reward thanks, full inventory/wallet, partial amount still owed, and later collection.
- Letter awaiting delivery, received, mailbox full, and reply sent.
- Already completed by another player.
- Retained-completed favor with no fresh work.
- Timeout, voluntary abandonment, and world-driven cancellation.
- Recovery mail for an unreachable reward giver.

Dialogue parameters come from structured state: requester, current target, item definition, requested fruit, flower remaining count, due time, claimant, and reward result. Do not parse prose to infer gameplay.

Personality changes phrasing and tone, plus the explicit patience table. It does not silently alter job weights or reward tables.

Do not reveal internal terms such as “75% gate,” “subtype lock,” “revision conflict,” or “reward-obligation pool” in normal player dialogue. Development diagnostics can expose them.

The UI must not promise that a random offer will exist, that a reward is known before it is resolved, or that planting/catching occurred after acceptance when that is not part of the actual objective.

---


## 14. Implementation sequence and acceptance tests

### 14.1 Recommended implementation sequence

| Stage | Implement | Exit condition |
| --- | --- | --- |
| 1. Contracts | Inspect repository systems; map stable identities, clock, inventory, mail, saves, and world events; validate content pools. | No duplicated authoritative systems; all required adapters and defaults are accounted for. |
| 2. Domain and save | Records, transactions, slot reservations, serialization, RNG, indexes, clock reconciliation, idempotency. | Save/load and interrupted-transaction tests pass with synthetic jobs. |
| 3. Availability | Conversation priority, stable offers, candidate selection, seasons, no-job cooldown, patience. | Deterministic generation/cooldown tests pass. |
| 4. Personal jobs | Local/remote/property/clothing deliveries and retrieval chains. | Complete handoff, referral, deadline, inventory, and identity tests pass. |
| 5. Supply/world favors | Fruit, fish, insects, ball, snowman, flowers; shared claims. | Every actual host-world event reaches the domain and produces the specified result. |
| 6. Mail and rewards | Letter scoring/replies, all reward tables, currency, catalog/house selection, recovery. | Exact reward, full-capacity, and mailbox tests pass. |
| 7. Presentation | Connect approved interaction UI and original dialogue; reuse assets. | Every result code has comprehensible feedback, with no gameplay mutation in rendering. |
| 8. Verification | Run required build/tests, inspect saves, exercise real gameplay flows, document conditional travel support. | Definition of done in section 18 is met. |

Do not defer all persistence work until after UI integration. The same domain transitions should power tests and real gameplay.

### 14.2 Minimal deterministic test fixture

Create six synthetic residents:

| NPC | Home acre | Personality | Current outfit |
| --- | --- | --- | --- |
| A | (0,0) | Normal | shirt_a |
| B | (1,0) | Peppy | shirt_b |
| C | (2,0) | Lazy | shirt_c |
| D | (3,0) | Jock | shirt_a |
| E | (4,0) | Cranky | shirt_b |
| F | (0,0) | Snooty | shirt_c |

Use two local player profiles and one visitor profile. Include:

- Native apple and at least one nonnative fruit.
- Three ordinary shirts, two ordinary furniture items, stationery, flooring, and wallpaper.
- At least one rare-pool item or an explicit ordinary fallback per relevant category.
- One normal fish, one insect, one property object, and one foreign NPC with a stable reachable identity.
- A home acre with configurable empty-cell, seedling, and mature-flower counts.
- Fake world/mail events and an injected queued-draw RNG.
- Controllable game-calendar time and active ticks.
- A wallet and mailbox with configurable capacity.

Do not use actual branded NPC names or require a particular production map to run domain tests.

### 14.3 Generation and conversation cases

| ID | Given / action | Required result |
| --- | --- | --- |
| G01 | Fresh enabled NPC, gate draw 0 of 4 | NO_JOB; disable fresh generation; initialize personality cooldown. |
| G02 | Gate passes, winter insect candidate | NO_JOB; no alternate-template roll. |
| G03 | Enumerate all family/subtype draws | Equal family weights, four equal delivery subtypes, seven equal favor subtypes. |
| G04 | Vary friendship with identical world and RNG | Same generation and reward-category decisions. |
| G05 | A chooses a local recipient | Never A or F; sleeping/unloaded B remains selectable. |
| G06 | No eligible local target | NO_JOB and cooldown; no invalid identity in an offer. |
| G07 | Foreign/moved memory missing or destination unsupported | Candidate fails; no impossible parcel is created. |
| G08 | A second same-subtype foreign/moved offer/job exists for that player | Reject duplicate; self-offer does not reject its own acceptance. |
| G09 | Repeat GetConversationOptions 100 times | No RNG, cooldown, offer, inventory, or relationship mutation. |
| G10 | Revisit stored offer | Same target/item; no new gate draw. |
| G11 | Stored offer's season has ended | Mutating ask/accept invalidates it once; no immediate reroll. |
| G12 | Delivery offer with full pockets | Stable offer retained; no parcel, active deadline, refusal penalty, or no-job cooldown. |
| G13 | Explicit decline, then replay same command/decline same offer | One -3 effect; no active job. |
| G14 | Close dialogue or choose casual chat | No refusal effect. |
| G15 | A owes a reward and could generate a new job | Reward collection takes priority. |
| G16 | NPC asleep/in cutscene | Ordinary interaction blocked even with stored offer/reward; no generation roll. |
| G17 | Annoyed NPC has a valid existing handoff | Permit handoff; suppress fresh work at refusal threshold. |
| G18 | Successful personal completion with no blocking state and no prior lock | No automatic success cooldown; a later fresh ask may roll. |
| G19 | Two offers for the same favor subtype accepted in succession | First acceptance wins; second revalidation does not create another occupying favor. |
| G20 | Reward-obligation pool is fully reserved | Preserve offer; acceptance returns REWARD_BACKLOG_FULL; no unpaid future objective is accepted. |

### 14.4 Cooldown and patience cases

| ID | Given / action | Required result |
| --- | --- | --- |
| C01 | Start normal cooldown; advance 1000 eligible ticks | Remaining 3000; budget 0. |
| C02 | Advance 10,000 more ticks in same acre | Remaining still 3000. |
| C03 | Cross an acre boundary | Budget replenished; remaining unchanged. |
| C04 | Cross twice without ticking | Budget remains at most 1000, not 2000. |
| C05 | Remaining 250; budget 1000; tick 250 | Unlock, reset conversation count, clear budget. |
| C06 | Advance real time in menus/dialogue/offline | Active cooldown counters unchanged. |
| C07 | Run render loop at 30/60/120 FPS for equal active time | Same domain tick/cooldown result. |
| C08 | Reach personality mild threshold | Restart its shared countdown; do not independently disable fresh-generation flag. |
| C09 | Reach refusal threshold, then rejected talk attempts | Refusal feedback; no further count increments or infinite countdown restarts. |
| C10 | Save/load or enter a same-acre house | Preserve counters; no fake acre replenishment. |
| C11 | Tick 1000, cross, then tick 1000 within one host update batch | Consume 2000 remaining ticks, preserving event order rather than collapsing both budgets. |

### 14.5 Personal job and inventory cases

| ID | Given / action | Required result |
| --- | --- | --- |
| P01 | Accept clothing delivery | Exactly one bound parcel, one personal record, one obligation reservation. |
| P02 | Move/sort parcel across inventory slots | Binding follows exact instance; delivery remains valid. |
| P03 | Attempt sale/open/mail/use/drop/generic gifting | Reject without destroying parcel or job. |
| P04 | Present ordinary item of same definition or parcel to wrong NPC | Reject; consume nothing. |
| P05 | Complete clothing delivery with full pre-handoff pockets | Parcel removal frees space; recipient wears new shirt; old shirt snapshot can be reward. |
| P06 | Deliver to destination | Destination rewards; no return-to-sender requirement. |
| P07 | Accept retrieval with full pockets | Allowed; no object is created yet. |
| P08 | Force retrieval stop at first borrower | One borrower counted; object pickup then owner return; row 1 reward. |
| P09 | Force three continuations | Visit B/C/D/E once each, then forced pickup; at most four borrowers. |
| P10 | Continue to a new borrower | Clear new stage decision to UNDECIDED; do not reuse the previous referral. |
| P11 | Continue chosen with no eligible unused borrower | Force pickup endpoint; no repeated participant or infinite retry loop. |
| P12 | Pickup with no free slot | Same persisted stop decision; no object, no deadline refresh, no penalty. |
| P13 | Commit referral or actual pickup | Current deadline refreshes by two days; reminders do not refresh it. |
| P14 | Return retrieval object | Original owner pays; count excludes owner and return trip. |
| P15 | Travel with remote delivery, save/load in destination | One owner, one parcel, same job/target IDs; no clone in source town. |
| P16 | Target moves again while reachable | Follow that same identity; do not retarget by name/species. |
| P17 | Target permanently disappears | World cancellation with no relationship penalty; no orphan parcel. |
| P18 | Personal reward pending | Personal record stays occupied even though its former parcel slot is free. |
| P19 | Invoke handoff/report/collection without the correct active conversation context | Reject; no remote item consumption, reward, or relationship effect. |

### 14.6 Favor objective cases

| ID | Given / action | Required result |
| --- | --- | --- |
| F01 | Requested pear; supply another nonnative fruit | Reject; exact requested definition is required. |
| F02 | Correct fruit/fish/insect acquired before acceptance | Accept if ordinary-condition and owned. |
| F03 | Expensive versus cheap fish with identical reward RNG | Same category weighting; value is not a multiplier. |
| F04 | Fish-like quest parcel or locked item | Not selectable as an ordinary fish contribution. |
| F05 | Ball near requester without collision | No completion evidence. |
| F06 | Preexisting ball collides with requester while active | Record claimant/evidence; no last-kicker requirement. |
| F07 | Ball collision while dialogue cannot start | Persist evidence; allow later claimant report. |
| F08 | Repeat collision event | No second claim/reward. |
| F09 | Existing pre-offer snowman | Does not satisfy the job. |
| F10 | New imperfect snowman in correct acre | Qualifies; perfection is irrelevant. |
| F11 | New perfect snowman in adjacent acre | Does not qualify. |
| F12 | Destroy exact credited snowman before report | Clear evidence/claim; active job can be fulfilled again. |
| F13 | Destroy a different snowman | Preserve the credited snowman evidence. |
| F14 | Flower baseline 10; current total 14 including seedlings | Report succeeds; plants remain. |
| F15 | Plant four but remove two; current total 12 | Report fails; count is net total. |
| F16 | Replace four flowers, total stays 10 | Report fails. |
| F17 | Plants across acre border | Do not count even if visually near the house. |
| F18 | Two players contribute plants | First valid reporting local player wins; no per-planter tracking required. |
| F19 | Successful non-letter favor with original deadline D | Retention ends at D+3 days, independent of report time and payout delay. |
| F20 | Completed-retained subtype exists elsewhere | Blocks new same-subtype favor and fresh work at its own requester. |

### 14.7 Reward and letter cases

| ID | Given / action | Required result |
| --- | --- | --- |
| R01 | Enumerate r=0..99 for every reward row | Exact category counts equal the configured weights. |
| R02 | Fish reward r=79/80/89/90/99 | Furniture/flooring/flooring/wallpaper/wallpaper. |
| R03 | Retrieval borrower counts 1,2,3,4 | Select the matching row; never count owner as borrower. |
| R04 | Furniture category then house draw 0 of 10 | Use valid cached candidate; invalid cache falls back; no house object removed. |
| R05 | Nonfurniture category or reply-mail present | No house-furniture bonus draw. |
| R06 | Currency base 1000, neutral jitter, money power 0/100/200/600 | 1000/1300/2000/7600. |
| R07 | Currency jitter endpoints and very high money power | Correct integer floor, cap rate 700, no 32-bit overflow. |
| R08 | Full inventory after a world favor | Earn fixed item entitlement and +3 once; collect same item later. |
| R09 | Wallet has room for only part of payment | Credit that part; retain exact remainder; replay cannot double credit. |
| R10 | Reward retries across reload | No RNG advancement/reroll; one relationship effect. |
| L01 | Delivered letter lengths 16/17/48/49 | Length buckets 0/1/1/2. |
| L02 | Passing length17, no gift | Rank4, ordinary furniture. |
| L03 | Passing length49, gift | Rank11, rare-pool flooring/wallpaper. |
| L04 | Failing length49, gift/no gift | Rank8 clothing / rank2 no present. |
| L05 | Empty body with gift | Rank6 native fruit; relationship +1. |
| L06 | Whitespace-separated repeated letters | Repetition detected after filtering. |
| L07 | Punctuation-only body length>=5 | Quality fails due no letter/digit. |
| L08 | Draft or queued-undelivered letter | No quest completion. |
| L09 | First qualifying delivered letter followed by better letter | Original claimant/rank/present remain unchanged. |
| L10 | Different local player sends first qualifying letter | Sender owns reply; accepting player cannot take it. |
| L11 | Full mailbox at follow-up | Exact reply remains owed; no RNG or duplicate friendship. |
| L12 | Deadline passes after receipt while mailbox full | Favor slot releases; durable reply still collectible by jobId. |
| L13 | Rank0/1/2 reply inserted | Reply exists without an attachment; no generic +3 success bonus. |
| L14 | Repeat receipt/follow-up command or load | One accepted mail effect, one reply, at most one attached present. |

### 14.8 Time, persistence, and recovery cases

| ID | Given / action | Required result |
| --- | --- | --- |
| S01 | Complete exactly at dueAt, then at dueAt+1 | First allowed; second expires under strict default. |
| S02 | Snowman offers Dec24/Dec25/Feb17/Feb18 and 07:59/08:00/16:59/17:00 | Exact inclusive date/hour boundaries. |
| S03 | Flower offer Feb24/Feb25/Aug31/Sep1 | Exact season boundaries. |
| S04 | Insect offer Feb28 or29/Mar1/Nov28/Nov29 | Exact season boundaries, including leap year. |
| S05 | Seasonal job's due time extends beyond season end | Season closure wins; earned reward survives. |
| S06 | Letter delivered before dueAt in queued offline events, load after dueAt | Process event chronologically; preserve earned reply. |
| S07 | Unseen backdated event older than committed watermark | STALE_EVENT; no resurrection of failed job. |
| S08 | Backward clock adjustment | Shift live acceptance/stage/original due/current due/retention/evidence times together; no reroll. |
| S09 | Save/load at each active, ready, owed, retained, and terminal phase | Equivalent state and next deterministic outcome. |
| S10 | Interrupt each economic transaction before/after commit marker | Old or new complete state; no half-consumed parcel or duplicate reward. |
| S11 | NPC giver disappears after objective completion | Fixed recovery mail/currency remains available; no reward loss. |
| S12 | Missing bound object on load without handoff receipt | Cancel active objective without penalty; do not mint a duplicate. |
| S13 | Removed reward item definition | Apply configured migration fallback once; preserve claim ownership/value category. |
| S14 | Duplicate event/command after personal job archived | Terminal receipt/high-water mark prevents repeat reward. |
| S15 | Free reward pool slot after settling an old obligation | Previously blocked offer can be accepted without replacing the offer. |
| S16 | Fail personal job, then revisit requester repeatedly | Exactly one configured timeout effect. |

### 14.9 Required real-engine checks

Domain tests do not substitute for these integration checks:

1. Complete one clothing delivery and verify the actual NPC outfit and inventory result.
2. Complete a multi-borrower retrieval, including a full-pocket pickup retry and a save/load between stages.
3. Complete one fish/insect handoff through the actual item selector.
4. Trigger actual ball collision, snowman creation/destruction, and acre flower counting.
5. Send a real in-game letter, wait for the mail subsystem's actual receipt event, and collect the reply with a mailbox-full retry.
6. Save during an owed reward and reload; collect exactly once.
7. If travel exists, complete one remote delivery across a real town transfer.
8. Exercise ordinary frame rates and scene transitions to verify cooldown tick/acre integration.

Keep test scenes/assets reusable. Do not add an unrelated debug UI to the player experience.

## 15. Relationship to the opening employment tutorial

The reference's opening shop-owner work sequence uses related quest machinery but separate stages: work clothes, introductions, planting, deliveries, letters, and a notice-board task. Its enums include special startup/continuation variants that are not additional repeatable resident job types. [S6]

This specification's required implementation is the **repeatable resident system** above. A startup tutorial is optional and must be owned by the story/tutorial director, not randomly selected from the resident pool.

If the host already has a tutorial:

- Reuse handoff, inventory binding, mail-delivery, and world-counting primitives.
- Let the tutorial choose its next fixed objective.
- Keep tutorial-specific wages/debt reduction, uniform rules, shop dialogue, and progression outside random reward tables.
- Gate ordinary jobs through an explicit host tutorial-completed flag if the host design requires it.
- Do not apply ordinary no-job rolls or NPC conversational patience to required tutorial progression.
- Do not block the entire resident system on an unimplemented shop-owner tutorial.

No exact original tutorial pay amount or full stage ordering is asserted here; those are outside the repeatable-job requirement and were not required to establish its twelve templates.

## 16. Reference behavior that must not be confused with the default

This table records consequential historical details and intentional departures. It prevents a future agent from “correcting” a default into a source quirk accidentally.

| Subject | Reference evidence | Required default |
| --- | --- | --- |
| Asking for work | A candidate can be generated during ordinary dialogue preparation. | Generate only on explicit AskForWork. |
| Cached offer | Primarily subtype metadata; payload can be regenerated and eligibility checks bypassed. | Stable full payload, revalidation on command. |
| Contest refusal | Contest may already be active before accept/reject. | Decline never activates a job. |
| Session reload | Clears runtime offer/cooldown/patience state. | Persist it. |
| Recipient selection | Source helper has slot-order bias and home-acre exclusions. | Uniform eligible list with the same acre concept. |
| Foreign-fruit selection | Original branch has bias; a bugfixed branch samples uniformly. | Uniform configured nonnative fruit selection. |
| Daily limits | No daily quota/friendship gate found in the examined generation path. | No daily quota or friendship generation multiplier. |
| Success cooldown | No universal fresh-job lock after acceptance/completion. | Preserve that absence. |
| Active ticks | Exploration-gated countdown; exact real-time rate not established here. | Fixed 60 active ticks/sec. |
| Retrieval dead end | A failed onward-target search need not become final pickup. | Force final pickup if no eligible onward borrower. |
| Personal deadlines | Traced destination completion can precede expiry checks; ordinary personal jobs lack the same periodic cleanup as contests. | Consistent strict expiry. |
| Failure relationship timing | Some penalties occur when acknowledging/returning an overdue object. | Apply configured personal timeout effect once at expiry. |
| Ball | Collision plus successful immediate forced-conversation request advances it. | Persist collision evidence if the conversation cannot start. |
| Snowman destruction | Home-acre/player credit without a specific snowman-instance binding. | Bind and invalidate only the credited snowman. |
| Flower credit | Net acre count, with incidental save-time player attribution. | First valid report claims shared net-count completion. |
| Deferred reward | Can reroll; differs from immediate payout friendship handling. | Frozen entitlement and once-only relationship effect. |
| Currency overflow | Reference can use 30,000-unit money bags after wallet overflow. | Partial wallet credit plus durable remainder. |
| Completed favor | Original due time +3 days; other-player/deferred-reward paths can clear early. | Retention independent of payment or other-player conversation. |
| Letter quality | Byte count and language/prefix/repetition logic; a separate validator affects friendship. | One explicit Unicode validator for both. |
| Letter follow-up | Can insert reply during later conversation preparation, even from another player's interaction. | Claimant explicitly confirms follow-up. |
| Earned reply deadline | Reference contest cleanup can still remove a pending reply. | Reward remains; subtype/requester occupancy releases at original deadline. |
| Clock reversal | Original validity checks can reject backward changes, including a 28-day interval guard. | Shift live timing anchors while retaining calendar seasonal rules. |
| Lost/opened parcel | Ordinary quest-condition parcels are protected rather than openable presents. | Protect bound objects; treat missing bindings as recovery/corruption, not a fabricated normal “opened gift” mechanic. |

The source was inspected, not executed in an emulator for this document. Low-level binary layout, exact original dialogue message IDs, the full item-stock distribution, and original locale-specific mail prefix tables are not required compatibility targets.

## 17. Source register and traceability

All source links below are pinned to commit **986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0** of the public [ACreTeam/ac-decomp repository](https://github.com/ACreTeam/ac-decomp). Function names and comments in a decompilation are reconstructed labels; the rules cited here come from the conditions, state mutations, and tables that were inspected.

| Key | File / principal evidence | Used for |
| --- | --- | --- |
| [S1] | ac_quest_talk_init.c: aQMgr_actor_decide_quest, aQMgr_actor_check_occur, aQMgr_actor_set_quest_data, aQMgr_actor_new_quest, aQMgr_actor_get_errand_reward, aQMgr_actor_get_rate/get_pay, reward and conversation handlers | Generation, gates, chain progression, reward selection, deadlines, handoff, refusal/failure, retention. |
| [S2] | ac_quest_manager.c: l_set_delivery_data, l_set_errand_data, l_set_contest_data, quest registration, ordinary update, aQMgr_set_talk_info | Template definitions, category percentages, update integration, clothing snapshot. |
| [S3] | m_npc.c: mNpc_GetOtherAnimalPersonalIDOtherBlock, mNpc_ClearTalkInfo, mNpc_SetQuestRequestOFF, mNpc_TalkInfoMove, mNpc_TalkEndMove, mNpc_AddFriendship, furniture/mail helpers | Targets, personality-indexed cooldown, patience, relationships, furniture candidate cache, mail receipt. |
| [S4] | m_quest.c: mQst_CheckLimitOver, inventory-binding helpers, mQst_GetOccuredContestIdx, flower counters, mQst_GetPresent/GetMailRank/SetReceiveLetter/SendRemail, ball/snowman hooks | Time, subtype lock, item identity, objectives, reply rewards and mail lifecycle. |
| [S5] | ac_quest_contest.c_inc: completion predicates and seasonal clear procedures | Fruit/ball/snowman/flower/fish/insect/letter conditions; seasonal expiry. |
| [S6] | include/m_quest.h | Quest families, subtype enums, original state structures, letter constants, separate tutorial stages. |
| [S7] | m_private.c: mPr_GetMoneyPower | Fortune adjustment and money-power service boundary. |
| [S8] | ac_ball.c: ball/NPC collision branch | Actual collision hook rather than a proximity check. |
| [S9] | ac_snowman.c: combine and destruction branches | Post-combination objective event, quality independence, destruction event. |
| [S10] | m_field_info.c: mFI_GetOtherFruit | Nonnative-fruit selection and bugfixed alternative. |
| [S11] | m_mail_check_ovl.c, alongside m_npc.c's mail evaluators | Original locale/prefix/repetition machinery and why the default specifies its own validator. |
| [S12] | m_submenu.c: normal-condition item category filters | Eligible fish/insect/sale/gift selections. |
| [S13] | actor/npc/ac_npc_act_get.c_inc: clothing receive action | Actual NPC outfit change. |
| [S14] | m_tag_ovl.c: quest/present menus, handoff, sale and mail checks | Protected quest objects and actual inventory consumption. |
| [S15] | m_start_data_init.c: mSDI_StartInitAfter | Session initialization of runtime NPC and talk state. |
| [S16] | include/m_npc_personal_id.h | Personality enumeration used by cooldown call sites. |
| [S17] | include/m_private.h | Fifteen inventory/delivery slots, five retrieval slots, player profile structure, wallet constant. |

A change that deliberately diverges from this document should update the corresponding configuration, acceptance case, and implementation report. Do not cite an adaptation as if it were established GameCube behavior.

[S1]: https://github.com/ACreTeam/ac-decomp/blob/986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0/src/actor/ac_quest_talk_init.c
[S2]: https://github.com/ACreTeam/ac-decomp/blob/986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0/src/actor/ac_quest_manager.c
[S3]: https://github.com/ACreTeam/ac-decomp/blob/986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0/src/game/m_npc.c
[S4]: https://github.com/ACreTeam/ac-decomp/blob/986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0/src/game/m_quest.c
[S5]: https://github.com/ACreTeam/ac-decomp/blob/986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0/src/actor/ac_quest_contest.c_inc
[S6]: https://github.com/ACreTeam/ac-decomp/blob/986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0/include/m_quest.h
[S7]: https://github.com/ACreTeam/ac-decomp/blob/986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0/src/game/m_private.c
[S8]: https://github.com/ACreTeam/ac-decomp/blob/986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0/src/actor/ac_ball.c
[S9]: https://github.com/ACreTeam/ac-decomp/blob/986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0/src/actor/ac_snowman.c
[S10]: https://github.com/ACreTeam/ac-decomp/blob/986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0/src/game/m_field_info.c
[S11]: https://github.com/ACreTeam/ac-decomp/blob/986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0/src/game/m_mail_check_ovl.c
[S12]: https://github.com/ACreTeam/ac-decomp/blob/986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0/src/game/m_submenu.c
[S13]: https://github.com/ACreTeam/ac-decomp/blob/986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0/src/actor/npc/ac_npc_act_get.c_inc
[S14]: https://github.com/ACreTeam/ac-decomp/blob/986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0/src/game/m_tag_ovl.c
[S15]: https://github.com/ACreTeam/ac-decomp/blob/986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0/src/game/m_start_data_init.c
[S16]: https://github.com/ACreTeam/ac-decomp/blob/986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0/include/m_npc_personal_id.h
[S17]: https://github.com/ACreTeam/ac-decomp/blob/986dce2514c2d4a2ba5fefb75360fa3e4f3a04c0/include/m_private.h

## 18. Definition of done

The implementing agent may call the feature complete only when:

- All twelve definitions and their domain behavior exist; remote jobs are explicitly gated by real travel capability.
- The ten local templates have functioning host-game integrations, including ball, snowman, flower counts, and actual NPC mail receipt.
- Generation is request-driven, deterministic, and respects the specified no-reroll, slot, seasonal, and personality rules.
- Retrieval has the correct maximum and reward count, with stable stage decisions.
- Every category table, letter rank, currency calculation, and relationship effect has deterministic tests.
- Offers, personal/shared ownership, world evidence, countdowns, parcels, and owed rewards survive save/load.
- Full inventory/wallet/mailbox, NPC movement, content migration, clock changes, duplicate events, and interrupted save boundaries have defined and verified outcomes.
- Already-earned rewards cannot disappear merely because their objective record expires or their NPC leaves.
- Existing UI/assets are reused and any new UI has followed the required mockup approval.
- The repository's required build/checks pass, and any conditional or unsupported host capability is reported explicitly.
- The implementation report identifies the chosen adapters, tested flows, known limitations, and any approved departures from this specification.
