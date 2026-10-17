"""Create the reproducible installer payload, without game assets or interop DLLs."""
from pathlib import Path
import hashlib
import json
import shutil
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
LOADER_URL = "https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip"


def package():
    artifacts = ROOT / "artifacts"
    original = ROOT / ".tools/BepInEx-6.0.0-be.788.zip"
    info = {"GameAssemblySha256": json.loads((artifacts / "source/source-fingerprints.json").read_text())["GameAssembly.dll"], "Files": {}}
    compatibility = {"gameAssemblySha256": info["GameAssemblySha256"], "steamBuild": "25442225", "unity": "2022.3.62f3"}
    (ROOT / "data/compatibility.json").write_text(json.dumps(compatibility, indent=2) + "\n", encoding="utf-8")
    (artifacts / "loader-sha256.txt").write_text(hashlib.sha256(original.read_bytes()).hexdigest() + "\n")
    with zipfile.ZipFile(artifacts / "payload.zip", "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as out:
        def add(name, data):
            entry = zipfile.ZipInfo(name, date_time=(2026, 10, 17, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            out.writestr(entry, data)
            info["Files"][name] = hashlib.sha256(data).hexdigest()
        # Use the upstream archive, never our tool/reference copy of the loader.
        with zipfile.ZipFile(original) as loader:
            for entry in loader.infolist():
                if not entry.is_dir(): add(entry.filename, loader.read(entry))
        base = "BepInEx/plugins/ShroomGerman/"
        for name, path in {
            "ShroomGerman.dll": ROOT / "src/ShroomGerman/bin/Release/net6.0/ShroomGerman.dll",
            "translations.de.json": ROOT / "data/translations.de.json",
            "compatibility.json": ROOT / "data/compatibility.json",
            "LIESMICH.txt": ROOT / "docs/LIESMICH.txt",
            "THIRD-PARTY.txt": ROOT / "docs/THIRD-PARTY.txt",
        }.items(): add(base + name, path.read_bytes())
        for path in sorted((ROOT / "docs/licenses").glob("*")):
            add(base + "Lizenzen/" + path.name, path.read_bytes())
    (artifacts / "package-info.json").write_text(json.dumps(info, indent=2) + "\n", encoding="utf-8")
    print(f"Payload: {len(info['Files'])} files, {(artifacts / 'payload.zip').stat().st_size:,} bytes")


if __name__ == "__main__":
    package()
