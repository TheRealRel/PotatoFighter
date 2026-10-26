# Potato Tweaks page icons (Valheim): small 3D models built in code, textured, lit and rendered at 80 px with a dark
# outline, the same look as PotatoFarmer's piece icons (which were rendered from the pieces' models).
#   python3 -I tools/render_icons.py docs/images [name ...]
import sys, os, math
import numpy as np
import trimesh
from PIL import Image

R, OUT_PX, UP = 320, 40, 2    # render size, pixel-art size, blow-up (40 px art shown at 80 px, like PotatoFarmer's)
OUTLINE = np.array([38, 24, 14]) / 255.0
COVER = 0.45   # share of a pixel the model must cover to be drawn (lower keeps thin diagonal parts unbroken)

# ---------- noise and materials ----------

def _hash(ix, iy, iz, seed):
    h = np.sin(ix * 127.1 + iy * 311.7 + iz * 74.7 + seed * 19.19) * 43758.5453
    return h - np.floor(h)

def vnoise(p, seed=0):
    i = np.floor(p); f = p - i
    u = f * f * (3 - 2 * f)
    x0, y0, z0 = i[:, 0], i[:, 1], i[:, 2]
    def c(dx, dy, dz): return _hash(x0 + dx, y0 + dy, z0 + dz, seed)
    x00 = c(0, 0, 0) * (1 - u[:, 0]) + c(1, 0, 0) * u[:, 0]
    x10 = c(0, 1, 0) * (1 - u[:, 0]) + c(1, 1, 0) * u[:, 0]
    x01 = c(0, 0, 1) * (1 - u[:, 0]) + c(1, 0, 1) * u[:, 0]
    x11 = c(0, 1, 1) * (1 - u[:, 0]) + c(1, 1, 1) * u[:, 0]
    y0_ = x00 * (1 - u[:, 1]) + x10 * u[:, 1]
    y1_ = x01 * (1 - u[:, 1]) + x11 * u[:, 1]
    return y0_ * (1 - u[:, 2]) + y1_ * u[:, 2]

def fbm(p, seed=0, octaves=3):
    total, amp, norm = 0, 1, 0
    for o in range(octaves):
        total = total + vnoise(p * (2 ** o), seed + o) * amp
        norm += amp; amp *= 0.5
    return total / norm

def rgb(*c): return np.array(c, float) / 255.0

def flat(color, rough=0.08, scale=6, seed=1):
    def m(p, n):
        k = 1 + (fbm(p * scale, seed) - 0.5) * 2 * rough
        return np.clip(color[None, :] * k[:, None], 0, 1)
    return m

def wood(color, axis=0, planks=None, plank_axis=1, plank_gap=0.035, seed=3, grain=1.0):
    # Grain runs along `axis`; planks (optional) are bands across `plank_axis` with dark gaps between them.
    def m(p, n):
        q = p.copy(); q[:, axis] *= 0.15
        g = fbm(q * 9, seed)
        streak = 0.5 + 0.5 * np.sin((p[:, (axis + 1) % 3] + p[:, (axis + 2) % 3]) * 60 + g * 8)
        k = 0.82 + 0.28 * g + 0.08 * streak * grain
        col = color[None, :] * k[:, None]
        if planks:
            t = p[:, plank_axis] / planks
            frac = t - np.floor(t)
            gap = (frac < plank_gap / planks) | (frac > 1 - plank_gap / planks)
            shade = 0.88 + 0.12 * _hash(np.floor(t), 0, 0, seed)
            col = col * shade[:, None]
            col[gap] *= 0.45
        return np.clip(col, 0, 1)
    return m

def metal(color, seed=5):
    def m(p, n):
        k = 0.85 + 0.3 * fbm(p * 14, seed)
        return np.clip(color[None, :] * k[:, None], 0, 1)
    return m

GLOW = set()   # materials that ignore lighting

def glow(color):
    def m(p, n):
        k = 0.9 + 0.2 * fbm(p * 10, 9)
        return np.clip(color[None, :] * k[:, None], 0, 1)
    GLOW.add(m)
    return m

WOOD = rgb(150, 98, 55); WOOD_D = rgb(105, 66, 36); WOOD_L = rgb(185, 128, 75)
IRON = rgb(105, 105, 112); IRON_D = rgb(70, 70, 78); BRONZE = rgb(176, 120, 60)
STONE = rgb(120, 118, 122); GOLD = rgb(230, 175, 60); LEATHER = rgb(140, 92, 52)

