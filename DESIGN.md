# Vizzy Studio: design

Pause in flight, open a craft's program in the stock Vizzy editor, change it and apply. The program
carries on from where it was, so you can repeat quickload, edit, fly to tune things like PID gains.

## Rules

- **Apply never restarts.** It always carries the running threads over to the edited program. A new
  On Start doesn't run: "on start" only fires when a program starts from the top. The one restart is the
  Restart Vizzy button, for the selected part's program, after a confirmation.
- **A quickload keeps the latest applied edit.** Craft state and thread positions come from the
  quicksave; the program is the latest edit. A key returns to the quicksave's program.
- **Only crafts with a Matrix block need the mod.** No part modifiers; other programs stay ordinary stock XML.
- **Harmony is required**, from the Juno Harmony mod, and never inside the mod file. Without it the mod logs
  an error, shows one message and stays off. Patches: auto-fill keys, coloring on each Funk field, and the
  Matrix blocks in the toolbox; each has no public alternative, and each applies on its own.
- **In the designer**, only auto-fill, coloring, the Matrix blocks and the Clean Variables button differ
  from stock.
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
- `FunkAutoFill`, `FunkList`, `FunkInputPatch`: auto-fill's game side.
- `FunkHighlight`, `FlightButtons`, `MatrixBlocks`, `VizzyVariableCleaner`: from Vizzy Debugger.
- `Exporter/`, `VizzyOrganizer/`: from Vizzy Exporter and Vizzy McBlinky.

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
  stock `OnMovedToNewCraft`. A part's id changes when it moves to another craft, so kept edits are found
  by program content, with the id only choosing between several matches. Two crafts running the same program
  can still share a history. Selected parts must be on the player's craft.
- **Failed Apply:** the editor stays open with a message, so the work isn't lost. That includes a part
  that's gone.
- **Several programs:** a picker lists them, one editor at a time. Each keeps its own latest edit and
  canvas position. Other programs keep running.
- **Execution order:** Apply runs from a UI event while paused, so not alongside a program update.

## Funk auto-fill

A list under a Funk field while typing: Up and Down choose, Enter takes it, Esc closes the list.

- **Only what works in Vizzy Funk.** Names come at runtime from the game's Vizzy Funk context: `Context`
  defaults, `CraftControls`, `CraftFlightData`, `CraftOrbitData` properties of value types, the Vizzy list
  functions and the program's variables (a bare name for a number, `v:` for a vector). It runs only in
  Funk fields (`EvaluationExpression`) of a Vizzy editor, in the designer or in flight, whichever opened it.
  It only ever changes text the player chooses to insert. Part references aren't offered, nor are
  `Vector3` properties: `FD.AccelerationFrame` stopped a running program with `TargetParameterCountException`.
- **Ranking:** start-of-name matches first, then recent use, use in this program, a short common list,
  then alphabetical. Flight and orbit names also appear, with their prefix, after 2 typed characters.
- **Keys:** a prefix on `TMP_InputField.OnUpdateSelected`. The field reads queued key events itself, so
  without a patch Enter has already ended the edit by the time a mod can see it. The patch only acts while
  the list is open for its field, or while a key it took is still held.

## Merged from Vizzy Debugger

Code by Oxeter and Torifune, UI textures by QiQi, merged with their agreement.

- **Funk coloring and error marks** (`FunkHighlight`): colors and red marks through
  `TMP_Text.OnPreRenderText`. Names come from `FunkNames`, shared with auto-fill.
- **Flight buttons** (`FlightButtons`): a Vizzy button with the editor's icon at the bottom of the nav panel,
  opening a flyout styled like the nav sphere's, with Restart Vizzy and Edit Vizzy. Both act on the selected
  part's program straight away, otherwise on the craft's only program or a choice of them. Restart asks first,
  and isn't offered for MFD programs. Show Flight Buttons turns them off.
- **Matrix blocks** (`MatrixBlocks`): registered in the serializer's private tables, and added to the bottom
  of the toolbox's Operators category by a patch, which is what needs Harmony. `IsModRequiredForCraft` is true only for a craft that uses one.
- **MFD programs:** custom ones are editable. Stock ones are excluded, because the game reloads them.

Known risk to test: widgets belong to `MfdScript` and survive an Apply, so widgets from deleted blocks can
stay on screen. Known limit: `FindError` is a second parser and differs from the game's on rare forms such
as `+a` and `((a)`.

**Startup:** checks and patches run at the first `SceneLoaded`, because a mod's `OnModLoaded` runs as soon as
that mod loads, before later mods. `_loadPriority` is above Juno Harmony's.

- **Variable cleaner** (`VizzyVariableCleaner`): the Clean Variables button in every Vizzy editor, and the
  console command `CleanUnusedVariables`, list the global variables the open program doesn't use and remove
  them after asking, as one Undo step. Names in Funk text count as used, and it removes nothing if a Funk block's
  text is built by other blocks. It can't see another part reading a variable through `Part.VZ.Name`.

MFD Runtime Editor is planned later; it may not bring its own copy of Harmony.

## Merged from Vizzy Exporter and Vizzy McBlinky

Both by Dooiereier. Replace the separate mods, and the mod warns if they're also loaded.

- **Export** (`Exporter/`): right-click a block for an inspector panel with Export To, then the game's Load
  Program list (`LoadProgramViewModel`). It writes to the target program's file on disk, with the variables and
  custom blocks it needs. The right-click needs a patch on `OnPointerClick`, as there's no public click event.
  Export Menu turns it off.
- **Jump To** (`VizzyOrganizer/`): a left-bar button and stock flyout built in the editor's XML, listing
  `//Path/To` tag comments. Selecting a row pans the canvas. Tags are plain comments, so crafts don't need the mod.

## Testing

