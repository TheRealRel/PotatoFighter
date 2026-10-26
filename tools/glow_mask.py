# Glow mask for a weapon's grooves (PotatoFighter), in the source textures' layout, for relayout.py's glow=.
#   python3 -I glow_mask.py model.glb out_mask.png [tilt=0.35] [grow=3]
# Grooves are where the normal map tilts sharply on metal: keep only long, connected lines (curl outlines), not the
# small dents of a hammered finish, kept off the metal's edges, then widened a little so they survive the shrink.
import io, json, struct, sys
import numpy as np
from PIL import Image
from scipy import ndimage
Image.MAX_IMAGE_PIXELS = None
src, out = sys.argv[1:3]; opt = dict(a.split("=", 1) for a in sys.argv[3:])
data = open(src, "rb").read(); jlen = struct.unpack("<I", data[12:16])[0]
gltf = json.loads(data[20:20 + jlen]); blob = data[20 + jlen + 8:]
def image(i):
    v = gltf["bufferViews"][gltf["images"][i]["bufferView"]]
    img = Image.open(io.BytesIO(blob[v.get("byteOffset", 0):v.get("byteOffset", 0) + v["byteLength"]]))
    return img.reduce(img.size[0] // 2048) if img.size[0] > 2048 else img
mat = gltf["materials"][0]; pbr = mat["pbrMetallicRoughness"]
n = np.asarray(image(gltf["textures"][mat["normalTexture"]["index"]]["source"]).convert("RGB"), float) / 127.5 - 1
mr = np.asarray(image(gltf["textures"][pbr["metallicRoughnessTexture"]["index"]]["source"]).convert("RGB"), float)
tilt = np.sqrt(n[..., 0] ** 2 + n[..., 1] ** 2)
metal = ndimage.binary_erosion(mr[..., 2] / 255 > 0.6, iterations=4)
m = (tilt > float(opt.get("tilt", 0.35))) & metal
lab, k = ndimage.label(m, structure=np.ones((3, 3)))
sizes = ndimage.sum(m, lab, range(1, k + 1)); keep = np.zeros(k + 1, bool)
for i, (sl, sz) in enumerate(zip(ndimage.find_objects(lab), sizes), 1):
    if sz >= 250 and max(sl[0].stop - sl[0].start, sl[1].stop - sl[1].start) >= 60: keep[i] = True
m = ndimage.binary_dilation(keep[lab], iterations=int(opt.get("grow", 3))) & metal
Image.fromarray((m * 255).astype(np.uint8)).save(out)
print(f"glow mask: {keep.sum()} lines kept of {k}, {m.mean():.1%} of the texture")
