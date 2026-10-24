#!/usr/bin/env python3
# Build check (Potato mods): every class marked [HarmonyPatch(...)] must actually be switched on, or the build stops.
# Mods that call harmony.PatchAll(...) switch on every marked class by themselves, so they pass. Mods that patch
# class by class (Patch(typeof(X)), lists of typeof(...)) must name each marked class somewhere outside its own
# declaration; a marked class that is never named is a fix that is written but never runs.
# Runs at build time only; the mod itself carries nothing extra.
import re, sys, pathlib

files = [pathlib.Path(p) for p in sys.argv[1:]] or sorted(pathlib.Path("src").glob("**/*.cs"))
text = {f: f.read_text(encoding="utf-8", errors="replace") for f in files}
everything = "\n".join(text.values())

if re.search(r"\.PatchAll\s*\(", everything):
    print("check_patches: PatchAll switches on every patch class.")
    sys.exit(0)

# Groups switched on whole: typeof(Group).GetNestedTypes(...) loops over every class declared inside Group.
groups = set(re.findall(r"typeof\(\s*(?:[\w.]+\.)?(\w+)\s*\)\s*\.GetNestedTypes", everything))

def spans(src, name):
    # Character ranges of the body of each "class name { ... }" in src (brace matching; braces in strings are rare here).
    out = []
    for m in re.finditer(r"\bclass\s+" + re.escape(name) + r"\b[^{;]*\{", src):
        depth, i = 1, m.end()
        while i < len(src) and depth:
            depth += {"{": 1, "}": -1}.get(src[i], 0)
            i += 1
        out.append((m.end(), i))
    return out

# A class whose declaration has [HarmonyPatch(...)] attributes directly above it (other attributes may sit between).
decl = re.compile(r"((?:\s*\[[^\]]*\]\s*)+)\s*(?:(?:public|internal|private|protected|static|sealed|partial)\s+)*class\s+(\w+)")
missing = []
for f, src in text.items():
    inside = [s for g in groups for s in spans(src, g)]
    for m in decl.finditer(src):
        attrs, name = m.group(1), m.group(2)
        if "HarmonyPatch(" not in attrs:
            continue
        if any(a <= m.start(2) < b for a, b in inside):
            continue
        # Named anywhere as a type (typeof(Outer.Name), typeof(Name)), apart from its own declaration.
        if not re.search(r"typeof\(\s*(?:[\w.]+\.)?" + re.escape(name) + r"\s*\)", everything):
            line = src.count("\n", 0, m.start(2)) + 1
            missing.append(f"{f}:{line}: {name}")

if missing:
    print("check_patches: these patch classes are never switched on (add them to the plugin's patch list):")
    for m in missing:
        print("  " + m)
    sys.exit(1)
print("check_patches: every patch class is switched on.")
