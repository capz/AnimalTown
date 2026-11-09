# Villager attention bubbles

The selected concept is the first mockup in the design review: warm cream
speech balloons with a separate sage name tab, an ochre tab and amber `!` for
urgent messages, and a short tail pointing to the speaker. Text uses the existing
IPL UI font; characters, world art and regular conversation panels are reused.

Each resident owns an independent ambient notice. It never opens dialogue,
completes an errand, takes an item or grants a reward. A nearby **A** interaction
still starts the ordinary conversation. A caller briefly pauses its existing
outdoor route and faces the player; the route resumes when the notice ends.
The player remains free to move.

A known resident can greet the player after **two town days without a visible,
nearby encounter**. First encounters and unknown legacy history establish a
baseline. A request is urgent when the player carries its requested item, or
between **05:00 and 06:00**, the final hour before existing errands expire.
These use “Is that my …?” and “before morning!” copy respectively; ordinary
pending errands are not labelled urgent all day.

Friendly messages last four seconds; urgent ones last six. There are at most
two active notices, staggered by 0.8 seconds. Urgent notices take priority, then
nearest distance. A villager can display one greeting and one urgent notice per
town day. A conversation adds a 15-second quiet period. Daily cooldowns start
only when a bubble actually fits and is drawn, so crowding does not consume an
unseen message. Opening menus/dialogue, going indoors, developer tours, leaving
range, a resolved request or a blocked sight line hides the relevant notices.

The scheduler scans six residents with a nine-metre range and bounded terrain
sight checks. Projection follows the existing cylindrical surface/camera basis.
The layout protects speaker/player faces, top safe margins and the bottom HUD /
minimap area; near-horizon notices can sit beside their speakers. If two bubbles
cannot fit, the lower-priority bubble waits for space. Shapes use the existing
bounded native UI geometry cache; strings are formatted only when notice state
changes. No new 3D meshes, textures, fonts, navigation planners or dialogue audio
are needed.

Combined save format **8** and split-record format **4** append three integer
history fields per resident: last encounter day, last displayed greeting day and
last displayed urgent day (72 bytes total). Readers retain all earlier formats.
For earlier saves, met residents start with the saved town day as a conservative
last encounter; unknown residents retain an unknown date. Migration retains
terrain, elevation, structures, belongings, names, finances, progress and routes.
As with other progress, this history is persisted by the existing explicit save;
notices do not trigger automatic card writes. Runtime durations are not saved.

`tools/test-changes.ps1` selects the relevant checks. The life suite covers notice
priority, expiry, daily limits, actual display acknowledgement, sight/range,
clock rollback, continued player movement and route resumption. It also checks
v7 combined / v3 split migration and current round trips. The Dolphin workflow
uses disposable GCI-folder fixtures made by the matching published tools:

```
dotnet AnimalTown.Tools.dll attention-fixture ROOT OUTPUT.gci TOWN_DAY [deadline]
```

This creates synthetic input only; it never reads or modifies a player's card.
The scenarios capture two named notices, free movement, opt-in dialogue, explicit
save/reload cooldowns and a real final-hour deadline notice. Evidence and its
checksums are published with the same-commit ISO/server release. Physical
GameCube validation remains outstanding.

Research: [Nintendo’s neighbour guide](https://play.nintendo.com/news-tips/tips-tricks/animal-crossing-new-horizons-neighbors-tips/)
describes approaching residents and thought bubbles; [Animal Crossing World](https://animalcrossingworld.com/guides/new-horizons/reactions-emotes-how-to-unlock-complete-list-by-personality/)
describes player-name callouts before optional conversations. Name-titled
reunion/urgent bubbles and these timing rules are AnimalTown’s adaptation.