def from_glb(path):
    # A Meshy model: its own texture, sampled at each pixel's UV.
    scene = trimesh.load(path, force="scene")
    parts = []
    for geom in scene.dump():
        tex = getattr(getattr(geom.visual, "material", None), "baseColorTexture", None)
        uv = getattr(geom.visual, "uv", None)
        if tex is None or uv is None:
            col = np.array(getattr(getattr(geom.visual, "material", None), "baseColorFactor", [200, 200, 200, 255])[:3]) / 255.0
            parts.append((geom, flat(col, 0.05))); continue
        img = np.asarray(tex.convert("RGB"), float) / 255.0
        h, w = img.shape[:2]
        fuv = uv[geom.faces]                      # (faces, 3, 2)
        def m(p, n, t, b, img=img, fuv=fuv, h=h, w=w):
            st = (fuv[t] * b[..., None]).sum(1)
            x = np.clip((st[:, 0] % 1) * (w - 1), 0, w - 1).astype(int)
            y = np.clip((1 - st[:, 1] % 1) * (h - 1), 0, h - 1).astype(int)
            return img[y, x]
        m.textured = True
        parts.append((geom, m))
    return parts

# ---------- shapes ----------

def box(size, at=(0, 0, 0)):
    m = trimesh.creation.box(extents=size); m.apply_translation(at); return m

def cyl(r, h, at=(0, 0, 0), axis="y", sections=28, r2=None):
    m = trimesh.creation.cylinder(radius=r, height=h, sections=sections)   # along z
    if r2 is not None:   # a tapered cylinder
        v = m.vertices.copy(); t = (v[:, 2] + h / 2) / h
        s = (r + (r2 - r) * t) / r; v[:, 0] *= s; v[:, 1] *= s; m.vertices = v
    rot = {"z": np.eye(4), "y": trimesh.transformations.rotation_matrix(-math.pi / 2, [1, 0, 0]),
           "x": trimesh.transformations.rotation_matrix(math.pi / 2, [0, 1, 0])}[axis]
    m.apply_transform(rot); m.apply_translation(at); return m

def sphere(r, at=(0, 0, 0), scale=(1, 1, 1), sub=2):
    m = trimesh.creation.icosphere(subdivisions=sub, radius=r)
    m.apply_scale(scale); m.apply_translation(at); return m

def torus(R_, r, at=(0, 0, 0), axis="y"):
    m = trimesh.creation.torus(major_radius=R_, minor_radius=r, major_sections=32, minor_sections=10)   # around z
    rot = {"z": np.eye(4), "y": trimesh.transformations.rotation_matrix(-math.pi / 2, [1, 0, 0]),
           "x": trimesh.transformations.rotation_matrix(math.pi / 2, [0, 1, 0])}[axis]
    m.apply_transform(rot); m.apply_translation(at); return m

def prism(points2d, depth, at=(0, 0, 0)):
    # A 2D outline (x, y) pushed out along z.
    from shapely.geometry import Polygon
    m = trimesh.creation.extrude_polygon(Polygon(points2d), depth)
    m.apply_translation((0, 0, -depth / 2)); m.apply_translation(at); return m

def rot(m, axis, deg, about=(0, 0, 0)):
    m.apply_transform(trimesh.transformations.rotation_matrix(math.radians(deg), axis, about)); return m

# ---------- renderer ----------

def ambient_occlusion(depth, rad, strength=0.75):
    # Screen-space ambient occlusion: a point is darkened by how many nearby points (in a ring around it on screen)
    # sit in front of it, within a little depth. Crevices, overhangs and the edges where parts meet get the soft
    # shadow the rendered Meshy icons have (the chest's bands, the anvil on its stump); open faces stay lit.
    solid = np.isfinite(depth)
    d = np.where(solid, depth, np.inf)
    occ = np.zeros_like(depth); n = 0
    for radius in (R * 0.02, R * 0.045, R * 0.08):
        for a in np.linspace(0, 2 * np.pi, 12, endpoint=False):
            dx, dy = int(round(np.cos(a) * radius)), int(round(np.sin(a) * radius))
            sh = np.full_like(d, np.inf)
            H, W = d.shape
            sh[max(-dy, 0):H + min(-dy, 0), max(-dx, 0):W + min(-dx, 0)] = d[max(dy, 0):H + min(dy, 0), max(dx, 0):W + min(dx, 0)]
            with np.errstate(invalid="ignore"):
                diff = d - sh                               # > 0: the neighbour is in front
                occ += np.where(np.isfinite(diff), np.clip(diff / (rad * 0.04), 0, 1) * (diff < rad * 0.5), 0)
            n += 1
    occ = np.where(solid, occ / n, 0)
    return 1 - strength * np.clip(occ * 1.6, 0, 1)

