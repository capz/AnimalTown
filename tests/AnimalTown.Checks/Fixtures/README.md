The two v3 fixtures were produced with the unchanged 400 x 400-cell SaveCodec
from game source 9b6c03ba60733c506ffceaae34b6a59a9f7495ad, before changing
the map size or save codec. They use seed 7319, sequence 42, day/month 100,
30 backpack slots, upgraded house/garden, outdoor and indoor furniture, catalog
ownership, mail, and edited terrain at (140,140) and (159,158).

town-20x20-v3.bin has the house at (12,22) and player at (18.25,38.5).
town-20x20-outskirts-v3.bin has the house at (300,300), player at
(350.25,345.5), one resident at (300,250), and dropped/buried/planted/tree
records in the removed outskirts. The money bag belongs to owner 2.
Both have exactly 128 trees within the retained 160 x 160-cell rectangle.

These fixtures verify actual old-format compatibility independently of the new
encoder. Tests also derive v2 and original 80 x 72 v1 wire layouts and check
corruption, run bounds, crop stride, relocation and stable v4 round trips.
