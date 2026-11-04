# Vizzy Studio

A mod for [Juno: New Origins](https://www.simplerockets.com/) that lets you edit a running craft's Vizzy program in flight, in the game's own editor, without restarting the program.

Pause, change a PID gain, apply, and the program carries on from where it was. Quickload and the latest edit is still there, with threads back at their quicksave positions, so you can tune the same phase of flight over and over.

Vizzy Studio is still in development and has no release yet. It also includes the features of Vizzy Debugger, Vizzy Exporter and Vizzy McBlinky, so turn those mods off when using it.

## Requirements

- Juno: New Origins, tested on 1.4.105 and 1.4.201
- The **Juno Harmony** mod. Without it, Vizzy Studio stays off and tells you why.

Crafts don't need the mod, unless a program on them uses a Matrix block.

## Editing in flight

Press **Shift+O**, use **Edit Vizzy** in the flight menu, or use the Vizzy button at the bottom of the right-hand bar. The flight pauses while you edit.

- With a part selected, its program opens straight away. Otherwise you choose from the craft's programs, or the only one opens. In the list, the one you opened last is blue: Up and Down move the selection, Enter opens it.
- **Shift+O** in the editor applies your edit and returns to flight, and the editor reopens where you left it.
- **Shift+Alt+O** returns to the quicksave's program.
- **Restart Vizzy**, on the same button, is the only thing that restarts a program, and it asks first.
- Custom MFD programs can be edited. Stock ones can't, because the game reloads them in flight.

### What to expect

- Apply never restarts a program, and a newly added On Start doesn't run.
- A block that has already run doesn't run again, so put values you want to tune inside a loop.
- If you delete the block a thread is on, it continues at the next block that's left in its chain.
- Each Apply restarts the memory of `PID`, `smooth`, `sum` and `rate` in Funk.
- Renaming a variable while it's in use loses its running value.

## In the Vizzy editor

These work in the designer and in flight.

- **Funk auto-fill:** suggests functions, variables, `FD.` and `OD.` data as you type. Up and Down choose, Enter or a click accepts, Esc closes. Variables known to hold a vector come with `v:`.
- **Funk coloring:** colors each kind of name and marks errors in red.
- **Matrix blocks:** four 3×3 matrix blocks at the bottom of Operators.
- **Clean Variables:** in the menu, removes global variables the program doesn't use, after listing them. Another part's program or an Input modifier could still read them by name.
- **Export Block:** in the menu. Click a block, then press this to copy it to another saved program, with the variables and custom blocks it needs. An instruction brings the blocks below it.
- **Jump To:** on the left bar. Start a comment with `//` to tag the blocks after it, such as `//Launch` or `//Launch/Pitch Program`, then select the tag to move the view there. Tags are plain comments.

## Settings

In the game's mod settings: the editor key, the flight buttons, Export Block, the update check, Funk coloring, auto-fill and debug logging.

The update check reads the latest version from the mod's page once per game start and offers to open the page. Nothing is downloaded automatically.

## Building

You need Unity 2022.3.62f3 and the Juno ModTools. Open the project and save the mod from the ModTools window. Harmony must never end up inside the mod file: it comes from Juno Harmony.

```bash
python3 tools/compile_check.py
```

```bash
python3 tools/run_tests.py
```

These compile the scripts without Unity and run the offline tests. How it all works is in [DESIGN.md](DESIGN.md).

## Credits

By Dooiereier, with code by Oxeter and Torifune from Vizzy Debugger and UI textures by QiQi, merged with their agreement.
