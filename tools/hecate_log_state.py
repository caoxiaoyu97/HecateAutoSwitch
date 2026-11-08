#!/usr/bin/env python3
"""Read HECATE G3 Max connection state from the official debug log.

This program only reads the log. It does not send HID reports or modify audio settings.
"""

from __future__ import annotations

import argparse
import json
import re
import time
from pathlib import Path

LOG = Path.home() / "AppData/Roaming/HECATE/log" / f"log_{time.strftime('%Y%m%d')}.log"
FRAME = re.compile(
    r"\[(?P<time>[^]]+)\].*?HID\s+read\[64\]:\s*"
    r"D0 35 BB 0A(?P<payload>(?: [0-9A-F]{2}){3})"
)


def decode_line(line: str) -> dict | None:
    match = FRAME.search(line)
    if not match:
        return None
    payload = [int(value, 16) for value in match.group("payload").split()]
    online = payload[1] == 1
    return {
        "state": "online" if online else "offline",
        "battery": payload[2] if online else None,
        "log_time": match.group("time"),
        "payload": "D0 35 BB 0A" + match.group("payload"),
    }


def read_history(path: Path) -> list[dict]:
    if not path.is_file():
        raise FileNotFoundError(path)
    history = []
    with path.open("r", encoding="utf-8", errors="replace") as stream:
        for line in stream:
            item = decode_line(line)
            if item is not None:
                history.append(item)
    return history


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--log", type=Path, default=LOG)
    parser.add_argument("--history", action="store_true", help="print state transitions")
    parser.add_argument("--follow", action="store_true", help="print new state transitions")
    args = parser.parse_args()

    history = read_history(args.log)
    transitions = []
    last = None
    for item in history:
        key = (item["state"], item["battery"])
        if key != last:
            transitions.append(item)
            last = key

    if args.history:
        for item in transitions:
            print(json.dumps(item, ensure_ascii=False))
        return 0
    if not history:
        print(json.dumps({"state": "unknown", "reason": "status frame not found"}))
        return 2

    current = history[-1]
    print(json.dumps(current, ensure_ascii=False), flush=True)
    if not args.follow:
        return 0

    offset = args.log.stat().st_size
    last = (current["state"], current["battery"])
    while True:
        time.sleep(0.5)
        if not args.log.is_file():
            continue
        size = args.log.stat().st_size
        if size < offset:
            offset = 0
        if size == offset:
            continue
        with args.log.open("r", encoding="utf-8", errors="replace") as stream:
            stream.seek(offset)
            for line in stream:
                item = decode_line(line)
                key = (item["state"], item["battery"]) if item else None
                if item and key != last:
                    print(json.dumps(item, ensure_ascii=False), flush=True)
                    last = key
            offset = stream.tell()


if __name__ == "__main__":
    raise SystemExit(main())
