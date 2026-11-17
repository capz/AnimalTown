# Traffic paths: review evidence

The 240-frame gameplay measurements in measurements.json use clean build `0.52.0+9b660396a601`, based on main `d9bc5ab`. Full texture hashing was enabled to validate sparse live mask edits. Five real walking shuttles produced 103 visited tiles and 31 selected paths; saving through the normal menu published sequence 2 and migrated the original 18-block archive to one 34-block AnimalTown2 file. No terrain archive rebuild occurred during walking.

This software-rendered Dolphin run measured 58.69 guest FPS, 48.62 host wall-clock FPS, 6.03 ms mean CPU work and 13.69 ms p95 CPU work. Physical GameCube performance has not been measured. The trace recorded five managed allocations across 240 frames and no GC; traffic's warmed entry/classification checks allocate nothing.

Tracking retains 15,360 counter bytes plus a reused 512-byte histogram per active town. The existing packed selection is 3,840 bytes. Visible paths use an 8,192-byte native I4 mask and the existing shared 2,752-byte CMPR dirt page; their leases are released outside demand and indoors. The latest sparse-cache fix adds a four-byte native publication serial in existing texture padding, with no second texture. The shadow union remains 522,016 bytes.

The three unedited UI captures are from feature commit `49b9674865ef`; that approved screen implementation is unchanged in the captured game. The title retains its logo, and picker/onboarding headers do not load it. The live path screenshots and raw trace were captured successfully but remain in the unavailable execution workspace and could not be uploaded here.

Local validation before the disconnection included the canonical selected suites, 2,312,445 gameplay/render checks, 46 card-adapter checks, 57,856 SDK record-store checks and 72,064 native shadow checks. The sparse-cache native fixture also passed: a live cell edit missed by sampled probes becomes observable through the padding serial, including after recreation at the same revision.

## Pending recovery work

PR #113 is deliberately a draft and unmerged. The workspace disconnected during rebase onto main's new mip-debug/legacy-water changes (`f2dee247`). Finish that integration, run the latest marker build with Dolphin's default texture cache, and run final-head CI/release publication. Git publishing credentials were also unavailable; PR changes and this evidence were saved through the connected GitHub app.

The local worktree is /workspace/animaltown-traffic-paths. Its rebase stopped while applying the card-capacity fix, with conflicts in TerrainRenderer.cs and native-shadow-combiner.inc. Preserve those edits: the mip-material disposal and water lamp-register fix must coexist with traffic paths. The original pre-rebase publication-stamp commit is c60f4bd in the local history. The remote PR head above already preserves the independently tested stamp change.

The card reservation is storage, separate from RAM: 34 physical 8 KiB blocks permanently, and 52 temporarily for verified replacement of an existing 18-block archive. Original records remain intact until replacement publication; interrupted copies, cleanup and lack of workspace are covered by the card tests.
