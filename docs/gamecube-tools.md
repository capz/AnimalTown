# GameCube tool audit and pocket menus

The GameCube Animal Crossing tool set is the fishing rod, net, shovel and axe,
with golden versions of those four. The original game's item definitions are
documented in [ACreTeam's decompilation](https://github.com/ACreTeam/ac-decomp/blob/master/include/m_name_table.h)
(`ITM_NET`, `ITM_AXE`, `ITM_SHOVEL`, `ITM_ROD` and their `ITM_GOLDEN_*` variants).
Watering cans belong to later games and are excluded from active acquisition and
equipment here. Handheld accessories such as umbrellas exist in the GameCube
game but are not implemented as equipment in AnimalTown; the current "Lost
umbrella" is a quest item.

## Implemented behavior

| Tool | Current AnimalTown behavior | Missing original-game behavior |
| --- | --- | --- |
| Fishing rod | Receives a generic river fish when interacting toward water. | Casting, visible fish targets, bites and timed reeling. |
| Bug net | Can receive a butterfly on interaction; misses are silent. | Visible catch targets and swing/catch detection. |
| Shovel | Recovers buried items; empty-ground interaction currently creates a buried fossil placeholder. | Empty holes and the complete digging workflow. |
| Axe | Converts a tree ground item to a sapling for replanting. | Repeated chopping hits, stumps and durability. |

These are basic interactions rather than complete tool simulations. Golden tools,
held tool models and tool-specific animations are not implemented; no menu entry
pretends that they are available.

## Menus and compatibility

* Supported tools offer **Equip**, **Drop**, **Quit** outdoors; **Equip**, **Quit**
  indoors, where loose drops are unsupported. Equipping swaps the previous tool
  into the selected pocket without losing it. The existing Z shortcut remains.
* Other items expose their own capabilities: for example Wear for clothing,
  Eat/Plant for fruit, Read for letters and Open money bag for currency. Outdoor
  actions are hidden indoors, and indoor-only furniture cannot offer Place
  outdoors. Popup height follows the number of available actions.
* Shop stock contains the four supported tools. Catalog rows exclude the retired
  watering can and map to stable item IDs rather than row numbers.
* Item ID 8 and catalog/save versions are retained. Existing watering cans remain
  readable, recoverable, droppable and sellable, but cannot be bought, ordered or
  newly equipped. A previously equipped one survives loading until replaced,
  returning to the pocket on swap. Same-day old shop stock is repaired too.

`PocketMenuChecks` covers action filtering and dispatch, tool swaps, catalog
mapping, store tiers and legacy single/split/profile save compatibility. Console
rendering and original-game mechanics require separate native/hardware testing.
