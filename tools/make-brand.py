"""Generate the brand set: the Acrylic and Paper marks for Windows and the web.

Same geometry as make-icon.py -- it imports geometry(), write_ico() and the size table
from there, so every raster frame is laid out on its own integer pixel grid and checked
for mirror symmetry. What differs is the treatment: a vertical gradient ground, a glass
tray (translucent fill with a rim), and a highlight across the top of the magnified pill.

    python tools/make-brand.py brand

writes

    brand/windows/ArtDock.ico            Acrylic, the nine frames the app icon carries
    brand/windows/ArtDock-light.ico      Paper, same frames
    brand/png/<mark>-<size>.png          16 .. 1024, both marks
    brand/web/favicon.ico                Acrylic 16/32/48
    brand/web/favicon.svg                Acrylic, vector
    brand/web/mark-light.svg             Paper, vector
    brand/web/favicon-16.png, favicon-32.png, apple-touch-icon.png (180, square ground),
              icon-192.png, icon-512.png, icon-maskable-512.png, site.webmanifest
    brand/installer/splash.png           the setup's splash, from the dark stacked lockup

The lockups are not drawn here -- they are their SVGs rendered at 2x -- so the splash is made
from brand/lockups/lockup-stacked-dark@2x.png, and skipped with a warning when that is missing.

Needs Pillow. The shipped src/ArtDock/Assets/ArtDock.ico is a copy of brand/windows/ArtDock.ico,
adopted on 2026-09-28. Running this does not update it: copy the new ArtDock.ico over it
deliberately, and rebuild.
"""
import importlib.util
import json
import os
import sys

from PIL import Image, ImageChops, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("make_icon", os.path.join(HERE, "make-icon.py"))
make_icon = importlib.util.module_from_spec(spec)
spec.loader.exec_module(make_icon)
geometry, write_ico, SIZES = make_icon.geometry, make_icon.write_ico, make_icon.SIZES

HOT_TOP = (140, 153, 255)
HOT_BOT = (77, 92, 255)

STYLES = {
    # name: ground gradient, tray fill (top, bottom, with alpha), rim (colour, alpha), pill
    "acrylic": dict(
        ground=((31, 35, 51), (20, 22, 31)),
        tray=((255, 255, 255, 56), (255, 255, 255, 20)),
        rim=(255, 255, 255, 46),
        pill=(230, 233, 255),
    ),
    "paper": dict(
        ground=((246, 247, 252), (228, 231, 242)),
        tray=((58, 63, 88, 26), (58, 63, 88, 51)),
        rim=(255, 255, 255, 217),
        pill=(58, 63, 88),
    ),
}

HIGHLIGHT_ALPHA = 115           # 0.45 of white at the top of the magnified pill, fading to 0

# Velopack draws the splash at its pixel size times the monitor's scale, so it is made at 1x.
# Three-quarters of the lockup's 640x520 keeps it a splash rather than a window at 100%. The
# setup lays its 12px progress bar over the bottom of the image, full width, which the lockup's
# margin below the tagline leaves room for; tools/release.ps1 colours the bar HOT_BOT.
SPLASH = (480, 390)


def vgrad(w, h, top, bot):
    """Vertical RGBA gradient; colours may be RGB or RGBA."""
    top = tuple(top) + (255,) * (4 - len(top))
    bot = tuple(bot) + (255,) * (4 - len(bot))
    g = Image.new("RGBA", (1, h))
    px = g.load()
    for y in range(h):
        t = y / max(1, h - 1)
        px[0, y] = tuple(round(a + (b - a) * t) for a, b in zip(top, bot))
    return g.resize((w, h), Image.NEAREST)


def paste_grad(img, box, radius, top, bot):
    """Fill a rounded rectangle with a vertical gradient, composited over img."""
    x0, y0, x1, y1 = box
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle(box, radius=radius, fill=255)
    grad = Image.new("RGBA", img.size, (0, 0, 0, 0))
    grad.paste(vgrad(x1 - x0 + 1, y1 - y0 + 1, top, bot), (x0, y0))
    layer.paste(grad, (0, 0), mask)
    img.alpha_composite(layer)


