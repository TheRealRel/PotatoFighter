# Potato mod icon (PotatoRider, PotatoFighter): the Gemini art already has a transparent background with soft edges. Keep only the
# figure (drops stray specks in the corners), crop square around it with a 2% margin, resize to 256 in
# premultiplied colour so edges don't get dark fringes.
#   python3 -I make_icon.py art/icon-source.png icon.png
import sys
import numpy as np
from PIL import Image
from scipy import ndimage
im = np.asarray(Image.open(sys.argv[1]).convert("RGBA"), float) / 255
lab, n = ndimage.label(im[..., 3] > 0.01)
keep = lab == (np.argmax(ndimage.sum(np.ones(lab.shape), lab, range(1, n + 1))) + 1)
keep = ndimage.binary_dilation(keep, iterations=3)   # keep the soft edge around the figure
im[..., 3] *= keep
ys, xs = np.where(im[..., 3] > 0.01)
x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
side = max(x1 - x0, y1 - y0); side += 2 * int(side * 0.02)
cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
X0, Y0 = cx - side // 2, cy - side // 2
canvas = np.zeros((side, side, 4))
sx0, sy0, sx1, sy1 = max(X0, 0), max(Y0, 0), min(X0 + side, im.shape[1]), min(Y0 + side, im.shape[0])
canvas[sy0 - Y0:sy1 - Y0, sx0 - X0:sx1 - X0] = im[sy0:sy1, sx0:sx1]
canvas[..., :3] *= canvas[..., 3:4]
ch = [np.asarray(Image.fromarray(np.uint8(np.clip(canvas[..., i], 0, 1) * 255)).resize((256, 256), Image.LANCZOS), float) / 255 for i in range(4)]
A = np.clip(ch[3], 0, 1)
rgb = np.stack(ch[:3], -1) / np.maximum(A, 1e-4)[..., None]
Image.fromarray(np.uint8(np.dstack([np.clip(rgb, 0, 1), A]) * 255), "RGBA").save(sys.argv[2])
