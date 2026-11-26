# Full-resolution antialiasing

Title **Options → Video → Edge smoothing** has **On / Off** choices, enabled by
default. Left/Right selects a choice; A applies it and saves to the selected memory
card. The underline follows the active display, while navigation alone changes
neither the live mode nor the stored preference. A card error leaves the live
choice active and reports that it was not saved. Temporary play applies only for
the session. Both former debug AA controls are retired.
Holding X at startup bypasses saved video options, including AA, for recovery.

AA uses the SDK's native three-sample RGB565/Z16 mode with two overlapping EFB
passes. It retains 640×480 resolution, including the HUD. It does not enable
emulator host MSAA. Enabling AA selects 640×480 while retaining scan/aspect;
turn AA off to select 576p. Destination alpha is unavailable in this hardware mode.

Hypercube's FrameRenderer replays prepared drawing. Simulation, animation,
visibility, shadow masks, rain and texture residency prepare once per frame.
World/HUD drawing repeats, with bounded batch cursors reset for each part.
Splash, title, loading, profile picker and onboarding also support all parts.

The two 640×480 scanout buffers remain 1,200 KiB, and switches between ordinary
640×480 and full AA reuse that pair. Texture and shadow-map budgets are unchanged.
Scene submission and geometry processing repeat, so leave AA off when performance
matters more than edge quality. Real GameCube performance still needs measurement.

ATO v3 stores smoothing in byte 7 of the existing 16-byte options record. Saved
choices in ATD v4–v6 migrate when no ATO v3 choice exists. ATD v7 retires the old
byte only after a migrated video record is durably verified. Town save data is
not written by this preference. See [migration details](home-plot-and-video-settings.md).

For a disposable fresh or existing-town card, run the emulator smoke helper with
`--full-resolution-aa --dsp-hle --component-cable --season spring`. It captures
the title Video row Off/On, rejects 576p while smoothing is on, restarts with On
saved, then restores Off and checks that town records did not change. Fresh cards
also verify the enabled default and capture the starter home preview. Host MSAA
remains 1. The `video-options` CI scenario also checks the other video rows and
the separate deletion controls, with captures in its Actions artifact and under
`release/video-options-*` inside the same-commit Depot ZIP.

The [approved layout](concepts/title-edge-smoothing.svg) uses the existing options
panel and On/Off selector. The older debug-menu screenshot in `screenshots/` is
historical. SDK fixtures independently check all 480 rows, the band join, capture
alignment, framebuffer canaries and buffer reuse.
