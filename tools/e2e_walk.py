"""Walks the rows of docs/E2E_CHECKLIST.md and records the result of each in the Verified column. Usage: python3 tools/e2e_walk.py"""
import re, datetime, sys
p = "docs/E2E_CHECKLIST.md"
lines = open(p).read().split("\n")
today = datetime.date.today().isoformat()
print("For each step answer y (works), n (fails) or s (skip). Notes are optional.\n")
for i, l in enumerate(lines):
    c = [x.strip() for x in l.strip().strip("|").split("|")]
    if len(c) != 4 or not c[0].isdigit(): continue
    n, step, expect, _ = c
    while True:
        a = input(f"{n}. {step}: {expect}\n   works? [y/n/s] ").strip().lower()
        if a in ("y", "n", "s"): break
    note = input("   note (optional): ").strip() if a == "n" else ""
    res = {"y": f"Yes {today}", "n": f"No {today}" + (f": {note}" if note else ""), "s": "Skipped"}[a]
    lines[i] = f"| {n} | {step} | {expect} | {res.replace('|', '/')} |"
open(p, "w").write("\n".join(lines))
print("\nSaved", p)
