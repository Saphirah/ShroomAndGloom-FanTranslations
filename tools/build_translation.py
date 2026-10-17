"""Build the reviewed, ID-based runtime catalog and a focused consistency report."""
import collections
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def visible_cleanup(text):
    # Never modify the game's machine-readable tokens or link targets.
    parts = re.split(r"(\[[^\]]*\]|<[^>]*>)", text)
    replacements = {
        "Erkunden-Karten": "Erkundungskarten", "Erkunden-Karte": "Erkundungskarte",
        "Rostfrei": "Fleckenfrei", "ROSTFREI": "FLECKENFREI",
        "FATAL": "TÖDLICH", "fatal": "tödlich",
        "gelagerte Finsternis": "gespeicherte Finsternis",
        "Gelagerte Finsternis": "Gespeicherte Finsternis",
        "gelagerten Finsternis": "gespeicherten Finsternis",
        "auf die Hand kriegst": "auf die Hand bekommst",
    }
    for i in range(0, len(parts), 2):
        for a, b in replacements.items():
            parts[i] = parts[i].replace(a, b)
    return "".join(parts)


def edges(text):
    return re.match(r"^\s*", text)[0], re.search(r"\s*$", text)[0]


def preserve_edges(source, translated):
    if not source.strip():
        return translated
    leading, trailing = edges(source)
    return leading + translated.strip() + trailing


def separate_fragments(text):
    # JOINER resolves to a word (e.g. "in"), not to a guaranteed separator.
    text = re.sub(r"\s*(\[JOINER=[^\]]+\])\s*", r" \1 ", text)
    # Actions followed directly by target fragments otherwise form LÄHMENGEGNER.
    return re.sub(r"(?<=\])(?=\[MULTIPLICITY_)", " ", text)


def build():
    source_path = ROOT / "artifacts/source/source.json"
    rows = json.loads(source_path.read_text(encoding="utf-8"))
    if len(rows) != 2857 or hashlib.sha256(source_path.read_bytes()).hexdigest() != "2826627f1a9d4423e1abf990c4d94e3a128de3c81a2f9ef2d121fcc74e7b700b":
        raise ValueError("Source changed: re-export and review the new build before rebuilding")
    edits = {}
    for p in sorted((ROOT / "data").glob("editorial-*.json")):
        incoming = json.loads(p.read_text(encoding="utf-8"))
        duplicate = edits.keys() & incoming.keys()
        if duplicate:
            raise ValueError(f"Duplicate editorial decisions in {p}: {duplicate}")
        edits.update(incoming)
    generic = {"Further Detail Here": "Weitere Einzelheiten", "Detail Here": "Einzelheiten",
               "TEXT GOES HERE": "HIER STEHT TEXT", "This is further detail": "Weitere Einzelheiten",
               "n/a": "k. A."}
    result = []
    issues = []
    counts = collections.Counter()
    for i, row in enumerate(rows):
        en = row["en"]
        de = edits.get(str(i), generic.get(en, row["original_de"]))
        if en == "_":
            de = "_"  # Unity/game intentional-empty marker, not missing translation.
        if i == 1106:
            # The shipped German credits are outdated and omit contributors.
            de = en.replace("<b>Testing</b>", "<b>Qualitätssicherung</b>").replace("<b>Localization</b>", "<b>Lokalisierung</b>")
        if i == 1164:
            de = "[HEXCODE]EXPLODIERT BEIM EIGENEN ZUG</color>"
        de = preserve_edges(en, separate_fragments(visible_cleanup(de)))
        if en.strip() and edges(en) != edges(de):
            issues.append({"row": i, "kind": "boundary_whitespace"})
        if re.search(r"\[JOINER=[^\]]+\](?=\S)|\S(?=\[JOINER=)", de):
            issues.append({"row": i, "kind": "joined_words", "de": de})
        if en.strip() and not de.strip():
            issues.append({"row": i, "kind": "empty", "en": en})
        # JOINER tokens are grammar helpers; German often spells their replacement out.
        def tokens(s):
            return collections.Counter(x for x in re.findall(r"\[(?:[^\[\]]|\[[^\[\]]*\])*\]", s)
                                       if not x.startswith("[JOINER="))
        if tokens(en) != tokens(de):
            issues.append({"row": i, "kind": "tokens", "missing": list((tokens(en)-tokens(de)).elements()),
                           "extra": list((tokens(de)-tokens(en)).elements()), "en": en, "de": de})
        result.append({"table": row["table"], "id": row["id"], "key": row["key"], "de": de})
        counts[row["table"]] += 1
    supplement = json.loads((ROOT / "data/supplemental.de.json").read_text(encoding="utf-8"))
    known = {(row["table"], row["id"]) for row in result}
    for row in supplement:
        if (row["table"], row["id"]) in known or not row["de"].strip():
            raise ValueError("Invalid supplemental entry")
        result.append(row)
        counts[row["table"]] += 1
        known.add((row["table"], row["id"]))
    dest = ROOT / "data/translations.de.json"
    dest.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    report = {"entries": len(result), "tables": dict(counts), "englishReferenceEntries": len(rows), "additionalGermanEntries": len(supplement), "editorialChanges": sum(a["original_de"] != b["de"] for a,b in zip(rows,result)),
              "boundaryWhitespace": "exactly preserved from English reference", "fragmentSeparators": "explicit around JOINER and target fragments",
              "sourceSha256": hashlib.sha256(source_path.read_bytes()).hexdigest(), "issues": issues}
    (ROOT / "artifacts/translation-check.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return bool(issues)


if __name__ == "__main__":
    raise SystemExit(build())
