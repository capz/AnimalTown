# First-save card space and diagnostics

The first town save publishes eight component records and a manifest in each
bank: 18 files. Previously every file repeated the 6,144-byte banner and
2,048-byte icon. The SDK adds its 96-byte container/comment prefix, so even a
192-byte manifest consumed two 8 KiB card blocks before journal space.

Only `AT-Town0` and `AT-Town1` now carry the artwork. All other records retain
the AnimalTown title and version comment, and usually occupy one block. Both
town banks retain the banner/icon; no save schema, filenames, checksums or
publication/recovery rules change. The SDK reads each container's own payload
offset, so existing files remain readable. Unchanged records are not rewritten
just to strip artwork.

The source-linked card adapter fixture models the pinned SDK's container,
sector rounding and replacement journal. A fresh generated town named Emi with
the Fox appearance now commits and reloads with 24 available blocks, including
the journal. The previous adapter fails that same fixture for lack of space.
An interrupted first attempt remains retryable after freeing space. These are
host capacity/ownership tests, not physical card qualification.

Every write still requires physical readback. Onboarding now reports its failing
phase and native card code with the exception detail on a second line. The full diagnostic is logged too;
failure keeps the draft available for retry and never formats a card.

An unmodified development build `380dfaabcb58` (whose game/save source matches
main `448652c`) passed the full native Dolphin onboarding scenario with a blank
card: discard/back navigation, explicit first commit, completed profile/home,
return to gameplay and no implicit gameplay save. The reporter's exact physical
failure has not been reproduced. The reporter has since confirmed a physical
Memory Card 251 in Slot A; whether Swiss card emulation is enabled is unknown.

## Physical mount versus interrupted-save recovery

The subsequent hardware screenshot reports `mount / card 2`. That positive `2`
is the old `txn_write` operation marker, not libogc's `CARD_ERROR_WRONGDEVICE`
(`-2`). It can also be stale from an earlier operation. The SDK mount entry point
both mounts the device and replays an interrupted replacement journal, so the
generic exception alone cannot distinguish a driver failure from recovery.

The pinned native overlay now resets the operation marker for each fresh mount
and retains two separate signed results: the exact libogc mount result and the
recovery errno. Onboarding displays the failing operation, numeric result and a
readable reason. For example, `CARD -3` means the driver did not detect a card;
`recovery / error 28` means insufficient space to replay a pending save.

A failed libogc directory/FAT verification can leave the card attached to the
SDK-allocated workarea. The SDK previously freed that area without unmounting.
The overlay now unmounts before freeing it, matching the SDK card manager's
cleanup and allowing a later attempt to start detached. This is a defensive
retry fix, not a confirmed explanation of the first hardware failure. Recovery
failures retain their journal; no card is formatted and no recovery data is
discarded by the cleanup.

The native host fixture covers failed-but-attached mounts, exact driver and
recovery results, cleanup order, allocation failure, slot ownership and retry.
The source-linked UI fixture checks the corresponding error descriptions.
Neither fixture qualifies physical Memory Card 251 operation through Swiss.
