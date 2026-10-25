#!/usr/bin/env python3
"""Generate COMMANDS.md: the main-window command ids and the file that registers each.

The Avalonia window declares most command ids in a `public static readonly ... Commands`
field on a `MainWindow*.cs` type (the same convention MainWindowMenuTests discovers by
reflection), but a number of ids are registered inline with `commands.Add(...)` /
`commands.AddWhen(...)` instead. This script reads both: the `Commands` fields on the
`MainWindow*.cs` types and every inline registration in `Source/src/TabForge.Avalonia` whose id
is a string literal or a resolvable constant (same file, or a `Type.Member` lookup in
Source/src/TabForge.Avalonia and Source/src/TabForge.Core). Ids built at run time (a loop variable, a
string concatenation) are left to the `Commands` array they come from, and the table's own
handler map (`File.Exit`, `View.ResetPanels`) is not listed.

Usage (from the repo root; writes the checked-in file):

    python Source/tools/command-map.py
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "Source" / "src" / "TabForge.Avalonia"
OUTPUT = ROOT / "Source" / "docs" / "COMMANDS.md"
SOURCES = sorted(APP.glob("MainWindow*.cs"))

COMMANDS_FIELD = re.compile(
    r"public static readonly (?:string\[\]|IReadOnlyList<string>|IReadOnlyCollection<string>)"
    r"\s+Commands\s*=\s*(.*?);",
    re.S,
)
STRING_PAIR = re.compile(r"(\w+)\s*=\s*\"([^\"]+)\"")
LITERAL = re.compile(r"\"([^\"]+)\"")
IDENT = re.compile(r"\b([A-Z][A-Za-z0-9_]*)(?:\.([A-Z][A-Za-z0-9_]*))?\b")
TYPE_DECL = re.compile(r"\b(?:class|record|struct|enum)\s+([A-Z][A-Za-z0-9_]*)")
INLINE_ADD = re.compile(
    r"\bcommands\.(?:Add|AddWhen)\(\s*(?:\"([^\"]+)\"|([A-Za-z_][A-Za-z0-9_]*)(?:\.([A-Za-z_][A-Za-z0-9_]*))?)"
)


def const_pairs(text):
    """The `const string` / `static readonly string` name -> value pairs declared in a file."""
    pairs = {}
    for decl in re.finditer(r"(?:const|static readonly) string ([^;]+);", text):
        for name, value in STRING_PAIR.findall(decl.group(1)):
            pairs.setdefault(name, value)
    return pairs


def declared_types():
    """Simple type name -> file text, for the `Type.Member` references the Commands fields use."""
    found = {}
    for path in (ROOT / "Source" / "src").rglob("*.cs"):
        text = path.read_text(encoding="utf-8", errors="replace")
        for name in TYPE_DECL.findall(text):
            found.setdefault(name, text)
    return found


def zoom_preset_ids(text):
    """The ids of MainWindowZoomPresets' Presets array (`Presets.Select(p => p.CommandId)`)."""
    block = re.search(r"Presets\s*=\s*\{(.*?)\};", text, re.S)
    return LITERAL.findall(block.group(1)) if block else []


def inline_rows(types):
    """The ids inline `commands.Add` / `commands.AddWhen` calls register, with the file that registers each.

    Only a string literal, a const in the same file or a `Type.Member` constant is resolved; a loop variable or a
    concatenation is skipped (its ids are covered by that file's `Commands` array).
    """
    rows = []
    for path in sorted(APP.rglob("*.cs")):
        text = path.read_text(encoding="utf-8", errors="replace")
        own = const_pairs(text)
        name = path.relative_to(APP).as_posix()
        for match in INLINE_ADD.finditer(text):
            # A prefix used in a concatenation builds its ids at run time; it is not an id of its own.
            if text[match.end():match.end() + 8].lstrip().startswith("+"):
                continue
            literal, type_name, member = match.group(1), match.group(2), match.group(3)
            if literal is not None:
                rows.append((literal, name))
            elif member is None:
                if type_name in own:
                    rows.append((own[type_name], name))
            else:
                value = const_pairs(types[type_name]).get(member) if type_name in types else None
                if value is not None:
                    rows.append((value, name))
    return rows


def ids_of(text, block, types, where):
    """The command ids a Commands initializer names, resolving literals, consts and `Type.Member`."""
    if "Presets.Select" in block:
        return zoom_preset_ids(text)
    ids = LITERAL.findall(block)
    own = const_pairs(text)
    # The identifiers are read from the initializer with its string literals removed, so a quoted id such as
    # "Bar.First" is not mistaken for a Type.Member reference.
    for match in IDENT.finditer(LITERAL.sub("", block)):
        type_name, member = match.group(1), match.group(2)
        if type_name in ("new", "string", "Presets", "Commands"):
            continue
        if member is None:
            if type_name in own:
                ids.append(own[type_name])
            continue
        if type_name in types:
            value = const_pairs(types[type_name]).get(member)
            if value is None:
                raise SystemExit(f"{where}: cannot resolve {type_name}.{member}")
            ids.append(value)
        elif member in own:
            ids.append(own[member])
        else:
            raise SystemExit(f"{where}: cannot resolve {type_name}.{member}")
    return ids


def main():
    types = declared_types()
    rows = []
    unresolved_files = []
    for path in SOURCES:
        text = path.read_text(encoding="utf-8", errors="replace")
        field = COMMANDS_FIELD.search(text)
        if not field:
            continue
        block = field.group(1)
        try:
            ids = ids_of(text, block, types, path.name)
        except SystemExit as error:
            unresolved_files.append(str(error))
            continue
        rows.extend((command_id, path.name) for command_id in ids)

    # Inline `commands.Add` / `commands.AddWhen` registrations fill the ids no `Commands` array lists.
    seen = {command_id for command_id, _ in rows}
    inline_count = 0
    for command_id, name in inline_rows(types):
        if command_id in seen:
            continue
        seen.add(command_id)
        rows.append((command_id, name))
        inline_count += 1

    if unresolved_files:
        print("\n".join(unresolved_files), file=sys.stderr)
        return 1
    rows.sort(key=lambda row: (row[1], row[0]))
    files = len({name for _, name in rows})

    lines = [
        "# Main-window command ids, and the file that registers each",
        "",
        "Generated from the Avalonia command registration source (the `Commands` arrays plus the inline `commands.Add` / `commands.AddWhen` calls).",
        "Do not edit by hand: regenerate with",
        "",
        "```",
        "python Source/tools/command-map.py",
        "```",
        "",
        f"**{len(rows)} command ids** in {files} files (all distinct). Generated from the `Commands` arrays of"
        " `Source/src/TabForge.Avalonia/MainWindow*.cs` and from the inline `commands.Add` / `commands.AddWhen` registrations whose id"
        " is a literal or a constant. Not listed: the command table's own handler map (`File.Exit`, `View.ResetPanels`), ids built"
        " at run time from a prefix (`ContextMenuRows.ScoreLayoutIdPrefix` rows), and ids passed through a loop variable already"
        " covered by a `Commands` array.",
        "",
        "| Command id | Registered in |",
        "|---|---|",
    ]
    lines.extend(f"| `{command_id}` | `{name}` |" for command_id, name in rows)
    OUTPUT.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    print(f"wrote COMMANDS.md: {len(rows)} command ids in {files} files ({inline_count} from inline registrations)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
