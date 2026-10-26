# Puts a converted weapon .mesh (convert.py / relayout.py: standing on y = 0, longest side up) into the game's weapon
# frame, as on the iron axe's "attach": haft along +Z, head at +Z, blades along X, the hand at the origin.
#   python3 -I orient.py in.mesh out.mesh [grip_m=0.15]
# grip_m: how far above the pommel the hand holds it (the iron axe: 0.16 of 0.88 m). The haft is centred on the hand
# at that height, so a curved haft still sits in the palm.
import struct, sys
import numpy as np
src, dst = sys.argv[1:3]; grip = float(sys.argv[3]) if len(sys.argv) > 3 else 0.15
b = open(src, "rb").read(); assert b[:4] == b"PTM1"
nv, ni = struct.unpack_from("<ii", b, 4); o = 12
P = np.frombuffer(b, "<f4", nv * 3, o).reshape(nv, 3).astype(np.float64); o += nv * 12
N = np.frombuffer(b, "<f4", nv * 3, o).reshape(nv, 3).astype(np.float64); o += nv * 12
rest = b[o:]
band = np.abs(P[:, 1] - grip) < 0.02
cx, cz = P[band, 0].mean(), P[band, 2].mean()
def turn(v): return np.stack([v[:, 0], -v[:, 2], v[:, 1]], 1)   # rotate +90 deg about X: up (Y) becomes forward (Z)
P2 = turn(P - [cx, grip, cz]); N2 = turn(N)
open(dst, "wb").write(b[:12] + P2.astype("<f4").tobytes() + N2.astype("<f4").tobytes() + rest)
print(f"grip at {grip} m (haft centre x {cx:.3f}, z {cz:.3f}); bounds {np.round(P2.min(0), 3)} to {np.round(P2.max(0), 3)}")
