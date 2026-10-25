# 갱도 원본 그림(MineShaft_gpt.webp) 위에 흙을 채우고 판자를 덧대 '폐쇄된 갱도' 그림을 만든다.
import math
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SRC = "MineShaft_gpt.webp"
OUT_FULL = "SealedShaft_full.png"
OUT_TEX = "../YuudongDeepMining/Textures/YDM/SealedShaft.png"

# 원본 1254px 기준 좌표 (틀 바깥선, 안쪽 입구)
FRAME = (173, 1080, 177, 1088)
OPEN = (282, 973, 293, 976)
SS = 2
OUTLINE = (24, 18, 14, 255)

rnd = random.Random(42)
base = Image.open(SRC).convert("RGBA")
W, H = base.size
layer = Image.new("RGBA", (W * SS, H * SS), (0, 0, 0, 0))
d = ImageDraw.Draw(layer)


def s(v):
    return v * SS


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(len(a)))


def jit(c, n):
    k = rnd.randint(-n, n)
    return tuple(max(0, min(255, c[i] + k)) for i in range(3)) + (255,)


# 1) 흙: 가운데가 살짝 봉긋하게 밝은 흙 + 덩어리·자갈
x0, x1, y0, y1 = OPEN
dirt = (104, 76, 50, 255)
d.rectangle((s(x0), s(y0), s(x1), s(y1)), fill=dirt)
cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
for r in range(330, 0, -6):
    t = r / 330
    col = lerp((128, 96, 64, 255), dirt, t ** 1.5)
    d.ellipse((s(cx - r), s(cy - r * 0.95), s(cx + r), s(cy + r * 0.95)), fill=col)
d.rectangle((s(x0), s(y0), s(x1), s(y1)), outline=None)
for _ in range(700):
    x, y = rnd.uniform(x0, x1), rnd.uniform(y0, y1)
    r = rnd.uniform(4, 13)
    c0 = jit((114, 86, 58, 255), 14)
    d.ellipse((s(x - r), s(y - r + 2), s(x + r), s(y + r + 2)), fill=lerp(c0, (40, 26, 16, 255), 0.35))
    d.ellipse((s(x - r), s(y - r), s(x + r), s(y + r)), fill=c0)
for _ in range(26):
    x, y = rnd.uniform(x0 + 20, x1 - 20), rnd.uniform(y0 + 20, y1 - 20)
    r = rnd.uniform(9, 20)
    k = rnd.randint(6, 8)
    pts = [(s(x + math.cos(2 * math.pi * i / k + rnd.uniform(-.25, .25)) * r * rnd.uniform(.75, 1.15)),
            s(y + math.sin(2 * math.pi * i / k + rnd.uniform(-.25, .25)) * r * rnd.uniform(.75, 1.15))) for i in range(k)]
    g = rnd.randint(88, 120)
    d.polygon(pts, fill=(g, g - 2, g - 6, 255))
    d.line(pts + [pts[0]], fill=OUTLINE, width=s(4))
# 틀 안쪽 가장자리 그늘
for i in range(18):
    a = int(110 * (1 - i / 18))
    d.rectangle((s(x0 + i), s(y0 + i), s(x1 - i), s(y1 - i)), outline=(20, 14, 10, a), width=s(1))
d.rectangle((s(x0), s(y0), s(x1), s(y1)), outline=OUTLINE, width=s(6))

