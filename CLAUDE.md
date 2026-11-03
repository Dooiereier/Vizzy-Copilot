# Vizzy Flight Editor

A Juno: New Origins mod: edit a running craft's Vizzy program in the stock editor in flight, and
continue from where it was. Design: `DESIGN.md`.

Follow the `juno-modding` skill (`~/.claude/skills/juno-modding/`): design note before code, native
Juno mechanisms first, lifecycle checklist, independent review at milestones. Record new verified
facts in its `reference/juno-facts.md`.

## Hard constraints

- The program never restarts on Apply, and a quickload keeps the latest applied edit.
- Crafts and saves never need the mod: no part modifiers, `IsModRequiredForCraft` stays false.
- The designer's editor stays stock, and an editor another mod opens keeps its stock buttons.
- Harmony only as `DESIGN.md` justifies it, never bundled. No code copied from other mods without permission.

## Working here

- Unity 2022.3.62f3. Mod name "Vizzy Flight Editor"; the root asmdef has the same name.
- Commit as `Dooiereier <323592728+Dooiereier@users.noreply.github.com>`. One branch per phase or
  feature; merge to `main` only after the user has tested in the game.
- `python3 tools/compile_check.py "~/Projects/pr_check/Juno Harmony.csproj"` compiles the scripts
  without Unity. `python3 tools/run_tests.py` (same argument) runs the offline tests.
- On a fresh clone, copy `~/Drood 2.0/References/com.pardeike.harmony` to `References/` (gitignored) before
  opening Unity or compiling.
- Must keep working with Vizzy McBlinky and Vizzy Debugger's Funk coloring.
- Logs: `~/Library/Logs/Jundroo/SimpleRockets 2/Player.log` (lines start with `[VizzyFlightEditor]`).
  The game keeps only the last two sessions: read them right after a test.
