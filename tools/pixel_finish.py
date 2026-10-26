# Pixel-art finishing pass for the Potato page icons (Valheim mods). Takes a rendered icon (40 px art shown at 80 px,
# dark outline) and applies the rules the pixel-art guides agree on:
#   1. Few shades per material: pixels are grouped by colour family, and each group is snapped to 4 shades.
#   2. Hue-shifted shades: shadows lean cool (blue), highlights lean warm (yellow), instead of only darker/lighter.
#   3. Selective outline: each outline pixel is a darker, cooler version of the colour it borders; a little lighter
#      on the lit (top-left) edges, darker underneath.
#   4. No stray pixels: lone pixels sticking out of the silhouette are dropped, and lone shade pixels inside a
#      group take their neighbours' shade.
#   python3 -I tools/pixel_finish.py in.png out.png   (or import finish())
import sys
import numpy as np
from PIL import Image

ART, UP = 40, 2
COOL = np.array([0.32, 0.38, 0.62])      # what shadows lean toward
WARM = np.array([1.0, 0.9, 0.62])        # what highlights lean toward
OUTLINE_MIN = np.array([28, 18, 12]) / 255.0

def _kmeans(x, k, iters=20, seed=0):
    rng = np.random.default_rng(seed)
    c = x[rng.choice(len(x), k, replace=False)]
    for _ in range(iters):
        lab = np.argmin(((x[:, None] - c[None]) ** 2).sum(-1), 1)
        for j in range(k):
            if (lab == j).any(): c[j] = x[lab == j].mean(0)
    return lab, c