def render(parts, yaw=35, pitch=25, fov=28, light=(-0.5, 0.8, 0.6), shadows=True):
    tris, mats, mids, starts = [], [], [], []
    for i, (mesh, mat) in enumerate(parts):
        starts.append(sum(len(t) for t in tris))
        tris.append(mesh.triangles); mids.append(np.full(len(mesh.faces), i)); mats.append(mat)
    T = np.concatenate(tris); M = np.concatenate(mids)
    centre = (T.reshape(-1, 3).max(0) + T.reshape(-1, 3).min(0)) / 2
    rad = np.linalg.norm(T.reshape(-1, 3) - centre, axis=1).max()
    y, p = math.radians(yaw), math.radians(pitch)
    fwd = -np.array([math.sin(y) * math.cos(p), math.sin(p), math.cos(y) * math.cos(p)])
    dist = rad / math.sin(math.radians(fov / 2)) * 1.02
    eye = centre - fwd * dist
    right = np.cross(fwd, [0, 1, 0]); right /= np.linalg.norm(right); up = np.cross(right, fwd)
    rel = T - eye
    cx, cy, cz = rel @ right, rel @ up, rel @ fwd
    # Fill the frame: scale and centre the picture by where the model actually lands on screen.
    u, v = cx / cz, cy / cz
    f = R * 0.94 / max(u.max() - u.min(), v.max() - v.min())
    mu, mv = (u.max() + u.min()) / 2, (v.max() + v.min()) / 2
    sx = R / 2 + (u - mu) * f; sy = R / 2 - (v - mv) * f

    depth = np.full((R, R), np.inf); tid = np.full((R, R), -1); bary = np.zeros((R, R, 3))
    for t in range(len(T)):
        xs, ys, zs = sx[t], sy[t], cz[t]
        x0, x1 = max(int(xs.min()), 0), min(int(xs.max()) + 1, R - 1)
        y0, y1 = max(int(ys.min()), 0), min(int(ys.max()) + 1, R - 1)
        if x0 > x1 or y0 > y1: continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        d = (ys[1] - ys[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (ys[0] - ys[2])
        if abs(d) < 1e-9: continue
        w0 = ((ys[1] - ys[2]) * (gx - xs[2]) + (xs[2] - xs[1]) * (gy - ys[2])) / d
        w1 = ((ys[2] - ys[0]) * (gx - xs[2]) + (xs[0] - xs[2]) * (gy - ys[2])) / d
        w2 = 1 - w0 - w1
        inside = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
        if not inside.any(): continue
        iz = w0 / zs[0] + w1 / zs[1] + w2 / zs[2]
        z = 1 / iz
        sub = depth[y0:y1 + 1, x0:x1 + 1]
        win = inside & (z < sub)
        sub[win] = z[win]
        tid[y0:y1 + 1, x0:x1 + 1][win] = t
        b = np.stack([w0 / zs[0], w1 / zs[1], w2 / zs[2]], -1) * z[..., None]
        bary[y0:y1 + 1, x0:x1 + 1][win] = b[win]

    img = np.zeros((R, R, 3)); alpha = (tid >= 0).astype(float)
    L = np.array(light, float); L /= np.linalg.norm(L)
    nrm = np.cross(T[:, 1] - T[:, 0], T[:, 2] - T[:, 0]); nrm /= np.linalg.norm(nrm, axis=1)[:, None] + 1e-12
    # Normals face the camera.
    facing = (nrm * (T.mean(1) - eye)).sum(1) > 0; nrm[facing] *= -1
    # Cast shadows: a ray from each visible point towards the light; anything in the way leaves it in shadow (only the
    # ambient part of the light), so an arrow shades the bow behind it and a part shades what sits under it.
    lit = np.ones((R, R))
    if shadows:
        import trimesh
        whole = trimesh.Trimesh(vertices=T.reshape(-1, 3), faces=np.arange(len(T) * 3).reshape(-1, 3), process=False)
        hit_mask = tid >= 0
        tt_all = tid[hit_mask]; P_all = (T[tt_all] * bary[hit_mask][..., None]).sum(1)
        origins = P_all + nrm[tt_all] * rad * 0.006 + L * rad * 0.004
        blocked = trimesh.ray.ray_triangle.RayMeshIntersector(whole).intersects_any(origins, np.tile(L, (len(origins), 1)))
        lit[hit_mask] = np.where(blocked, 0.0, 1.0)
    for i, mat in enumerate(mats):
        mask = (tid >= 0) & (M[np.maximum(tid, 0)] == i)
        if not mask.any(): continue
        tt = tid[mask]; b = bary[mask]
        P = (T[tt] * b[..., None]).sum(1); n = nrm[tt]
        col = mat(P, n, tt - starts[i], b) if getattr(mat, "textured", False) else mat(P, n)
        if mat not in GLOW:
            lam = np.clip(n @ L, 0, 1) * lit[mask]
            col = col * (0.42 + 0.78 * lam)[:, None]
        img[mask] = col

    img *= ambient_occlusion(depth, rad)[..., None]

    # Down to 80 px: average 4x4 blocks, keep pixels that are mostly covered, then a dark outline.
    k = R // OUT_PX
    a = alpha.reshape(OUT_PX, k, OUT_PX, k).mean((1, 3))
    c = (img * alpha[..., None]).reshape(OUT_PX, k, OUT_PX, k, 3).sum((1, 3)) / np.maximum(a * k * k, 1e-6)[..., None]
    solid = a > COVER
    out = np.zeros((OUT_PX, OUT_PX, 4))
    out[solid, :3] = c[solid]; out[solid, 3] = 1
    pad = np.pad(solid, 1)
    edge = ~solid & (pad[:-2, 1:-1] | pad[2:, 1:-1] | pad[1:-1, :-2] | pad[1:-1, 2:])
    out[edge, :3] = OUTLINE; out[edge, 3] = 1
    return Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8), "RGBA").resize((OUT_PX * UP, OUT_PX * UP), Image.NEAREST)

