"""Read-only export of the installed game's Unity Localization tables."""
import argparse
import hashlib
import json
from pathlib import Path

import UnityPy


def objects(path):
    return [o.read_typetree() for o in UnityPy.load(str(path)).objects if o.type.name == "MonoBehaviour"]


def export(game, out):
    aa = game / "Shroom and Gloom_Data/StreamingAssets/aa/StandaloneWindows64"
    shared = {t["m_TableCollectionName"]: t for t in objects(aa / "localization-assets-shared_assets_all.bundle")}
    english = objects(aa / "localization-string-tables-english(en)_assets_all.bundle")
    german = {t["m_Name"][:-3]: t for t in objects(aa / "localization-string-tables-german(de)_assets_all.bundle")}
    rows = []
    for table in english:
        collection = table["m_Name"][:-3]
        keys = {e["m_Id"]: e["m_Key"] for e in shared[collection]["m_Entries"]}
        de = {e["m_Id"]: e["m_Localized"] for e in german[collection]["m_TableData"]}
        for e in table["m_TableData"]:
            eid = e["m_Id"]
            rows.append({"table": collection, "id": str(eid), "key": keys.get(eid),
                         "en": e["m_Localized"], "original_de": de.get(eid, "")})
    out.mkdir(parents=True, exist_ok=True)
    (out / "source.json").write_text(json.dumps(rows, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    fingerprints = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in aa.glob("*.bundle")}
    fingerprints["GameAssembly.dll"] = hashlib.sha256((game / "GameAssembly.dll").read_bytes()).hexdigest()
    (out / "source-fingerprints.json").write_text(json.dumps(fingerprints, indent=2) + "\n", encoding="utf-8")
    print(f"Exported {len(rows)} entries from {len(english)} tables into {out}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--game", type=Path, default=Path(r"E:\SteamLibrary\steamapps\common\Shroom and Gloom"))
    parser.add_argument("--out", type=Path, default=Path("artifacts/source"))
    args = parser.parse_args()
    export(args.game, args.out)
