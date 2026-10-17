"""Focused semantic/terminology pass after complete per-entry editorial review.

Keeps the manually edited German pack intact. Also restores intentionally
non-linguistic glyph effects and repairs flagged literal gameplay quantities.
"""
from concurrent.futures import ThreadPoolExecutor, as_completed
import hashlib
import json
from pathlib import Path
import re

import build_languages as generator
from build_translation import preserve_edges
from validate_languages import validate, numbers, credited_names

ROOT = Path(__file__).resolve().parents[1]
NOTES = """Important semantic distinctions for this game:
GLOOM is captured DARK ENERGY/darkness, never emotional sadness, depression or gloominess.
STORE GLOOM means absorb/capture that dark energy. It is distinct from ABSORB damage mitigation.
GUNK is a grimy coating on cards. GUNKY means covered in that same substance. Use the same
localized stem consistently across property labels, adjectives, tooltips and prose.
Gunk costs energy to play; gunked cards linger; one Gunk is lost at turn start.
GOO is a gelatinous gel/slime, distinct from that grimy GUNK. Do not merge these terms.
PERSISTENT means the card is NOT discarded when played. Permanent upgrades/deck changes
are a different mechanic and must not use the Persistent keyword interchangeably.
LODGED means a card is stuck inside a creature and returns to hand when the creature dies.
CONSUME means the card is destroyed upon being played; keep it distinct from FORGET.
MAKE a card means CREATE a card, never a generic DO verb.
Keep literal gameplay quantities as Arabic digits matching the source, including quest
number suffixes. Internal reward titles such as P1_Boss may use natural localized ordinals.
Preserve the game title Shroom and Gloom and all real person's/company names exactly.
This is terminology/quantity editing, not an invitation to change any gameplay rules.
Use fluent idiomatic target language and concise card UI wording. Do not leave generic
Gunk/Gloom words in English in a non-Latin script language; localize their actual meaning.
"""


def review(language, source, issues):
    code = language["code"]
    path = ROOT / "data/languages" / (code + ".json")
    data = json.loads(path.read_text(encoding="utf-8"))
    original_bytes = path.read_bytes()
    cache = generator.CACHE / "semantic-review" / (code + ".json")
    source_sha = hashlib.sha256((ROOT / "artifacts/source/source.json").read_bytes()).hexdigest()
    if cache.exists() and json.loads(cache.read_text(encoding="utf-8")).get("sourceSha256") == source_sha and json.loads(cache.read_text(encoding="utf-8"))["resultSha256"] == hashlib.sha256(original_bytes).hexdigest():
        print("SEMANTIC COMPLETE " + code + " (checkpoint)", flush=True); return
    lookup = {(r["table"], r["id"]): r for r in data["entries"]}
    technical = False
    for row in source:
        if len(re.findall(r"\\u[0-9a-fA-F]{4}", row["en"])) >= 8 or row.get("key") == "CREDITS_SECTION_02_NAMESONLYTEXT":
            entry = lookup[(row["table"], row["id"])]
            technical |= entry["text"] != row["en"]; entry["text"] = row["en"]
    corrections = 0
    if code not in ("en", "de"):
        flagged = {issue["row"] for issue in issues if issue.get("locale") == code and "row" in issue and issue["kind"] != "technical_glyph_effect"}
        flagged = {index for index in flagged if source[index].get("key") != "CREDITS_SECTION_02_NAMESONLYTEXT"}
        selected = []
        for index, row in enumerate(source):
            if index in flagged or re.search(r"\bgunk\w*|\bgloom\w*|\bgoo(?:ey|s)?\b|\bpersistent\b|\bpermanent\b|\blodged\b|\babsorb\b", row["en"], re.I):
                selected.append({"i": index, "key": row["key"], "en": row["en"], "draft": lookup[(row["table"], row["id"])]["text"]})
        expected = {r["i"]: r for r in selected}
        prompt = generator.RULES + "\n" + NOTES + f"\nTarget: {language['name']} ({code}). "
        prompt += "Review EVERY provided draft for terminology consistency, wrong meanings and missing quantities. "
        prompt += "When changing a canonical term, correct affected prose too. Return only changed i/t items. "
        prompt += "\nFlagged rows requiring attention: " + json.dumps(sorted(flagged))
        prompt += "\nItems: " + json.dumps(selected, ensure_ascii=False)
        response = generator.api(prompt, thinking="high")
        seen = set()
        for item in response:
            index = item.get("i")
            if index not in expected or index in seen: raise ValueError("Unexpected semantic review row")
            seen.add(index); row = source[index]; text = item["t"]
            if not generator.valid(row["en"], text):
                fixed = generator.run_batch(language, "translate", [{"i": index, "en": row["en"], "draft": text}], {"semanticNotes": NOTES})
                text = next((r["t"] for r in fixed if r["i"] == index), text)
            if not generator.valid(row["en"], text): raise ValueError(f"Semantic review damaged markup: {code}/{index}")
            lookup[(row["table"], row["id"])]["text"] = preserve_edges(row["en"], text); corrections += 1
        # Each automated quantity/credits flag must be resolved before committing the pack.
        for index in flagged:
            row = source[index]; text = lookup[(row["table"], row["id"])]["text"]
            remaining = numbers(row["en"]) - numbers(text) if "_TITLE_" not in (row["key"] or "") else {}
            names = [name for name in credited_names(row["en"]) if name not in text] if (row["key"] or "").endswith("NAMESONLYTEXT") else []
            if remaining or names:
                repair = generator.api(generator.RULES + "\n" + NOTES + f"\nTarget: {language['name']}. "
                    "Correct this ONE item. Keep source numeric digits exactly and all source contributor names. "
                    + json.dumps({"i": index, "en": row["en"], "draft": text, "requiredNumbers": dict(remaining), "requiredNames": names}, ensure_ascii=False), thinking="high")
                fixed = next((r["t"] for r in repair if r.get("i") == index), text)
                if not generator.valid(row["en"], fixed) or numbers(row["en"]) - numbers(fixed) or any(name not in fixed for name in names):
                    raise ValueError(f"Semantic flag remains: {code}/{index}")
                lookup[(row["table"], row["id"])]["text"] = preserve_edges(row["en"], fixed); corrections += 1
    if corrections or technical: generator.write(path, data)
    generator.write(cache, {"locale": code, "corrections": corrections, "technicalGlyphsPreserved": technical,
        "sourceSha256": source_sha, "resultSha256": hashlib.sha256(path.read_bytes()).hexdigest(), "method": "targeted semantic/editorial pass; high thinking; not native-speaker certification"})
    print(f"SEMANTIC COMPLETE {code}: {corrections} corrections", flush=True)


def main():
    generator.USAGE.update(json.loads((generator.CACHE / "usage.json").read_text(encoding="utf-8")))
    source = json.loads((ROOT / "artifacts/source/source.json").read_text(encoding="utf-8"))
    validate(partial=True)
    issues = json.loads((ROOT / "artifacts/language-validation.json").read_text(encoding="utf-8"))["issues"]
    languages = json.loads((ROOT / "data/languages.json").read_text(encoding="utf-8"))["languages"]
    failed = []
    with ThreadPoolExecutor(max_workers=4) as pool:
        jobs = {pool.submit(review, language, source, issues): language["code"] for language in languages}
        for job in as_completed(jobs):
            try: job.result()
            except Exception as error: failed.append({"locale": jobs[job], "error": str(error)}); print("SEMANTIC FAILED", failed[-1], flush=True)
    generator.write(generator.CACHE / "semantic-failures.json", failed)
    return bool(failed or validate())


if __name__ == "__main__":
    raise SystemExit(main())