def load_models(path):
    # A .glb, or the game-ready .mesh files of a Potato mod (comma separated, each with its <name>_d.png beside it).
    if not path.endswith(".mesh"): return trimesh.load(path, force="scene").dump()
    import struct
    from trimesh.visual import TextureVisuals
    from trimesh.visual.material import PBRMaterial
    out = []
    for one in path.split(","):
        raw = open(one, "rb").read()
        nv, ni = struct.unpack("<ii", raw[4:12]); o = 12
        pos = np.frombuffer(raw, "<f4", nv * 3, o).reshape(-1, 3); o += nv * 12
        o += nv * 12                                               # normals: recomputed per face
        uv = np.frombuffer(raw, "<f4", nv * 2, o).reshape(-1, 2); o += nv * 8
        idx = np.frombuffer(raw, "<i4", ni, o).reshape(-1, 3)
        tex = Image.open(one[:-5] + "_d.png")
        out.append(trimesh.Trimesh(pos.astype(float), idx, process=False,
                                   visual=TextureVisuals(uv=uv.astype(float), material=PBRMaterial(baseColorTexture=tex))))
    return out

def render_lowpoly(path, lift=1.65, **view):
    # A game-ready model with few, large triangles: drawn triangle by triangle with its texture. The textures of the
    # Potato pieces are made darker for the game's lighting (about 1.65x brighter in game), so they're lifted back.
    parts = []
    for geom in load_models(path):
        img = np.clip(np.asarray(geom.visual.material.baseColorTexture.convert("RGB"), float) / 255.0 * lift, 0, 1)
        h, w = img.shape[:2]; fuv = geom.visual.uv[geom.faces]
        def m(p, n, t, b, img=img, fuv=fuv, h=h, w=w):
            st = (fuv[t] * b[..., None]).sum(1)
            return img[np.clip(((1 - st[:, 1] % 1) * (h - 1)).astype(int), 0, h - 1), np.clip(((st[:, 0] % 1) * (w - 1)).astype(int), 0, w - 1)]
        m.textured = True
        parts.append((geom, m))
    return render(parts, **view)

