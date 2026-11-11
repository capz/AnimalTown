# Sunshine / Wind Waker water comparison (debug preview)

## Debug UI mockup — reuse existing settings panel

No new screen, asset or menu layout. Add one final option to the existing
**START → Settings → Debug / testing**, after **Ground mip distance**:

```text
┌───────────────────────────────────────────────┐
│  Debug / testing                    page 3 / 3 │
│                                               │
│    Season preview: Auto                       │
│    Villager attention bubbles: Off            │
│    Rim intensity: 100%                        │
│    SDK performance overlay: Off               │
│    Ground mipmaps: On                         │
│    Ground mip distance: Auto                  │
│  > Water rendering: Wind Waker                │
│                                               │
│  A / Left / Right: switch  ·  Z: Wind Waker    │
│  Session options 19-25 / 25                   │
└───────────────────────────────────────────────┘
```

A switches to `Water rendering: Sunshine`; pressing again switches back.
Left/right are captured on that row (L/R continue switching menu tabs).
Z restores Wind Waker. The option is **session-only**: saved-town,
calendar, collision, geometry revisions and season metadata are untouched.

## Rendering intent

- **Wind Waker:** identical current opaque/tile-textured surface + optional
  local wave mesh. No underwater floor pass.
- **Sunshine:** transparent, low-depth-write, two independently scrolling
  samples of a small shared wave texture. Individual hand-authored mip levels
  change highlight brightness and opacity with LOD, rather than being normal
  downsampled copies. Foam remains an independent bank overlay.
- **Underwater floor:** draw the **same immutable water meshes again**
  with an inward cylindrical transform, *before* the translucent surface.
  Share the game's existing cliff-soil/sand texture and apply a pale sandy tint.
  River floor is shallower than open sea. No additional water geometry, archive
  entries, per-frame texture uploads or modifications to saved water tiles.
- **Ordering:** floor before other opaque terrain; transparent water and
  existing shoreline foam after other opaque terrain, with depth testing
  retained and water depth writes disabled.
- **Resource policy:** allocate the Sunshine texture/effects only when
  the option is first selected. Keep the assets through the session for instant
  A/B switching. Original mode has no extra draws. Compare with the existing
  frame-performance debug overlay. The style is an original approximation,
  not copied game textures or an exact Sunshine alpha-test implementation.
