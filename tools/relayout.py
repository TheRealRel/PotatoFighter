# New UV layout for a small texture, baked from a model's full-size maps (PotatoFighter's weapons).
# Meshy packs hundreds of thin UV islands tightly. That's fine at 8192 px, but at a weapon's 128 px some islands are
# thinner than a texel and read their neighbours' colours (steel slivers on a wooden haft). This lays out fresh UVs with
# xatlas, sized and padded for the target texture, and bakes every map onto them from the source at full resolution.
#   python3 -I relayout.py model.glb converted.mesh out_dir name [color_px=128] [glow_px=512] [glow=mask.png]
#       [glowtris=head]   (head: glow only on triangles above the lowest point sticking out past the haft)
# converted.mesh is convert.py's output for the same .glb (Unity space, same vertices): its positions and normals are
# kept, its UVs (the old ones) say where to read the source maps.
import io, json, struct, sys
import numpy as np
import xatlas
from PIL import Image
from scipy import ndimage

Image.MAX_IMAGE_PIXELS = None
src, mesh_in, out, name = sys.argv[1:5]
opt = dict(a.split("=", 1) for a in sys.argv[5:])
CPX = int(opt.get("color_px", 128)); GPX = int(opt.get("glow_px", 512))

# --- source maps from the .glb (full size)
data = open(src, "rb").read()
jlen = struct.unpack("<I", data[12:16])[0]
gltf = json.loads(data[20:20 + jlen]); blob = data[20 + jlen + 8:]
def image(i):
    v = gltf["bufferViews"][gltf["images"][i]["bufferView"]]
    return Image.open(io.BytesIO(blob[v.get("byteOffset", 0):v.get("byteOffset", 0) + v["byteLength"]]))
