# Offline villager job UI mockups

Proposed layouts for the complete offline implementation. These reuse AnimalTown's
cream paper panels (#fff5dd), dark green text (#31483e), leaf-green selection and
nameplates (#356954), Gorditas headings and IPL body text. Existing dialogue
bubbles, item art, category sprites and controller glyphs are reused. The town
continues running behind every screen. No new persistent HUD panel is added.

The diagrams show content placement rather than a new font or new artwork.
Controls described below the diagrams are interaction specifications, not an
extra bottom-of-screen button legend.

## NPC conversation and errand choices

```text
             [live town remains visible]

                              ┌───────────────────────┐
                              │ ▶ My errands…         │
                              │   Got any work?       │
                              │   Let's chat          │
                              │   See you             │
                              └───────────────────────┘
    ╭ Juniper ╮
  ╭──────────────────────────────────────────────────╮
  │ Good to see you! What brings you over?            │
  ╰──────────────────────────────────────────────────╯
```

My errands appears when this NPC can receive a delivery/report or pay an owed
reward. It opens a short scrollable list when several actions are available.
Earned collection and handoff stay available when patience blocks casual chat.
A chooses, B closes. Existing text reveal behavior remains unchanged.

## Stored offer and acceptance

```text
                              ┌───────────────────────┐
                              │ ▶ I'll do it          │
                              │   No thanks           │
                              └───────────────────────┘
    ╭ Juniper ╮
  ╭──────────────────────────────────────────────────╮
  │ Could you take this shirt to Rowan?              │
  │ You will have two days after accepting.          │
  ╰──────────────────────────────────────────────────╯
```

A confirms a choice. B closes without declining. Capacity feedback appears in the
same bubble and preserves the offer. Declining gets original, personality-specific
dialogue; repeating the same declined offer cannot repeat its relationship effect.

## Job log and abandonment

```text
  ╭──────────────────────────────────────────────────╮
  │ Errands                                          │
  │ ▶ Shirt for Rowan                 Due Tue 14:35  │
  │   Juniper's borrowed notebook      Ask Hazel      │
  │   Flowers for Hazel               Ready to report│
  │   Letter for Rowan                Reply waiting  │
  │                                                  │
  │ Deliver Juniper's parcel to Rowan.                │
  │ [existing parcel icon]  Personal errand           │
  │                         [Abandon…]                │
  ╰──────────────────────────────────────────────────╯
```

Use the existing Jobs tab; show only the active player's personal jobs/claims and
relevant shared favors. A opens details or a contextual action. B returns. The
Abandon action opens an explicit confirmation explaining the -3 friendship
effect. Shared-favor details identify another player's existing claim without
offering collection. Completed retention is explained only when relevant.

## Protected parcels and contribution selection

```text
  ╭──────────────────────────────────────────────────╮
  │ Your pockets                                     │
  │       [existing item grid and highlight]          │
  │                                                  │
  │ Parcel for Rowan                                 │
  │ Juniper's delivery · Due Tue 14:35                │
  │                              ┌─────────────────┐ │
  │                              │ ▶ Details       │ │
  │                              │   Close         │ │
  │                              └─────────────────┘ │
  ╰──────────────────────────────────────────────────╯
```

Bound parcels use the existing icon with an errand marker and restricted actions.
An NPC contribution reuses this grid filtered to eligible ordinary items. A hands
over the selected item; B returns. Before an explicit fish/insect handoff selector
opens, NPC text explains that cancelling that offered handoff costs one friendship
point. Empty results and wrong-item feedback use the existing dialogue bubble.

## Mailbox and message reading

```text
  ╭──────────────────────────────────────────────────╮
  │ Mailbox                                          │
  │ ▶ [letter] Juniper               New reply        │
  │   [letter] Town shop             Order arrived    │
  │   [letter] Rowan                Read             │
  │                                                  │
  │                  [Read]  [Take present]           │
  ╰──────────────────────────────────────────────────╯

  ╭──────────────────────────────────────────────────╮
  │ From Juniper                                     │
  │ Dear Moss,                                       │
  │ Thank you for your letter…                       │
  │           [scrollable message body]              │
  │                                 [present icon]  │
  ╰──────────────────────────────────────────────────╯
```

A opens or selects an action; B returns. The stick scrolls long messages. A full
mailbox is explained during NPC follow-up and leaves the reply waiting; it does
not discard mail or automatically insert a normal reply later.

## Mail composition and notice-board composition

```text
  ╭──────────────────────────────────────────────────╮
  │ Write a letter                                   │
  │ To: [Juniper ▾]                 [Attach present] │
  │ Dear Juniper,                                    │
  │ [editable, wrapped and scrollable text]           │
  │                                    42 / 1000    │
  │                                                  │
  │ [existing-style paged controller keyboard]        │
  │                              [Send]  [Cancel]    │
  ╰──────────────────────────────────────────────────╯
```

Reuse onboarding's controller-text interaction where applicable. Keyboard pages
provide letters, numbers and punctuation; character count is normalized scalars.
A chooses a key/field/action, B returns or confirms discarding an unsent draft;
shoulder buttons change keyboard pages. Attachment selection reuses the pockets
grid and excludes protected parcels. Sending confirms the real recipient and
transferred present. It does not mark an undelivered letter as completed.

The notice board uses the same paper, list/read layout and composer. Its heading
is Town notice board, its action is Post, and the saved post displays its author.
The employment task uses a real submitted post rather than merely opening it.

## Offline town travel and NPC moving

```text
  ╭────────────────────────────────────────────╮
  │ Visit another town                         │
  │ Home:         Slot A · Oakrest              │
  │ Destination:  Slot B · Riverbend            │
  │                                            │
  │ Take your pockets and errands with you.    │
  │ Your home resident will be marked away.    │
  │                      [Travel]  [Back]      │
  ╰────────────────────────────────────────────╯
```

The same compact panel offers Return home during a visit. Unsupported, full,
missing or matching-clone cards show their status and disable Travel; unsaved
play requires choosing a save card before visiting. Card choices do not silently
change after an error. An eligible moving NPC uses existing dialogue choices:
Move to Riverbend / Not now, followed by an explicit destination confirmation.
The game does not evict an unrelated resident automatically.

## Interrupted trip recovery

```text
  ╭────────────────────────────────────────────╮
  │ Continue your trip                         │
  │ Your trip to Riverbend was interrupted.    │
  │ Insert Oakrest and Riverbend's cards.      │
  │                                            │
  │ Slot A · Oakrest        Ready              │
  │ Slot B                 No memory card     │
  │                      [Recheck]  [Back]     │
  ╰────────────────────────────────────────────╯
```

Once both identities match, Continue replaces Recheck and resumes the recorded
handoff. Errors preserve the prepared transfer. No button fabricates a new
parcel, discards an earned reward or unlocks an uncertain second player copy.

## Approval scope

The user explicitly approved this complete UI set on 20 November 2026.

Approval covers these layouts and their ordinary empty, full, unavailable,
confirmation, success and failure states in the existing visual language. New
flows or material layout changes still require a new mockup. It does not approve
future online UI or a new HUD. Implementation begins only after explicit approval.
