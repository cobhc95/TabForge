#!/usr/bin/env python3
"""TabForge Stage 10 plug-in matrix runner.

One process per plug-in, hard timeout, checks 1-10 in the app (`TabForge --plugin-matrix`),
result JSON + log + editor screenshots per plug-in, a summary table and failure clusters.

The real manifest is the private file tools/plugin-matrix/manifest.private.json on branch wine-bridge only
(never merged to main, never packaged, never published in public docs).  The copy in this folder is anonymised.
Plug-ins are downloaded into a cache, never committed and never packaged.

Examples:
  python tools/plugin-matrix/run.py --validate --manifest tools/plugin-matrix/manifest.private.json
  python tools/plugin-matrix/run.py --manifest tools/plugin-matrix/manifest.private.json --app build\\...\\TabForge.exe --limit 10 --out results\\win
  python tools/plugin-matrix/run.py --manifest tools/plugin-matrix/manifest.private.json --app /tmp/TabForge.AppDir/AppRun --shard 0/6 --out results\\linux
  python tools/plugin-matrix/run.py --summarize results\\win
"""
from __future__ import annotations

import argparse
import base64
import ctypes
import gzip
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.request
import zipfile
from pathlib import Path

REQUIRED = ("id", "name", "version", "official_page", "download_url", "download_sha256",
            "sha256", "licence", "format", "bitness", "role")
HEX64 = re.compile(r"^[0-9a-f]{64}$")
SAFE = re.compile(r"[^A-Za-z0-9._-]+")


def eprint(*a):
    print(*a, file=sys.stderr, flush=True)


def load_manifest(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def validate(manifest: dict) -> list[str]:
    errors: list[str] = []
    plugins = manifest.get("plugins")
    if not isinstance(plugins, list):
        return ["manifest.plugins is not a list"]
    seen_ids, seen_hashes = set(), {}
    for i, p in enumerate(plugins):
        where = f"plugins[{i}]"
        for key in REQUIRED:
            if key not in p or p[key] in (None, ""):
                errors.append(f"{where}: missing {key}")
        if p.get("id") in seen_ids:
            errors.append(f"{where}: duplicate id {p.get('id')}")
        seen_ids.add(p.get("id"))
        for key in ("sha256", "download_sha256"):
            value = str(p.get(key, ""))
            if not HEX64.match(value):
                errors.append(f"{where}: {key} is not a lowercase SHA-256")
        if p.get("format") not in ("VST2", "VST3"):
            errors.append(f"{where}: format must be VST2 or VST3")
        if p.get("bitness") not in (32, 64):
            errors.append(f"{where}: bitness must be 32 or 64")
        if p.get("role") not in ("Instrument", "Effect"):
            errors.append(f"{where}: role must be Instrument or Effect")
        for key in ("official_page", "download_url"):
            if not str(p.get(key, "")).startswith(("http://", "https://")):
                errors.append(f"{where}: {key} must be an official http(s) URL")
        h = p.get("sha256")
        if h in seen_hashes:
            errors.append(f"{where}: duplicate binary sha256 (same build as {seen_hashes[h]})")
        else:
            seen_hashes[h] = p.get("id")
    counts = {
        "total": len(plugins),
        "vst2_32": sum(1 for p in plugins if p.get("format") == "VST2" and p.get("bitness") == 32),
        "vst2_64": sum(1 for p in plugins if p.get("format") == "VST2" and p.get("bitness") == 64),
        "vst3_64": sum(1 for p in plugins if p.get("format") == "VST3" and p.get("bitness") == 64),
        "vst3_32": sum(1 for p in plugins if p.get("format") == "VST3" and p.get("bitness") == 32),
        "juce8": sum(1 for p in plugins if p.get("risk_group") == "juce8"),
    }
    if counts["total"] < 50:
        errors.append(f"only {counts['total']} plug-ins; the plan requires at least 50")
    for key, floor in (("vst2_32", 15), ("vst2_64", 15), ("vst3_64", 15), ("vst3_32", 5)):
        if counts[key] < floor:
            errors.append(f"{key} has {counts[key]}, needs at least {floor}")
    if counts["juce8"] < 1:
        errors.append("no JUCE 8 known-risk entries")
    print("counts:", json.dumps(counts, sort_keys=True))
    return errors


def anonymise(manifest: dict, out: Path) -> None:
    plugins = []
    for i, p in enumerate(manifest.get("plugins", []), 1):
        plugins.append({
            "id": f"entry-{i:03d}",
            "name": f"entry-{i:03d}",
            "version": p.get("version", ""),
            "vendor": f"vendor-{i:03d}",
            "official_page": "",
            "download_url": "",
            "download_sha256": p.get("download_sha256", ""),
            "sha256": p.get("sha256", ""),
            "licence": p.get("licence", ""),
            "format": p.get("format", ""),
            "bitness": p.get("bitness", 0),
            "role": p.get("role", ""),
            "features": p.get("features", []),
            "risk_group": p.get("risk_group"),
            "skip_audio_reason": p.get("skip_audio_reason"),
            "automated_testing": p.get("automated_testing", ""),
            "notes": "omitted from the public copy; the full test manifest stays outside the repo",
        })
    copy = {
        "schema": manifest.get("schema", 1),
        "public_copy": True,
        "note": ("Anonymised copy: names, maker pages and download URLs are removed so public docs never "
                 "ship the test plug-in list. Run with the full external manifest."),
        "counts": manifest.get("counts", {}),
        "plugins": plugins,
    }
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(copy, indent=2) + "\n", encoding="utf-8")
    print(f"wrote anonymised copy: {out}")


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def download(url: str, target: Path, expected: str, timeout: int = 300) -> None:
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists() and sha256_file(target) == expected:
        print(f"cache hit {target.name} ({target.stat().st_size} bytes)")
        return
    if target.exists():
        target.unlink()
    print(f"download {url}")
    request = urllib.request.Request(url, headers={"User-Agent": "TabForge-matrix/1.0"})
    tmp = target.with_suffix(target.suffix + ".part")
    with urllib.request.urlopen(request, timeout=timeout) as response, tmp.open("wb") as out:
        shutil.copyfileobj(response, out)
    actual = sha256_file(tmp)
    if actual != expected:
        tmp.unlink(missing_ok=True)
        raise RuntimeError(f"SHA-256 mismatch for {url}: got {actual}, expected {expected}")
    tmp.replace(target)
    print(f"downloaded {target.name} ({target.stat().st_size} bytes)")