mat = gltf["materials"][gltf["meshes"][0]["primitives"][0]["material"]]; pbr = mat["pbrMetallicRoughness"]
tex = lambda key: image(gltf["textures"][key["index"]]["source"])
_c = tex(pbr["baseColorTexture"]).convert("RGB")
if _c.size[0] > 4096: _c = _c.reduce(_c.size[0] // 4096)  # memory: 4096 is plenty to bake 128-512 px from
src_color = np.asarray(_c, np.float32)
src_mr = np.asarray(tex(pbr["metallicRoughnessTexture"]).convert("RGB"), np.float32)
src_n = np.asarray(tex(mat["normalTexture"]).convert("RGB"), np.float32) / 127.5 - 1
src_glow = np.asarray(Image.open(opt["glow"]).convert("L"), np.float32) / 255 if "glow" in opt else None

# --- the converted mesh
b = open(mesh_in, "rb").read(); assert b[:4] == b"PTM1"
nv, ni = struct.unpack_from("<ii", b, 4); o = 12
P = np.frombuffer(b, "<f4", nv * 3, o).reshape(nv, 3).astype(np.float64); o += nv * 12
N = np.frombuffer(b, "<f4", nv * 3, o).reshape(nv, 3).astype(np.float64); o += nv * 12
U = np.frombuffer(b, "<f4", nv * 2, o).reshape(nv, 2).astype(np.float64); o += nv * 8
I = np.frombuffer(b, "<i4", ni, o).reshape(-1, 3).astype(np.int64)

# --- new layout. Meshy's mesh is split at every old UV seam, so weld corners by position for xatlas (bigger islands),
# then rebuild one output vertex per (new island corner, original vertex): each keeps its own normal (the model's hard
# facets) and its old UV (where the bake reads).
key = np.round(P / 1e-5).astype(np.int64)
_, weld, inv = np.unique(key, axis=0, return_index=True, return_inverse=True); inv = inv.reshape(-1)
Nw = np.zeros((len(weld), 3)); np.add.at(Nw, inv, N); Nw /= np.linalg.norm(Nw, axis=1, keepdims=True).clip(1e-9)
Iw = inv[I]
area = (np.linalg.norm(np.cross(P[I[:, 1]] - P[I[:, 0]], P[I[:, 2]] - P[I[:, 0]]), axis=1) / 2).sum()
ppm = float(opt.get("px_per_m", 165))  # 165: the look of Meshy's own layout at 128 px, approved in the previews
co = xatlas.ChartOptions(); co.max_cost = 8
po = xatlas.PackOptions(); po.padding = 2; po.bilinear = True; po.resolution = CPX; po.texels_per_unit = ppm
atlas = xatlas.Atlas(); atlas.add_mesh(P[weld].astype(np.float32), Iw.astype(np.uint32), Nw.astype(np.float32))
atlas.generate(co, po)
if atlas.atlas_count != 1: sys.exit(f"{ppm} px/m doesn't fit on one {CPX}px page; use a bigger color_px or fewer px_per_m")
vmap, In, UVn = atlas[0]
vmap = vmap.astype(np.int64); In = In.astype(np.int64)
assert len(In) == len(I) and (vmap[In] == Iw).all(), "xatlas changed the triangle or corner order"
pairs = np.stack([In.reshape(-1), I.reshape(-1)], 1)
uniq, corner_vertex = np.unique(pairs, axis=0, return_inverse=True)
I2 = corner_vertex.reshape(-1, 3)
P2, N2, Uold = P[uniq[:, 1]], N[uniq[:, 1]], U[uniq[:, 1]]
Unew = UVn[uniq[:, 0]].astype(np.float64); Unew[:, 1] = 1 - Unew[:, 1]  # xatlas: v down the image; Unity: v up
print(f"layout: {atlas.chart_count} islands at {ppm:.0f} px/m on {CPX}x{CPX}")

# --- tangent frames: old (to read the source normal map) and new (to write ours), per triangle
def frames(Pv, Uv, Nv, tris):
    p0, p1, p2 = Pv[tris[:, 0]], Pv[tris[:, 1]], Pv[tris[:, 2]]
    u0, u1, u2 = Uv[tris[:, 0]], Uv[tris[:, 1]], Uv[tris[:, 2]]
    e1, e2 = p1 - p0, p2 - p0; d1, d2 = u1 - u0, u2 - u0
    r = d1[:, 0] * d2[:, 1] - d2[:, 0] * d1[:, 1]; r = np.where(np.abs(r) < 1e-12, 1e-12, r)
    T = (e1 * d2[:, 1:2] - e2 * d1[:, 1:2]) / r[:, None]
    B = (e2 * d1[:, 0:1] - e1 * d2[:, 0:1]) / r[:, None]
    return T, B
T_old, B_old = frames(P2, Uold, N2, I2)
T_new, B_new = frames(P2, Unew, N2, I2)

# head triangles (for glow): above the lowest point that sticks out sideways past the haft
x, y = P2[:, 0], P2[:, 1]
head_bottom = y[np.abs(x) > 0.07].min()
head = P2[I2].mean(1)[:, 1] >= head_bottom - 0.005

def sample(img, uv):
    h, w = img.shape[:2]
    r = np.clip(((1 - uv[:, 1]) % 1) * h, 0, h - 1).astype(int); c = np.clip((uv[:, 0] % 1) * w, 0, w - 1).astype(int)
    return img[r, c]

def bake(S, kind, sub=6):
    acc = np.zeros((S, S, 4 if kind != "glow" else 1)); cnt = np.zeros((S, S))
    g = (np.arange(sub) + 0.5) / sub
    for ti, t in enumerate(I2):
        if kind == "glow" and not head[ti]: continue
        q = np.stack([Unew[t, 0] * S, (1 - Unew[t, 1]) * S], 1)
        lo = np.floor(q.min(0)).astype(int).clip(0, S - 1); hi = np.ceil(q.max(0)).astype(int).clip(0, S - 1)
        ys, xs = np.mgrid[lo[1]:hi[1] + 1, lo[0]:hi[0] + 1]
        z = np.zeros((1, 1, sub, sub))
        sx = (xs[..., None, None] + g[None, None, :, None] + z).reshape(-1); sy = (ys[..., None, None] + g[None, None, None, :] + z).reshape(-1)
        cx = np.repeat(xs.reshape(-1), sub * sub); cy = np.repeat(ys.reshape(-1), sub * sub)
        (x0, y0), (x1, y1), (x2, y2) = q
        den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
        if abs(den) < 1e-12: continue
        w0 = ((y1 - y2) * (sx - x2) + (x2 - x1) * (sy - y2)) / den
        w1 = ((y2 - y0) * (sx - x2) + (x0 - x2) * (sy - y2)) / den
        w2 = 1 - w0 - w1
        ins = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
        if not ins.any(): continue
        W = np.stack([w0[ins], w1[ins], w2[ins]], 1)
        uv_old = W @ Uold[t]
        if kind == "color": val = np.c_[sample(src_color, uv_old), np.zeros(len(uv_old))]
        elif kind == "mr":
            mr = sample(src_mr, uv_old); val = np.c_[mr[:, 2], mr[:, 2], mr[:, 2], 255 - mr[:, 1]]
        elif kind == "glow": val = sample(src_glow, uv_old)[:, None] * 255
        else:  # normal: old tangent space -> object -> new tangent space
            n = sample(src_n, uv_old)
            Nn = W @ N2[t]; Nn /= np.linalg.norm(Nn, axis=1, keepdims=True)
            def ortho(Tv, Bv):
                Tv = Tv[None] - (Nn @ Tv)[:, None] * Nn; Tv /= np.linalg.norm(Tv, axis=1, keepdims=True).clip(1e-9)
                Bs = np.sign(np.einsum("ij,j->i", np.cross(Nn, Tv), Bv)); Bs[Bs == 0] = 1
                return Tv, np.cross(Nn, Tv) * Bs[:, None]
            To, Bo = ortho(T_old[ti], B_old[ti]); Tn, Bn = ortho(T_new[ti], B_new[ti])
            obj = n[:, 0:1] * To + n[:, 1:2] * Bo + n[:, 2:3] * Nn
            obj /= np.linalg.norm(obj, axis=1, keepdims=True).clip(1e-9)
            val = np.c_[(obj * Tn).sum(1), (obj * Bn).sum(1), (obj * Nn).sum(1), np.zeros(len(obj))]
        np.add.at(acc, (cy[ins], cx[ins]), val); np.add.at(cnt, (cy[ins], cx[ins]), 1)
    filled = cnt > 0
    acc[filled] /= cnt[filled][:, None]
    # padding: spread each island's edge colour outward, so mipmaps never mix islands
    idx = ndimage.distance_transform_edt(~filled, return_distances=False, return_indices=True)
    if kind != "glow": acc = acc[idx[0], idx[1]]
    return acc, filled

col, filled = bake(CPX, "color")
Image.fromarray(col[..., :3].round().clip(0, 255).astype(np.uint8), "RGB").save(f"{out}/{name}_d.png")
mr, _ = bake(CPX, "mr")
Image.fromarray(mr.round().clip(0, 255).astype(np.uint8), "RGBA").save(f"{out}/{name}_m.png")
nm, _ = bake(CPX, "normal")
n3 = nm[..., :3]; n3 /= np.linalg.norm(n3, axis=2, keepdims=True).clip(1e-6); n3 = (n3 + 1) * 127.5
# Same packing as convert.py: X in alpha with red at full, so it reads right whether the shader unpacks RG or AG.
Image.fromarray(np.dstack([np.full(n3.shape[:2], 255.0), n3[..., 1], n3[..., 2], n3[..., 0]]).round().clip(0, 255).astype(np.uint8), "RGBA").save(f"{out}/{name}_n.png")
if src_glow is not None:
    gl, _ = bake(GPX, "glow")
    g8 = gl[..., 0].round().clip(0, 255).astype(np.uint8)
    Image.fromarray(np.dstack([g8, g8, g8]), "RGB").save(f"{out}/{name}_e.png")

with open(f"{out}/{name}.mesh", "wb") as f:
    f.write(b"PTM1"); f.write(struct.pack("<ii", len(P2), I2.size))
    f.write(P2.astype("<f4").tobytes()); f.write(N2.astype("<f4").tobytes())
    f.write(Unew.astype("<f4").tobytes()); f.write(I2.astype("<i4").tobytes())
print(f"{name}: verts {len(P2)} (was {len(P)}), triangles {len(I2)}, texels used {filled.mean():.0%} of {CPX}x{CPX}, glow {GPX}px")
