# Vizzy Flight Editor: design

Pause in flight, open a craft's program in the stock Vizzy editor, change it and apply. The program
carries on from where it was, so you can repeat quickload, edit, fly to tune things like PID gains.

## Rules

- **Never restart.** Apply always carries the running threads over to the edited program. A new
  On Start doesn't run: "on start" only fires when a program starts from the top.
- **A quickload keeps the latest applied edit.** Craft state and thread positions come from the
  quicksave; the program is the latest edit. A key returns to the quicksave's program.
- **Crafts and saves never need the mod.** No part modifiers; programs stay ordinary stock XML.
- **Harmony only for Funk auto-fill**, optional (see below). The designer's editor is unchanged.
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

The frames of the loops and ifs around a thread's block are rebuilt from the new program, so wrapping
or unwrapping blocks keeps the thread's nesting right. Loops return to themselves; ifs return to the
block after their else-ifs.

Known limits:

- Edited one-off blocks that already ran don't run again, so gains need setting inside a loop or in
  `PID(...)` in a Funk expression inside one.
- A new `For` wrapped around a running thread has no loop variable until its next pass.
- Renaming a variable while it's in use loses its running value.
- Each Apply recompiles Funk expressions, so `PID`, `smooth`, `sum` and `rate` restart their memory.
- Events queued but not yet started at the moment of Apply are dropped.
- A beep sounding at Apply is silenced.

## Files

- `Mod`: startup and scene hooks. `ModSettings`, `EditorLog`: settings and logging.
- `FlightEditorController`: per-flight component: keys, requests, restoring edits after a load.
- `EditorSession`: one open editor: flight UI, canvas position, closing. `FlightPause`: pause and keys.
- `FlightEditorUi`: XML changes to the flight menu and the Vizzy editor.
- `ProgramPicker`: choice panel for crafts with several programs.
- `CraftPrograms`: which programs a craft has. `ProgramSwapper`: puts a program in place.
- `ThreadRemapper`, `LatestEdits`, `FunkCompletion`: no game code, tested offline.
- `FunkAutoFill`, `FunkCatalog`, `FunkList`, `FunkInputPatch`: auto-fill's game side.

## Lifecycle

- **Pause:** the game pauses as soon as the key or menu button is used, including while the picker is
  open, and returns to its previous state when the picker or editor closes. Programs don't update
  while paused.
- **Time warp:** opening is refused, with a message. This also covers physics on rails.
- **Quicksave:** stores the edited program and positions, through the game's own code.
- **Quickload:** the edit goes back in right after the load. A save without running threads is a new
  launch (including Retry and Relaunch): it keeps its own program and ends that part's edit history.
- **Scene change:** the editor closes. `LatestEdits` clears on leaving flight; canvas positions stay
  until the game quits. Nothing else is kept across scenes.
- **Input:** the game's keys still fire while paused, so `FlightPause` turns them off (except Undo and
  Redo) while choosing or editing. Esc is read raw, so `EditorSession` re-hides the flight UI each frame
  and `FlightPause` re-asserts the pause.
- **Craft switch, EVA, docking:** blocked with the other keys while editing. Docking is handled by the
  stock `OnMovedToNewCraft`. Edits are keyed by part id, which outlives a separation or docking.
- **Failed Apply:** the editor stays open with a message, so the work isn't lost. That includes a part
  that's gone.
- **Several programs:** a picker lists them, one editor at a time. Each keeps its own latest edit and
  canvas position. Other programs keep running.
- **Execution order:** Apply runs from a UI event while paused, so not alongside a program update.

## Stock MFD programs

Not editable: the game reloads them from resources in flight. Custom MFD programs wait for a later
milestone.

## Funk auto-fill

A list under a Funk field while typing: Up and Down choose, Enter takes it, Esc closes the list.

- **Only what works in Vizzy Funk.** Names come at runtime from the game's Vizzy Funk context: `Context`
  defaults, `CraftControls`, `CraftFlightData`, `CraftOrbitData` properties of value types, the Vizzy list
  functions and the program's variables (a bare name for a number, `v:` for a vector). It runs only in
  Funk fields (`EvaluationExpression`) of an editor this mod opened. Part references aren't offered.
- **Ranking:** start-of-name matches first, then recent use, use in this program, a short common list,
  then alphabetical. Flight and orbit names also appear, with their prefix, after 2 typed characters.
- **Harmony, optional.** One prefix on `TMP_InputField.OnUpdateSelected`: the field reads queued key events
  itself, so without a patch Enter has already ended the edit by the time a mod can see it, which adds
  an undo step and a re-selection. The patch only acts while the list is open for its field, or while
  a key it took is still held. Without Juno Harmony the mod works with no auto-fill and logs why.
  The DLL is never bundled: `References/` (gitignored) is outside `Assets/`.

## Testing

`python3 tools/compile_check.py` and `python3 tools/run_tests.py`. Only the game proves behaviour.
