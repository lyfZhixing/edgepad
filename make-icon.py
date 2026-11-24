# -*- coding: utf-8 -*-
"""
生成 EdgePad 图标（app.ico + 预览 png）。

设计：深色圆角底板 + 精准触摸板（1800:1275）+ 四条紫色边缘带 + 绿色触点与轨迹。
- 只用「板 + 四条边 + 一个点」，16px 下仍能认出是「带边缘的触控板」
- 配色对齐应用内 UI：底板 #141418 / 强调紫 #7C5CFF / 触点绿 #4ADE80
用法： python make-icon.py
"""
import math
import os

from PIL import Image, ImageDraw, ImageFilter

S = 2048
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))

ACCENT = (124, 92, 255)
GREEN = (74, 222, 128)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def vgrad(size, top, bottom):
    img = Image.new("RGB", (1, size), top)
    p = img.load()
    for y in range(size):
        p[0, y] = lerp(top, bottom, y / max(1, size - 1))
    return img.resize((size, size), Image.BILINEAR).convert("RGBA")


def glow(size, cx, cy, radius, color, peak):
    small = 192
    img = Image.new("RGBA", (small, small), (0, 0, 0, 0))
    p = img.load()
    cxs, cys, rs = cx * small, cy * small, radius * small
    for y in range(small):
        for x in range(small):
            d2 = (x - cxs) ** 2 + (y - cys) ** 2
            if d2 >= rs * rs:
                continue
            t = 1.0 - math.sqrt(d2) / rs
            a = int(peak * 255 * (t ** 2))
            if a:
                p[x, y] = (color[0], color[1], color[2], a)
    return img.resize((size, size), Image.BICUBIC)


def main():
    canvas = Image.alpha_composite(
        Image.new("RGBA", (S, S), (0, 0, 0, 0)),
        vgrad(S, (31, 31, 44), (8, 8, 12)),
    )
    canvas = Image.alpha_composite(canvas, glow(S, 0.5, 0.16, 0.75, (92, 70, 200), 0.32))

    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=int(S * 0.235), fill=255)
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(canvas, (0, 0), mask)

    # ---- 触摸板 ----
    pw = int(S * 0.70)
    ph = int(pw * 1275 / 1800)
    x0 = (S - pw) // 2
    y0 = (S - ph) // 2
    rad = int(ph * 0.16)

    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle(
        [x0, y0 + int(S * 0.015), x0 + pw, y0 + ph + int(S * 0.015)], radius=rad, fill=(0, 0, 0, 170)
    )
    out = Image.alpha_composite(out, shadow.filter(ImageFilter.GaussianBlur(S * 0.022)))
    d = ImageDraw.Draw(out)

    d.rounded_rectangle(
        [x0, y0, x0 + pw, y0 + ph], radius=rad,
        fill=(23, 23, 29, 255), outline=(64, 64, 84, 255), width=max(2, S // 420),
    )

    # ---- 四条边缘带 ----
    band = int(pw * 0.105)
    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    fill = ACCENT + (108,)
    ld.rectangle([x0, y0, x0 + band, y0 + ph], fill=fill)
    ld.rectangle([x0 + pw - band, y0, x0 + pw, y0 + ph], fill=fill)
    ld.rectangle([x0, y0, x0 + pw, y0 + band], fill=fill)
    ld.rectangle([x0, y0 + ph - band, x0 + pw, y0 + ph], fill=fill)

    lw = max(4, S // 230)
    edge = ACCENT + (255,)
    ld.line([x0 + band, y0, x0 + band, y0 + ph], fill=edge, width=lw)
    ld.line([x0 + pw - band, y0, x0 + pw - band, y0 + ph], fill=edge, width=lw)
    ld.line([x0, y0 + band, x0 + pw, y0 + band], fill=edge, width=lw)
    ld.line([x0, y0 + ph - band, x0 + pw, y0 + ph - band], fill=edge, width=lw)

    padmask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(padmask).rounded_rectangle([x0, y0, x0 + pw, y0 + ph], radius=rad, fill=255)
    out = Image.alpha_composite(out, Image.composite(layer, Image.new("RGBA", (S, S), (0, 0, 0, 0)), padmask))

    # ---- 触点与运动残影（左带内向上滑） ----
    cx = x0 + band * 0.52
    cy = y0 + ph * 0.52
    r0 = int(S * 0.032)
    echoes = [(1.00, 1.00, 255), (0.60, 0.62, 145), (0.34, 0.38, 70)]
    for i, (scale, rel, alpha) in enumerate(echoes):
        dy = ph * 0.105 * i
        rr = r0 * scale
        if i == 0:
            g = Image.new("RGBA", (S, S), (0, 0, 0, 0))
            ImageDraw.Draw(g).ellipse([cx - rr * 2.2, cy - rr * 2.2, cx + rr * 2.2, cy + rr * 2.2], fill=GREEN + (70,))
            out = Image.alpha_composite(out, g.filter(ImageFilter.GaussianBlur(S * 0.016)))
        d = ImageDraw.Draw(out)
        d.ellipse([cx - rr, cy + dy - rr, cx + rr, cy + dy + rr], fill=GREEN + (alpha,))

    r = int(S * 0.034)
    glowdot = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(glowdot).ellipse([cx - r * 2, cy - r * 2, cx + r * 2, cy + r * 2], fill=GREEN + (60,))
    out = Image.alpha_composite(out, glowdot.filter(ImageFilter.GaussianBlur(S * 0.014)))
    d = ImageDraw.Draw(out)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=GREEN + (255,))

    # ---- 输出 ----
    big = out
    ico_base = big.resize((256, 256), Image.LANCZOS)
    ico_path = os.path.join(HERE, "app.ico")
    ico_base.save(
        ico_path, format="ICO",
        sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)],
    )
    big.resize((512, 512), Image.LANCZOS).save(os.path.join(ROOT, "docs", "edge-pad-icon.png"))
    big.resize((256, 256), Image.LANCZOS).save(os.path.join(HERE, "icon-preview.png"))

    print("ok", ico_path, os.path.getsize(ico_path))


if __name__ == "__main__":
    main()
