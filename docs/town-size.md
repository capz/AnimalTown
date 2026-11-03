# Town bounds and existing saves

The playable town is 8×8 screen-sized square tiles. Each screen tile is
20×20 logical metres, so collision and terrain use 160×160 one-metre fine cells.
The existing camera and cylinder radius are unchanged: reducing playable bounds
does not tighten the rolling view, and rendered ground still extends beyond the
viewport. Shops, hall, gate, campsite, six resident homes and the default player
plot all remain within the retained region.

The terrain buffer falls from 160,000 to 25,600 bytes (84% less). The map fills
the same 280-pixel square with 8 rows and columns, labelled A–H and 1–8. Its
cached native terrain image is now 32×32 rather than 128×128, using 4 KiB rather
than 64 KiB for both the RGBA upload buffer and native image individually. Map
preparation remains conditional on the map being open and its terrain changing.

Save schema 4 stores explicit dimensions and the smaller run-length terrain.
Schema 2/3 saves are interpreted using their original 400-cell row stride, then
cropped to x=0..159 and z=0..159. The retained cells are copied exactly; the
town is not regenerated or rescaled. The decoder streams old terrain runs into
the small destination buffer without allocating a full legacy terrain array.
Schema 1 still imports its original 80×72 raw terrain region.

Retained terrain edits, player positions, money, inventory, mail, upgrades and
resident progress remain intact. Trees outside the crop are removed. Dropped,
buried, planted and lost possessions outside it move to the town hall's Lost &
Found, keeping ownership and gaining the normal seven-day recovery window.
That window starts on the first calendar update, even after a long absence or
an intervening save, so catch-up rollover cannot immediately expire recovery.
Homes whose full 15×13 reservation would fall outside the crop relocate to a
valid retained plot, initially trying (12,22). Outdoor furniture moves by the
same offset; indoor furniture keeps its room coordinates. Outlying residents
return to a safe retained home area, and stranded players arrive at their home
or another walkable cell. If a heavily edited crop has no valid home plot or
safe arrival, migration fails and the original save remains available through
the existing alternating-file recovery policy.

The same shared codec handles console saves and stored backend state. The next
successful write emits schema 4; older game builds cannot read that new schema.
The original card slot is retained until a new alternating save passes readback
verification. Regression fixtures produced by the actual old encoder cover
retained and outlying homes, furniture, possessions and edited terrain, along
with original-format compatibility, malformed runs and stable re-saving.
