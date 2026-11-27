# Debug villager visuals

Enable debug menus with three Z presses, then open **Start → Debug / testing →
Villager visuals (session)**. The layout was approved before implementation.

Up/Down selects Villager, Appearance or Apply appearance. Left/Right changes
the selected villager or cycles appearances. A on Apply appearance loads the
replacement. B returns to Debug / testing. The player and all present NPCs,
including visitors, can be selected. Appearance labels describe the artwork;
resident names are never renamed to the asset labels.

Only appearances unused by the current player/present NPCs are offered. A
successful swap frees the previous appearance for reuse, if no other actor
is displaying it. Every actor keeps its original game identity, saved species,
name, location, house, job state, relationships and inventory. Changes are
session-only and are discarded when returning to title or leaving the town.

Loading occurs asynchronously outside drawing, after reserving the cooked
package's measured allocation peak. The previous visual remains visible until
the new model and instance are complete. Failure keeps the old visual and
selection and releases the failed candidate. If memory is unavailable, wait
briefly and apply again. Repeated replacements dispose the retired instance
and its detached model. Shared species models remain under their existing
residency/LOD owner. The animation clock and attention bounds use the new rig.

Host regression checks exercise duplicate rejection, delayed/failed loads,
allocation backoff, package mismatch, repeated swaps, cancellation/teardown,
unchanged other actors and byte-identical serialized player/town/job state.
The normal importer checks cover all twelve GLBs and retained animation samples.
Dolphin checks apply all six appearances to the player, exercise movement,
and swap one NPC. Their captures and save-preservation report ship under
`release/scenarios/villager-visuals/` inside the Depot ZIP, with the exact build
and NKit image identity.
Physical GameCube validation remains outstanding.