def render_dense(path, yaw=35, pitch=25, fov=28, light=(-0.5, 0.8, 0.6), splat=2):
    # Meshy models have millions of tiny triangles: each one is drawn as a dot of its own colour (its texture at the
    # triangle's centre), nearest dot wins. At icon size the dots join up into a solid surface.
    cents, cols, nrms = [], [], []
    for geom in load_models(path):
        tri = geom.triangles
        n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]); n /= np.linalg.norm(n, axis=1)[:, None] + 1e-12
        mat = getattr(geom.visual, "material", None)
        tex = getattr(mat, "baseColorTexture", None); uv = getattr(geom.visual, "uv", None)
        if tex is not None and uv is not None:
            img = np.asarray(tex.convert("RGB"), float) / 255.0; h, w = img.shape[:2]
            st = uv[geom.faces].mean(1)
            c = img[np.clip(((1 - st[:, 1] % 1) * (h - 1)).astype(int), 0, h - 1), np.clip(((st[:, 0] % 1) * (w - 1)).astype(int), 0, w - 1)]
        else:
            c = np.tile(np.array(getattr(mat, "baseColorFactor", [200, 200, 200, 255])[:3], float) / 255.0, (len(tri), 1))
        f = getattr(mat, "baseColorFactor", None)
        if f is not None and tex is not None: c = c * (np.array(f[:3], float) / 255.0 if max(f[:3]) > 1 else np.array(f[:3], float))
        cents.append(tri.mean(1)); cols.append(c); nrms.append(n)
    C = np.concatenate(cents); COL = np.concatenate(cols); N_ = np.concatenate(nrms)
    centre = (C.max(0) + C.min(0)) / 2
    rad = np.linalg.norm(C - centre, axis=1).max()
    y, p = math.radians(yaw), math.radians(pitch)
    fwd = -np.array([math.sin(y) * math.cos(p), math.sin(p), math.cos(y) * math.cos(p)])
    eye = centre - fwd * rad / math.sin(math.radians(fov / 2)) * 1.02
    right = np.cross(fwd, [0, 1, 0]); right /= np.linalg.norm(right); up = np.cross(right, fwd)
    rel = C - eye
    cx, cy, cz = rel @ right, rel @ up, rel @ fwd
    u, v = cx / cz, cy / cz
    f = R * 0.94 / max(u.max() - u.min(), v.max() - v.min())
    mu, mv = (u.max() + u.min()) / 2, (v.max() + v.min()) / 2
    sx = (R / 2 + (u - mu) * f).astype(int); sy = (R / 2 - (v - mv) * f).astype(int)
    facing = (N_ * rel).sum(1) > 0; N_[facing] *= -1
    L = np.array(light, float); L /= np.linalg.norm(L)
    shade = np.clip(COL * (0.58 + 0.8 * np.clip(N_ @ L, 0, 1))[:, None], 0, 1)   # Meshy textures run dark; lifted to sit with the Farmer icons
    img = np.zeros((R, R, 3)); depth = np.full((R, R), np.inf); alpha = np.zeros((R, R))
    order = np.argsort(-cz)                         # far first, so near dots are written last and win
    for dx in range(splat):
        for dy in range(splat):
            xs, ys = np.clip(sx[order] + dx, 0, R - 1), np.clip(sy[order] + dy, 0, R - 1)
            z = cz[order]
            better = z < depth[ys, xs]
            img[ys[better], xs[better]] = shade[order][better]; depth[ys[better], xs[better]] = z[better]
            alpha[ys[better], xs[better]] = 1
    return finish_px(img, alpha)

def finish_px(img, alpha):
    k = R // OUT_PX
    a = alpha.reshape(OUT_PX, k, OUT_PX, k).mean((1, 3))
    c = (img * alpha[..., None]).reshape(OUT_PX, k, OUT_PX, k, 3).sum((1, 3)) / np.maximum(a * k * k, 1e-6)[..., None]
    solid = a > COVER
    out = np.zeros((OUT_PX, OUT_PX, 4))
    out[solid, :3] = c[solid]; out[solid, 3] = 1
    pad = np.pad(solid, 1)
    edge = ~solid & (pad[:-2, 1:-1] | pad[2:, 1:-1] | pad[1:-1, :-2] | pad[1:-1, 2:])
    out[edge, :3] = OUTLINE; out[edge, 3] = 1
    return Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8), "RGBA").resize((OUT_PX * UP, OUT_PX * UP), Image.NEAREST)

# ---------- the icons ----------

