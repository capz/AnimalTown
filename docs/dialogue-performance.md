# Dialogue text preparation

The 5a45abe release rebuilt every visible dialogue prefix every frame. Wrapping
used a new one-character array/string followed by string concatenation for each
character. Each growing result copied all earlier characters again. Completing
a message did not stop this work: the same fully visible text was rebuilt on
every later frame, creating quadratic copies and allocation pressure.

The fix wraps the complete message once in Say using one character buffer and
one final string. Wrapping still replaces spaces one-for-one, preserving every
typewriter index and the existing line breaks. UiFont.Draw accepts a visible
character count, so the original text layout is reused while revealing glyphs;
no substring is needed. Batched and legacy font paths honor the same count.
Long unrotated body text also avoids measuring an unused rotation pivot.
Item titles and save status each retain one wrapped value, invalidated on text
changes. These caches add no native textures or geometry residency.

Regression checks compare the actual production wrapper with the original
algorithm at every visible prefix, including long words, repeated spaces,
explicit control characters and non-ASCII input. Ten thousand requests for an
unchanged wrapped value allocate no managed bytes on the host.

Baseline from released ISO 5a45abe3e37e, with guest-clock telemetry in Dolphin:

| Panel | Samples | Mean frame interval | Mean UI phase |
|---|---:|---:|---:|
| World | 352 | 16.616 ms | 1.756 ms |
| Idle interior | 612 | 16.727 ms | 2.445 ms |
| Backpack item actions | 147 | 71.691 ms | 57.688 ms |
| Long Controls dialogue | 7 | 636.357 ms | 618.429 ms |

The GPU blocking-wait phase measured zero in these samples; dialogue simulation
was roughly 1 ms. UI CPU work dominated the long dialogue. These are emulated
guest timings, not physical GameCube measurements. The dialogue sample count
is small because the old path was slow; typing is included in the panel groups.

Reproduce with a copied regression card and the released ISO:

```
python3 tools/iso-smoke.py ISO CAPTURE_DIR --interaction-profile --profile-seconds 25
python3 tools/read-frame-profile.py CAPTURE_DIR/user/Dump/Frames/*.avi --interaction-panels
```

The regression card used here starts at the house entrance. The first A press
enters its interior; later Controls dialogue and item actions use that same
background. The decoder distinguishes opaque panel coverage in old releases
whose telemetry grouped them with other/indoor screens. A third dialogue body
line identifies the long-message group. Inspect the screenshots as well as the
decoded panel IDs before comparing runs. The script records both short/typing
and complete-message screenshots; compare body text and bubble geometry between
the exact before/after ISOs, avoiding changing telemetry/HUD regions.

Run --resume --item-navigation to test item action D-pad movement, wraparound,
held stick progress, and simultaneous stick/D-pad input as used by shared
Dolphin bindings. These inputs responded on the previous ISO in the isolated
pipe-controller test; slow frame polling can still miss short physical presses.
