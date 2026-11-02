AnimalTown terracotta transition atlases

Four atlas PNGs, one per variant. Each is 768 x 768 pixels, RGB, opaque.
Each atlas contains nine 256 x 256 slices without gutters or labels:

 top_left     top       top_right
 left         center    right
 bottom_left  bottom    bottom_right

Slice rectangles use top-left image coordinates: x/y = 0, 256, 512.
Mode: STANDARD NINE-SLICE STRETCH. Keep four corners at fixed size, stretch
top/bottom horizontally, left/right vertically, and center on both axes.
Do not repeat/tile center and edge slices: stone joints are continuous
across the atlas but not periodic at opposite sides of each slice.
Previews show the native 3x3 with guides and an enlarged stretched layout.
The guides occur only on previews, never on the actual atlases.
All nine extracted slices reassemble byte-exactly to their source atlas.
Mipmaps/linear filtering may require engine-side atlas padding or UV inset.
The outside atlas perimeter is not designed to repeat the entire atlas.

Artwork: revised with the built-in image-generation tool using the original
animaltown_terracotta_9slice.zip found in Downloads as visual reference.
The spring revision established the shared terracotta arrangement; the
other three edits use it plus their original seasonal preview. Export
normalizes resolution without altering the continuous generated artwork.
Four revised atlases are stored in atlases/. Visual checks are in previews/.
