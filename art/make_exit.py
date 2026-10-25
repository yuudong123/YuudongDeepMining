# 지하 출구 텍스처: 원본(MineShaftExit_gpt.webp)을 틀이 2x2를 덮도록 잘라 512px로 만들고,
# 맨 위(천장 쪽) 사다리를 알파로 부드럽게 흐린다. 게임에서는 Transparent 셰이더로 그린다.
import numpy as np
from PIL import Image

FRAME = (217, 1039, 288, 1130)
im = Image.open("MineShaftExit_gpt.webp").convert("RGBA")
a = np.array(im)
l, r, t, b = FRAME
cx, cy = (l + r) / 2, (t + b) / 2
ys, xs = np.where(a[:, :, 3] > 8)
half = max(cx - xs.min(), xs.max() - cx, cy - ys.min(), ys.max() - cy) + 4
side = int(round(2 * half))
canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
canvas.alpha_composite(im, (int(round(half - cx)), int(round(half - cy))))
out = np.array(canvas.convert("RGBa").resize((512, 512), Image.LANCZOS).convert("RGBA")).astype(float)
fade = 90
ramp = np.clip(np.arange(512) / fade, 0, 1) ** 1.6
out[:, :, 3] *= ramp[:, None]
Image.fromarray(np.clip(out, 0, 255).astype("uint8"), "RGBA").save("../YuudongDeepMining/Textures/YDM/MineShaftExit.png")
print("ok")
