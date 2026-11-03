#!/usr/bin/env python3
"""Compiles the mod's scripts without Unity, to catch compile errors early.

Uses Unity's bundled Roslyn compiler and the references (Unity, ModTools, game assemblies) from a
Unity-generated .csproj of any Juno ModTools project. This project's own .csproj is used when
Unity has generated one; otherwise pass another project's, e.g.:

    python3 tools/compile_check.py "~/Projects/pr_check/Juno Harmony.csproj"

The ModTools references (game, ModApi) are compiled against each game version in turn: this
project's Assets/ModTools and, if present, older copies under ~/Projects/JunoModTools/<version>/
ModTools (the mod's DLL has to run on every version it supports).

Only compile errors are caught; only the game proves behaviour.
"""
import glob
import os
import re
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UNITY = "/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents"
FALLBACK_CSPROJ = os.path.expanduser("~/Projects/pr_check/Juno Harmony.csproj")
OLDER_MODTOOLS = os.path.expanduser("~/Projects/JunoModTools")


def find_csproj():
    if len(sys.argv) > 1:
        return os.path.expanduser(sys.argv[1])
    own = glob.glob(os.path.join(ROOT, "*.csproj"))
    return own[0] if own else FALLBACK_CSPROJ


def modtools_sets():
    """Yields (label, ModTools folder) for each game version to compile against."""
    yield "this project", os.path.join(ROOT, "Assets", "ModTools")
    for path in sorted(glob.glob(os.path.join(OLDER_MODTOOLS, "*", "ModTools"))):
        yield os.path.basename(os.path.dirname(path)), path


def script_sources():
    """The mod's C# scripts."""
    return sorted(glob.glob(os.path.join(ROOT, "Assets", "Scripts", "**", "*.cs"), recursive=True))


def read_references(csproj, modtools):
    """The .csproj's references (Harmony left out), those inside a ModTools folder pointing at
    <modtools>, and its define constants."""
    base = os.path.dirname(csproj)
    text = open(csproj, encoding="utf-8").read()
    hints = [path if os.path.isabs(path) else os.path.join(base, path)
             for path in re.findall(r"<HintPath>([^<]+)</HintPath>", text)]
    hints = [path for path in hints if "Harmony" not in os.path.basename(path)]
    references = [re.sub(r"^.*/Assets/ModTools(?=/)", lambda _: modtools, path) for path in hints]
    references = [path for path in references if os.path.exists(path)]
    references.append(os.path.join(ROOT, "References", "com.pardeike.harmony", "0Harmony.dll"))
    return references, re.findall(r"<DefineConstants>([^<]+)</DefineConstants>", text)


def compile_once(references, defines, sources, target="library", out=None):
    with tempfile.TemporaryDirectory() as temp:
        out = out or os.path.join(temp, "check.dll")
        lines = ["-nologo", "-nostdlib+", f"-target:{target}", "-langversion:9.0",
                 "-nowarn:1701,1702,0169,0414,0649", f'-out:"{out}"']
        if defines:
            lines.append("-define:" + defines[0])
        lines += [f'-r:"{path}"' for path in references]
        lines += [f'"{path}"' for path in sources]
        response = os.path.join(temp, "compile.rsp")
        with open(response, "w", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")

        return subprocess.run(
            [f"{UNITY}/NetCoreRuntime/dotnet", "exec", f"{UNITY}/DotNetSdkRoslyn/csc.dll", f"@{response}"],
            capture_output=True, text=True)


def main():
    csproj = find_csproj()
    if not os.path.isfile(csproj):
        print(f"No .csproj found ({csproj}). Open the project in Unity once, or pass a Juno project's .csproj.")
        return 2

    sources = script_sources()
    failed = 0
    for label, modtools in modtools_sets():
        references, defines = read_references(csproj, modtools)
        result = compile_once(references, defines, sources)
        problems = [line for line in result.stdout.splitlines() if " error " in line or " warning " in line]
        for line in problems:
            print(line.replace(ROOT + "/", ""))
        print(f"{'OK' if result.returncode == 0 else 'FAILED'} ({label}): {len(sources)} files, {len(references)} references "
              f"({len([p for p in problems if ' error ' in p])} errors).")
        failed |= result.returncode != 0

    # Editor-only scripts (Assets/Editor, e.g. the build's variant filter), once, with Unity's editor module.
    editor_sources = sorted(glob.glob(os.path.join(ROOT, "Assets", "Editor", "**", "*.cs"), recursive=True))
    if editor_sources:
        references, defines = read_references(csproj, os.path.join(ROOT, "Assets", "ModTools"))
        references.append(f"{UNITY}/Managed/UnityEngine/UnityEditor.CoreModule.dll")
        result = compile_once(references, defines, editor_sources)
        problems = [line for line in result.stdout.splitlines() if " error " in line or " warning " in line]
        for line in problems:
            print(line.replace(ROOT + "/", ""))
        print(f"{'OK' if result.returncode == 0 else 'FAILED'} (editor scripts): {len(editor_sources)} files.")
        failed |= result.returncode != 0

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
