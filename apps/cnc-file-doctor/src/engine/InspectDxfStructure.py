"""Independent read-only structural pass, separate from the C# geometry reader.
No COM, no DXF rewriting, no packages. Uses native handles/layouts and SHA256.
"""
import argparse
import hashlib
import json
import sys
from collections import Counter, defaultdict
from decimal import Decimal
from pathlib import Path


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest().upper()


def inspect(path):
    raw = Path(path).read_bytes()
    lines = raw.decode("utf-8-sig", errors="strict").splitlines()
    assert len(lines) % 2 == 0, "Odd DXF group count"
    pairs = [(int(lines[i].strip()), lines[i + 1].strip()) for i in range(0, len(lines), 2)]
    assert pairs[-1] == (0, "EOF")
    records, headers, sections = [], {}, []
    section, block, record = None, None, None
    for i, pair in enumerate(pairs):
        code, value = pair
        if pair == (0, "SECTION"):
            assert section is None and pairs[i + 1][0] == 2
            section = pairs[i + 1][1]
            assert section not in sections
            sections.append(section)
            record = None
        elif pair == (0, "ENDSEC"):
            assert section is not None
            section, record = None, None
        elif code == 0:
            if section is not None:
                record = {"section": section, "pairs": [pair], "block": None}
                records.append(record)
        elif record is not None:
            record["pairs"].append(pair)
        if section == "HEADER" and code == 9:
            end = i + 1
            while end < len(pairs) and pairs[end][0] not in (0, 9):
                end += 1
            headers[value] = pairs[i + 1:end]

    def one(row, code, default=None):
        values = [v for k, v in row["pairs"] if k == code]
        assert len(values) <= 1, (row["pairs"][0], code, values)
        return values[0] if values else default

    block_records, layouts, definitions = {}, {}, {}
    for row in records:
        kind = row["pairs"][0][1]
        if row["section"] == "TABLES" and kind == "BLOCK_RECORD":
            block_records[one(row, 5)] = {"name": one(row, 2), "layoutHandle": one(row, 340)}
        if row["section"] == "OBJECTS" and kind == "LAYOUT":
            marker = row["pairs"].index((100, "AcDbLayout"))
            part = {"pairs": row["pairs"][marker + 1:]}
            layouts[one(row, 5)] = {"name": one(part, 1), "blockOwner": one(part, 330)}
        if row["section"] == "BLOCKS":
            if kind == "BLOCK":
                assert block is None
                block = one(row, 2)
                definitions[block] = {"owner": one(row, 330), "basePoint": [one(row, k, "0") for k in (10, 20, 30)]}
            elif kind == "ENDBLK":
                assert block is not None
                block = None
            else:
                assert block is not None
                row["block"] = block
    candidates = [r for r in records if r["section"] == "ENTITIES" or (r["section"] == "BLOCKS" and r["block"] is not None)]
    entities, spaces, handles = [], set(), set()
    for row in candidates:
        kind = row["pairs"][0][1]
        assert kind in ("LINE", "ARC", "CIRCLE", "SPLINE"), "Unsupported drawing entity"
        handle, owner = one(row, 5), one(row, 330)
        assert handle is not None and handle not in handles
        handles.add(handle)
        br = block_records[owner]
        layout = layouts[br["layoutHandle"]]
        assert layout["blockOwner"] == owner
        assert br["name"] == "*Model_Space" or br["name"].startswith("*Paper_Space")
        paper = br["name"] != "*Model_Space"
        flag = one(row, 67)
        assert flag is None or flag == ("1" if paper else "0")
        hint = one(row, 410)
        assert hint is None or hint == layout["name"]
        if row["block"] is not None:
            assert row["block"] == br["name"]
            definition = definitions[row["block"]]
            assert definition["owner"] == owner
            assert all(Decimal(x) == 0 for x in definition["basePoint"])
        normal = [one(row, k, d) for k, d in ((210, "0"), (220, "0"), (230, "1"))]
        assert [Decimal(x) for x in normal] == [Decimal(0), Decimal(0), Decimal(1)]
        assert all(Decimal(v) == 0 for k, v in row["pairs"] if 30 <= k <= 39)
        spaces.add((br["name"], layout["name"]))
        entities.append({"handle": handle, "type": kind, "section": row["section"], "block": row["block"], "owner330": owner, "group67": flag, "layout410": hint, "space": br["name"], "layout": layout["name"], "extrusion": normal, "layer": one(row, 8, "0"), "entityAci": one(row, 62, "256"), "trueColor": one(row, 420), "linetype": one(row, 6, "BYLAYER"), "coordinates": [[k, v] for k, v in row["pairs"] if k in (10, 11, 20, 21, 30, 31)]})
    assert len(spaces) == 1 and entities, "Not one populated layout"
    assert headers["$INSUNITS"] == [(70, "4")]
    layers, linetypes = {}, {}
    for row in records:
        if row["section"] != "TABLES":
            continue
        kind = row["pairs"][0][1]
        if kind == "LAYER":
            layers[one(row, 2)] = {"aci": int(one(row, 62)), "trueColor": one(row, 420), "linetype": one(row, 6)}
        elif kind == "LTYPE":
            segments = [v for k, v in row["pairs"] if k == 49]
            length = one(row, 40, "0")
            assert sum(abs(Decimal(v)) for v in segments) == Decimal(length)
            linetypes[one(row, 2)] = {"description": one(row, 3), "patternLengthMm": length, "segmentsMm": segments}
    for entity in entities:
        layer = layers[entity["layer"]]
        aci = int(entity["entityAci"])
        rgb = entity["trueColor"] if entity["trueColor"] is not None else (layer["trueColor"] if aci == 256 else None)
        entity["effectiveRgb"] = None if rgb is None else f"#{int(rgb):06X}"
        entity["effectiveAciFallback"] = layer["aci"] if aci == 256 else aci
        entity["colorSource"] = "TRUECOLOR_420" if rgb is not None else "ACI_62"
        entity["effectiveLinetype"] = layer["linetype"] if entity["linetype"].upper() == "BYLAYER" else entity["linetype"]
        assert entity["effectiveLinetype"] in linetypes
    assert digest(path) == hashlib.sha256(raw).hexdigest().upper()
    return {"path": str(path), "sha256": digest(path), "bytes": len(raw), "encoding": "UTF-8 strict (AC1032; legacy DWGCODEPAGE does not override UTF-8)", "headers": headers, "sectionRecordCounts": {name: dict(Counter(r["pairs"][0][1] for r in records if r["section"] == name)) for name in sections}, "nativeBlockRecords": block_records, "layouts": layouts, "selectedSpace": next(iter(spaces)), "geometryCounts": dict(Counter(e["type"] for e in entities)), "entityCount": len(entities), "layers": layers, "linetypes": linetypes, "entities": entities, "ownerLayoutConsistencyVerified": True, "standardPositiveZExtrusionVerified": True, "allGeometricZCoordinatesZero": True, "fileUnchanged": True, "productionReadinessCertified": False}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--group-report", required=True)
    parser.add_argument("--control-report", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    group = json.loads(Path(args.group_report).read_text(encoding="utf-8-sig"))
    control = json.loads(Path(args.control_report).read_text(encoding="utf-8-sig"))
    control_hashes = {r["path"]: r["sha256"] for r in control["productionFiles"]}
    inputs, results = {}, []
    for entry in group["files"]:
        assert digest(entry["path"]) == entry["sha256"]
        inputs[entry["path"]] = entry["sha256"]
        result = inspect(entry["path"])
        binding = [b for b in group["flatPatternBindings"] if Path(entry["path"]).name.startswith(b["flatPattern"] + " - ")]
        assert len(binding) == 1 and binding[0]["faceOwnerVerified"]
        result["bodyNameFromExistingNativeBinding"] = binding[0]["bodyName"]
        results.append(result)
    for row in control["sheetBodyExports"]:
        for key in ("exactDxfPath", "bendDxfPath"):
            path = row[key]
            assert digest(path) == control_hashes[path]
            inputs[path] = control_hashes[path]
            item = inspect(path)
            item["bodyName"] = row["bodyName"]
            item["role"] = key
            results.append(item)
    assert all(digest(path) == expected for path, expected in inputs.items())
    with Path(args.output).open("x", encoding="utf-8") as stream:
        json.dump({"tool": "Independent Python DXF structural inspection", "files": results, "inputHashesUnchanged": True, "fileCount": len(results), "productionReadinessCertified": False}, stream, ensure_ascii=False, indent=2)
    print(f"Independent structure/ownership/hash checks: {len(results)}/{len(results)} PASS")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
