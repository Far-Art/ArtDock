"""Generate src/ArtDock/Assets/ArtDock.ico.

The icon this replaces was drawn once at 256px and downscaled blind to every other size.
Two things were wrong with that. The artwork spanned [-0.5, 255.5] rather than [0, 256],
so the left and top edges carried 50% alpha while the right and bottom were clipped hard
-- at 16px that reads as a grey ghost column down the left side and a mark that sits
visibly off-centre. And a bicubic downscale of a five-icon composition to 16px turns the
outer icons into 1px smears, which is why the small sizes looked soft.

So each size is laid out here on its own integer pixel grid: widths, gaps and the tray
are whole pixels, the row is built outward from the centre so symmetry is structural
rather than arithmetic, and the render is supersampled 16x then box-filtered down.
Straight edges therefore land exactly on pixel boundaries and only the corners are
antialiased. Sizes below 32px drop the outer icon pair and use hand-set widths, because
the ratios round down to 2px there and the magnification wave stops reading.

    python tools/make-icon.py src/ArtDock/Assets/ArtDock.ico
    python tools/make-icon.py out.ico --png C:/tmp/frames    # also dump each frame

Needs Pillow (pip install pillow). This is the source of truth for the mark's geometry, which
tools/make-brand.py imports. It is no longer what ships: since 2026-09-28 the app carries
make-brand.py's Acrylic mark, brand/windows/ArtDock.ico, copied to Assets/ArtDock.ico. So do
not point this at Assets/ArtDock.ico -- it would put the older flat mark back. After a change
to the geometry, run make-brand.py and copy its icon over instead.
"""
from PIL import Image, ImageChops, ImageDraw
import io
import os
import struct
import sys

BG      = (27, 30, 43, 255)     # rounded-square ground
TRAY    = (54, 56, 68, 255)     # the dock tray
PILL    = (201, 206, 247, 255)  # resting icons
HOT_TOP = (140, 153, 255, 255)  # magnified icon, gradient top
HOT_BOT = (77,  92, 255, 255)   # magnified icon, gradient bottom

# Design ratios, as a fraction of the canvas, tuned at 256px and snapped per size.
R_BG    = 0.223   # ground corner radius
R_LARGE = 0.25    # the magnified icon
R_MED   = 0.125
R_SMALL = 0.086
R_GAP   = 0.035
R_TRAYH = 0.234   # tray height
R_TRAYP = 0.027   # tray padding either side of the icon row
R_LIFT  = 0.031   # gap between the icon baseline and the bottom edge of the tray

# Below this the outer icon pair turns to mush, so the wave is cut from five to three.
FIVE_PILL_MIN = 32

# 20 and 40 exist for the tray at 125% and 150% DPI: Icon(stream, SmallIconSize) picks
# the frame that matches, and without them it was rescaling 24 down to 20.
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

# Hand-set widths where the ratios round too small to read.
#   (large, med, small, gap, tray padding)
OVERRIDES = {
    16: (6, 3, None, 1, 0),
    20: (6, 3, None, 1, 1),
    24: (8, 4, None, 1, 1),
}


def even(v):
    """Round to an even integer -- the centre icon must straddle S/2 exactly."""
    return max(2, int(round(v / 2)) * 2)


def geometry(S):
    """Integer, symmetric geometry for one raster size."""
    if S in OVERRIDES:
        large, med, small, gap, forced_pad = OVERRIDES[S]
    else:
        large = even(S * R_LARGE)
        med = max(2, round(S * R_MED))
        small = max(1, round(S * R_SMALL))
        gap = max(1, round(S * R_GAP))
        forced_pad = None

    widths = [large, med] + ([small] if small and S >= FIVE_PILL_MIN else [])

    # Build the row outward from the centre, so symmetry is structural.
    boxes = []                                  # (x0, width, is_centre)
    left = S // 2 - large // 2
    right = left + large
    boxes.append((left, large, True))
    for w in widths[1:]:
        left -= gap + w
        right += gap
        boxes.append((left, w, False))
        boxes.append((right, w, False))
        right += w
    assert left + right == S, f"{S}: icon row is not symmetric ({left} + {right})"

    pad = forced_pad if forced_pad is not None else max(1, round(S * R_TRAYP))
    tx0 = max(1, left - pad)                    # never let the tray bleed off the ground
    assert 1 <= tx0 < S // 2, f"{S}: icon row is wider than the tray can hold"

    tray_h = max(3, round(S * R_TRAYH))
    lift = max(1, round(S * R_LIFT))

    # Centre the whole mark: the magnified icon rises above the tray, so the content box
    # runs from the top of that icon down to the bottom of the tray.
    baseline_off = tray_h - lift                # baseline, measured from the tray top
    top_off = min(0, baseline_off - large)
    ty0 = (S - (tray_h - top_off)) // 2 - top_off

    return dict(bg_r=max(2, round(S * R_BG)),
                tray=(tx0, ty0, S - tx0, ty0 + tray_h),
                tray_r=tray_h // 2,
                baseline=ty0 + baseline_off,
                boxes=boxes)


