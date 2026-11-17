# Cloud shadows

Clouds, cliffs, trees, buildings and villagers use the same coverage union and
shadow tint: RGB (35,49,66), with peak opacity 66/255. Coverage combines by
maximum, so a cloud passing over another shadow does not darken it twice.
There is no separate cloud material, ambient tint, GPU cloud texture or TEV
program. Existing receiving ground/cap/opaque-water draws sample the shared map
and apply the existing two shadow stages once. Indoors retain their actor shadows
without outdoor clouds or cliffs.

The original periodic 64×64 I8 cloud pattern occupies 4 KiB in the charged native
buffer. It repeats every 32 logical metres and drifts at 0.6 / 0.3 m/s, independent
of camera motion, terrain bending and landscape debug animation settings. The
former 128 m repeat at 0.18 / 0.09 m/s made soft patches look like uniform dimming
through the gameplay camera. Animation reuses a two-float managed array.

The static cliff field and moving combined field each use 256×128 I8 (32 KiB).
Together they occupy the same 64 KiB as the previous 256×256 cliff field, retaining
one texel per metre and filtering gutters. The composite scales the cloud mask
with bilinear sampling, takes the maximum with cliff coverage, and merges into
existing 32×32 object/actor pages. Existing GPU bilinear filtering softens edges;
there is no extra draw call, blur texture or caster/page slot.

Drift is quantized to one eighth of a world metre, so the global field and affected
pages refresh only when that phase changes, rather than every rendered frame.
Vacated cloud coverage is restored from static data, avoiding trails. The global
update is bounded; actor-page work retains its own deadline. The native allocation
is 521,120 bytes in the host ABI, below 512 KiB, versus 517,024 before cloud
integration. The old separate 4 KiB GPU cloud mask/effect is removed. These are
allocation measurements, not whole-game MEM1 or hardware frame-time measurements.

Cast shadows are always enabled. The title/session shadow and cloud-cover options,
L+R+Z+Y lighting cycle and L+R+D-pad Right cloud cycle are removed. Old dev-config
fields remain readable for card compatibility but cannot disable shadows. Rim
lighting remains a separate lighting control.

`tools/test-changes.ps1` covers cloud packing, repeat seams and scrolling, native
maximum composition, drifting/restored coverage, shared intensity, page gutters,
indoor isolation, bridge input validation, GPU dimensions/UVs and alpha retention.
`tools/iso-smoke.py --cloud-shadows --dsp-hle` captures stationary clouds twenty
seconds apart, menus, interiors and return outside. Its fresh-card save assertions
still predate consolidated record storage; use a disposable existing saved town
and the real resume UI for visual acceptance until that helper is migrated.

For visual acceptance, keep the camera stationary: distinct light and shaded
patches should move across the ground. Check cliff/ramp and actor overlaps for
unchanged maximum darkness, and check interiors for absence of cloud motion.
Physical GameCube visual quality and timing remain unverified.