def _inside(root: Path, path: Path) -> bool:
    try:
        path.resolve().relative_to(root.resolve())
        return True
    except ValueError:
        return False


def extract_entry(entry: dict, archive: Path, fixtures: Path) -> Path:
    fixture = fixtures / SAFE.sub("-", entry["id"])
    marker = fixture / ".ready"
    if marker.exists():
        return fixture
    if fixture.exists():
        shutil.rmtree(fixture)
    fixture.mkdir(parents=True, exist_ok=True)
    member = entry["member"].replace("\\", "/")
    extract_dir = (entry.get("extract_dir") or "").replace("\\", "/").strip("/")
    with zipfile.ZipFile(archive) as z:
        names = {n.replace("\\", "/"): n for n in z.namelist()}
        if extract_dir:
            prefix = extract_dir + "/"
            selected = [n for n in names if n == extract_dir or n.startswith(prefix)]
            if not selected:
                raise RuntimeError(f"{entry['id']}: archive has no folder {extract_dir}")
            for name in selected:
                info = z.getinfo(names[name])
                rel = Path(name)
                dest = fixture / rel
                if not _inside(fixture, dest):
                    raise RuntimeError(f"{entry['id']}: zip path escapes the fixture folder: {name}")
                if name.endswith("/"):
                    dest.mkdir(parents=True, exist_ok=True)
                    continue
                dest.parent.mkdir(parents=True, exist_ok=True)
                with z.open(info) as src, dest.open("wb") as out:
                    shutil.copyfileobj(src, out)
        else:
            if member not in names:
                raise RuntimeError(f"{entry['id']}: archive has no member {member}")
            dest = fixture / Path(member).name
            with z.open(names[member]) as src, dest.open("wb") as out:
                shutil.copyfileobj(src, out)
    marker.write_text("ok\n", encoding="utf-8")
    return fixture


def find_binary(fixture: Path, binary_member: str) -> Path:
    wanted = binary_member.replace("\\", "/").lower()
    files = [p for p in fixture.rglob("*") if p.is_file()]
    for path in files:
        if path.as_posix().lower().endswith(wanted):
            return path
    # A flat extraction keeps only the member's basename; the manifest path still names the archive folder.
    basename = Path(wanted).name
    for path in files:
        if path.name.lower() == basename:
            return path
    raise RuntimeError(f"extracted binary not found: {binary_member}")


def entry_path(entry: dict, fixture: Path) -> Path:
    member = entry["member"].replace("\\", "/")
    extract_dir = (entry.get("extract_dir") or "").replace("\\", "/").strip("/")
    if entry.get("bundle"):
        return fixture / Path(extract_dir or member).name
    if extract_dir:
        return fixture / extract_dir / Path(member).name
    return fixture / Path(member).name


def prepare(entry: dict, cache: Path) -> Path:
    archive = cache / "downloads" / f"{entry['download_sha256']}.zip"
    download(entry["download_url"], archive, entry["download_sha256"])
    fixture = extract_entry(entry, archive, cache / "fixtures")
    binary = find_binary(fixture, entry["binary_member"])
    actual = sha256_file(binary)
    if actual != entry["sha256"]:
        raise RuntimeError(f"{entry['id']}: extracted binary SHA-256 {actual} != manifest {entry['sha256']}")
    path = entry_path(entry, fixture)
    if not path.exists():
        raise RuntimeError(f"{entry['id']}: expected plug-in path does not exist: {path}")
    return path


def run_command(cmd: list[str], timeout: int) -> subprocess.CompletedProcess:
    return subprocess.run(cmd, capture_output=True, text=True, timeout=timeout, check=False)


