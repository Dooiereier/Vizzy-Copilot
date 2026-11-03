# Vizzy Flight Editor: design

Pause in flight, open a craft's program in the stock Vizzy editor, change it and apply. The program
carries on from where it was, so you can repeat quickload, edit, fly to tune things like PID gains.

## Rules

- **Never restart.** Apply always carries the running threads over to the edited program. A new
  On Start doesn't run: "on start" only fires when a program starts from the top.
- **A quickload keeps the latest applied edit.** Craft state and thread positions come from the
  quicksave; the program is the latest edit. A key returns to the quicksave's program.
- **Crafts and saves never need the mod.** No part modifiers; programs stay ordinary stock XML.
- **No Harmony** unless a note here justifies it. The designer's editor is unchanged.
- **Other mods stay unaffected.** Only an editor this mod opens gets its Apply and Cancel replaced,
  so another mod that opens the stock editor in flight keeps the stock buttons.

## Native mechanisms

| Need | Game mechanism |
|---|---|
| Open the editor | Prefab `Ui/Vizzy/Vizzy`, public `VizzyUIScript.Initialize` |
| Apply | Public `ApplyChangesToCraft`, `Close`; new XML through the apply callback |
| Capture and resume | Public `FlightProgramScript.UpdateXml`, `StartProgram`; `FlightProgramData.ProcessXml` |
| Pause | `ITimeManager.RequestPauseChange` |
| Menu button | `AddBuildUserInterfaceXmlAction(UserInterfaceIds.Flight.FlightSceneUI, …)` |
| Program picker | `IUserInterface.CreateInspectorPanel`, the game's own panel style |
| Settings | `SettingsCategory` |

The stock Apply and Cancel handlers reach for the designer after closing, which is null in flight.
For an editor this mod opens, `FlightEditorUi` removes their `onClick` from the XML and adds its own
listeners that do the same without the designer.

## Remapping (`ThreadRemapper`)

Saving renumbers every block, and threads point at blocks by id (`nextInstruction`, `returnId`,
`states`). Old and new program XML are compared, never ids on live objects (other mods can serialize
the live program, which renumbers it).

Unchanged stacks pair first, then edited stacks by best in-order match, then single blocks dragged
elsewhere when exactly one block could be the one. A thread on a deleted block continues at the next
surviving block in its chain, leaving frames as the game would at a chain's end. It ends only when
nothing after it survives, and a flight message says so. Saved variable values stay; variables the
edit added are created with the program's values.

Known limit: edited one-off blocks that already ran don't run again, so gains need setting inside a
loop or in `PID(...)` in a Funk expression inside one.

## Files

- `Mod`: startup and scene hooks. `ModSettings`, `EditorLog`: settings and logging.
- `FlightEditorController`: per-flight component: keys, requests, restoring edits after a load.
- `EditorSession`: one open editor: flight UI, canvas position, closing. `FlightPause`: the pause.
- `FlightEditorUi`: XML changes to the flight menu and the Vizzy editor.
- `ProgramPicker`: choice panel for crafts with several programs.
- `CraftPrograms`: which programs a craft has. `ProgramSwapper`: puts a program in place.
- `ThreadRemapper`, `LatestEdits`: XML and memory only, tested offline.

## Lifecycle

- **Pause:** the game pauses as soon as the key or menu button is used, including while the picker is
  open, and returns to its previous state when the picker or editor closes. Programs don't update
  while paused.
- **Time warp:** opening is refused, with a message. This also covers physics on rails.
- **Quicksave:** stores the edited program and positions, through the game's own code.
- **Quickload:** the edit goes back in right after the load. A save without running threads is a new
  launch and keeps its own program.
- **Scene change:** the editor closes. `LatestEdits` clears on leaving flight; canvas positions stay
  until the game quits. Nothing else is kept across scenes.
- **Craft switch, EVA, docking:** the editor pauses the game. Docking is handled by the stock
  `OnMovedToNewCraft`.
- **Part destruction:** if the part is gone on Apply, the edit is dropped with a message.
- **Several programs:** a picker lists them, one editor at a time. Each keeps its own latest edit and
  canvas position. Other programs keep running.
- **Execution order:** Apply runs from a UI event while paused, so not alongside a program update.

## Stock MFD programs

Not editable: the game reloads them from resources in flight. Custom MFD programs wait for a later
milestone.

## Next: Funk auto-fill

A list under a Funk field while typing, Up/Down to move, Enter to take, Esc to close. Names come from
the game at runtime (`Context.Functions`, its constants and `SpecialFunctions`, and the `CraftControls`,
`CraftFlightData`, `CraftOrbitData` properties), filtered by what's been typed (`FD.`, `OD.`, `v:`).
Ranked by prefix match, recent use, use in this program, a short common list, then alphabetical. The
text field handles those keys itself, so the mod restores the cursor or re-selects the field after
it. `FunkKeyTest` is the temporary in-game check of that.

## Testing

`python3 tools/compile_check.py` and `python3 tools/run_tests.py`. Only the game proves behaviour.
