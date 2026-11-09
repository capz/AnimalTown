# Stronger cylinder and session rendering controls

World curvature (1/radius) is 8% greater than the previous main build:
`Radius = (360 / 2π) / 1.08`, approximately 53.05 m. Playable dimensions and
logical movement distances, the camera lens/eye offsets, terrain collision and
save layouts are unchanged. Ground, water, trees, actors, shadows and visibility
all use the same shared cylinder projection and rigid scrolling transforms.

Open **START → Settings → Debug / testing**. The last page adds:

- **Season preview:** Auto → spring → summer → fall → winter → Auto. This affects
  outdoor floor/cliff material bindings, water tint and tree foliage. Auto returns
  to the calendar/build configuration. Calendar progression, NPC requests and
  saved terrain/material IDs are untouched. Each explicit season change loads
  its existing assets and rebuilds the cold archive once, behind the loading UI;
  ordinary movement continues reusing the archive afterward.
- **Villager attention bubbles:** Off by default. Forces a visual exclamation
  chat bubble above NPCs in the current room/view, without changing requests,
  dialogue or journeys. A shared depth-tested 3D billboard lets scenery hide it.
  Anchors are derived once from each rig's node heights; indoor camera rotation,
  doorway positions, viewport/horizon culling and opaque fades are respected.
- **Rim intensity:** 0–200%, initially 100% (the previous shader strength).
  Left/right adjust by 5%; A increases and Z resets to 100%. L/R still change
  tabs. The existing Actor rim lighting switch remains a separate enable control.
  At 0% the extra rim TEV stage and normal texture generation are disabled.

All three previews are session-only. They never write saves automatically and
are not serialized by Save now. The attention icon uses one reusable 44-triangle
mesh and six transforms; no icon animation or per-frame particle allocation is
introduced. Its mesh is prepared between frames on first enable. Rim intensity
changes only the TEV constant color, with one immutable shared lookup texture;
no geometry or texture is regenerated when changing the percentage.

## Optimized defaults

Normal startup already uses immutable ARAM terrain, fixed MEM1 mesh views,
indexed/batched drawing and cached merged object/villager/cliff shadows. Keep
these defaults enabled. Ground mesh recreation and the older ground-ring
comparison remain off; enabling that comparison would select the older rolling
builder. Independent frame-pacing/clear-reuse experiments keep their existing
settings. The 8 MiB terrain ARAM reservation, 1 MiB working cache and native/managed
budgets are unchanged.

## Validation

Host checks cover season overrides independent of calendar/build season,
clamped numerical rim controls and reset, unchanged saves, exact curvature ratio,
full frustum/horizon/streaming coverage, chord-correct projected shadows and
camera-facing icon geometry/bounds. Native rim policy checks cover 0/100/200%
and extreme integer inputs. Native release and emulator results will be recorded
once the clean same-commit console/backend delivery is validated.
