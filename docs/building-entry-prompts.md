# Building entry prompts

The world popover shows a round green **A** button glyph followed by the action:

- `Enter Alex's House` for a player home, including another saved profile's home.
- `Enter Fern's House` for an NPC resident's home.
- `Enter Hearth's Store` and `Enter Town Hall` for the current public structures.

`BuildingWorld.Role` explicitly classifies placed building IDs as house, store
or town hall, independent of `BuildingDefinition`/`BuildingTypes`, which select
shared architectural artwork. Houses resolve their actual owning profile or NPC.
No new saved fields, regenerated towns or catalog migrations are needed.
`BuildingPrompt` caches each owner's display/entry strings and refreshes them
when a name changes. The interior location label uses the same identity.

The popover is centred and sizes to its text, with a maximum width of 584 pixels
on the 640px canvas. Long names scale to fit. Waiting/transition messages keep
their existing meaning; interior exits show the same button glyph with `Exit`.

The player's exterior reach is now **1.6m**, previously 0.95m. `Nearby` and
`Request` share the same approach predicate, including a small floating-point
boundary tolerance. The player must face the entrance, stay on its front
approach and have a traversable path to the socket. Trees, furniture, water
and abrupt cliff steps prevent entry across an obstruction. The existing
portal queue, landing checks and animation remain in use. NPC entry and
interior exit ranges remain 0.95m.

Checks cover all four profile owners, six NPC owners, both public buildings,
renames, allocation-free cached labels, front/diagonal/boundary reach, actual
transition completion, blocked approaches, unchanged NPC/exit range, shared
save migrations and NPC doorway traffic.
