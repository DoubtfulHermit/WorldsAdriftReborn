#!/usr/bin/env python3
"""For each MonoBehaviour class on a shipped prefab root, list its [Require] fields
and map each Reader/Writer type back to a SpatialOS component id.

Usage: requires.py <census.tsv> <root_name>
"""
import os, re, sys, csv

DEC = "/home/ttanurhan/Games/WAReborn-decompiled"

# component id map
ids = {}
with open(os.path.join(DEC, "component-map.tsv")) as f:
    r = csv.reader(f, delimiter="\t")
    next(r)
    for row in r:
        if len(row) >= 2:
            ids[row[1]] = row[0]

# index class name -> file
files = {}
for root, _, names in os.walk(os.path.join(DEC, "acs")):
    for n in names:
        if n.endswith(".cs"):
            files.setdefault(n[:-3], os.path.join(root, n))

census, target = sys.argv[1], sys.argv[2]
classes = []
with open(census) as f:
    r = csv.reader(f, delimiter="\t")
    next(r)
    for row in r:
        if len(row) >= 3 and row[1] == target and not row[2].startswith("<"):
            if row[2] not in classes:
                classes.append((row[2], row[3] if len(row) > 3 else ""))

REQ = re.compile(r"\[Require\][^\]]*?\n\s*(?:\[[^\]]*\]\s*\n\s*)*(?:private|protected|public|internal)?\s*([\w\.<>]+)\s+(\w+)\s*;")

def comp_of(t):
    t = t.split(".")[-1]
    for suf in ("StateReader", "StateWriter", "Reader", "Writer"):
        pass
    m = re.match(r"(.+?)\.(Reader|Writer)$", t)
    base = None
    if t.endswith("Reader"):
        base = t[:-6]
    elif t.endswith("Writer"):
        base = t[:-6]
    return base

print(f"# [Require] map for prefab root: {target}\n")
print("class\tenabled\trequire_type\tcomponent\tcomponentId")
for cls, en in classes:
    p = files.get(cls)
    if not p:
        print(f"{cls}\t{en}\t<source not found>\t\t")
        continue
    src = open(p, encoding="utf-8", errors="replace").read()
    found = REQ.findall(src)
    if not found:
        print(f"{cls}\t{en}\t-\t\t")
    for typ, fld in found:
        t = typ.split(".")[-1]
        # "Foo.Reader" form -> the class before the dot
        if t in ("Reader", "Writer"):
            base = typ.split(".")[-2]
        else:
            base = comp_of(t)
        cid = ids.get(base, "?")
        print(f"{cls}\t{en}\t{typ}\t{base}\t{cid}")