def find_linux_window(helper_pid: int | None = None) -> dict | None:
    try:
        found = run_command(["xdotool", "search", "--name", "^TabForge"], 10)
    except (FileNotFoundError, subprocess.TimeoutExpired):
        return None
    ids = [line.strip() for line in found.stdout.splitlines() if line.strip()]
    if not ids:
        return None
    # X ids increase; the newest matching window is this entry's editor. A stale editor from an
    # earlier entry has a lower id. The helper PID check is the stronger filter when Wine reports it.
    win = ids[-1]
    if helper_pid:
        for candidate in reversed(ids):
            pid_text = run_command(["xdotool", "getwindowpid", candidate], 10).stdout.strip()
            if pid_text == str(helper_pid):
                win = candidate
                break
    try:
        geo = run_command(["xdotool", "getwindowgeometry", "--shell", win], 10).stdout
    except subprocess.TimeoutExpired:
        return None
    values = {}
    for line in geo.splitlines():
        if "=" in line:
            k, v = line.split("=", 1)
            values[k.strip()] = v.strip()
    try:
        return {"id": win, "x": int(values["X"]), "y": int(values["Y"]),
                "w": int(values["WIDTH"]), "h": int(values["HEIGHT"])}
    except (KeyError, ValueError):
        return None


def image_stats(path: Path) -> dict:
    stats = {"file": str(path), "bytes": path.stat().st_size if path.exists() else 0}
    try:
        colors = run_command(["convert", str(path), "-format", "%k", "info:"], 30).stdout.strip()
        deviation = run_command(["convert", str(path), "-colorspace", "Gray", "-format", "%[fx:standard_deviation]", "info:"], 30).stdout.strip()
        stats["colors"] = int(colors or 0)
        stats["stddev"] = round(float(deviation or 0), 6)
    except (FileNotFoundError, subprocess.TimeoutExpired, ValueError):
        stats["colors"] = 0
        stats["stddev"] = 0.0
    stats["non_blank"] = stats["bytes"] > 5000 and stats.get("colors", 0) > 32 and stats.get("stddev", 0) > 0.02
    return stats


def capture_linux(out_dir: Path, label: str, helper_pid: int | None = None) -> dict:
    window = None
    deadline = time.time() + 10
    while window is None and time.time() < deadline:
        window = find_linux_window(helper_pid)
        if window is None:
            time.sleep(0.25)
    if window is None:
        try:
            (out_dir / "window-tree.txt").write_text(
                run_command(["xwininfo", "-root", "-tree"], 15).stdout, encoding="utf-8")
            (out_dir / "wmctrl.txt").write_text(
                run_command(["wmctrl", "-l"], 15).stdout, encoding="utf-8")
        except (FileNotFoundError, subprocess.TimeoutExpired):
            pass
        return {"label": label, "found": False, "error": "no TabForge editor window"}
    root = out_dir / f"{label}.root.png"
    png = out_dir / f"{label}.png"
    try:
        # Raise this entry's editor above any editor left by an earlier entry in the same Xvfb.
        run_command(["xdotool", "windowactivate", "--sync", window["id"]], 10)
        run_command(["import", "-window", "root", str(root)], 30)
        run_command(["convert", str(root), "-crop", f"{window['w']}x{window['h']}+{window['x']}+{window['y']}",
                     "+repage", str(png)], 30)
    finally:
        root.unlink(missing_ok=True)
    stats = image_stats(png)
    stats.update({"label": label, "found": True, "window": window})
    return stats


def close_linux_window(shot: dict) -> None:
    """Close the helper-owned editor through openbox (WM_DELETE_WINDOW -> Wine WM_CLOSE), like the Stage 7 gate."""
    if os.name == "nt" or not shot.get("found") or not shot.get("window", {}).get("id"):
        return
    win = shot["window"]["id"]
    try:
        run_command(["xdotool", "windowactivate", "--sync", win], 10)
    except (FileNotFoundError, subprocess.TimeoutExpired):
        pass
    try:
        closed = run_command(["wmctrl", "-i", "-c", f"0x{int(win):x}"], 10)
        if closed.returncode != 0:
            eprint(f"wmctrl close {win} failed: {(closed.stderr or closed.stdout).strip()}")
    except (ValueError, FileNotFoundError, subprocess.TimeoutExpired):
        pass


_DPI_SET = False


def _windows_api():
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)
    user32.EnumWindows.argtypes = [ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p), ctypes.c_void_p]
    return user32, gdi32


class _Rect(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long), ("right", ctypes.c_long), ("bottom", ctypes.c_long)]


def _window_info(user32, hwnd: int) -> dict:
    length = user32.GetWindowTextLengthW(hwnd)
    title = ctypes.create_unicode_buffer(length + 1)
    user32.GetWindowTextW(hwnd, title, length + 1)
    cls = ctypes.create_unicode_buffer(256)
    user32.GetClassNameW(hwnd, cls, 256)
    pid = ctypes.c_ulong()
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    rect = _Rect()
    user32.GetWindowRect(hwnd, ctypes.byref(rect))
    return {"hwnd": hwnd, "title": title.value, "class": cls.value, "pid": int(pid.value),
            "x": rect.left, "y": rect.top, "w": rect.right - rect.left, "h": rect.bottom - rect.top,
            "visible": bool(user32.IsWindowVisible(hwnd))}


def _enum_windows(user32, parent: int | None = None) -> list[dict]:
    proc_type = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
    found: list[dict] = []

    def callback(hwnd, _):
        info = _window_info(user32, hwnd)
        if info["visible"]:
            found.append(info)
        return True

    if parent is None:
        user32.EnumWindows(proc_type(callback), 0)
    else:
        user32.EnumChildWindows(parent, proc_type(callback), 0)
    return found


