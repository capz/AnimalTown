# Item action audit and proposed menus

Reuse the backpack's existing cream paper popup, green selection, Gorditas/IPL
fonts, item icons and A/B/R glyphs. Keep its measured width and existing placement.
The town continues to run behind it. No new artwork or persistent HUD is needed.

## Proposed popup changes

```text
  Leaf writing paper          Pear                     Oak floor (your home)
  ┌─────────────────────┐     ┌─────────────────────┐  ┌─────────────────────┐
  │ ▶ Write a letter    │     │   Drop              │  │ ▶ Spread on floor   │
  │   Drop              │     │   Plant             │  │   Attach to letter  │
  │   Attach to letter  │     │   Eat               │  │   Quit              │
  │   Quit              │     │   Bury              │  └─────────────────────┘
  └─────────────────────┘     │ ▶ Attach to letter  │
                              │   Quit              │  Wallpaper: Hang on wall
  Protected errand parcel     └─────────────────────┘  (only in your own home)
  ┌─────────────────────┐
  │ ▶ Details           │     Indoor paper omits Drop, as today.
  │   Close             │     Existing primary actions retain their order.
  └─────────────────────┘
```

- **Write a letter** opens the existing recipient picker and composer, using the
  selected stationery slot. It consumes that paper only on successful sending.
- **Attach to letter** opens that same flow with the selected ordinary gift
  attached. If no other stationery is available, explain that paper is needed
  and preserve the item. Paper can itself be a present when another sheet is
  available, matching the existing mail service's rules.
- Cancel/back returns to the originating backpack slot or town-hall entry.
  Cancelling, full outgoing post, invalid text, or other failure consumes nothing.
- The existing attachment selector will show eligible gift items (including
  stationery) and No present; protected parcels, empty slots, and the sheet being
  written on are excluded. Existing keyboard and confirmation panels are reused.
- Flooring and wallpaper get specific labels, and their apply action appears
  only in the active player's own home. Furniture placement is likewise limited
  to the player's supported garden/home context; final collision/space validation
  still occurs when chosen.
- Letter-writing/attachment actions follow the existing offline mail service's
  availability and are unavailable during town visits. No fake online send is
  introduced.

## Catalog audit (all 44 nonempty item IDs)

| Items | Existing actions to retain | Missing/corrected access |
| --- | --- | --- |
| All four tools | Equip; outdoor Drop | No missing tool operation |
| All six shirts | Wear; outdoor Drop | Attach to letter |
| Peach, pear, apple, orange | Eat; outdoor Drop/Plant/Bury | Attach to letter |
| Flower and both saplings | Outdoor Drop/Plant | Attach to letter |
| Furniture, including indoor-only lamp and non-giftable stall | Place in supported home/garden; outdoor Drop/Rotate | Attach for giftable furniture; suppress impossible placement contexts |
| Fish, insect, fossil, shell | Outdoor Drop; fossil Bury | Attach to letter |
| Notebook, camera, handheld game, glasses case | Outdoor Drop | Attach to letter; retrieval-bound property keeps Details/Close |
| Both stationery variants | Outdoor Drop | Write a letter; Attach to letter |
| Both floors and both wallpapers | Outdoor Drop; apply in own home | Specific floor/wall labels; Attach to letter; suppress apply in other interiors |
| Ordinary parcel | Outdoor Drop | Attach to letter; bound parcels stay protected |
| Legacy pocket letter | Read; outdoor Drop | Retain legacy read; real letters use mailbox Read/Take present/Discard |
| Lost umbrella, weed, retired watering can | Outdoor Drop; sell where allowed | No fabricated use/equip actions |
| Three money bags | Put in wallet; outdoor Drop; Bury where supported | Existing denomination/overflow checks retained |

Selling remains at the store. Job delivery/contribution remains with the NPC so
that distance, recipient, identity and claim checks are preserved. Mailbox letters
remain envelopes with real bodies/presents, rather than generic pocket item 19.
