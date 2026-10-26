# Turns a .glb (e.g. from Meshy) into a PotatoFarmer piece's assets: <name>.mesh and three textures in the layout
# Valheim's piece shader (Custom/Piece) expects.
#   python3 -I convert.py model.glb assets_dir name [length_m] [pixels_per_m] [metal=H,S,V] [wood=H,S,V] [px=N]
# metal= / wood= recolor the metal parts (from the metal map) or the wood (everything else except bright straw-yellow):
# H = hue in degrees, S = saturation (0-1), V = brightness multiplier. Black metal: metal=0,0.03,0.55
# The Potato look: textures sized to about 50 pixels per metre of surface (Valheim's forge is ~50, the reinforced
# chest ~35), rounded to a power of two, and drawn in game with sharp (point) filtering like the game's own pieces.
import io, json, struct, sys
import numpy as np
from PIL import Image
from scipy import ndimage

src, out, name = sys.argv[1], sys.argv[2], sys.argv[3]
plain = [a for a in sys.argv[4:] if "=" not in a]
options = dict(a.split("=", 1) for a in sys.argv[4:] if "=" in a)
length = float(plain[0]) if len(plain) > 0 else 2.0
density = float(plain[1]) if len(plain) > 1 else 50.0

data = open(src, "rb").read()
jlen = struct.unpack("<I", data[12:16])[0]
gltf = json.loads(data[20:20 + jlen])
blob = data[20 + jlen + 8:]

def accessor(i):
    a = gltf["accessors"][i]; v = gltf["bufferViews"][a["bufferView"]]
    comps = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[a["type"]]
    dtype = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}[a["componentType"]]
    start = v.get("byteOffset", 0) + a.get("byteOffset", 0)
    stride = v.get("byteStride", 0)
    count = a["count"]
    if stride and stride != comps * np.dtype(dtype).itemsize:
        raw = np.frombuffer(blob, np.uint8, count * stride, start).reshape(count, stride)
        arr = raw[:, :comps * np.dtype(dtype).itemsize].copy().view(dtype)
    else:
        arr = np.frombuffer(blob, dtype, count * comps, start).copy()
    return arr.reshape(count, comps) if comps > 1 else arr

Image.MAX_IMAGE_PIXELS = None
MAX_SOURCE = int(options.get("maxsrc", 2048))  # big Meshy textures (8192) are shrunk first, or the island pass runs out of memory