def editor_helper_pid(out_dir: Path) -> int | None:
    """The helper PID the app wrote before opening the editor (None for the native route)."""
    try:
        return int((out_dir / "helper-pid.txt").read_text(encoding="utf-8").strip())
    except (OSError, ValueError):
        return None


def capture_windows(out_dir: Path, label: str, title: str, app_pid: int, helper_pid: int | None = None) -> dict:
    started = time.time()
    png = out_dir / f"{label}.png"
    global _DPI_SET
    if not _DPI_SET:
        try:
            ctypes.WinDLL("user32").SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
        except (AttributeError, OSError):
            try:
                ctypes.WinDLL("user32").SetProcessDPIAware()
            except (AttributeError, OSError):
                pass
        _DPI_SET = True
    user32, _ = _windows_api()
    allowed = {app_pid}
    if helper_pid:
        allowed.add(helper_pid)
    owner = editor = child = None
    deadline = time.time() + 3.0
    while True:
        owner = editor = None
        for info in _enum_windows(user32):
            if info["title"] == title and info["pid"] in allowed:
                owner = info
            if (info["class"] == "TabForgePluginEditor" and info["title"].startswith("TabForge - ")
                    and info["pid"] in allowed):
                editor = info
        child = None
        if editor is None and owner is not None:
            children = [c for c in _enum_windows(user32, owner["hwnd"]) if c["visible"]]
            child = max(children, key=lambda c: c["w"] * c["h"], default=None)
            if child is not None and child["w"] * child["h"] <= 1000:
                child = None
        if editor is not None or child is not None or time.time() >= deadline:
            break
        time.sleep(0.1)
    selected = None
    host = None
    if editor is not None:
        selected, host = editor, editor
    elif owner is not None and child is not None:
        selected, host = child, owner
    elif owner is not None:
        selected, host = owner, owner
    if selected is None:
        return {"label": label, "file": str(png), "found": False,
                "error": "no matrix-owned editor/owner window",
                "capture_ms": int((time.time() - started) * 1000)}
    try:
        # Raise only the window this run created (no activation); never capture another application's window.
        user32.ShowWindow(host["hwnd"], 4)  # SW_SHOWNOACTIVATE
        user32.SetWindowPos(ctypes.c_void_p(host["hwnd"]), ctypes.c_void_p(-1), 0, 0, 0, 0,
                            0x0001 | 0x0010 | 0x0040)  # NOSIZE | NOACTIVATE | SHOWWINDOW
        user32.UpdateWindow(selected["hwnd"])
        time.sleep(0.3)
        info = _window_info(user32, selected["hwnd"])
        if info["w"] <= 0 or info["h"] <= 0:
            raise RuntimeError("empty window")
        from PIL import ImageGrab, ImageStat
        # Direct2D/JUCE child windows do not answer WM_PRINT, so copy the raised window's own rect.
        image = ImageGrab.grab(bbox=(info["x"], info["y"], info["x"] + info["w"], info["y"] + info["h"]))
        method = "Screen(topmost own window)"
        image.save(png)
        colors = len(image.convert("RGB").getcolors(maxcolors=1 << 24) or [])
        deviation = ImageStat.Stat(image.convert("L")).stddev[0] / 255.0
        data = {"found": True, "bytes": os.path.getsize(png), "colors": colors,
                "stddev": round(deviation, 6), "method": method, "pid": selected["pid"],
                "class": selected["class"], "title": selected["title"],
                "window": {"x": info["x"], "y": info["y"], "w": info["w"], "h": info["h"]}}
    except Exception as ex:
        data = {"found": False, "error": str(ex)[-500:]}
    data.update({"label": label, "file": str(png), "capture_ms": int((time.time() - started) * 1000)})
    if data.get("found"):
        data["non_blank"] = data.get("bytes", 0) > 5000 and data.get("colors", 0) > 32 and data.get("stddev", 0) > 0.02
    eprint(f"capture {label}: {data.get('found')} {data.get('capture_ms')} ms {data.get('error', '')}")
    return data


def kill_tree(proc: subprocess.Popen) -> None:
    if os.name == "nt":
        subprocess.run(["taskkill", "/PID", str(proc.pid), "/T", "/F"], capture_output=True, check=False)
    else:
        try:
            os.killpg(proc.pid, 9)
        except ProcessLookupError:
            pass


def windows_pids_for_title(title: str) -> list[int]:
    """PIDs of top-level windows with this exact title. Used only to reap this run's own matrix windows."""
    if os.name != "nt":
        return []
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    proc_type = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
    pids: list[int] = []

    def callback(hwnd, _):
        length = user32.GetWindowTextLengthW(hwnd)
        if length > 0:
            buffer = ctypes.create_unicode_buffer(length + 1)
            user32.GetWindowTextW(hwnd, buffer, length + 1)
            if buffer.value == title:
                pid = ctypes.c_ulong()
                user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
                if pid.value:
                    pids.append(int(pid.value))
        return True

    user32.EnumWindows(proc_type(callback), 0)
    return pids