def render(S, style, square=False, canvas=None, ss=16):
    """One frame. `square` fills the ground to the edges (Apple touch icon); `canvas`
    draws the mark at S centred on a larger square of that size (maskable icon)."""
    st = STYLES[style]
    g = geometry(S)
    C = canvas or S
    off = (C - S) // 2
    W = C * ss
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))

    r = 0 if square or canvas else g["bg_r"] * ss
    paste_grad(img, (0, 0, W - 1, W - 1), r, *st["ground"])

    tx0, ty0, tx1, ty1 = (v + off for v in g["tray"])
    box = (tx0 * ss, ty0 * ss, tx1 * ss - 1, ty1 * ss - 1)
    paste_grad(img, box, g["tray_r"] * ss, *st["tray"])
    if S >= 32:                                 # the rim is 2px at 256 and vanishes below
        rim = Image.new("RGBA", (W, W), (0, 0, 0, 0))
        ImageDraw.Draw(rim).rounded_rectangle(box, radius=g["tray_r"] * ss,
                                              outline=st["rim"], width=max(1, round(S / 128)) * ss)
        img.alpha_composite(rim)

    d = ImageDraw.Draw(img)
    base = g["baseline"] + off
    for x0, w, centre in g["boxes"]:
        x0 += off
        pbox = (x0 * ss, (base - w) * ss, (x0 + w) * ss - 1, base * ss - 1)
        pr = max(1, round(w * 0.30)) * ss
        if centre:
            paste_grad(img, pbox, pr, HOT_TOP, HOT_BOT)
            if S >= 24:
                half = (x0 * ss, (base - w) * ss, (x0 + w) * ss - 1, (base - w // 2) * ss - 1)
                paste_grad(img, half, pr, (255, 255, 255, HIGHLIGHT_ALPHA), (255, 255, 255, 0))
        else:
            d.rounded_rectangle(pbox, radius=pr, fill=st["pill"] + (255,))

    out = img.resize((C, C), Image.BOX)
    mirrored = out.transpose(Image.FLIP_LEFT_RIGHT)
    assert ImageChops.difference(out, mirrored).getbbox() is None, \
        f"{style} {S}px frame is not mirror-symmetric"
    return out


def svg(style):
    """The same mark as a vector, for favicon.svg and the lockups."""
    st = STYLES[style]
    hexc = lambda c: "#%02x%02x%02x" % tuple(c[:3])
    op = lambda c: "%.2f" % (c[3] / 255)
    g0, g1 = st["ground"]
    t0, t1 = st["tray"]
    pill = hexc(st["pill"])
    rows = ""
    for x0, w, centre in geometry(256)["boxes"]:
        rx = round(w * 0.30)
        fill = "url(#hot)" if centre else pill
        rows += f'<rect x="{x0}" y="{156 - w}" width="{w}" height="{w}" rx="{rx}" fill="{fill}"/>'
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">'
        "<defs>"
        f'<linearGradient id="g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{hexc(g0)}"/><stop offset="1" stop-color="{hexc(g1)}"/></linearGradient>'
        f'<linearGradient id="t" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{hexc(t0)}" stop-opacity="{op(t0)}"/><stop offset="1" stop-color="{hexc(t1)}" stop-opacity="{op(t1)}"/></linearGradient>'
        f'<linearGradient id="hot" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{hexc(HOT_TOP)}"/><stop offset="1" stop-color="{hexc(HOT_BOT)}"/></linearGradient>'
        '<linearGradient id="hl" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity=".45"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient>'
        "</defs>"
        '<rect width="256" height="256" rx="57" fill="url(#g)"/>'
        f'<rect x="17" y="104" width="222" height="60" rx="30" fill="url(#t)" stroke="{hexc(st["rim"])}" stroke-opacity="{op(st["rim"])}" stroke-width="2"/>'
        + rows +
        '<rect x="96" y="92" width="64" height="32" rx="19" fill="url(#hl)"/>'
        "</svg>"
    )


def main(argv):
    out = argv[0] if argv else "brand"
    for sub in ("windows", "png", "web"):
        os.makedirs(os.path.join(out, sub), exist_ok=True)

    # Windows: the app icon's nine frames, both marks.
    for style, name in (("acrylic", "ArtDock.ico"), ("paper", "ArtDock-light.ico")):
        write_ico(os.path.join(out, "windows", name), [render(S, style) for S in SIZES])

    # Flat PNGs at the usual sizes.
    for style in STYLES:
        for S in (16, 32, 48, 64, 128, 256, 512, 1024):
            render(S, style).save(os.path.join(out, "png", f"{style}-{S}.png"))

    # Web.
    web = lambda n: os.path.join(out, "web", n)
    write_ico(web("favicon.ico"), [render(S, "acrylic") for S in (16, 32, 48)])
    render(16, "acrylic").save(web("favicon-16.png"))
    render(32, "acrylic").save(web("favicon-32.png"))
    render(180, "acrylic", square=True).save(web("apple-touch-icon.png"))
    render(192, "acrylic").save(web("icon-192.png"))
    render(512, "acrylic").save(web("icon-512.png"))
    render(420, "acrylic", canvas=512).save(web("icon-maskable-512.png"))
    with open(web("favicon.svg"), "w", encoding="utf-8") as f:
        f.write(svg("acrylic"))
    with open(web("mark-light.svg"), "w", encoding="utf-8") as f:
        f.write(svg("paper"))
    with open(web("site.webmanifest"), "w", encoding="utf-8") as f:
        json.dump({
            "name": "ArtDock", "short_name": "ArtDock",
            "icons": [
                {"src": "/icon-192.png", "sizes": "192x192", "type": "image/png"},
                {"src": "/icon-512.png", "sizes": "512x512", "type": "image/png"},
                {"src": "/icon-maskable-512.png", "sizes": "512x512", "type": "image/png", "purpose": "maskable"},
            ],
            "theme_color": "#1f2333", "background_color": "#14161f", "display": "standalone",
        }, f, indent=2)
    with open(web("head.html"), "w", encoding="utf-8") as f:
        f.write(
            '<link rel="icon" href="/favicon.ico" sizes="16x16 32x32 48x48">\n'
            '<link rel="icon" href="/favicon.svg" type="image/svg+xml">\n'
            '<link rel="apple-touch-icon" href="/apple-touch-icon.png">\n'
            '<link rel="manifest" href="/site.webmanifest">\n'
            '<meta name="theme-color" content="#1f2333">\n'
        )
    print(f"wrote {out}/windows, {out}/png, {out}/web")

    # The installer's splash.
    lockup = os.path.join(out, "lockups", "lockup-stacked-dark@2x.png")
    if os.path.exists(lockup):
        os.makedirs(os.path.join(out, "installer"), exist_ok=True)
        splash = Image.open(lockup).convert("RGB").resize(SPLASH, Image.LANCZOS)
        splash.save(os.path.join(out, "installer", "splash.png"))
        print(f"wrote {out}/installer")
    else:
        print(f"no {lockup}, so no installer splash", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