def image(i):
    v = gltf["bufferViews"][gltf["images"][i]["bufferView"]]
    img = Image.open(io.BytesIO(blob[v.get("byteOffset", 0):v.get("byteOffset", 0) + v["byteLength"]]))
    if img.size[0] > MAX_SOURCE:
        img = img.reduce(img.size[0] // MAX_SOURCE)
    return img

if len(gltf["meshes"]) != 1 or len(gltf["meshes"][0]["primitives"]) != 1:
    sys.exit("expected one mesh with one primitive")
prim = gltf["meshes"][0]["primitives"][0]
pos = accessor(prim["attributes"]["POSITION"]).astype(np.float64)
nrm = accessor(prim["attributes"]["NORMAL"]).astype(np.float64)
uv = accessor(prim["attributes"]["TEXCOORD_0"]).astype(np.float64)
idx = accessor(prim["indices"]).astype(np.int64).reshape(-1, 3)

uv_src, idx_src = uv.copy(), idx.copy()
# glTF is right-handed, Unity left-handed: mirror X and reverse each triangle's winding. glTF UVs start at the top.
pos[:, 0] *= -1; nrm[:, 0] *= -1
idx = idx[:, ::-1]
uv[:, 1] = 1 - uv[:, 1]
# Longest side along X, scaled to the requested length, centred, standing on y = 0.
lo, hi = pos.min(0), pos.max(0)
if (hi - lo)[2] > (hi - lo)[0]:
    pos = pos[:, [2, 1, 0]]; nrm = nrm[:, [2, 1, 0]]; idx = idx[:, ::-1]
    lo, hi = pos.min(0), pos.max(0)
scale = length / (hi - lo)[0]
pos = (pos - [(lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2]) * scale
nrm /= np.linalg.norm(nrm, axis=1, keepdims=True).clip(1e-9)

with open(f"{out}/{name}.mesh", "wb") as f:
    f.write(b"PTM1"); f.write(struct.pack("<ii", len(pos), idx.size))
    f.write(pos.astype("<f4").tobytes()); f.write(nrm.astype("<f4").tobytes())
    f.write(uv.astype("<f4").tobytes()); f.write(idx.astype("<i4").tobytes())

# Texture size from the pixels-per-metre target.
a, b, c = pos[idx[:, 0]], pos[idx[:, 1]], pos[idx[:, 2]]
surface = (np.linalg.norm(np.cross(b - a, c - a), axis=1) / 2).sum()
u0, u1, u2 = uv_src[idx_src[:, 0]], uv_src[idx_src[:, 1]], uv_src[idx_src[:, 2]]
uv_area = (np.abs((u1[:, 0] - u0[:, 0]) * (u2[:, 1] - u0[:, 1]) - (u2[:, 0] - u0[:, 0]) * (u1[:, 1] - u0[:, 1])) / 2).sum()
want = density * np.sqrt(surface / uv_area)
px = int(2 ** max(5, min(11, round(np.log2(want)))))
# px=N picks an exact texture size instead (the source texture's size must be a whole multiple of it).
if "px" in options: px = int(options["px"])

mat = gltf["materials"][prim["material"]]
pbr = mat["pbrMetallicRoughness"]
color_img = image(gltf["textures"][pbr["baseColorTexture"]["index"]]["source"]).convert("RGB")
size = color_img.size[0]

def recolor(img, metal_map):
    # Optional per-piece color matching (see the usage line at the top).
    if "metal" not in options and "wood" not in options: return img
    rgb = np.asarray(img).astype(np.float64) / 255
    mx = rgb.max(2); mn = rgb.min(2); v = mx; s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-9), 0)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    d = np.maximum(mx - mn, 1e-9)
    h = np.select([mx == r, mx == g], [((g - b) / d) % 6, (b - r) / d + 2], (r - g) / d + 4) / 6
    metal = np.asarray(metal_map.convert("RGB").resize(img.size)).astype(np.float64)[..., 2] / 255  # glTF: metal in blue
    straw = (h * 360 > 30) & (h * 360 < 60) & (s > 0.45) & (v > 0.45)
    def apply(spec, weight):
        nonlocal h, s, v
        th, ts, tv = (float(x) for x in spec.split(","))
        h = np.where(weight > 0, h * (1 - weight) + (th / 360) * weight, h)
        s = s * (1 - weight) + ts * weight
        v = np.clip(v * (1 - weight + tv * weight), 0, 1)
    if "metal" in options: apply(options["metal"], np.clip((metal - 0.3) / 0.4, 0, 1))
    if "wood" in options: apply(options["wood"], np.where(straw, 0.0, 1 - np.clip((metal - 0.3) / 0.4, 0, 1)))
    i = np.floor(h * 6) % 6; f = h * 6 - np.floor(h * 6)
    p_, q_, t_ = v * (1 - s), v * (1 - f * s), v * (1 - (1 - f) * s)
    rgb_out = np.zeros_like(rgb)
    for k, (a_, b_, c_) in enumerate([(v, t_, p_), (q_, v, p_), (p_, v, t_), (p_, q_, v), (t_, p_, v), (v, p_, q_)]):
        sel = i == k
        rgb_out[sel] = np.stack([a_[sel], b_[sel], c_[sel]], -1)
    return Image.fromarray((rgb_out * 255).round().clip(0, 255).astype(np.uint8), "RGB")

color_img = recolor(color_img, image(gltf["textures"][pbr["metallicRoughnessTexture"]["index"]]["source"]))
if px >= size: px = size

# Which UV island each source pixel belongs to. Meshy packs many small islands tightly, so a plain shrink mixes
# neighbouring islands into blotches; here every new pixel takes its colour only from the island that covers most of it.
parent = np.arange(len(uv_src))
def find(x):
    while parent[x] != x:
        parent[x] = parent[parent[x]]; x = parent[x]
    return x
for t in idx_src:
    r0 = find(t[0])
    for v in t[1:]:
        r = find(v)
        if r != r0: parent[r] = r0
