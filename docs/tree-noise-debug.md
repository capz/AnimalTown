# Tree-noise debug controls

The title screen's **Debug configuration** includes:

- **Tree noise strength:** 0–300%, in 10% steps. 100% keeps the approved look;
  0% removes the texture and shows the original vertex colours.
- **Tree noise scale:** 25–400%, in 25% steps. Larger percentages make the
  features larger, with fewer repeats across a tree; 100% is the original scale.

Use A to increase, Left/Right to adjust, and Z to reset either control to 100%.
B saves title-screen changes in the existing `dev_config` card file.

During play, open **START → Debug / testing** for the same controls. Changes
apply immediately. **Save tree noise settings** persists them; **Restore saved
tree noise** reloads the last saved values. Editing, resetting or closing the
in-game menu does not write the memory card automatically. A failed save/load
reports the existing Slot A status. Restore uses defaults when no valid file
exists. These actions do not save, replace or regenerate the town.

Debug format v2 is 40 bytes with a checksum over the full header/settings/padding.
The decoder still accepts v1's 36-byte files, preserving all 27 existing options
and defaulting both tree controls to 100%. The two new options occupy slots 27
and 28; the gameplay menu has its own row mapping. Actions 14/15 remain transient.

The generated noise remains one shared opaque 32×32 I8 texture, 1,440 native
pixel bytes with six mip levels. Strength regenerates only that texture when
changed; disabling releases it. Scale changes only UVs in the three shared meshes,
reusing the texture. Season changes reuse its strength/scale. All updates happen
outside rendering passes. Unchanged settings cause no uploads, allocations or
extra draw calls. Superseded textures/meshes are disposed, and town teardown
releases the current resources.
