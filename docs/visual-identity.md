# AnimalTown visual identity

An original village identity built around a welcoming doorway, a gently curved
meadow line and two golden seed leaves. The mark is a little place to call home.
It accompanies the readable AnimalTown wordmark.

## Typography

Gorditas Bold: AnimalTown wordmark, title screen and major screen headings.
The console's native IPL font: dialogue, menus, descriptions, counters,
controller hints, selected items, speaker tabs and primary actions. Use mixed
case and the firmware's original advance widths. Selection emphasis comes
from colour and shape, rather than substituting a different text typeface.

Gorditas and its SIL Open Font License live in `assets/fonts`. Its heading
glyphs are baked at 48 pixels into a 512×512 IA8 atlas (512 KiB); the
1024×256 Gorditas wordmark adds 512 KiB. The IPL atlas uses 256 KiB plus shared
firmware decode storage: 131,360 ANSI bytes or 1,183,488 Shift-JIS bytes. No IPL
font data is stored in the repository or ISO. SDK PR #145 exposes it directly.
An original 512×256 RGB5A3 icon atlas adds 256 KiB. Printable ASCII is the
current runtime text coverage; additional languages need explicit mapping.

## Palette

| Token | Colour | Role |
|---|---|---|
| Fern ink | #31483E | Text and wordmark |
| Meadow moss | #356954 | Primary actions, speaker tabs and doorway |
| River mist | #95C6BA | Calm supporting colour |
| Terracotta | #C36D54 | Warm accents and meadow line |
| Warm honey | #E8B756 | Seed leaves and small highlights |
| Oat paper | #FFF5DD | Panels, speech and reversed text |

## Shape and composition

All UI follows the speech bubble's rounded language: 30-pixel panel corners,
gently convex bulging sides, pill HUD cards and selection backgrounds, round keyboard keys, offset soft
shadows and generous internal padding. Speech uses oat paper with a rounded
moss name tab and a short organic tail. Lettering must remain clear at 640×480
and within the television safe area. Major panel titles rock gently around
their centred pivots, up to 1.8 degrees.

The title screen centres the Gorditas wordmark and doorway mark. Supporting line:
“A little life, well rooted.” Menus remain quiet, with one leaf selection accent
at a time. The commit remains visible in IPL text outside the title treatment.
The backpack is a five-column grid with 10/20/30-slot tiers, warm pocket shapes,
original illustrated icons and a compact selected-item title popover.

## Exports

`tools/AnimalTown.Tools/BrandAssets.cs` produces the Gorditas atlas/metrics,
wordmark, 96×32 disc/save artwork and 32×32 memory-card icon. They
share one palette, mark and typography system. The identity has cozy life-sim
influences and its own name, mark, layout and colour system.