def finish(img, groups=None):
    """img: the renderer's 80 px RGBA image. Returns the finished 80 px RGBA image."""
    a = np.asarray(img.convert("RGBA").resize((ART, ART), Image.NEAREST), float) / 255.0
    rgb, alpha = a[..., :3], a[..., 3] > 0.5
    lum = rgb.max(-1)
    old_outline = alpha & (np.abs(rgb - np.array([38, 24, 14]) / 255.0).sum(-1) < 0.06)
    solid = alpha & ~old_outline

    # 4. Lone pixels on the silhouette go: no solid neighbour at all, counting diagonals, so thin diagonal lines
    #    (a bowstring, an arrow shaft) stay.
    p = np.pad(solid, 1)
    nb8 = sum(p[1 + dy:ART + 1 + dy, 1 + dx:ART + 1 + dx].astype(int)
              for dy in (-1, 0, 1) for dx in (-1, 0, 1) if (dy, dx) != (0, 0))
    solid &= nb8 >= 1

    # 1. Colour families: cluster by chroma (colour with brightness divided out) plus a little saturation.
    ys, xs = np.where(solid)
    col = rgb[ys, xs]
    l = np.maximum(col.max(-1), 1e-3)
    chroma = col / l[:, None]
    sat = (col.max(-1) - col.min(-1)) / l
    feat = np.column_stack([chroma, sat * 0.8])
    k = groups or min(4, max(1, len(np.unique(np.round(feat * 6), axis=0)) // 3))
    lab, _ = _kmeans(feat, k) if len(feat) >= k else (np.zeros(len(feat), int), None)

    out = np.zeros((ART, ART, 4))
    level = np.full((ART, ART), -1); group = np.full((ART, ART), -1)
    dark_detail = np.zeros((ART, ART), bool)
    shades = {}
    for g in range(k):
        sel = lab == g
        if not sel.any(): continue
        gl = l[sel].copy()
        # Average each pixel's brightness with its same-family neighbours first: grain becomes gentle variation
        # instead of speckle once snapped to 4 shades.
        gmap = np.full((ART, ART), np.nan); gmap[ys[sel], xs[sel]] = gl
        gp = np.pad(gmap, 1, constant_values=np.nan)
        stack = np.stack([gp[1 + dy:ART + 1 + dy, 1 + dx:ART + 1 + dx] for dy in (-1, 0, 1) for dx in (-1, 0, 1)])
        sm = np.nanmean(np.where(np.isnan(stack), np.nan, stack), 0)
        grey = float(np.median(sat[sel])) < 0.18                        # iron, stone, a grey coat
        raw = gl.copy()
        gl = (0.15 if grey else 0.4) * gl + (0.85 if grey else 0.6) * sm[ys[sel], xs[sel]]
        # Small dark details (nail holes, seams, an eye) keep their darkness instead of being smoothed away.
        detail = raw < 0.6 * np.median(raw)
        gl[detail] = raw[detail]
        base = (chroma[sel] * gl[:, None]).sum(0) / gl.sum()           # the family's colour at brightness 1
        base = base / max(base.max(), 1e-3)
        qs = np.quantile(gl[~detail] if (~detail).sum() > 8 else gl, [0.12, 0.4, 0.68, 0.92])   # 4 levels from the body's own spread
        if qs[-1] - qs[0] < 0.08: qs = np.linspace(qs[1] - 0.12, qs[1] + 0.12, 4)
        lv = np.argmin(np.abs(gl[:, None] - qs[None]), 1)
        lv[detail] = 0
        level[ys[sel], xs[sel]] = lv; group[ys[sel], xs[sel]] = g
        dark_detail[ys[sel][detail], xs[sel][detail]] = True
        sh = []
        for i, L in enumerate(qs):
            t = (i - 1.5) / 1.5                                         # -1 darkest .. +1 brightest
            c = base * L
            k_ = 0.12 if grey else 0.28                                 # greys only lean a little, or they turn blue
            if t < 0: c = c * (1 + t * k_) + COOL * L * (-t * k_)       # 2. cool shadows
            else: c = c * (1 - t * k_ * 0.7) + WARM * L * (t * k_ * 0.7)  #    warm highlights
            sh.append(np.clip(c, 0, 1))
        shades[g] = sh

    # 4. A lone shade pixel inside its group takes the most common shade around it.
    lv2 = level.copy()
    for y, x in zip(ys, xs):
        g = group[y, x]; ns = []
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            yy, xx = y + dy, x + dx
            if 0 <= yy < ART and 0 <= xx < ART and group[yy, xx] == g: ns.append(level[yy, xx])
        if len(ns) >= 3 and level[y, x] not in ns and not dark_detail[y, x]:
            lv2[y, x] = max(set(ns), key=ns.count)
    for y, x in zip(ys, xs):
        c = shades[group[y, x]][lv2[y, x]]
        out[y, x, :3] = c * 0.62 if dark_detail[y, x] else c; out[y, x, 3] = 1

    # 3. Selective outline around the cleaned silhouette.
    p = np.pad(solid, 1)
    ring = ~solid & (p[:-2, 1:-1] | p[2:, 1:-1] | p[1:-1, :-2] | p[1:-1, 2:])
    # Small enclosed holes (a stirrup, a ring) stay see-through instead of filling up with outline.
    from scipy import ndimage
    holes, nh = ndimage.label(~solid)
    border = set(np.unique(np.concatenate([holes[0], holes[-1], holes[:, 0], holes[:, -1]])))
    for h_ in range(1, nh + 1):
        if h_ not in border and (holes == h_).sum() <= 6: ring &= holes != h_
    for y, x in zip(*np.where(ring)):
        best, lit = None, False
        for dy, dx in ((1, 0), (0, 1), (-1, 0), (0, -1)):
            yy, xx = y + dy, x + dx
            if 0 <= yy < ART and 0 <= xx < ART and solid[yy, xx]:
                if best is None: best = shades[group[yy, xx]][0]
                if (dy, dx) in ((1, 0), (0, 1)): lit = True             # the outline sits above/left of the shape
        c = best * (0.5 if lit else 0.32)
        c = c * 0.8 + COOL * c.max() * 0.2
        out[y, x, :3] = np.maximum(np.minimum(c, best * 0.6), OUTLINE_MIN * 0.6); out[y, x, 3] = 1
    return Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8), "RGBA").resize((ART * UP, ART * UP), Image.NEAREST)

if __name__ == "__main__":
    finish(Image.open(sys.argv[1])).save(sys.argv[2])
