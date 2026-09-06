#!/usr/bin/env python3
"""Compatibility entry point. New users can download PulsarConfig directly."""

import hashlib
import json
import os
import platform
import re
from pathlib import Path
import sys
import tempfile
import urllib.request

REPO = "CometWorks/config-tools"
ASSET = "PulsarConfig-linux-x64.bin"


def request(url):
    return urllib.request.urlopen(
        urllib.request.Request(url, headers={"User-Agent": "PulsarConfig-Bootstrap"}),
        timeout=60,
    )


def main():
    if sys.platform != "linux" or platform.machine() not in ("x86_64", "amd64"):
        raise RuntimeError("Pulsar setup supports Linux x64.")
    if os.geteuid() == 0:
        raise RuntimeError("Run as your normal Steam user, without sudo.")
    print("Pulsar setup has moved to a standalone executable; downloading it from config-tools…", flush=True)
    candidates = []
    for page in range(1, 11):
        with request(f"https://api.github.com/repos/{REPO}/releases?per_page=100&page={page}") as response:
            releases = json.load(response)
        for release in releases:
            match = re.fullmatch(r"pulsarconfig-v(\d+)\.(\d+)\.(\d+)", release.get("tag_name", ""))
            if not match or release.get("draft") or release.get("prerelease"):
                continue
            assets = [asset for asset in release.get("assets", []) if asset["name"] == ASSET]
            if len(assets) == 1:
                candidates.append((tuple(map(int, match.groups())), assets[0]))
        if len(releases) < 100:
            break
    else:
        raise RuntimeError("Release history exceeds the update lookup limit. Download from the releases page.")
    if not candidates:
        raise RuntimeError("No released PulsarConfig binary is available yet. See github.com/CometWorks/config-tools for build instructions.")
    asset = max(candidates, key=lambda candidate: candidate[0])[1]
    digest = asset.get("digest") or ""
    url = asset["browser_download_url"]
    if not re.fullmatch(r"sha256:[0-9a-fA-F]{64}", digest) or not url.startswith(f"https://github.com/{REPO}/releases/download/"):
        raise RuntimeError("Release asset URL or checksum is missing/invalid.")
    destination = Path(__file__).resolve().with_name("PulsarConfig.bin")
    with tempfile.TemporaryDirectory(prefix=".pulsar-setup-", dir=destination.parent) as work:
        package = Path(work) / ASSET
        checksum = hashlib.sha256()
        total = 0
        with request(url) as response, package.open("wb") as output:
            while chunk := response.read(1024 * 1024):
                total += len(chunk)
                if total > 256 * 1024 * 1024:
                    raise RuntimeError("Setup executable exceeds the 256 MiB download limit.")
                checksum.update(chunk)
                output.write(chunk)
        if checksum.hexdigest() != digest[7:].lower():
            raise RuntimeError("Downloaded setup executable failed SHA-256 verification.")
        package.chmod(0o755)
        os.replace(package, destination)
    os.execv(str(destination), [str(destination), *sys.argv[1:]])


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"Pulsar setup: {error}", file=sys.stderr)
        sys.exit(1)