def cleanup_windows_entry(entry_id: str, helper_pid: int | None = None) -> None:
    """Kill only windows/processes that this matrix entry created. Never touch the owner's windows."""
    if os.name != "nt":
        return
    title = f"TabForge Matrix - {entry_id}"
    for pid in set(windows_pids_for_title(title)):
        subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"], capture_output=True, check=False)
    if helper_pid:
        subprocess.run(["taskkill", "/PID", str(helper_pid), "/T", "/F"], capture_output=True, check=False)


def write_synthetic(out_dir: Path, entry: dict, reason: str, exit_code: int | None, timed_out: bool) -> dict:
    return {
        "id": entry["id"], "name": entry["name"], "version": entry["version"],
        "format": entry["format"], "bitness": entry["bitness"], "role": entry["role"],
        "path": "", "route": "", "overall": "timeout" if timed_out else "crash",
        "error": reason, "failureClass": "timeout" if timed_out else "crash",
        "startedUtc": "", "durationMs": 0, "host": "",
        "checks": [], "screenshots": [], "exit_code": exit_code, "timed_out": timed_out,
    }


def failure_category(text: str) -> str:
    t = (text or "").lower()
    for needle, category in (
        ("timeout", "timeout"), ("non-finite", "non-finite"), ("silent", "silent"),
        ("not identified", "identify"), ("refus", "identify"), ("editor", "editor"),
        ("state", "state"), ("parameter", "parameters"), ("sample rate", "reconfigure"),
        ("reconfigure", "reconfigure"), ("offline", "offline"), ("realtime", "offline"),
        ("kill", "kill"), ("crash", "kill"), ("lost", "kill"), ("load", "load"),
        ("missing", "load"), ("not found", "load"),
    ):
        if needle in t:
            return category
    return "other"


