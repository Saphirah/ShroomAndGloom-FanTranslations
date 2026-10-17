"""Build full runtime + separately downloadable, hash-addressed language packs."""
from pathlib import Path
import argparse
import hashlib
import io
import json
import shutil
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]
STAMP = (2026, 10, 17, 0, 0, 0)
BASE = "BepInEx/plugins/ShroomGerman/"
FULL_ASSET = "ShroomAndGloom-FanTranslations.zip"


def entry(name, data, compressed=True):
    item = zipfile.ZipInfo(name, date_time=STAMP)
    item.compress_type = zipfile.ZIP_DEFLATED if compressed else zipfile.ZIP_STORED
    return item, data


def package(release_build=False):
    artifacts = ROOT / "artifacts"
    dist = ROOT / "dist"; dist.mkdir(exist_ok=True)
    version = json.loads((ROOT / "data/release.json").read_text(encoding="utf-8"))["Version"]
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    dirty = bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip())
    if release_build and dirty:
        raise ValueError("Commit pending source changes before building a release package")
    languages = json.loads((ROOT / "data/languages.json").read_text(encoding="utf-8"))["languages"]
    missing = [row["code"] for row in languages if not (ROOT / "data/languages" / (row["code"] + ".json")).exists()]
    if missing: raise ValueError("Language generation incomplete: " + ", ".join(missing))
    from validate_languages import validate
    if validate(): raise ValueError("Language pack validation failed")
    info = {"InstallerProtocol": 2, "Version": version, "Commit": commit, "SourceDirty": dirty,
            "LanguageAssets": {}, "GameAssemblySha256": json.loads((artifacts / "source/source-fingerprints.json").read_text())["GameAssembly.dll"], "Files": {}}
    compatibility = {"gameAssemblySha256": info["GameAssemblySha256"], "steamBuild": "25442225", "unity": "2022.3.62f3"}
    (ROOT / "data/compatibility.json").write_text(json.dumps(compatibility, indent=2) + "\n", encoding="utf-8")
    files = {}
    archive = ROOT / ".tools/BepInEx-6.0.0-be.788.zip"
    if hashlib.sha256(archive.read_bytes()).hexdigest() != "f4cc496bd098a0df4164b81e3737297707f13a47c2478dba2f60eefab784817a":
        raise ValueError("Unexpected loader hash")
    with zipfile.ZipFile(archive) as loader:
        for item in loader.infolist():
            if not item.is_dir(): files[item.filename] = loader.read(item)
    for name, path in {
        "ShroomGerman.dll": ROOT / "src/ShroomGerman/bin/Release/net6.0/ShroomGerman.dll",
        "compatibility.json": ROOT / "data/compatibility.json", "LIESMICH.txt": ROOT / "docs/LIESMICH.txt",
        "THIRD-PARTY.txt": ROOT / "docs/THIRD-PARTY.txt"
    }.items(): files[BASE + name] = path.read_bytes()
    for path in sorted((ROOT / "docs/licenses").glob("*")): files[BASE + "Lizenzen/" + path.name] = path.read_bytes()
    files[BASE + "Lizenzen/ShroomGerman-LICENSE.txt"] = (ROOT / "LICENSE").read_bytes()
    fonts = json.loads((ROOT / "assets/fonts/sources.json").read_text(encoding="utf-8"))
    for font in fonts:
        if not font["name"].endswith((".ttf", ".otf")): continue
        data = (ROOT / "assets/fonts" / font["name"]).read_bytes()
        if hashlib.sha256(data).hexdigest() != font["sha256"]: raise ValueError("Font hash changed")
        files[BASE + "fonts/" + font["name"]] = data
    for row in languages:
        path = ROOT / "data/languages" / (row["code"] + ".json")
        data = path.read_bytes(); pack = json.loads(data)
        if pack["locale"] != row["code"]: raise ValueError("Language identity mismatch")
        relative = BASE + "languages/" + path.name
        files[relative] = data; info["LanguageAssets"][relative] = "language-" + row["code"] + ".zip"
    info["Files"] = {name: hashlib.sha256(data).hexdigest() for name, data in files.items()}
    manifest = (json.dumps(info, indent=2) + "\n").encode()
    (artifacts / "package-info.json").write_bytes(manifest)
    (dist / "package-info.json").write_bytes(manifest)

    def make_payload(subset):
        out = io.BytesIO()
        with zipfile.ZipFile(out, "w", compresslevel=9) as zip:
            for name, data in subset.items(): zip.writestr(*entry(name, data))
        return out.getvalue()

    def make_release(name, payload):
        with zipfile.ZipFile(dist / name, "w") as zip:
            zip.writestr(*entry("package-info.json", manifest, False))
            zip.writestr(*entry("payload.zip", payload, False))

    payload = make_payload(files)
    (artifacts / "payload.zip").write_bytes(payload)
    make_release(FULL_ASSET, payload)
    total = 0
    for path, asset in info["LanguageAssets"].items():
        make_release(asset, make_payload({path: files[path]}))
        total += (dist / asset).stat().st_size
    print(f"Full package: {len(files)} files, {len(payload):,} bytes")
    print(f"Language packages: {len(languages)}, {total:,} bytes combined; only changed packs are fetched")
    print(f"Version {version}; commit {commit}; preview={dirty}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--release", action="store_true", help="Require a clean committed source tree")
    package(parser.parse_args().release)
