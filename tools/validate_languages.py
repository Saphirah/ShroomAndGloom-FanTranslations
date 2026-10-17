"""Static checks for language completeness, source tokens, IDs and whitespace."""
import argparse
import json
from pathlib import Path
import re
import sys
import unicodedata
from decimal import Decimal, InvalidOperation
from collections import Counter

from build_languages import tokens
from build_translation import edges

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".tools/python"))


def numbers(text):
    text = re.sub(r"\[(?:[^\[\]]|\[[^\[\]]*\])*\]|<[^>]+>", "", text)
    text = re.sub(r"\\u[0-9a-fA-F]{4}", "", text)
    found = []
    for value in re.findall(r"\d+(?:[.,]\d+)*", text):
        if re.fullmatch(r"[1-9]\d{0,2}(?:[,.]\d{3})+", value): value = value.replace(",", "").replace(".", "")
        else: value = value.replace(",", ".")
        try: found.append(str(Decimal(value).normalize()))
        except InvalidOperation: found.append(value)
    return Counter(found)


def credited_names(text):
    names = []
    for line in re.split(r"\r?\n|/n", text):
        line = line.strip().split(" – ")[0].strip()
        if re.fullmatch(r"[A-ZÀ-ÖØ-Þ][\w.'’\-]+(?: [A-ZÀ-ÖØ-Þ][\w.'’\-]+){1,4}", line):
            names.append(line)
    return names


def validate(partial=False):
    languages = json.loads((ROOT / "data/languages.json").read_text(encoding="utf-8"))["languages"]
    english = json.loads((ROOT / "artifacts/source/source.json").read_text(encoding="utf-8"))
    reference = {(row["table"], row["id"]): (index, row) for index, row in enumerate(english)}
    literal_keys = set(json.loads((ROOT / "data/ui-literals.de.json").read_text(encoding="utf-8")))
    report = {"expectedLanguages": len(languages), "languages": {}, "missingLanguages": [], "issues": []}
    from fontTools.ttLib import TTFont
    font_characters = set()
    for path in (ROOT / "assets/fonts").iterdir():
        if path.suffix.lower() in (".ttf", ".otf"):
            with TTFont(path, lazy=True) as font: font_characters.update(font.getBestCmap())
    original_characters = set("".join(row["en"] for row in english))
    for language in languages:
        code = language["code"]; path = ROOT / "data/languages" / (code + ".json")
        if not path.exists(): report["missingLanguages"].append(code); continue
        print("Checking " + code, flush=True)
        data = json.loads(path.read_text(encoding="utf-8"))
        if data["locale"] != code: report["issues"].append({"locale": code, "kind": "identity"})
        seen = set()
        for row in data["entries"]:
            key = (row["table"], row["id"])
            if key in seen: report["issues"].append({"locale": code, "kind": "duplicate", "key": key})
            seen.add(key)
            if key not in reference:
                if code != "de": report["issues"].append({"locale": code, "kind": "unexpected_entry", "key": key})
                continue
            index, original = reference[key]
            text = row["text"]; en = original["en"]
            if tokens(en) != tokens(text): report["issues"].append({"locale": code, "row": index, "kind": "tokens"})
            if en.strip() and edges(en) != edges(text): report["issues"].append({"locale": code, "row": index, "kind": "whitespace"})
            if en.strip() and not text.strip(): report["issues"].append({"locale": code, "row": index, "kind": "empty"})
            name_key = original.get("key") or ""
            # Source internal title labels (P1_Boss etc.) may legitimately become
            # localized ordinals. Gameplay quantities retain explicit digits.
            if "_TITLE_" not in name_key and numbers(en) - numbers(text):
                report["issues"].append({"locale": code, "row": index, "kind": "numbers", "missing": dict(numbers(en)-numbers(text))})
            if len(re.findall(r"\\u[0-9a-fA-F]{4}", en)) >= 8 and text != en:
                report["issues"].append({"locale": code, "row": index, "kind": "technical_glyph_effect"})
            if (original.get("key") or "").endswith("NAMESONLYTEXT"):
                absent = [name for name in credited_names(en) if name not in text]
                if absent: report["issues"].append({"locale": code, "row": index, "kind": "credits_names", "missing": absent})
        for key in reference.keys() - seen: report["issues"].append({"locale": code, "kind": "missing_entry", "key": key})
        if set(data["literals"]) != literal_keys: report["issues"].append({"locale": code, "kind": "literal_keys"})
        for source, text in data["literals"].items():
            if not text.strip() or tokens(source) != tokens(text): report["issues"].append({"locale": code, "kind": "literal", "source": source})
        visible = "".join(row["text"] for row in data["entries"]) + "".join(data["literals"].values()) + data["nativeName"]
        missing_glyphs = sorted({c for c in visible if c not in original_characters and unicodedata.category(c)[0] in "LM"
                                and not (0xFE00 <= ord(c) <= 0xFE0F) and ord(c) not in font_characters})
        if missing_glyphs: report["issues"].append({"locale": code, "kind": "font_coverage", "characters": missing_glyphs})
        report["languages"][code] = {"entries": len(data["entries"]), "literals": len(data["literals"]), "quality": data["quality"], "bytes": path.stat().st_size}
    (ROOT / "artifacts/language-validation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"validatedLanguages": len(report["languages"]), "missing": report["missingLanguages"], "issues": report["issues"]}, ensure_ascii=True))
    return bool(report["issues"] or (report["missingLanguages"] and not partial))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(); parser.add_argument("--partial", action="store_true")
    raise SystemExit(validate(parser.parse_args().partial))
