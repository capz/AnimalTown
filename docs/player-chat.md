# Player chat

Press **R** while playing, or open **Settings → Chat**. The controller keyboard
accepts up to 120 printable ASCII characters using the game's existing IPL font.
The town and NPC journeys continue while the player types.

| Control | Action |
| --- | --- |
| Stick / D-pad | Select a key |
| A | Type the selected character |
| L | Switch lowercase / uppercase |
| R | Add a space |
| X | Erase the last character |
| START | Send |
| B | Cancel and return to the previous screen |

Ordinary speech uses the active playable villager's name and the regular
dialogue panel. Text wraps using actual IPL glyph advances, including unbroken
words; longer messages use three-line pages. A or B finishes the current page's
typewriter animation, then advances or closes it. Chat drafts and messages are
transient and do not enter the memory-card save.

## Debug commands

With debug menus visible (toggle with three consecutive Z presses), enter:

```text
/attend rowan Come over here!
```

Replace `rowan` with a resident's full lowercase name, as shown in the Jobs tab.
The resident's own name labels the supplied dialogue, and that resident gets
the shared attention icon for the duration of the conversation. Existing room,
viewport, horizon and depth culling still apply; the command neither moves the
NPC nor pins an off-screen model in RAM. The cue is released when the dialogue
ends or debug menus are hidden. Missing arguments, uppercase names and unknown
commands show an editable error in the keyboard panel.

The current debug-menu setting is checked when sending, including if it changed
while entering text. With debug menus hidden, slash-prefixed text is ordinary
player speech and has no command side effects.

This implements local speech and debug attention. The existing online transport
remains a separate feature; no remote delivery or automatic NPC replies are
claimed.