def vgrad(w, h, top, bot):
    g = Image.new("RGBA", (1, h))
    px = g.load()
    for y in range(h):
        t = y / max(1, h - 1)
        px[0, y] = tuple(round(a + (b - a) * t) for a, b in zip(top, bot))
    return g.resize((w, h), Image.NEAREST)


def render(S, ss=16):
    """Render one frame. Geometry is integral at S, so scaling by ss stays exact."""
    g = geometry(S)
    W = S * ss
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    d.rounded_rectangle([0, 0, W - 1, W - 1], radius=g["bg_r"] * ss, fill=BG)

    tx0, ty0, tx1, ty1 = g["tray"]
    d.rounded_rectangle([tx0 * ss, ty0 * ss, tx1 * ss - 1, ty1 * ss - 1],
                        radius=g["tray_r"] * ss, fill=TRAY)

    base = g["baseline"]
    for x0, w, centre in g["boxes"]:
        box = [x0 * ss, (base - w) * ss, (x0 + w) * ss - 1, base * ss - 1]
        r = max(1, round(w * 0.30)) * ss        # dock icons are square squircles
        if centre:
            mask = Image.new("L", (W, W), 0)
            ImageDraw.Draw(mask).rounded_rectangle(box, radius=r, fill=255)
            img.paste(vgrad(W, W, HOT_TOP, HOT_BOT), (0, 0), mask)
        else:
            d.rounded_rectangle(box, radius=r, fill=PILL)

    return img.resize((S, S), Image.BOX)


def dib_frame(im):
    """One ICO frame as a bottom-up 32bpp DIB plus its 1bpp AND mask.

    Pillow writes every frame as PNG. Explorer reads those, but System.Drawing.Icon --
    which is what Services/TrayIcon.cs goes through -- is happier with DIBs below 256px,
    and DIBs are what Windows icon tooling emits. Only the 256 frame is left as PNG.
    """
    w, h = im.size
    px = im.load()
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)

    xor = bytearray()
    for y in range(h - 1, -1, -1):              # DIB rows run bottom-up
        for x in range(w):
            r, g, b, a = px[x, y]
            xor += bytes((b, g, r, a))

    stride = ((w + 31) // 32) * 4               # 1bpp rows pad to 4 bytes
    and_mask = bytearray()
    for y in range(h - 1, -1, -1):
        row = bytearray(stride)
        for x in range(w):
            if px[x, y][3] < 128:               # a set bit means transparent
                row[x >> 3] |= 0x80 >> (x & 7)
        and_mask += row

    return header + bytes(xor) + bytes(and_mask)


def write_ico(path, imgs):
    frames = []
    for im in imgs:
        if im.size[0] >= 256:
            buf = io.BytesIO()
            im.save(buf, format="PNG")
            frames.append((im.size[0], buf.getvalue()))
        else:
            frames.append((im.size[0], dib_frame(im)))

    offset = 6 + 16 * len(frames)
    entries, body = b"", b""
    for size, data in frames:
        entries += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32,
                               len(data), offset)
        offset += len(data)
        body += data

    with open(path, "wb") as f:
        f.write(struct.pack("<HHH", 0, 1, len(frames)) + entries + body)


def main(argv):
    if not argv:
        print("usage: make-icon.py <out.ico> [--png <dir>]")
        print(f"sizes: {SIZES}")
        return 1

    out, dump = argv[0], None
    if "--png" in argv:
        dump = argv[argv.index("--png") + 1]
        os.makedirs(dump, exist_ok=True)

    imgs = [render(S) for S in SIZES]

    # Check the symmetry rather than trusting it. The icon this replaces was off by half
    # a pixel and nobody spotted it until the pixels were measured.
    for S, im in zip(SIZES, imgs):
        mirrored = im.transpose(Image.FLIP_LEFT_RIGHT)
        assert ImageChops.difference(im, mirrored).getbbox() is None, \
            f"{S}px frame is not mirror-symmetric"
        if dump:
            im.save(os.path.join(dump, f"ArtDock-{S}.png"))

    write_ico(out, imgs)
    print(f"wrote {out}: {len(SIZES)} frames, all mirror-symmetric {SIZES}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