def chest():
    w = wood(WOOD, axis=0, planks=0.16, plank_axis=1)
    lid = wood(WOOD_L, axis=0, planks=0.2, plank_axis=2)
    parts = [(box((1.2, 0.55, 0.7), (0, 0.275, 0)), w),
             (box((1.26, 0.22, 0.76), (0, 0.66, 0)), lid)]
    for x in (-0.45, 0.45):
        parts.append((box((0.07, 0.8, 0.78), (x, 0.4, 0)), metal(IRON_D)))
    parts.append((box((0.18, 0.2, 0.05), (0, 0.5, 0.37)), metal(IRON)))
    return render(parts, yaw=32, pitch=24)

def door():
    w = wood(WOOD, axis=1, planks=0.18, plank_axis=0)
    parts = [(box((0.9, 1.8, 0.08), (0, 0.9, 0)), w),
             (box((0.12, 2.0, 0.16), (-0.51, 1.0, 0)), wood(WOOD_D, axis=1)),
             (box((0.12, 2.0, 0.16), (0.51, 1.0, 0)), wood(WOOD_D, axis=1)),
             (box((1.14, 0.14, 0.18), (0, 2.02, 0)), wood(WOOD_D, axis=0))]
    for y in (0.4, 1.4):
        parts.append((box((0.7, 0.07, 0.03), (-0.08, y, 0.055)), metal(IRON_D)))
    parts.append((torus(0.07, 0.016, (0.3, 0.95, 0.07), axis="z"), metal(IRON)))
    return render(parts, yaw=-28, pitch=10, fov=30)

def smelter():
    st = lambda: flat(STONE, rough=0.35, scale=9, seed=11)
    parts = [(cyl(0.55, 1.1, (0, 0.55, 0), r2=0.35, sections=9), st()),
             (cyl(0.33, 0.5, (0, 1.35, 0), r2=0.24, sections=8), st()),
             (box((0.38, 0.3, 0.12), (0, 0.42, 0.47)), glow(rgb(255, 150, 45))),
             (box((0.26, 0.18, 0.13), (0, 0.42, 0.48)), glow(rgb(255, 215, 110))),
             (cyl(0.13, 0.25, (0, 1.68, 0), r2=0.02, sections=8), glow(rgb(255, 160, 50)))]
    return render(parts, yaw=20, pitch=14, fov=30)

def map_():
    paper = rgb(226, 206, 160)
    def parch(p, n):
        col = flat(paper, 0.12, 5, 21)(p, n)
        # A dotted trail and a red X on the sheet's top face.
        top = n[:, 1] > 0.5
        x, z = p[:, 0], p[:, 2]
        trail = top & (np.abs(z - (-0.18 + 0.45 * (x + 0.45) / 0.8) + 0.06 * np.sin(x * 9)) < 0.022) & (np.sin(x * 55) > 0) & (x < 0.25)
        col[trail] = rgb(110, 80, 55)
        cx, cz = 0.32, 0.2
        xx = top & (np.abs(np.abs(x - cx) - np.abs(z - cz)) < 0.03) & (np.abs(x - cx) < 0.09)
        col[xx] = rgb(185, 35, 28)
        return col
    parts = [(box((1.1, 0.02, 0.8), (0, 0, 0)), parch),
             (cyl(0.07, 0.86, (-0.58, 0.04, 0), axis="z"), flat(rgb(200, 175, 125), 0.1)),
             (cyl(0.07, 0.86, (0.58, 0.04, 0), axis="z"), flat(rgb(200, 175, 125), 0.1))]
    return render(parts, yaw=10, pitch=55, fov=30)

def house():
    logs = wood(WOOD, axis=0, planks=0.14, plank_axis=1, plank_gap=0.05)
    logs_z = wood(WOOD, axis=2, planks=0.14, plank_axis=1, plank_gap=0.05)
    def thatch(p, n):
        g = fbm(p * np.array([30, 6, 30]), 31)
        return np.clip(rgb(150, 115, 62)[None, :] * (0.75 + 0.4 * g)[:, None], 0, 1)
    walls = box((1.4, 0.7, 0.9), (0, 0.35, 0))
    parts = [(walls, lambda p, n: np.where(np.abs(n[:, 0:1]) > 0.5, logs_z(p, n), logs(p, n))),
             (prism([(-0.55, 0), (0.55, 0), (0, 0.6)], 1.6, (0, 0.7, 0)), thatch),
             (box((0.26, 0.42, 0.04), (0.25, 0.21, 0.56)), wood(rgb(70, 44, 26), axis=1))]
    rot(parts[1][0], [0, 1, 0], 90, (0, 0.7, 0))
    return render(parts, yaw=38, pitch=22, fov=30)

