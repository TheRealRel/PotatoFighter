# PotatoFighter page icons: the Kraken Axe from its game model (assets/kraken_axe.mesh), three angles, plus a shield for
# attack cancel. Uses render_icons.py (the Potato renderer) and pixel_finish.py (the pixel-art finishing pass).
#   python3 -I tools/page_icons.py docs/images
import os, sys, struct
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(__file__))
import trimesh
import render_icons as ri
from pixel_finish import finish

out = sys.argv[1]; os.makedirs(out, exist_ok=True)
A = os.path.join(os.path.dirname(__file__), "..", "assets")
raw = open(os.path.join(A, "kraken_axe.mesh"), "rb").read()
nv, ni = struct.unpack("<ii", raw[4:12]); o = 12
pos = np.frombuffer(raw, "<f4", nv * 3, o).reshape(-1, 3).astype(float); o += nv * 24
uv = np.frombuffer(raw, "<f4", nv * 2, o).reshape(-1, 2).astype(float); o += nv * 8
idx = np.frombuffer(raw, "<i4", ni, o).reshape(-1, 3)
pos = pos[:, [0, 2, 1]] * np.array([1, 1, -1])          # weapon frame (haft +Z) to upright (haft +Y)
mesh = trimesh.Trimesh(pos, idx, process=False)
col = np.clip(np.asarray(Image.open(os.path.join(A, "kraken_axe_d.png")).convert("RGB"), float) / 255 * 1.3, 0, 1)
glow = np.asarray(Image.open(os.path.join(A, "kraken_axe_e.png")).convert("L"), float) / 255
frost = np.array([0.35, 0.75, 1.0])
fuv = uv[idx]
def sample(img, st):
    h, w = img.shape[:2]
    return img[np.clip(((1 - st[:, 1] % 1) * (h - 1)).astype(int), 0, h - 1), np.clip(((st[:, 0] % 1) * (w - 1)).astype(int), 0, w - 1)]
def mat(p, n, t, b):                                   # colour plus the frost glow in the grooves
    st = (fuv[t] * b[..., None]).sum(1)
    return np.clip(sample(col, st) + frost[None] * sample(glow, st)[:, None] * 0.9, 0, 1)
mat.textured = True

def rot(axis, deg):
    return trimesh.transformations.rotation_matrix(np.radians(deg), axis)

def axe(yaw, pitch, lean=-42):
    # Turned to the angle, then leaned so the haft runs corner to corner like the game's weapon icons.
    m = mesh.copy()
    m.apply_transform(rot([0, 0, 1], lean) @ rot([1, 0, 0], pitch) @ rot([0, 1, 0], yaw))
    return finish(ri.render([(m, mat)], yaw=0, pitch=0, fov=26))

shots = {"axe-front": (-12, 8), "axe-turned": (38, 10), "axe-back": (165, 14)}
done = []
for name, (yaw, pitch) in shots.items():
    im = axe(yaw, pitch); im.save(os.path.join(out, f"icon-{name}.png")); done.append(im)
sh = finish(ri.shield()); sh.save(os.path.join(out, "icon-shield.png")); done.append(sh)
sheet = Image.new("RGBA", (len(done) * 88, 88), (58, 50, 44, 255))
for i, im in enumerate(done): sheet.paste(im, (i * 88 + 4, 4), im)
sheet.resize((sheet.width * 2, sheet.height * 2), Image.NEAREST).save(os.path.join(out, "_sheet.png"))
