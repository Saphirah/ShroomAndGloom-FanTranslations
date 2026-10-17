"""Small integration check: discovery, real installer install/remove and conflict protection.

Never starts the game. The test folder contains a dummy game exe and real metadata
fingerprint, so running the installer cannot accidentally launch a game.
"""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main(game, installer=None):
    exe = installer.resolve() if installer else ROOT / "dist/ShroomAndGloom-FanTranslations-Setup.exe"
    temp = Path(tempfile.mkdtemp(prefix="installer-smoke-", dir=ROOT / "artifacts"))
    def run(*args, expected=0):
        report = temp / "report.json"
        if report.exists(): report.unlink()
        proc = subprocess.run([str(exe), *map(str,args), "--report", str(report)], timeout=180)
        result = json.loads(report.read_text(encoding="utf-8-sig"))
        assert proc.returncode == expected, result
        return result
    found = run("--detect")
    assert str(game) in found["Paths"], found
    target = temp / "Test Game"
    target.mkdir(); (target / "Shroom and Gloom_Data").mkdir()
    (target / "Shroom and Gloom.exe").write_bytes(b"TEST ONLY - NOT EXECUTABLE")
    shutil.copyfile(game / "GameAssembly.dll", target / "GameAssembly.dll")
    sentinel = target / "original-file.txt"; sentinel.write_text("KEEP ME")
    run("--install", target)
    checked = run("--verify", target)
    assert all(checked["Files"].values()), checked
    run("--install", target)  # repeat installation must remain reversible
    run("--uninstall", target)
    assert not (target / "winhttp.dll").exists()
    assert not (target / "BepInEx/plugins/ShroomGerman/ShroomGerman.dll").exists()
    assert not (target / ".shroomgerman-install.json").exists()
    assert sentinel.read_text() == "KEEP ME"
    assert (target / "Shroom and Gloom.exe").read_bytes() == b"TEST ONLY - NOT EXECUTABLE"
    (target / "winhttp.dll").write_bytes(b"FOREIGN LOADER")
    conflict = run("--install", target, expected=1)
    assert (target / "winhttp.dll").read_bytes() == b"FOREIGN LOADER"
    assert not (target / "BepInEx/plugins/ShroomGerman/ShroomGerman.dll").exists()
    summary = {"discovery": found, "payloadFilesVerified": len(checked["Files"]), "reinstallAndUninstall": "passed",
               "originalFilesPreserved": True, "foreignLoaderProtected": True, "conflict": conflict, "testFolder": str(temp),
               "gameStarted": False}
    (ROOT / "artifacts/installer-smoke.json").write_text(json.dumps(summary,indent=2),encoding="utf-8")
    print(json.dumps(summary,indent=2))


if __name__ == "__main__":
    p=argparse.ArgumentParser();p.add_argument("--game",type=Path,default=Path(r"E:\SteamLibrary\steamapps\common\Shroom and Gloom"))
    p.add_argument("--installer", type=Path)
    args=p.parse_args()
    main(args.game, args.installer)
