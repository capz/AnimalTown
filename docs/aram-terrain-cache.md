# Indoor terrain cache suspension

The debug option **discard terrain cache when indoors** is off by default.
It is stored with the other startup debug options in the memory-card `dev config`
item and is available from the existing debug configuration UI.
The title modal and in-game debug menu use the same approved label and default.
Dev-config version three retains the current 40-byte record size. Published
version-one and version-two records remain readable, preserve their settings,
and initialize this new option to Off. The terrain value follows the two tree
noise settings. Gameplay save formats are independent of this change.

[Approved debug-menu mockup](concepts/aram-terrain-debug-mockup.png).

When enabled, an indoor idle interval can release the outdoor archive's fixed
1,048,576-byte MEM1 cache. The 8 MiB ARAM allocation, immutable terrain payload,
native metadata and 256 reusable mesh-view identities remain alive. This does
not rebake terrain, reduce geometry or texture quality, or discard saved town
data. The ordinary outdoor cache remains resident when the flag is off.

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

Memory-card saves retain their existing temporary cache release. That blocking
I/O boundary drains any owned terrain transfer before freeing the cache, then
restores only the cache the save itself released. An already suspended indoor
cache stays suspended. Save-only release and restoration emit no debug residency
markers. Water-bed geometry and triangle accounting remain correct when the
water style changes during a partial restore.

Warm outdoor movement retains the prior complete demand-bind path. Those native
binds now drain only their own broker request; cold indoor restoration uses the
polling path. Hardware timing is still required to assess bandwidth contention,
allocator latency and doorway readiness under actual MEM1 pressure.

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
