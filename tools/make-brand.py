"""Generate the brand set: the Ring mark, dark (Acrylic) and light (Paper), for Windows and the web.

The mark is a disc behind an outlined bar -- something bright seen through the dock. The bar's
outline cuts a gap through the disc, so the disc shows as a dome above the bar and a slice
inside it. Everything is flat: no gradients, no glass, no highlight. Adopted on 2026-11-21 in
place of the tray-and-pills mark, which tools/make-icon.py still draws.

Each raster size is laid out on its own integer pixel grid, as make-icon.py does: the disc's
width, the outline's weight, the gaps and the window inside the bar are whole pixels, the disc
straddles S/2 exactly, and the render is supersampled 16x then box-filtered down. So every
straight edge -- the bar's sides, the cuts through the disc -- lands on a pixel boundary and
only curves are antialiased. Below 32px the widths are hand-set, because the ratios round the
outline and the gaps unevenly there. Every frame is checked for mirror symmetry.

    python tools/make-brand.py brand

writes

    brand/windows/ArtDock.ico            Acrylic, the nine frames the app icon carries
    brand/windows/ArtDock-light.ico      Paper, same frames
    brand/png/<mark>-<size>.png          16 .. 1024, both marks
    brand/web/favicon.ico                Acrylic 16/32/48
    brand/web/favicon.svg                Acrylic, vector
    brand/web/mark-light.svg             Paper, vector
    brand/web/mark-mono.svg              one colour, no ground
    brand/web/favicon-16.png, favicon-32.png, apple-touch-icon.png (180, square ground),
              icon-192.png, icon-512.png, icon-maskable-512.png, site.webmanifest
    brand/lockups/*.svg                  the mark inside each lockup, redrawn in place
    brand/installer/splash.png           the setup's splash, from the dark stacked lockup

The lockups' lettering is not drawn here, and their @2x PNGs are their SVGs rendered at 2x by
a browser (the SVGs embed their font, which most other renderers ignore). So after a change to
the mark: run this, render the four lockup SVGs to their @2x PNGs, and run this again -- the
splash is made from brand/lockups/lockup-stacked-dark@2x.png, and skipped with a warning when
that is missing.

Needs Pillow; write_ico() and the size table come from make-icon.py. The shipped
src/ArtDock/Assets/ArtDock.ico is a copy of brand/windows/ArtDock.ico. Running this does not
update it: copy the new ArtDock.ico over it deliberately, and rebuild.
"""
import importlib.util
import json
import math
import os
import re
import sys

from PIL import Image, ImageChops, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("make_icon", os.path.join(HERE, "make-icon.py"))
make_icon = importlib.util.module_from_spec(spec)
spec.loader.exec_module(make_icon)
write_ico, SIZES = make_icon.write_ico, make_icon.SIZES

STYLES = {
    # name: ground, disc, bar outline
    "acrylic": dict(ground=(24, 27, 39), disc=(111, 125, 255), bar=(230, 233, 255)),
    "paper": dict(ground=(238, 240, 247), disc=(91, 105, 255), bar=(43, 47, 68)),
}
INK = (27, 30, 43)              # the one-colour mark

# Design ratios, as a fraction of the canvas, tuned at 256px and snapped per size.
R_BG     = 0.223    # ground corner radius
R_DISC   = 0.484    # the disc's diameter
R_INSET  = 0.102    # from the canvas edge to the bar's ends
R_WEIGHT = 0.059    # the bar's outline
R_GAP    = 0.035    # clearance between the disc and the outline, everywhere they meet
R_WINDOW = 0.094    # height of the opening inside the bar that the disc shows through

# Hand-set where the ratios round too unevenly to read.
#   (disc diameter, inset, outline weight, gap, window)
OVERRIDES = {
    16: (8, 1, 1, 1, 2),
    20: (10, 2, 1, 1, 3),
    24: (12, 2, 2, 1, 2),
}

# Velopack draws the splash at its pixel size times the monitor's scale, so it is made at 1x.
# Three-quarters of the lockup's 640x520 keeps it a splash rather than a window at 100%. The
# setup lays its 12px progress bar over the bottom of the image, full width, which the lockup's
# margin below the tagline leaves room for; tools/release.ps1 colours the bar.
SPLASH = (480, 390)

# Which mark each lockup carries.
LOCKUPS = {
    "lockup-light.svg": "paper",
    "lockup-light-transparent.svg": "paper",
    "lockup-stacked-dark.svg": "acrylic",
    "lockup-stacked-transparent.svg": "acrylic",
}