island_of_vertex = np.array([find(v) for v in range(len(uv_src))])
island = np.full((size, size), -1, np.int64)
ys, xs = np.mgrid[0:size, 0:size]
for t in idx_src:
    p = uv_src[t] * size  # glTF UVs: x across, y down the image
    lo = np.floor(p.min(0)).astype(int).clip(0, size - 1); hi = np.ceil(p.max(0)).astype(int).clip(0, size - 1)
    sx = xs[lo[1]:hi[1] + 1, lo[0]:hi[0] + 1] + 0.5; sy = ys[lo[1]:hi[1] + 1, lo[0]:hi[0] + 1] + 0.5
    (x0, y0), (x1, y1), (x2, y2) = p
    den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
    if abs(den) < 1e-12: continue
    w0 = ((y1 - y2) * (sx - x2) + (x2 - x1) * (sy - y2)) / den
    w1 = ((y2 - y0) * (sx - x2) + (x0 - x2) * (sy - y2)) / den
    inside = (w0 >= -0.01) & (w1 >= -0.01) & (1 - w0 - w1 >= -0.01)
    island[lo[1]:hi[1] + 1, lo[0]:hi[0] + 1][inside] = island_of_vertex[t[0]]
# Pixels outside every island take the nearest island's id.
empty = island < 0
if empty.any():
    near = ndimage.distance_transform_edt(empty, return_distances=False, return_indices=True)
    island = island[near[0], near[1]]

step = size // px
blocks = island.reshape(px, step, px, step).transpose(0, 2, 1, 3).reshape(px, px, step * step)
def majority(row):
    values, counts = np.unique(row, return_counts=True)
    return values[counts.argmax()]
owner = np.apply_along_axis(majority, 2, blocks)
keep = blocks == owner[:, :, None]

def shrink(img):
    a = np.asarray(img).astype(np.float64)
    ch = a.shape[2]
    blk = a.reshape(px, step, px, step, ch).transpose(0, 2, 1, 3, 4).reshape(px, px, step * step, ch)
    return (blk * keep[:, :, :, None]).sum(2) / keep.sum(2)[:, :, None]

Image.fromarray(shrink(color_img).round().clip(0, 255).astype(np.uint8), "RGB").save(f"{out}/{name}_d.png")
# Custom/Piece metal map: metal in RGB, smoothness in alpha. glTF: roughness in G, metal in B.
mr = shrink(image(gltf["textures"][pbr["metallicRoughnessTexture"]["index"]]["source"]).convert("RGB").resize((size, size)))
metal = mr[:, :, 2]; smooth = 255 - mr[:, :, 1]
Image.fromarray(np.dstack([metal, metal, metal, smooth]).round().clip(0, 255).astype(np.uint8), "RGBA").save(f"{out}/{name}_m.png")
# Normal map: averaged as directions, then X in alpha with red at full, so it reads right whether the shader unpacks RG
# or AG. X stays as painted: the tangents are rebuilt from the mirrored mesh, so the map still lines up.
n = shrink(image(gltf["textures"][mat["normalTexture"]["index"]]["source"]).convert("RGB").resize((size, size))) / 127.5 - 1
n /= np.linalg.norm(n, axis=2, keepdims=True).clip(1e-6)
n = (n + 1) * 127.5
Image.fromarray(np.dstack([np.full(n.shape[:2], 255.0), n[:, :, 1], n[:, :, 2], n[:, :, 0]]).round().clip(0, 255).astype(np.uint8), "RGBA").save(f"{out}/{name}_n.png")

# emit=mask.png (same layout as the source textures, white = glow): an emission map <name>_e.png shrunk the same
# island-aware way; gain=G brightens thin lines that a shrink would average away (default 3).
if "emit" in options:
    e = Image.open(options["emit"]).convert("L").resize((size, size), Image.NEAREST)
    ea = shrink(Image.fromarray(np.dstack([np.asarray(e)] * 3)))[:, :, 0] / 255 * float(options.get("gain", 3))
    ea = (ea.clip(0, 1) * 255).round().astype(np.uint8)
    Image.fromarray(np.dstack([ea, ea, ea]), "RGB").save(f"{out}/{name}_e.png")

print(f"{name}: verts {len(pos)}, triangles {len(idx)}, size {np.round(pos.max(0) - pos.min(0), 3)} m, "
      f"textures {px}px ({px * np.sqrt(uv_area / surface):.0f} px/m)")
