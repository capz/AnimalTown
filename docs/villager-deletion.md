# Delete a playable villager

On the villager picker, Z opens a modal naming the highlighted resident. Cancel
is focused every time. Left/Up selects Cancel, Right/Down selects Delete, A/START
confirms the focused action, and B cancels. The cards cannot change behind the
modal. Empty slots have no Z action. Confirmation is a separate input after Z;
it removes that resident's personal progress, home and owned furnishings.

The modal explains that deletion cannot be undone through the game and that the
town and other villagers remain. A busy state asks the player to keep the card
in Slot A. On success, the same card becomes “New Villager”; A can start a fresh
resident there. Deleting the last resident keeps the existing town with four
empty cards. This action is an explicit save request, separate from the in-game
Save action. Browsing, cancelling and ordinary character selection never save.

## What survives

Terrain/elevation edits, loose and buried world items, plants/trees, shops,
calendar and other residents' identity, inventory, finances, relationships,
progression and home plots remain. The removed resident's pockets, mail, catalog,
money, upgrades and appearance reset with the slot. Their interior and garden
furnishings are removed; the personal building definition returns to its default.
Visitors in that home return safely outside, and NPC visits/routes to it stop.
No town is regenerated. The current wire formats retain all seven records,
including explicit empty profile records; deletion never removes a shared card
file or a backend account.

An all-empty roster uses slot 1 as a non-playable serialization anchor. Decoders
accept that state only when every slot is empty; selecting a slot creates a new
resident through the existing flow. Legacy single-player encoders reject an
empty roster so exporting cannot recreate a deleted resident. Previous combined
and split saves remain readable. Older game builds do not support an all-empty
roster; use this build or newer after deleting the last resident.

## Persistence and failures

`SaveRepository.DeleteProfile` reloads the card and compares it with the displayed
snapshot before writing. A swapped card, changed town, stale generation or
missing complete save is refused. Changes are made to that private candidate;
the displayed state is never mutated during a failed write.

The existing write/readback/manifest protocol commits a complete bank, then
mirrors a second verified generation. After successful deletion both normal
fallback banks contain the removal, so corrupting one bank cannot bring the
resident back. Read-only legacy migration inputs and external backups remain
recovery sources; this is logical profile deletion, not secure media erasure.

If either commit cannot be fully verified, the UI requires a return to the title
and fresh load. It does not claim nothing changed: the first bank may already
contain a complete deletion. The last complete valid generation remains usable.
No repeated delete or gameplay continuation uses an uncertain in-memory snapshot.

## Validation

`tools/test-changes.ps1` selects save/menu, native, tooling and backend regression
coverage. Profile checks cover all four owner/room mappings, exact preservation
of other profiles, world edits, visitors, slot reuse, last-resident round trips,
legacy-export refusal, stale cards, corruption of the latest mirrored bank and
32 interruptions before/after each of the 16 writes.

Dolphin scenarios exercise Z, default Cancel, B cancellation with Delete focused,
confirmation, persistence after reboot, creating a replacement and deleting the
last resident. Only disposable test cards are used. Physical GameCube card/media
testing remains outstanding.
