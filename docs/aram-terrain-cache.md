# Indoor terrain cache suspension

Indoor suspension is automatic. The obsolete debug row has been removed from
both menus; its historical byte remains readable in old `dev config` records
but does not control residency. No town-save format changes are required.

After two seconds indoors, away from an exit, the game releases the outdoor
archive's fixed 1,048,576-byte MEM1 cache and its floor texture leases. The
baked ARAM blocks, metadata and reusable mesh-view identities stay alive.
Walking inside no longer keeps outdoor visuals warm.
Approaching the exit or starting a crossing restores the actual destination
before the portal commits. This does not rebake terrain or discard town data.

## Lifetime and readiness

Suspension first cancels/drains a pending bind, then unbinds every mesh alias
after the previous frame has completed. Only then may the native cache resource
be released. Native guards reject suspension during an active frame or while
another mesh still references the cache. `Reset` still means complete archive
destruction and is deliberately separate from suspension.

The game starts restoring the known outdoor destination before a door
transition may open or commit. `PrepareOutdoorResidency` performs at most eight
bind operations per call, additionally respecting its time deadline. A pending
DMA returns immediately; no partial destination alias is published. Native
requests use the SDK's owned DMA broker, so background model transfers cannot
turn a terrain bind into a foreign-owner `-EBUSY` failure.
Pending binds wake its cached background driver so hardware chunks can complete
between frames while their final publication remains on the application thread.

New cache storage is uninitialized until DMA fills each selected part. An alias
is exposed only after its own request completes. This avoids zeroing a full
1 MiB allocation in one restore frame. GPU-visible ranges never include the
uninitialized remainder of that cache.

`ArchiveReady` describes the completed immutable bake and stays true while the
cache is suspended. `Ready` describes a complete drawable selection and becomes
false during suspension or cold prefetch. An archive that has never been built
uses the existing landscape-loading path. No synthetic empty landscape is
declared ready.

Allocation failure leaves the ARAM archive intact and retryable, with a 250 ms
backoff. DMA failure leaves the old view unchanged and the new destination
unpublished. A rapid reversal cancels/drains the pending request before cache
release; a changed destination cancels the old bind before selecting another.
Changing season, town or legacy terrain mode drains residency work before
releasing the old archive and constructing the replacement.

## Cold rebuild workspace

Before the archive is sealed, no mesh view can bind its cache. The bake uses
that existing 1 MiB allocation as temporary workspace: the first 64 KiB holds
DMA input, the next 64 KiB holds coalesced output, and bounded merge descriptors
follow at offset 128 KiB. A compile-time bound keeps all three regions inside
the cache. Coalesced writes use the second region's DMA offset, preserving the
first region until every source vertex and index has been copied.

The bake owns one hold on the cache. Each DMA request completes and releases its
own hold before the workspace is reused. Sealing drops the bake hold and
returns all unused ARAM blocks. The full MEM1 cache is then available to
ordinary terrain views. Reset drops the bake hold before releasing the cache.

The 8 MiB bake budget uses independent 256 KiB ARAM blocks. The final
allocation contains the baked payload and at most one partially filled block.
If a mesh crosses a block boundary, each transfer completes before the view
publishes the mesh. Reset rolls back all allocations on a failed reservation.

This removes a separate 65,536-byte staging allocation from the loading peak.
A [Dolphin rebuild](https://github.com/capz/AnimalTown/actions/runs/45050684467)
admitted the archive and cache, then failed that staging allocation with
134,856 aggregate free native bytes: free space alone
did not provide a large enough contiguous block. Reusing the cache removes
that allocation rather than relying on another heap placement. Reset failures
still report the specific remaining allocation/hold boundary and resource
counters before rollback.

## Save and restore behavior

Memory-card saves retain their existing temporary cache release. That blocking
I/O boundary drains any owned terrain transfer before freeing the cache, then
restores only the cache the save itself released. An already suspended indoor
cache stays suspended. Save-only release and restoration emit no debug residency
markers. Water-bed geometry and triangle accounting remain correct when the
water style changes during a partial restore.

Warm movement and cold restoration both use bounded asynchronous DMA polling.
A six-metre guarded selection in two-metre buckets prefetches beyond the actual
view; prefetched pages are not drawn. Required pages take priority over the
bounded speculative set. The existing 256 mesh views and 1 MiB cache do not grow.
See [visual resource policy](visual-resource-policy.md) for pressure admission
and the injected-latency travel checks. Physical hardware timing remains pending.

Transition-only diagnostics expose the actual cache allocation change for
emulator acceptance:

```text
ANIMALTOWN TERRAIN CACHE SUSPENDED cache bytes 0 reclaimed bytes 1048576 archive bytes <N>
ANIMALTOWN TERRAIN CACHE READY cache bytes 1048576 archive bytes <N>
```

The archive payload `<N>` must remain unchanged across that pair. Initial baking,
warm selections and repeated suspension do not emit these markers.
The game forwards these sparse events and `ANIMALTOWN ` messages to the native
serial report channel before the SDK's on-screen ring truncates them. Dolphin's
`OSREPORT` logger can therefore capture complete byte and timing fields even
while the overlay is hidden. Ordinary frame messages are excluded.

## Verification

The existing native terrain fixture additionally checks:

- A bake succeeds when an allocator rejects the former separate staging
  request; reset and sealing balance the shared cache hold without double
  release. Two-section coalescing across a full 64 KiB source page preserves
  input bytes, vertex attributes, indices and disjoint workspace bounds.
- An exact 1 MiB drop in charged draw-resource bytes, unchanged ARAM allocation
  and payload lengths, and unchanged mesh-view identities.
- Live-alias and active-frame guards, idempotent suspension/resumption, and
  cache allocation failure without archive or allocation leaks.
- Byte-exact polling restoration alongside a background DMA request, request
  cancellation and late view disposal, plus 100 suspend/resume cycles.
- Injected DMA failure when built with `DND_ARAM_TEST`, retaining the previous
  published view and rolling back the reserved destination blocks.

`TerrainResidencyChecks` links the actual managed `TerrainArchive` into the
SDK-free `AnimalTown.Checks` terrain suite. Deterministic completion tests cover
work limits, no partial publication, expired budgets, allocation retry backoff,
rapid reversal, recovery after transfer failure, unchanged view identities,
allocation-free unchanged warm selections and disposal after drainage.
The merged save-cache path, already suspended saves, interrupted restoration,
and water-bed selection changes also have direct regression coverage.

The native fixture passed strict optimized compilation and address/undefined
behavior sanitizers; the managed terrain suite passed in Release. These are
ownership and correctness checks, not a physical-console performance claim.