def anvil():
    prof = [(-0.75, 0.62), (-0.35, 0.55), (-0.3, 0.5), (0.5, 0.5), (0.5, 0.7), (-0.3, 0.7), (-0.75, 0.66)]
    top = [(-0.3, 0.5), (0.5, 0.5), (0.5, 0.72), (-0.3, 0.72)]
    parts = [(prism([(-0.78, 0.66), (-0.3, 0.56), (-0.3, 0.72)], 0.18), metal(IRON)),
             (prism(top, 0.36), metal(IRON)),
             (prism([(-0.12, 0.25), (0.32, 0.25), (0.25, 0.5), (-0.05, 0.5)], 0.24), metal(IRON_D)),
             (prism([(-0.3, 0.1), (0.5, 0.1), (0.4, 0.25), (-0.2, 0.25)], 0.4), metal(IRON)),
             (cyl(0.42, 0.12, (0.1, 0.05, 0), sections=14), wood(WOOD_D, axis=1))]
    return render(parts, yaw=28, pitch=20, fov=30)

def shield():
    red, plain = rgb(165, 52, 40), WOOD
    def face(p, n):
        band = (np.floor((p[:, 0] + 1) / 0.25) % 2).astype(bool)
        base = np.where(band[:, None], wood(red, axis=1, planks=None)(p, n), wood(plain, axis=1)(p, n))
        t = (p[:, 0] + 1) / 0.25; gap = np.abs(t - np.round(t)) < 0.06
        base[gap] *= 0.6
        return base
    parts = [(cyl(0.8, 0.06, (0, 0, 0), axis="z", sections=36), face),
             (torus(0.8, 0.045, (0, 0, 0), axis="z"), metal(IRON)),
             (sphere(0.2, (0, 0, 0.03), (1, 1, 0.6)), metal(IRON))]
    return render(parts, yaw=22, pitch=12, fov=28)

def pouch():
    lea = flat(LEATHER, 0.25, 7, 41)
    parts = [(sphere(0.5, (0, 0.42, 0), (1, 0.9, 0.9)), lea),
             (cyl(0.2, 0.3, (0, 0.95, 0), r2=0.3, sections=12), lea),
             (torus(0.21, 0.035, (0, 0.88, 0), axis="y"), flat(rgb(210, 180, 110), 0.1)),
             (cyl(0.13, 0.03, (0.12, 1.12, 0.05), axis="z", sections=18), metal(GOLD))]
    rot(parts[3][0], [1, 0, 0], -20, (0.12, 1.12, 0.05))
    return render(parts, yaw=25, pitch=18, fov=30)

def fire():
    parts = []
    for i in range(8):
        a = i / 8 * math.tau
        parts.append((sphere(0.13, (math.cos(a) * 0.55, 0.06, math.sin(a) * 0.55), (1.2, 0.8, 1), sub=1), flat(STONE, 0.3, 8, 51 + i)))
    for a in (20, 80, 140):
        l = cyl(0.07, 0.9, (0, 0.12, 0), axis="x", sections=10); rot(l, [0, 1, 0], a); parts.append((l, wood(WOOD_D, axis=0)))
    parts += [(cyl(0.28, 0.75, (0, 0.5, 0), r2=0.02, sections=10), glow(rgb(240, 110, 30))),
              (cyl(0.18, 0.55, (0.02, 0.42, 0.12), r2=0.02, sections=10), glow(rgb(255, 190, 60))),
              (cyl(0.15, 0.45, (-0.15, 0.36, 0.05), r2=0.02, sections=8), glow(rgb(250, 150, 40)))]
    return render(parts, yaw=30, pitch=28, fov=30)

def mirror():
    glass = glow(rgb(175, 205, 215))
    parts = [(cyl(0.5, 0.05, (0, 0.85, 0), axis="z", sections=36), lambda p, n: np.where((n[:, 2:3] > 0.5), glass(p, n) * (0.9 + 0.25 * ((p[:, 0:1] - p[:, 1:2] + 0.5) % 0.5 < 0.12)), metal(BRONZE)(p, n))),
             (torus(0.5, 0.05, (0, 0.85, 0), axis="z"), metal(BRONZE)),
             (cyl(0.06, 0.55, (0, 0.12, 0), sections=12), wood(WOOD_D, axis=1)),
             (sphere(0.08, (0, 0.38, 0)), metal(BRONZE))]
    return render(parts, yaw=-25, pitch=10, fov=30)

