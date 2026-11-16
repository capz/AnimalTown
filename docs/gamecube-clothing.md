# GameCube clothing audit and pocket menu

GameCube Animal Crossing uses one changeable shirt/outfit slot, rather than
separate tops, bottoms, headgear, shoes, socks or facial accessories. The same
shirt design is drawn on the player's outfit shape. Custom designs and indoor
shirt displays are also original-game features, but are not implemented here.
See [Nookipedia's clothing history](https://nookipedia.com/wiki/Clothing) and the
original game's [cloth item category](https://github.com/ACreTeam/ac-decomp/blob/master/include/m_name_table.h)
and [shirt-changing implementation](https://github.com/ACreTeam/ac-decomp/blob/master/src/game/m_player_main_change_cloth.c_inc).

## Current implementation

* **Work shirt (ID 4)** is the only wearable catalog item. Every new villager
  receives one. It can be worn, dropped outdoors, sold and reordered after
  catalog ownership is recorded by the existing receive/pickup flow. There is
  no clothing stock/shop yet; possessing the starter shirt alone does not mark
  its catalog ownership. The Gift flag permits backend market listings, but a
  direct clothing-gift interaction is not implemented.
* Wear changes the player's saved `Shirt` slot and returns the previously worn
  catalog item to the selected pocket. An initial value of zero represents the
  model's default outfit and does not create an extra item. Swaps do not need a
  free pocket and leave the equipped tool unchanged.
* Clothing state is independent for each town profile and persists through the
  existing single-record and split/profile save formats. IDs and save versions
  are unchanged. The first tutorial objective credits wearing the Work shirt.
* The rendered character still uses its supplied model/materials: `Shirt` does
  **not yet change the visible outfit**. Clothing textures, changing animations,
  additional shirt designs, custom-design editing and indoor displays remain
  unimplemented. No menu entries claim those features are available.

## Menu behavior

Clothing opens with **Wear** selected, followed by **Drop**, **Quit** outdoors;
indoors it contains **Wear**, **Quit**, since loose indoor drops/display are
unsupported. Eat, Equip, Plant, Place, Bury, Read and Open money bag do not appear
for the shirt. Selling remains in the shop's existing Sell screen.

The shared popup uses one cached action order while retaining stable action IDs.
Popup height follows the filtered row count, and selecting Wear rather than an
unrelated successful action is required for the clothing tutorial credit.
`PocketMenuChecks` covers menus, full-pocket swaps, rejected actions, item
conservation, selling/dropping, held-tool preservation, save compatibility,
profile independence and allocation-free queries.

The existing Dolphin `--resume --item-navigation` scenario uses a disposable
outdoor fixture with the Work shirt in pocket zero. It follows the resident
picker, checks the three-row popup's D-pad/stick navigation and wraparound, and
uses the grain-aware highlight comparison. The backpack layout scenario reaches
Quit with Up from the first row, regardless of the item's filtered row count.
