# PotatoFighter page icon for creature levels: a row of gold stars like the ones over a creature's health bar, as our own
# model (not the game's star image): each star is faceted (a low point raised in the middle, front and back), so every
# arm has a lit and a shaded side at icon size. Rendered with the Potato renderer and its pixel-art finishing pass.
#   python3 -I tools/star_icon.py docs/images [count]
import math, os, sys
import numpy as np
import trimesh
from PIL import Image
sys.path.insert(0, os.path.dirname(__file__))
import render_icons as ri
from pixel_finish import finish

def star(cx, r=0.42, inner=0.45, lift=0.16):
    ring = []
    for k in range(10):
        a = math.pi / 2 + k * math.pi / 5                       # first point straight up
        rr = r if k % 2 == 0 else r * inner
        ring.append((cx + rr * math.cos(a), rr * math.sin(a), 0.0))
    v = np.array(ring + [(cx, 0, lift), (cx, 0, -lift)])
    top, bottom = 10, 11
    faces = []
    for k in range(10):
        n = (k + 1) % 10
        faces.append((k, n, top))
        faces.append((n, k, bottom))
    return trimesh.Trimesh(v, faces, process=False)

def gold(p, n):
    # The game's star yellow, lit per facet: arms facing the light shine, the others fall to a deep amber.
    base = ri.rgb(255, 186, 48)
    k = 0.78 + 0.42 * np.clip(n @ np.array([-0.45, 0.7, 0.55]), 0, 1)
    return np.clip(base[None, :] * k[:, None], 0, 1)

def row(count=3, gap=0.86):
    xs = [(i - (count - 1) / 2) * gap for i in range(count)]
    parts = [(star(x), gold) for x in xs]
    return finish(ri.render(parts, yaw=0, pitch=8, fov=30 + 6 * (count - 3)))

if __name__ == "__main__":
    out = sys.argv[1]; count = int(sys.argv[2]) if len(sys.argv) > 2 else 3
    os.makedirs(out, exist_ok=True)
    im = row(count)
    im.save(os.path.join(out, "icon-stars.png"))
    big = Image.new("RGBA", (im.width * 4, im.height * 4), (58, 50, 44, 255))
    big.paste(im.resize((im.width * 4, im.height * 4), Image.NEAREST), (0, 0), im.resize((im.width * 4, im.height * 4), Image.NEAREST))
    big.save(os.path.join(out, "_stars_preview.png"))
