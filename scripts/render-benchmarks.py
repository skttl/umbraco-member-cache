"""Render the measured package comparison and its usage examples; requires Pillow."""
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "docs/screenshots"
NAVY, PANEL, IVORY, MUTED, MINT, CORAL = "#142D38", "#0E222B", "#F4F1E8", "#AFBFC3", "#22C7A9", "#FF7964"


def font(size, bold=False, mono=False):
    name = "consola.ttf" if mono else "segoeuib.ttf" if bold else "segoeui.ttf"
    path = Path("C:/Windows/Fonts") / name
    if not path.exists():
        name = "DejaVuSansMono.ttf" if mono else "DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf"
        path = Path("/usr/share/fonts/truetype/dejavu") / name
    return ImageFont.truetype(str(path), size)


def text(draw, xy, value, size=27, color=IVORY, bold=False, mono=False):
    assert draw.textlength(value, font=font(size, bold, mono)) <= 1944 - xy[0], value
    draw.text(xy, value, font=font(size, bold, mono), fill=color)


def canvas(title, subtitle):
    image = Image.new("RGB", (2000, 1200), NAVY)
    draw = ImageDraw.Draw(image)
    draw.rectangle((56, 50, 72, 90), fill=MINT)
    text(draw, (94, 48), "Umbraco.Community.MemberCache", 31, bold=True)
    text(draw, (56, 117), title, 49, bold=True)
    text(draw, (58, 190), subtitle, 26, MUTED)
    return image, draw


def comparison():
    image, draw = canvas("Member Picker: before and after", "Same Razor call. Same selected member value.")
    for x, label, title, color in [(460, "BEFORE", "Standard Umbraco picker", CORAL), (1190, "AFTER", "With Member Cache", MINT)]:
        text(draw, (x, 278), label, 31, color, bold=True)
        text(draw, (x, 325), title, 33, bold=True)
    for y, version in [(400, "17.0.1"), (650, "18.0.0")]:
        report = json.loads((ROOT / f"docs/benchmarks/package/umbraco{version}.json").read_text(encoding="utf-8-sig"))
        rows = {row["Method"]: row for row in report["Benchmarks"]}
        assert set(rows) == {"CorePicker", "CachedPicker"}
        assert all(row["Statistics"]["N"] >= 10 and row["Statistics"]["Mean"] > 0 for row in rows.values())
        before = rows["CorePicker"]["Statistics"]["Mean"] / 1000
        after = rows["CachedPicker"]["Statistics"]["Mean"] / 1000
        draw.rounded_rectangle((56, y, 1944, y + 218), radius=22, fill=PANEL)
        text(draw, (86, y + 38), "Umbraco " + version.split(".")[0], 35, bold=True)
        text(draw, (86, y + 95), "Version " + version, 24, MUTED)
        text(draw, (460, y + 25), f"{before:.3f} µs", 65, bold=True)
        text(draw, (460, y + 117), "Read picker and member value", 25, MUTED)
        text(draw, (1010, y + 45), "→", 58, MUTED)
        text(draw, (1190, y + 25), f"{after:.3f} µs", 65, MINT, bold=True)
        text(draw, (1190, y + 117), "Read picker and cached member value", 25, MUTED)
        text(draw, (1190, y + 161), f"{(1 - after / before) * 100:.1f}% less time", 28, MINT, bold=True)
    for y, line, size in [
        (915, "Repeated reads on a published document. Lower is better. 1,000 µs = 1 millisecond.", 28),
        (973, "Measured with the actual NuGet packages, including the automatic Member Picker converter.", 25),
        (1018, "Local SQLite and warm cache. The separate 17.0.0 minimum-version result is in the full report.", 25),
        (1063, "BenchmarkDotNet 0.15.8 • 5 warmups / 15 measurement iterations • Full errors and allocations retained.", 25),
        (1108, "This measures one picker read, not page rendering, cold cache, Redis or load-balanced throughput.", 25),
    ]:
        text(draw, (58, y), line, size, IVORY if y == 915 else MUTED)
    return image


def code_image(picker):
    title = "Member Picker: keep your existing Razor" if picker else "Member lookup: before and after"
    image, draw = canvas(title, "Install the package major that matches your Umbraco version.")
    razor = 'var member = Model.Value<IPublishedContent>(\n    "member");\n\nvar title = member?.Value<string>(\n    "profileTitle");'
    before = razor if picker else 'var entity = memberService.GetById(id);\n\nvar published = entity is null ? null :\n    await publishedMembers.GetAsync(entity);\n\nvar member = published is null ? null :\n    (IPublishedContent)modelFactory\n        .CreateModel(published);'
    after = '// Install Umbraco.Community.MemberCache.\n// The composer registers it automatically.\n\n' + razor if picker else 'var member = await memberLookup\n    .GetByIdAsync(id);\n\nvar named = await memberLookup\n    .GetByUsernameAsync(username);\n\nvar many = await memberLookup\n    .GetByIdsAsync(ids);'
    for x, label, code, color in [(56, "BEFORE", before, CORAL), (1022, "AFTER", after, MINT)]:
        draw.rounded_rectangle((x, 280, x + 922, 1040), radius=22, fill=PANEL)
        text(draw, (x + 30, 310), label, 31, color, bold=True)
        for i, line in enumerate(code.splitlines()):
            assert draw.textlength(line, font=font(25, mono=True)) < 862
            text(draw, (x + 30, 390 + i * 43), line, 25, mono=True)
    text(draw, (58, 1085), "Existing Razor stays unchanged; direct lookups use the injected IPublishedMemberLookup service.", 26, MUTED)
    text(draw, (58, 1135), "Batches preserve order and duplicates and omit missing members. Delivery API member output stays unsupported.", 24, MUTED)
    return image


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for name, image in [("picker-converter-results.png", comparison()), ("member-picker-before-after.png", code_image(True)), ("member-lookup-before-after.png", code_image(False))]:
        assert image.size == (2000, 1200)
        image.save(OUT / name)
        print(OUT / name)
