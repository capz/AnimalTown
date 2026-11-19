# Memory-card selection

At boot, the first usable memory card supplies console/debug preferences.
Start selects the sole usable card if the other slot is empty. With two cards,
it selects the one with AnimalTown save data if exactly one has a town.
Settings-only archives do not count as town data.

Start opens the chooser when both cards have towns, both are empty, neither
is present, or no destination can be safely selected. The player can select a
usable card, recheck after inserting a card with X, or return to the title
with B. Reopening the chooser retains a confirmed choice until the process
restarts.

The approved layout reuses the cream paper panel, leaf-green selection, IPL body
and Gorditas heading, with no logo or new textures:

```text
┌────────────────────────────────────────┐
│             Memory card                │
│ Choose where to keep your town.        │
│ > Slot A       Ready / 59 blocks free  │
│   Slot B       No memory card          │
│   Play without saving                  │
│ A: continue   X: recheck   B: back       │
└────────────────────────────────────────┘
```

Unavailable cards remain labelled and cannot be selected. A new archive needs
34 free 8-KiB blocks: 32 record-store blocks plus two header/artwork blocks, and
one free directory entry. A current-size `AMTE/01` AnimalTown archive already
owns its update/recovery space and remains usable with zero free card blocks or
entries. Smaller or per-file legacy layouts need the full replacement capacity
before migration. A same-named file belonging to another game never counts as
reserved space. Card probing neither creates files nor formats a card.

`SaveCards` is the single destination for town loading, onboarding, manual
saves, resident/town deletion, video options and debug settings. Archive/block
adapters require an explicit device, so their raw block reads/writes use the
same slot as the mounted card. A card removal produces a failure for that slot;
it never redirects a save to the other card. Save instructions name the chosen
slot. Save format and identity remain unchanged.

**Play without saving** creates a temporary town and completes onboarding
without mounting either card or advancing a save generation. Town saves,
settings persistence and deletion are blocked for the rest of that session.
The review button says **Start playing**, with a short unsaved-progress notice.
The chooser also warns that this is a temporary town. There is no automatic
transfer of temporary progress when choosing a card for a later session.

Probing runs at boot, on Start, on X, and when confirming a slot. It uses
bounded 16-entry pages from the SDK's read-only card manager. If two cards
are present, archive record names distinguish towns from preferences without
loading or changing a town. The chooser uses the title renderer and releases
its font lease before resident selection.

Host card-adapter tests cover both slots, exact block/directory capacity,
existing archives, foreign ownership, retained selection, Slot B loading and
preference/deletion writes, card removal without fallback, and temporary
onboarding with zero card access. Release CI also boots disposable Dolphin
cards for `slot-b`, `choose-b`, `without-saving` and `no-card`; PNGs, reports and
logs are retained in the `memory-cards-COMMIT` Actions artifact. These are
emulator/host checks, not physical-card qualification.
