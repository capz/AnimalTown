# Cloud shadows

See the [native seasonal test captures](screenshots/seasonal-shadows/README.md)
for the four-season comparison, raw screenshots and capture provenance.

Clouds, cliffs, trees, buildings and villagers use the same coverage union and
shadow tint: RGB (35,49,66), with maximum opacity 66/255. Winter clouds use a
lower peak of 33/255; other casters retain their existing intensity. Coverage combines by
maximum, so a cloud passing over another shadow does not darken it twice.
There is no separate cloud material, ambient tint, GPU cloud texture or TEV
program. Existing receiving ground/cap/opaque-water draws sample the shared map
and apply the existing two shadow stages once. Indoors retain their actor shadows
without outdoor clouds or cliffs.

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
is 522,016 bytes in the host ABI, below 512 KiB, including local-light staging.
The old separate 4 KiB GPU cloud mask/effect is removed. These are
allocation measurements, not whole-game MEM1 or hardware frame-time measurements.

Outdoor cloud cover and cast shadows follow the console clock. Using the existing
lamp schedule, they fade in from 05:30 to 07:30 and fade out from 17:00 to 19:30.
They are completely absent from 19:30 until sunrise at 05:30; lamps follow the
inverse fade and reach full strength overnight. The shared shader scales shadow
opacity, so cliffs, scenery and villagers fade together without changing their
coverage or rebuilding every shadow page as the clock advances. Static ground
light pools and dynamic model lights keep their existing rendering paths.

At full darkness, cloud coverage is removed once and wind stops dirtying the
cloud field. Sunrise resumes the current wind position and season style beneath
the same gradual shadow fade. Indoor contact shadows retain their room lighting.
This adds no texture, render target or mask storage; the charged native shadow
allocation remains 522,016 bytes in the host ABI. Clock gains are scalar state.

The title/session shadow and cloud-cover options,
L+R+Z+Y lighting cycle and L+R+D-pad Right cloud cycle are removed. Old dev-config
fields remain readable for card compatibility but cannot disable shadows. Rim
lighting remains a separate lighting control.

Release CI captures 12:00, 06:30, 18:00 and 20:00 from the same ISO. Native
shader/coverage tests check all fade levels, absence of night shadows, surviving
lamps, quiescent sleeping clouds, sunrise restoration and unchanged alpha.
Screenshots and reports are kept in the `daylight-COMMIT` Actions artifact and
the Depot ZIP's `release/daylight-*` files, bound to the verified ISO hash.

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
