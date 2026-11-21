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

# Independent version-9 save fixtures

`town-shop-v9.bin` was emitted by the actual codec at
`6b00dcfccae0d86ae16f8c464afc0f113012db7e`; `town-paths-v9.bin` was emitted by
main's codec at `a310cb8` before merging the two schemas. Both use seed 7319,
day 21014, month `2027 * 12 + 7`, name Archive and wallet 12,345.
The shop fixture also has a 1,234-acorn chair listing in slot 0, a 2,500-acorn
table in the old slot 7, 987 acorns owed to absent profile 1 and an NPC purchase
from display 0. The paths fixture retains its original generated road overlay.
These are actual old-format bytes, never current saves with relabelled versions.

## Snow and offline-job save compatibility fixtures

These fixtures are immutable regression inputs. They were generated once from
the unmodified `shared/AnimalTown.Domain` source at the listed commits, using
the original `Capz.Hypercube.Core` 0.1.0-preview.48.1 dependency. Do not regenerate
them using current codecs; that would remove the independent migration check.
`SaveFormatCompatibilityChecks` pins their SHA-256 values before decoding.

| File | Original source and format | SHA-256 |
| --- | --- | --- |
| `snow-main-v14-catalog1.bin` | `48a0adcb79adc793b00a7647fd1caa190a0e8125`; monolithic v14/catalog 1 | `7802ae2429edfccff92c920b5fcace3b7517fc1e965d337e7f435e04d7c2518e` |
| `snow-main-split-v10-catalog1.bin` | Same commit; town v10/catalog 1 with six companion records | `a075d7aaaff998f52b4592203f1d67d5a74e7f4db09c68843c057babf0200eaa` |
| `jobs-pr136-v14-catalog2.bin` | `d9a82f608307688aa7e9bd4403288a5f91fa3407`; monolithic v14/catalog 2 | `28f7707602f4fc98008bb7afbf1100d1e2cf5bb4f01a48194c02795497ddc0e5` |

Both source codecs successfully decoded their own output. The snow fixtures
contain Juniper and Milo, shared daylight location 1, and two adjacent dents at
different recovery ages, saved one tick before the next visible recovery step.
The job fixture contains an accepted clothing delivery (identity serial 9 in
town 12345), its bound parcel, and watermark 1796904000.

The split fixture is a test-only container: a little-endian 32-bit record count,
followed by a little-endian 32-bit byte length and the original bytes for each
of the seven records, in `SplitSaveCodec.Encode` order. None of the saved record
bytes or headers were rewritten after the original codec generated them.
