# Street lamp post shadows

See the [native test captures](screenshots/seasonal-shadows/README.md) for the
post shadow on the dirt path in the four-season review.

Street lamp posts participate in the existing scenery shadow union. Their
base, narrow pole and roof have a small analytic silhouette projected from the
same fixed northwest light as the other scenery. Roof dimensions and height
match the visible lamp mesh. Receivers follow the existing terrain clipping,
including water, building footprints and cliff boundaries.

Lamp posts share the fixed 32 scenery slots, with priority behind buildings
and ahead of trees. Cached coverage is rebuilt for changed scenery or terrain;
there are no additional textures, native cache storage or terrain draw passes.
The native host fixture still charges 522,016 bytes for the shadow cache,
below its 512 KiB cap, with the existing 160-page bound.

The native fixture verifies the base/pole/roof silhouette, receiver clipping,
deactivation and filtering gutters across page boundaries. Lamp geometry and
its shadow use the same socket selection when scenery changes.
