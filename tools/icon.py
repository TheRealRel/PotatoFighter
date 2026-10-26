# Inventory icon for a PotatoFighter weapon, laid out like the game's own weapon icons: lying diagonally, haft from
# bottom left to head top right, flat side to the viewer, filling the square, lit from the top left, with a soft dark
# edge. Renders the weapon-frame .mesh (orient.py: haft +Z, blades X) with its colour texture, nearest-pixel.
#   python3 -I icon.py weapon.mesh weapon_d.png out.png [size=64] [lift=1.25]
import struct, sys
import numpy as np
from PIL import Image, ImageFilter
mesh_path, tex_path, out = sys.argv[1:4]
opt = dict(a.split("=", 1) for a in sys.argv[4:])
SIZE = int(opt.get("size", 64)); LIFT = float(opt.get("lift", 1.25)); BIG = SIZE * 8
b = open(mesh_path, "rb").read(); nv, ni = struct.unpack_from("<ii", b, 4); o = 12
P = np.frombuffer(b, "<f4", nv * 3, o).reshape(nv, 3).astype(float); o += nv * 12
N = np.frombuffer(b, "<f4", nv * 3, o).reshape(nv, 3).astype(float); o += nv * 12
U = np.frombuffer(b, "<f4", nv * 2, o).reshape(nv, 2).astype(float); o += nv * 8
I = np.frombuffer(b, "<i4", ni, o).reshape(-1, 3)
tex = np.asarray(Image.open(tex_path).convert("RGB"), float); th, tw = tex.shape[:2]
# screen: u right, v up; haft (+Z) up-right, blades (X) down-right; depth toward the viewer = -Y
s2 = np.sqrt(0.5)
def screen(v): return np.stack([(v[:, 0] + v[:, 2]) * s2, (v[:, 2] - v[:, 0]) * s2, -v[:, 1]], 1)
k = 50
bs = np.array([(i, j, k - i - j) for i in range(k + 1) for j in range(k + 1 - i)], float) / k
Q = screen(np.einsum("sk,fkd->fsd", bs, P[I]).reshape(-1, 3))
Nn = screen(np.einsum("sk,fkd->fsd", bs, N[I]).reshape(-1, 3)); Nn /= np.linalg.norm(Nn, axis=1, keepdims=True).clip(1e-9)
T = np.einsum("sk,fkd->fsd", bs, U[I]).reshape(-1, 2)
col = tex[np.clip((1 - T[:, 1] % 1) * th, 0, th - 1).astype(int), np.clip((T[:, 0] % 1) * tw, 0, tw - 1).astype(int)]
L = np.array([-0.45, 0.55, 0.7]); L /= np.linalg.norm(L)        # from the top left, in front
shade = 0.45 + 0.75 * np.clip(Nn @ L, 0, 1)
c = np.clip(col * LIFT * shade[:, None], 0, 255)
lo, hi = Q[:, :2].min(0), Q[:, :2].max(0)
pad = BIG * 0.06
sc = (BIG - 2 * pad) / max(hi - lo)
off = (BIG - (hi - lo) * sc) / 2
x = ((Q[:, 0] - lo[0]) * sc + off[0]).astype(int); y = (BIG - 1 - ((Q[:, 1] - lo[1]) * sc + off[1])).astype(int)
order = np.argsort(Q[:, 2])
img = np.zeros((BIG, BIG, 4)); img[y[order], x[order], :3] = c[order]; img[y[order], x[order], 3] = 255
big = Image.fromarray(img.astype(np.uint8), "RGBA")
small = big.resize((SIZE, SIZE), Image.LANCZOS)
a = np.asarray(small).astype(float)
# soft dark edge like the game's icons: a blurred, darkened copy of the silhouette under the weapon
alpha = Image.fromarray(a[:, :, 3].astype(np.uint8)).filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.GaussianBlur(0.8))
edge = np.zeros_like(a); edge[:, :, :3] = [18, 12, 8]; edge[:, :, 3] = np.asarray(alpha, float) * 0.85
fa = a[:, :, 3:4] / 255
outp = edge.copy(); outp[:, :, :3] = a[:, :, :3] * fa + edge[:, :, :3] * (1 - fa)
outp[:, :, 3] = np.maximum(a[:, :, 3], edge[:, :, 3])
Image.fromarray(outp.clip(0, 255).astype(np.uint8), "RGBA").save(out)
print(f"icon {SIZE}px -> {out}")
