# Cloud shadows

See the [native seasonal test captures](screenshots/seasonal-shadows/README.md)
for the four-season comparison, raw screenshots and capture provenance.

Clouds, cliffs, trees, buildings and villagers use the same coverage union and
shadow tint: RGB (35,49,66), with maximum opacity 66/255. Winter clouds use a
lower peak of 33/255; other casters retain their existing intensity. Coverage combines by
maximum, so a cloud passing over another shadow does not darken it twice.
There is no separate cloud material, ambient tint, GPU cloud texture or TEV
program. Receiving ground, caps and opaque water sample the shared map in their
four-stage TEV footer. Indoors retain their actor shadows without outdoor clouds
or cliffs.

The original periodic 64×64 I8 cloud pattern occupies 4 KiB in the charged native
buffer. Autumn retains the original heavy cloud coverage, 32-metre repeat and
contrast. Spring removes density levels below 96 and remaps the remaining
96–255 range; summer removes levels below 176 before applying its narrower
64-level transition. This opens more clear ground instead of merely fading the
same cloud footprint. On the shared mask, coverage above 10% of peak is about
69% in autumn, 47% in spring/winter and 26% in summer, before terrain sampling.
Summer retains the hardest edges and the shared 66/255 maximum. Winter uses
the same density cutoff as spring, stretches the mask to a 64-metre repeat and
halves its opacity: transitions span twice the world distance without a blur
texture. Its proportional coverage matches spring despite the larger clouds.
Drift remains 0.6 / 0.3 m/s in every season, independent
of camera motion, terrain bending and landscape debug animation settings. The
former 128 m repeat at 0.18 / 0.09 m/s made soft patches look like uniform dimming
through the gameplay camera. Animation reuses one six-float managed array:
normalized drift, repeat distance, edge width, opacity and density cutoff. The calendar season,
season preview and forced terrain-season setting all use the same selection.
Season changes dirty the existing cloud composite; no pattern is regenerated,
and static lamp/cliff data is retained. The native style parameters fit existing
allocation padding, keeping the current host buffer at 522,016 bytes. Managed
staging uses six floats (24 bytes); no per-frame allocation, texture or draw is
added. The cutoff/contrast are combined once on a style change, so each sampled
texel still needs only one contrast multiply and bias add.

The static terrain field and moving composite each use 256×128 IA4 (32 KiB).
The low nibble holds shadow coverage and the high nibble holds lamp coverage.
Together they occupy 64 KiB, with one texel per metre and filtering gutters.
The composite scales the cloud mask
with bilinear sampling, takes the maximum with cliff coverage, and merges into
existing 32×32 object/actor pages. Existing GPU bilinear filtering softens edges;
there is no extra draw call, blur texture or caster/page slot.

Drift is quantized to one eighth of a world metre, so the global field and affected
pages refresh only when that phase changes, rather than every rendered frame.
Vacated cloud coverage is restored from static data, avoiding trails. The global
update is bounded; actor-page work retains its own deadline. The native allocation
is 522,016 bytes in the host ABI, below 512 KiB, including local-light staging.
The old separate 4 KiB GPU cloud mask/effect is removed. These are
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