def hammer():
    parts = [(cyl(0.08, 1.3, (0, 0.25, 0), sections=12), wood(WOOD, axis=1)),
             (cyl(0.1, 0.35, (0, -0.3, 0), sections=12), flat(rgb(85, 55, 35), 0.2)),
             (box((0.8, 0.36, 0.36), (0, 0.95, 0)), metal(IRON)),
             (box((0.2, 0.42, 0.42), (0, 0.95, 0)), metal(IRON_D))]
    for m, _ in parts: rot(m, [0, 0, 1], -30)
    return render(parts, yaw=30, pitch=15, fov=30)

def gem():
    crown = cyl(0.5, 0.22, (0, 0.11, 0), sections=8, r2=0.3)          # the cut top, flat table
    pav = cyl(0.5, 0.62, (0, -0.31, 0), sections=8, r2=0.001)        # the point underneath
    pav.apply_transform(trimesh.transformations.rotation_matrix(math.pi, [1, 0, 0]))
    pav.apply_translation((0, -0.62 + 0.0, 0)); pav.apply_translation((0, 0.62 - 0.62, 0))
    def facets(p, n):
        base = rgb(150, 70, 210)
        k = 0.65 + 0.55 * np.clip(n @ np.array([-0.4, 0.8, 0.45]), 0, 1) + 0.3 * (n[:, 1] > 0.95)
        return np.clip(base[None, :] * k[:, None], 0, 1)
    GLOW.add(facets)
    return render([(crown, facets), (pav, facets)], yaw=12, pitch=22, fov=30)

def coins():
    parts = []
    for i in range(6):
        c = cyl(0.42, 0.075, (0.03 * math.sin(i * 2.1), 0.04 + i * 0.08, 0.03 * math.cos(i * 1.7)), sections=24)
        parts.append((c, lambda p, n, i=i: np.where(np.abs(n[:, 1:2]) > 0.5, metal(GOLD, 61 + i)(p, n), metal(GOLD * 0.8, 61 + i)(p, n))))
    lean = cyl(0.42, 0.075, (0.6, 0.42, 0.1), axis="z", sections=24); rot(lean, [0, 1, 0], -25, (0.6, 0.42, 0.1)); rot(lean, [1, 0, 0], 12, (0.6, 0.42, 0.1))
    def face(p, n):
        col = metal(GOLD, 70)(p, n)
        r = np.hypot(p[:, 0] - 0.6, p[:, 1] - 0.42)
        col[(np.abs(r - 0.3) < 0.03)] *= 0.7
        return col
    parts.append((lean, face))
    return render(parts, yaw=25, pitch=25, fov=30)

ICONS = {"chest": chest, "door": door, "smelter": smelter, "map": map_, "house": house, "anvil": anvil, "shield": shield,
         "pouch": pouch, "fire": fire, "mirror": mirror, "hammer": hammer, "gem": gem, "coins": coins}

if __name__ == "__main__":
    # A Meshy model: python3 -I tools/render_icons.py OUT --glb model.glb NAME YAW PITCH
    if len(sys.argv) > 2 and sys.argv[2] == "--glb":
        path, name = sys.argv[3], sys.argv[4]
        yaw = float(sys.argv[5]) if len(sys.argv) > 5 else 35; pitch = float(sys.argv[6]) if len(sys.argv) > 6 else 25
        (render_lowpoly(path, yaw=yaw, pitch=pitch) if path.endswith(".mesh") else render_dense(path, yaw=yaw, pitch=pitch)).save(os.path.join(sys.argv[1], f"icon-{name}.png")); sys.exit()
    out = sys.argv[1]; names = sys.argv[2:] or list(ICONS)
    os.makedirs(out, exist_ok=True)
    done = []
    for n in names:
        im = ICONS[n](); im.save(os.path.join(out, f"icon-{n}.png")); done.append(im)
    sheet = Image.new("RGBA", (len(done) * 88, 88), (58, 50, 44, 255))
    for i, im in enumerate(done): sheet.paste(im, (i * 88 + 4, 4), im)
    sheet.resize((sheet.width * 2, sheet.height * 2), Image.NEAREST).save(os.path.join(out, "_sheet.png"))