def patch_result(out_dir: Path, entry: dict, proc_result: dict, shots: dict, timed_out: bool) -> dict:
    result_path = out_dir / "result.json"
    if result_path.exists():
        try:
            result = json.loads(result_path.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            result = write_synthetic(out_dir, entry, "result.json is not valid JSON", proc_result.get("returncode"), timed_out)
    else:
        result = write_synthetic(out_dir, entry, "the app wrote no result.json", proc_result.get("returncode"), timed_out)
    if timed_out:
        result["overall"] = "timeout"
        result["failureClass"] = "timeout"
        result["error"] = f"the runner killed the process after {proc_result.get('timeout_seconds')} s"
    result["exitCode"] = proc_result.get("returncode")
    result["timedOut"] = timed_out
    result["log"] = "run.log"
    result["engineLog"] = "engine.log"
    result["screenshots"] = [s["file"] for s in shots.values() if s.get("file") and Path(s["file"]).exists()]
    check7 = next((c for c in result.get("checks", []) if c.get("number") == 7), None)
    first = shots.get("editor")
    again = shots.get("editor-reopen")
    if first:
        result.setdefault("screenshots_meta", {})["editor"] = first
        if check7 is not None:
            check7.setdefault("metrics", {}).update({
                "screenshot_bytes": first.get("bytes"), "screenshot_colors": first.get("colors"),
                "screenshot_stddev": first.get("stddev"), "screenshot_non_blank": first.get("non_blank", False),
            })
            if check7.get("status") == "pass" and not first.get("non_blank"):
                check7["status"] = "fail"
                check7["detail"] = "the editor screenshot was blank or missing"
    if again:
        result.setdefault("screenshots_meta", {})["editor_reopen"] = again
        if check7 is not None:
            check7.setdefault("metrics", {}).update({
                "screenshot_reopen_bytes": again.get("bytes"), "screenshot_reopen_colors": again.get("colors"),
                "screenshot_reopen_stddev": again.get("stddev"), "screenshot_reopen_non_blank": again.get("non_blank", False),
            })
            if check7.get("status") == "pass" and not again.get("non_blank"):
                check7["status"] = "fail"
                check7["detail"] = "the reopened editor screenshot was blank or missing"
    failed = next((c for c in result.get("checks", []) if c.get("status") == "fail"), None)
    if failed is not None:
        result["overall"] = "fail"
        result.setdefault("failureClass", f"check{failed.get('number')}:{failure_category(failed.get('detail', ''))}")
    result_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return result


def run_one(entry: dict, app: Path, cache: Path, out_root: Path, timeout_s: int, platform_name: str,
            screenshots: bool, display: str | None) -> dict:
    out_dir = out_root / SAFE.sub("-", entry["id"])
    out_dir.mkdir(parents=True, exist_ok=True)
    for stale in ("result.json", "editor-open.txt", "editor-ready.txt", "editor-closed.txt",
                  "editor-reopen.txt", "editor-reopened-closed.txt", "editor-close.request", "editor-close2.request"):
        (out_dir / stale).unlink(missing_ok=True)
    try:
        path = prepare(entry, cache)
    except Exception as ex:
        eprint(f"{entry['id']}: prepare failed: {ex}")
        result = write_synthetic(out_dir, entry, f"prepare failed: {ex}", None, False)
        result["overall"] = "error"
        result["failure_class"] = "prepare"
        result["platform"] = platform_name
        (out_dir / "result.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        return result
    payload = {
        "id": entry["id"], "name": entry["name"], "version": entry["version"],
        "format": entry["format"], "bitness": entry["bitness"], "role": entry["role"],
        "path": str(path), "expectedSha256": entry["sha256"],
        "skipAudioReason": entry.get("skip_audio_reason") or None,
    }
    (out_dir / "entry.json").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    env = os.environ.copy()
    env.setdefault("DOTNET_NOLOGO", "1")
    env.setdefault("DOTNET_CLI_TELEMETRY_OPTOUT", "1")
    env["TABFORGE_INTERNAL"] = "1"   # the packaged matrix mode is gated in shipped builds (Stage 13 A5)
    if display:
        env["DISPLAY"] = display
    log_path = out_dir / "run.log"
    cmd = [str(app), "--plugin-matrix", str(out_dir / "entry.json"), str(out_dir)]
    print(f"[{entry['id']}] {entry['format']} {entry['bitness']} {entry['role']}: {path}")
    # Reap a window left by an earlier interrupted run of this exact entry before starting a new one.
    cleanup_windows_entry(entry["id"])
    started = time.time()
    timed_out = False
    shots: dict[str, dict] = {}
    with log_path.open("w", encoding="utf-8", errors="replace") as log:
        proc = subprocess.Popen(cmd, stdout=log, stderr=subprocess.STDOUT, cwd=str(out_dir), env=env,
                                start_new_session=(os.name != "nt"))
        ready_seen = False
        reopen_seen = False
        try:
            while proc.poll() is None:
                if screenshots and not ready_seen and (out_dir / "editor-ready.txt").exists():
                    time.sleep(0.7)
                    title = f"TabForge Matrix - {entry['id']}"
                    shots["editor"] = (capture_linux(out_dir, "editor", editor_helper_pid(out_dir)) if platform_name != "windows"
                                       else capture_windows(out_dir, "editor", title, proc.pid, editor_helper_pid(out_dir)))
                    (out_dir / "editor-close.request").write_text("close\n", encoding="utf-8")
                    close_linux_window(shots["editor"])
                    ready_seen = True
                if screenshots and ready_seen and not reopen_seen and (out_dir / "editor-reopen.txt").exists():
                    time.sleep(0.7)
                    title = f"TabForge Matrix - {entry['id']}"
                    shots["editor-reopen"] = (capture_linux(out_dir, "editor-reopen", editor_helper_pid(out_dir)) if platform_name != "windows"
                                              else capture_windows(out_dir, "editor-reopen", title, proc.pid, editor_helper_pid(out_dir)))
                    (out_dir / "editor-close2.request").write_text("close\n", encoding="utf-8")
                    close_linux_window(shots["editor-reopen"])
                    reopen_seen = True
                if time.time() - started > timeout_s:
                    timed_out = True
                    kill_tree(proc)
                    break
                time.sleep(0.2)
        finally:
            try:
                returncode = proc.wait(timeout=20)
            except subprocess.TimeoutExpired:
                kill_tree(proc)
                try:
                    returncode = proc.wait(timeout=20)
                except subprocess.TimeoutExpired:
                    returncode = None
            if os.name == "nt":
                cleanup_windows_entry(entry["id"])
            else:
                kill_tree(proc)
    duration = int((time.time() - started) * 1000)
    proc_result = {"returncode": returncode, "timeout_seconds": timeout_s}
    result = patch_result(out_dir, entry, proc_result, shots, timed_out)
    result["runner_duration_ms"] = duration
    result["platform"] = platform_name
    cleanup_windows_entry(entry["id"], result.get("helperPid"))
    (out_dir / "result.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"[{entry['id']}] {result.get('overall')} ({duration} ms)")
    return result


def select_entries(manifest: dict, args) -> list[dict]:
    plugins = manifest["plugins"]
    if args.ids:
        wanted = {x.strip() for x in args.ids.split(",") if x.strip()}
        plugins = [p for p in plugins if p["id"] in wanted]
    if args.format:
        plugins = [p for p in plugins if p["format"].upper() == args.format.upper()]
    if args.bitness:
        plugins = [p for p in plugins if p["bitness"] == args.bitness]
    if args.role:
        plugins = [p for p in plugins if p["role"].lower() == args.role.lower()]
    if args.limit:
        plugins = plugins[:args.limit]
    if args.shard:
        index, count = (int(x) for x in args.shard.split("/", 1))
        if not (0 <= index < count):
            raise SystemExit("--shard must be index/count with 0 <= index < count")
        plugins = [p for i, p in enumerate(plugins) if i % count == index]
    return plugins


def summarise(out_root: Path) -> dict:
    results = []
    for path in sorted(out_root.glob("*/result.json")):
        try:
            results.append(json.loads(path.read_text(encoding="utf-8")))
        except json.JSONDecodeError:
            continue
    total = len(results)
    overall = {}
    groups = {}
    clusters = {}
    nondeterministic = []
    data_gaps = {}
    parameter_skips = {}
    for r in results:
        status = r.get("overall", "error")
        overall[status] = overall.get(status, 0) + 1
        for c in r.get("checks", []):
            if c.get("number") == 5 and c.get("status") == "skip":
                parameter_skips[str(r.get("id") or "unknown")] = c.get("detail") or "check 5 skipped"
        platform = r.get("platform") or "unknown"
        group = f"{platform} / {r.get('format')} {r.get('bitness')}"
        g = groups.setdefault(group, {"total": 0, "pass": 0, "fail": 0, "refused": 0,
                                      "nondeterministic": 0, "skipped": 0, "timeout": 0, "crash": 0})
        g["total"] += 1
        if status == "pass":
            g["pass"] += 1
        elif status == "refused":
            g["refused"] += 1
        elif status == "nondeterministic":
            g["nondeterministic"] += 1
            nondeterministic.append(str(r.get("id") or "unknown"))
        elif status == "skipped":
            g["skipped"] += 1
            data_gaps[str(r.get("id") or "unknown")] = r.get("failureClass") or "skipped"
        elif status == "timeout":
            g["timeout"] += 1
        elif status in ("crash", "error"):
            g["crash"] += 1
        else:
            g["fail"] += 1
        if status not in ("pass", "refused", "nondeterministic", "skipped"):
            key = r.get("failureClass") or "unclassified"
            clusters.setdefault(key, []).append(str(r.get("id") or "unknown"))
    summary = {"total": total, "overall": overall, "groups": groups, "clusters": clusters,
               "nondeterministic": nondeterministic, "data_gaps": data_gaps,
               "parameter_skips": parameter_skips}
    lines = ["# TabForge Stage 10 matrix summary", "",
             f"Plug-ins run: **{total}**", "",
             "| Result | Count |", "| --- | ---: |"]
    for key in sorted(overall):
        lines.append(f"| {key} | {overall[key]} |")
    lines += ["", "| System / format | Total | Pass | Fail | Refused | Nondet | Skipped | Timeout | Crash/error | Pass rate |",
              "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"]
    for group, g in sorted(groups.items()):
        rate = 100.0 * g["pass"] / g["total"] if g["total"] else 0
        lines.append(f"| {group} | {g['total']} | {g['pass']} | {g['fail']} | {g['refused']} | "
                     f"{g['nondeterministic']} | {g['skipped']} | {g['timeout']} | {g['crash']} | {rate:.1f}% |")
    lines += ["", "| id | name | format | role | result | failed checks | ms | screenshots |",
              "| --- | --- | --- | --- | --- | --- | ---: | --- |"]
    for r in results:
        failed = ", ".join(str(c.get("number")) for c in r.get("checks", []) if c.get("status") == "fail") or "-"
        shots = len(r.get("screenshots", []))
        lines.append(f"| {r.get('id')} | {r.get('name')} | {r.get('format')} {r.get('bitness')} | {r.get('role')} | "
                     f"{r.get('overall')} | {failed} | {r.get('durationMs', r.get('runner_duration_ms', 0))} | {shots} |")
    lines += ["", "## Failure clusters", ""]
    if clusters:
        for key, ids in sorted(clusters.items(), key=lambda kv: (-len(kv[1]), kv[0])):
            lines.append(f"- **{key}** ({len(ids)}): {', '.join(ids)}")
    else:
        lines.append("- none")
    lines += ["", "## Nondeterministic (not a pass)", ""]
    if nondeterministic:
        lines.append("- " + ", ".join(nondeterministic))
    else:
        lines.append("- none")
    lines += ["", "## Check 5 parameter coverage skipped", ""]
    if parameter_skips:
        for key, reason in sorted(parameter_skips.items()):
            lines.append(f"- {key}: {reason}")
    else:
        lines.append("- none")
    lines += ["", "## Skipped for missing test data", ""]
    if data_gaps:
        for key, reason in sorted(data_gaps.items()):
            lines.append(f"- {key}: {reason}")
    else:
        lines.append("- none")
    (out_root / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    (out_root / "summary.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    (out_root / "clusters.json").write_text(json.dumps(clusters, indent=2) + "\n", encoding="utf-8")
    print("\n".join(lines))
    return summary


def _xdpyinfo(display: str) -> bool:
    """True when an X server accepts a connection on <display>."""
    try:
        result = subprocess.run(["xdpyinfo", "-display", display], capture_output=True, text=True, timeout=5)
    except (FileNotFoundError, subprocess.TimeoutExpired):
        return False
    return result.returncode == 0


def _wm_name() -> str:
    """The EWMH window-manager name, or empty when no WM manages this display."""
    try:
        result = run_command(["wmctrl", "-m"], 5)
    except (FileNotFoundError, subprocess.TimeoutExpired):
        return ""
    if result.returncode != 0:
        return ""
    for line in result.stdout.splitlines():
        if line.lower().startswith("name:"):
            return line.split(":", 1)[1].strip()
    return ""


def start_display(cache: Path) -> tuple[str | None, list[subprocess.Popen]]:
    # Always start a private Xvfb and window manager. A DISPLAY inherited from the job environment may
    # point at a bare Xvfb with no WM: Wine then creates undecorated windows, openbox never sees the
    # runner's WM_DELETE, and every editor close falls back to the app's 2 s CmdCloseEditor path.
    # (Stage 12c: that turned all 38 editor plug-ins into "the editor did not reopen".)
    if os.name == "nt":
        return None, []
    inherited = os.environ.get("DISPLAY")
    xvfb = shutil.which("Xvfb")
    if not xvfb:
        raise SystemExit("Xvfb is required on Linux (install xvfb xauth openbox)")
    number = 99
    for _ in range(50):
        candidate = f":{number}"
        if not Path(f"/tmp/.X11-unix/X{number}").exists() and not Path(f"/tmp/.X{number}-lock").exists():
            break
        number += 1
    proc = subprocess.Popen([xvfb, candidate, "-screen", "0", "1600x1000x24", "-nolisten", "tcp"],
                            stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True)
    deadline = time.time() + 10
    while time.time() < deadline and proc.poll() is None and not _xdpyinfo(candidate):
        time.sleep(0.1)
    if proc.poll() is not None or not _xdpyinfo(candidate):
        error = ""
        try:
            error = (proc.stderr.read() if proc.stderr else "")[-1000:]
        except (OSError, ValueError):
            pass
        try:
            proc.kill()
        except OSError:
            pass
        raise SystemExit(f"Xvfb {candidate} did not start: {error.strip() or 'not accepting connections'}")
    # xdotool/wmctrl/import and openbox all need DISPLAY too, not only the app child.
    os.environ["DISPLAY"] = candidate
    openbox = shutil.which("openbox")
    if not openbox:
        raise SystemExit("openbox is required on Linux (the editor close test needs a window manager)")
    wm = subprocess.Popen([openbox, "--sm-disable"], stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True)
    deadline = time.time() + 5
    name = ""
    while time.time() < deadline and wm.poll() is None and not (name := _wm_name()):
        time.sleep(0.1)
    if wm.poll() is not None or not name:
        error = ""
        try:
            error = (wm.stderr.read() if wm.stderr else "")[-1000:]
        except (OSError, ValueError):
            pass
        try:
            wm.kill()
        except OSError:
            pass
        try:
            proc.terminate()
        except OSError:
            pass
        raise SystemExit(f"openbox did not start on {candidate}: {error.strip() or 'no window manager'}")
    print(f"display {candidate} (private; inherited={inherited or 'unset'}); window manager: {name}")
    return candidate, [proc, wm]


def main() -> int:
    parser = argparse.ArgumentParser(description="TabForge Stage 10 plug-in matrix runner")
    parser.add_argument("--manifest", type=Path, help="full external manifest.json")
    parser.add_argument("--app", type=Path, help="TabForge executable or AppRun")
    parser.add_argument("--out", type=Path, default=Path("matrix-results"))
    parser.add_argument("--cache", type=Path, default=Path(os.environ.get("TABFORGE_MATRIX_CACHE", ".matrix-cache")))
    parser.add_argument("--platform", choices=("windows", "linux-wine", "linux-native"), default=None)
    parser.add_argument("--ids", help="comma-separated entry ids")
    parser.add_argument("--format", choices=("VST2", "VST3"))
    parser.add_argument("--bitness", type=int, choices=(32, 64))
    parser.add_argument("--role", choices=("Instrument", "Effect"))
    parser.add_argument("--limit", type=int)
    parser.add_argument("--shard", help="index/count, e.g. 0/6")
    parser.add_argument("--timeout", type=int, default=180, help="seconds per plug-in process")
    parser.add_argument("--no-screenshots", action="store_true")
    parser.add_argument("--validate", action="store_true")
    parser.add_argument("--list", action="store_true")
    parser.add_argument("--summarize", type=Path)
    parser.add_argument("--write-public", type=Path, help="write the anonymised tools/ copy")
    parser.add_argument("--manifest-b64", help="gzip+base64 manifest (CI input; never committed)")
    args = parser.parse_args()

    if args.summarize:
        summarise(args.summarize)
        return 0
    if args.manifest_b64:
        raw = gzip.decompress(base64.b64decode(args.manifest_b64))
        manifest = json.loads(raw.decode("utf-8"))
    elif args.manifest:
        manifest = load_manifest(args.manifest)
    else:
        parser.error("--manifest or --manifest-b64 is required")
    if args.write_public:
        anonymise(manifest, args.write_public)
    errors = validate(manifest)
    if errors:
        for error in errors:
            eprint("manifest:", error)
        return 1
    if args.validate:
        print("manifest ok")
        return 0
    if args.list:
        for p in select_entries(manifest, args):
            print(f"{p['id']}\t{p['format']} {p['bitness']}\t{p['role']}\t{p['name']}")
        return 0
    if not args.app:
        parser.error("--app is required to run the matrix")
    platform_name = args.platform or ("windows" if os.name == "nt" else "linux-wine")
    args.out.mkdir(parents=True, exist_ok=True)
    args.cache.mkdir(parents=True, exist_ok=True)
    entries = select_entries(manifest, args)
    if not entries:
        eprint("no entries selected")
        return 1
    display = None
    procs: list[subprocess.Popen] = []
    if platform_name != "windows":
        display, procs = start_display(args.cache)
        if display:
            # xdotool/import/wmctrl in this process must see the same X server as the app.
            os.environ["DISPLAY"] = display
            print(f"display {display}")
    try:
        for entry in entries:
            run_one(entry, args.app, args.cache, args.out, args.timeout, platform_name, not args.no_screenshots, display)
    finally:
        for proc in procs:
            if proc.poll() is None:
                proc.terminate()
        for proc in procs:
            try:
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                proc.kill()
    summarise(args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