def even(v):
    """Round to an even integer -- the disc must straddle S/2 exactly."""
    return max(2, int(round(v / 2)) * 2)


def geometry(S):
    """Integer, symmetric geometry for one raster size. Boxes are (x0, y0, x1, y1), x1/y1 exclusive."""
    if S in OVERRIDES:
        disc, inset, weight, gap, window = OVERRIDES[S]
    else:
        disc = even(S * R_DISC)
        inset = max(1, round(S * R_INSET))
        weight = max(1, round(S * R_WEIGHT))
        gap = max(1, round(S * R_GAP))
        window = max(1, round(S * R_WINDOW))
    assert S % 2 == 0 and disc % 2 == 0, f"{S}: the disc cannot be centred on a pixel boundary"

    bar_h = 2 * weight + 2 * gap + window
    # The content box runs from the top of the disc to the bottom of the bar; the disc's lowest
    # point stops a gap short of the outline's inner bottom edge.
    top = (S - (disc + gap + weight)) // 2
    dx0 = S // 2 - disc // 2
    bar_bottom = top + disc + gap + weight
    bar_top = bar_bottom - bar_h
    assert inset + bar_h <= S // 2, f"{S}: the bar is too short to have straight edges"

    return dict(bg_r=max(2, round(S * R_BG)),
                disc=(dx0, top, dx0 + disc, top + disc),
                bar=(inset, bar_top, S - inset, bar_bottom),
                weight=weight,
                cut=(bar_top - gap, bar_top + weight + gap))   # the band of the disc that is cut away


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
    ImageDraw.Draw(img).rounded_rectangle([0, 0, W - 1, W - 1], radius=r, fill=st["ground"] + (255,))

    def box(b, grow=0):
        x0, y0, x1, y1 = b
        return [(x0 + off - grow) * ss, (y0 + off - grow) * ss,
                (x1 + off + grow) * ss - 1, (y1 + off + grow) * ss - 1]

    # The disc, less the band the outline and its clearance take out of it.
    disc = Image.new("L", (W, W), 0)
    d = ImageDraw.Draw(disc)
    d.ellipse(box(g["disc"]), fill=255)
    c0, c1 = g["cut"]
    d.rectangle([0, (c0 + off) * ss, W - 1, (c1 + off) * ss - 1], fill=0)
    img.paste(st["disc"] + (255,), (0, 0), disc)

    # The bar: an outline, so the ground and the disc show through its middle.
    x0, y0, x1, y1 = g["bar"]
    w = g["weight"]
    ring = Image.new("L", (W, W), 0)
    d = ImageDraw.Draw(ring)
    d.rounded_rectangle(box(g["bar"]), radius=(y1 - y0) * ss // 2, fill=255)
    d.rounded_rectangle(box(g["bar"], -w), radius=(y1 - y0 - 2 * w) * ss // 2, fill=0)
    img.paste(st["bar"] + (255,), (0, 0), ring)

    out = img.resize((C, C), Image.BOX)
    mirrored = out.transpose(Image.FLIP_LEFT_RIGHT)
    assert ImageChops.difference(out, mirrored).getbbox() is None, \
        f"{style} {S}px frame is not mirror-symmetric"
    return out


def n(v):
    return f"{v:.3f}".rstrip("0").rstrip(".")


def paths():
    """The mark at 256 as three filled paths: the dome, the slice seen through the bar, the outline.

    Real edges, not masks or strokes: a mask is drawn at its own resolution and goes soft when
    the page is zoomed, which is how the first drawing of this mark looked."""
    g = geometry(256)
    dx0, dy0, dx1, dy1 = g["disc"]
    cx, cy, r = (dx0 + dx1) / 2, (dy0 + dy1) / 2, (dx1 - dx0) / 2
    c0, c1 = g["cut"]

    def chord(y):
        half = math.sqrt(r * r - (y - cy) ** 2)
        return cx - half, cx + half

    a0, a1 = chord(c0)
    b0, b1 = chord(c1)
    dome = f"M{n(a0)},{c0} A{n(r)},{n(r)} 0 1 1 {n(a1)},{c0} Z"
    seen = f"M{n(b0)},{c1} A{n(r)},{n(r)} 0 0 0 {n(b1)},{c1} Z"

    def pill(x0, y0, x1, y1, clockwise):
        rr = (y1 - y0) / 2
        if clockwise:
            return (f"M{n(x0 + rr)},{n(y0)} L{n(x1 - rr)},{n(y0)} A{n(rr)},{n(rr)} 0 0 1 {n(x1 - rr)},{n(y1)} "
                    f"L{n(x0 + rr)},{n(y1)} A{n(rr)},{n(rr)} 0 0 1 {n(x0 + rr)},{n(y0)} Z")
        return (f"M{n(x0 + rr)},{n(y0)} A{n(rr)},{n(rr)} 0 0 0 {n(x0 + rr)},{n(y1)} L{n(x1 - rr)},{n(y1)} "
                f"A{n(rr)},{n(rr)} 0 0 0 {n(x1 - rr)},{n(y0)} Z")

    x0, y0, x1, y1 = g["bar"]
    w = g["weight"]
    ring = pill(x0, y0, x1, y1, True) + " " + pill(x0 + w, y0 + w, x1 - w, y1 - w, False)
    return dome, seen, ring


def hexc(c):
    return "#%02x%02x%02x" % tuple(c[:3])


def mark(style):
    """The mark's SVG elements on a 256 box: the ground (unless one-colour) and the three paths."""
    dome, seen, ring = paths()
    if style == "mono":
        ground, disc, bar = "", hexc(INK), hexc(INK)
    else:
        st = STYLES[style]
        ground = f'<rect width="256" height="256" rx="57" fill="{hexc(st["ground"])}"/>'
        disc, bar = hexc(st["disc"]), hexc(st["bar"])
    return (ground + f'<path d="{dome}" fill="{disc}"/><path d="{seen}" fill="{disc}"/>'
            f'<path d="{ring}" fill="{bar}"/>')


def svg(style):
    """The same mark as a vector, for favicon.svg and the marks for the web."""
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">{mark(style)}</svg>'


def update_lockups(out):
    """Redraw the mark inside each lockup SVG, leaving its lettering and embedded font alone."""
    done = []
    for name, style in LOCKUPS.items():
        path = os.path.join(out, "lockups", name)
        if not os.path.exists(path):
            continue
        with open(path, encoding="utf-8") as f:
            text = f.read()
        # The mark is the lockup's one group: <g transform="translate(..) scale(..)"> ... </g>.
        new, count = re.subn(r'(<g transform="[^"]*">).*?(</g>)',
                             lambda m: m.group(1) + mark(style) + m.group(2), text, count=1, flags=re.S)
        assert count == 1, f"{name}: no mark group to redraw"
        if new != text:
            with open(path, "w", encoding="utf-8", newline="") as f:
                f.write(new)
        done.append(name)
    return done


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
    web = lambda name: os.path.join(out, "web", name)
    ground = hexc(STYLES["acrylic"]["ground"])
    write_ico(web("favicon.ico"), [render(S, "acrylic") for S in (16, 32, 48)])
    render(16, "acrylic").save(web("favicon-16.png"))
    render(32, "acrylic").save(web("favicon-32.png"))
    render(180, "acrylic", square=True).save(web("apple-touch-icon.png"))
    render(192, "acrylic").save(web("icon-192.png"))
    render(512, "acrylic").save(web("icon-512.png"))
    render(420, "acrylic", canvas=512).save(web("icon-maskable-512.png"))
    for name, style in (("favicon.svg", "acrylic"), ("mark-light.svg", "paper"), ("mark-mono.svg", "mono")):
        with open(web(name), "w", encoding="utf-8") as f:
            f.write(svg(style))
    with open(web("site.webmanifest"), "w", encoding="utf-8") as f:
        json.dump({
            "name": "ArtDock", "short_name": "ArtDock",
            "icons": [
                {"src": "/icon-192.png", "sizes": "192x192", "type": "image/png"},
                {"src": "/icon-512.png", "sizes": "512x512", "type": "image/png"},
                {"src": "/icon-maskable-512.png", "sizes": "512x512", "type": "image/png", "purpose": "maskable"},
            ],
            "theme_color": ground, "background_color": ground, "display": "standalone",
        }, f, indent=2)
    with open(web("head.html"), "w", encoding="utf-8") as f:
        f.write(
            '<link rel="icon" href="/favicon.ico" sizes="16x16 32x32 48x48">\n'
            '<link rel="icon" href="/favicon.svg" type="image/svg+xml">\n'
            '<link rel="apple-touch-icon" href="/apple-touch-icon.png">\n'
            '<link rel="manifest" href="/site.webmanifest">\n'
            f'<meta name="theme-color" content="{ground}">\n'
        )
    print(f"wrote {out}/windows, {out}/png, {out}/web")

    # The mark inside the lockups.
    done = update_lockups(out)
    if done:
        print(f"redrew the mark in {len(done)} lockups; render their @2x PNGs with a browser")

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
