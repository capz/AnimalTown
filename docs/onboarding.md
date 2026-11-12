# New-villager onboarding

Selecting **New Villager** on the [four-card picker](villager-picker.md) starts
the approved [four-screen storyboard](concepts/onboarding-storyboard.png).
Hearth introduces name entry, then the player chooses a look, previews a home
in the actual town and explicitly chooses **Save & begin**. The same flow serves
the first resident, an additional resident and a town whose residents were all
[deleted](villager-deletion.md). Existing residents with homes continue directly
to gameplay.

## Screens and controls

| Screen | Controls and behavior |
| --- | --- |
| Welcome and name | Stick/D-pad moves around the 10×4 keyboard. A types; X erases; START accepts. Names allow 1–16 letters, digits, spaces and `. ! ?`; a blank/space-only name cannot continue. Letters after the first character of a word are lower case. B opens the exit confirmation. |
| Find your look | Stick/D-pad cycles Fox, Bunny, Giraffe, Maine coon, Hamster and Spaniel. A/START accepts. B returns to the name keyboard with the entered name intact. The six cards and larger preview use the original model-derived portraits. |
| Find your little corner | Stick/D-pad moves through the real outdoor town with the proposed cottage and its 15×13 home/garden boundary. Green indicates a valid plot; red and Hearth's explanation describe an invalid one. A chooses a valid spot. B returns to look selection, where B can also return to the name. |
| Ready to put down roots? | Name, species and home appear together. A activates **Save & begin**. B **Review** returns to the home preview without saving; earlier choices remain editable through the back path. |

The home preview uses the ordinary resident cottage model and materials, with an
opaque, bounded border around the garden reserve. It shares the gameplay world
renderer rather than constructing a second miniature town. Placement checks
level grass, water/coastal cells, other reserved home plots and cottage/doorway
obstacles. Existing landscape and homes remain visible. The preview never
terraforms, clears objects or changes another resident's home.

From the name screen, B opens **Leave this new villager?** with **Keep editing**
focused. Left/Up selects Keep editing; Right/Down selects Discard; A/START
confirms; B resumes editing. Discard returns to the title and restores the town
snapshot from before onboarding, including after visiting the outdoor preview.

## Draft and explicit save

`OnboardingDraft` owns a private candidate and encoded baseline until the final
save. Identity and home edits affect that candidate. It shares existing terrain
read-only and never regenerates a saved town. Name entry, look selection, plot
preview, back navigation, review and discard do not write the town to the card.

**Save & begin** mounts Slot A and uses the existing complete-bank save and
readback verification. Before publishing, it reloads the mounted card and checks
that the saved town still matches the baseline. A changed/swapped town or missing
required save is refused. An interrupted attempt that already committed the
same candidate can be recognized safely on retry. For a first-ever save without
a committed bank, retry requires an authoritative directory listing and exact
byte matches for every existing town file against this draft's previous attempt;
foreign, torn or unreadable records are refused. That temporary attempted
snapshot is also released on commit/discard. Save failure retains the
choices and shows a retry message; gameplay begins only after a verified commit.
After that first explicit save, the normal **Settings → Save now** action remains
the way to save subsequent progress. Gameplay does not add an automatic save.

Other residents' personal records, home plots, possessions, progression and the
shared terrain are preserved. No new save schema is required for this flow.

## Resource lifetime

Name, look and review each own a short-lived `OnboardingUi`. The name screen loads
only Hearth's Spaniel portrait; look selection loads six portraits; review loads
only the chosen species. No portrait/image loading happens in the drawing loop.
Keyboard glyphs/count labels are prepared once for their screen.

Disposal releases every view's portraits, wordmark, font leases, background
texture and Draw2D buffers, clears managed layout references and trims unused
texture pages. The complete preview renderer is disposed before opening look
selection or review. This releases its actors/models, terrain, buildings,
shadows, audio and UI; collection runs after that renderer's scope unwinds.
There is never a full town resident alongside the image-based setup screens.

Returning from review or identity loads the outdoor preview again at the same
plot cursor. After **Save & begin**, review and the draft's encoded baseline and
attempted-save snapshot are released before the gameplay renderer loads. These
loading transitions keep resource lifetimes within GameCube MEM1. Gameplay
retains only its own fonts, renderer and other resources it actually uses.

After the setup/save stack unwinds, the runtime trims unused pages and collects
retired managed/native data before the first gameplay frame. A hard invariant
requires zero live onboarding views, no session draft and zero cached source
bytes for picker portraits or the wordmark. Failure stops the transition instead
of silently retaining setup resources. Successful cleanup logs
`ANIMALTOWN ONBOARDING RELEASED` with the current native allocation count.

## Validation

Run `tools/test-changes.ps1` for the affected managed, save and native checks,
then build the console normally. In the [Dolphin container](../README.md#checks),
run against a disposable card/output directory:

```sh
python3 tools/iso-smoke.py out/AnimalTown-VERSION.nkit.iso out/smoke-onboarding --onboarding
```

The smoke waits for actual name/look/review/world screen signatures. It exercises
blank-name rejection, default Keep editing, look/name and home/look back paths,
discard after outdoor loading, review/back, idle review, explicit saving and
idle gameplay. It compares card bytes before Save & begin, verifies the committed
name/species/home, and writes screenshots plus `onboarding.json`. Reaching the
gameplay screenshot also passes the runtime cleanup invariant.

For an additional resident, use a disposable fixture card with an unused slot
and pass `--profile-slot N` (zero-based, 0–3). The scenario checks that every
other profile's full payload/home and the terrain hash remain unchanged. Fixture
records can be converted to a raw test card with
`tools/profile-card-fixture.py`; never use a personal save as the test output.
Inspect the captures at native resolution for portrait cropping, focus, text and
the real home boundary. These commands describe validation coverage, not a claim
that a particular build has passed it. Physical GameCube/card testing remains
outstanding.