# 2) 판자: 가로로 여섯 장, 틀 위에 걸쳐 못질. 사이로 흙이 보인다.
wood = tuple(int(v) for v in np.array(base)[627, 225][:3]) + (255,)
wood = lerp(wood, (150, 108, 68, 255), 0.35)
n = 4
gap = 58
ph = (y1 - y0 - 30 - gap * (n - 1)) / n
for i in range(n):
    top = y0 + 15 + i * (ph + gap)
    left = FRAME[0] + 55 + rnd.uniform(-12, 12)
    right = FRAME[1] - 55 + rnd.uniform(-12, 12)
    ang = math.radians(rnd.uniform(-1.6, 1.6))
    h = ph + rnd.uniform(-5, 5)
    col = jit(wood, 10)
    plank = Image.new("RGBA", (int(right - left) * SS + 40, int(h) * SS + 40), (0, 0, 0, 0))
    pd = ImageDraw.Draw(plank)
    pw, phh = int(right - left) * SS, int(h) * SS
    ox, oy = 20, 20
    pd.rectangle((ox, oy, ox + pw, oy + phh), fill=col)
    pd.rectangle((ox, oy, ox + pw, oy + phh * 0.18), fill=lerp(col, (255, 232, 196, 255), 0.22))
    pd.rectangle((ox, oy + phh * 0.8, ox + pw, oy + phh), fill=lerp(col, (30, 18, 10, 255), 0.3))
    for _ in range(9):
        gy = rnd.uniform(oy + phh * 0.25, oy + phh * 0.75)
        gx = rnd.uniform(ox, ox + pw * 0.8)
        pd.line((gx, gy, gx + rnd.uniform(60, 220) * SS, gy + rnd.uniform(-3, 3) * SS), fill=lerp(col, (40, 24, 12, 255), 0.35), width=s(2))
    if rnd.random() < 0.5:
        kx, ky = rnd.uniform(ox + pw * 0.2, ox + pw * 0.8), rnd.uniform(oy + phh * 0.35, oy + phh * 0.65)
        pd.ellipse((kx - s(10), ky - s(6), kx + s(10), ky + s(6)), outline=lerp(col, (40, 24, 12, 255), 0.45), width=s(2))
    # 끝 쪽 쪼갠 자국
    pd.line((ox + pw - s(30), oy + phh * 0.5, ox + pw, oy + phh * 0.42), fill=OUTLINE, width=s(2))
    pd.rectangle((ox, oy, ox + pw, oy + phh), outline=OUTLINE, width=s(6))
    # 못: 양쪽 끝 (틀 위)
    for nx in (ox + s(40), ox + pw - s(40)):
        for ny in (oy + phh * 0.32, oy + phh * 0.68):
            pd.ellipse((nx - s(7), ny - s(7), nx + s(7), ny + s(7)), fill=(70, 70, 74, 255), outline=OUTLINE, width=s(2))
            pd.ellipse((nx - s(4), ny - s(5), nx, ny - s(1)), fill=(170, 170, 176, 255))
    plank = plank.rotate(math.degrees(ang), resample=Image.BICUBIC, expand=True)
    # 그림자
    sh = Image.new("RGBA", plank.size, (0, 0, 0, 0))
    sh.paste((0, 0, 0, 120), (0, 0), plank.split()[3])
    sh = sh.filter(ImageFilter.GaussianBlur(s(5)))
    pcx, pcy = s((left + right) / 2), s(top + h / 2)
    layer.alpha_composite(sh, (int(pcx - plank.width / 2 + s(5)), int(pcy - plank.height / 2 + s(8))))
    layer.alpha_composite(plank, (int(pcx - plank.width / 2), int(pcy - plank.height / 2)))

# 3) 원본 위에 겹친다
layer = layer.resize((W, H), Image.LANCZOS)
out = base.copy()
out.alpha_composite(layer)
out.save(OUT_FULL)

# 4) 갱도 텍스처와 같은 방식으로 잘라 512px로 (틀이 2x2를 덮도록, drawSize 2.41)
l, r, t, b = FRAME
cx, cy = (l + r) / 2, (t + b) / 2
arr = np.array(out)
ys, xs = np.where(arr[:, :, 3] > 8)
half = max(cx - xs.min(), xs.max() - cx, cy - ys.min(), ys.max() - cy) + 4
side = int(round(2 * half))
canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
canvas.alpha_composite(out, (int(round(half - cx)), int(round(half - cy))))
canvas.convert("RGBa").resize((512, 512), Image.LANCZOS).convert("RGBA").save(OUT_TEX)
print("ok", side)
