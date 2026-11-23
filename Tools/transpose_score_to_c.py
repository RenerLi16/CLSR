#!/usr/bin/env python3
from pathlib import Path

import cv2
import numpy as np


ROOT = Path("/Users/renerli/Documents/GitHub/CLSR")
OUT = ROOT / "transposed_to_C"

PAGES = [
    ("/Users/renerli/Pictures/Screenshots/SCR-20261122-tkmz.png", "Night Journey in C - page 1.png"),
    ("/Users/renerli/Pictures/Screenshots/SCR-20261122-tkol.png", "Night Journey in C - page 2.png"),
    ("/Users/renerli/Pictures/Screenshots/SCR-20261122-tkpw.png", "Night Journey in C - page 3.png"),
    ("/Users/renerli/Pictures/Screenshots/SCR-20261122-tkqz.png", "Night Journey in C - page 4.png"),
    ("/Users/renerli/Pictures/Screenshots/SCR-20261122-tksa.png", "Night Journey in C - page 5.png"),
]

# Top-left pixel of each printed natural glyph. The transposition C-sharp major
# to C major changes every one of these cancellations into a flat.
NATURALS = {
    0: [(481, 1554), (1372, 1454)],
    1: [(1070, 87)],
    2: [(1224, 158), (1268, 207), (1034, 404), (1074, 1077), (1347, 1873)],
    3: [(794, 124), (799, 957)],
    4: [],
}


def staff_lines(image: np.ndarray) -> list[list[int]]:
    """Return each grand staff as ten center-line y coordinates."""
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    dark_counts = (gray < 128).sum(axis=1)
    candidates = np.where(dark_counts > gray.shape[1] * 0.82)[0]

    groups: list[list[int]] = []
    for y in candidates:
        if not groups or y > groups[-1][-1] + 1:
            groups.append([int(y)])
        else:
            groups[-1].append(int(y))

    centers = [round(sum(group) / len(group)) for group in groups]
    if len(centers) % 10:
        raise RuntimeError(f"Expected complete grand staffs; found {len(centers)} lines")
    return [centers[i : i + 10] for i in range(0, len(centers), 10)]


def restore_line_profiles(
    edited: np.ndarray,
    original: np.ndarray,
    x0: int,
    x1: int,
    lines: list[int],
    sample_x0: int,
    sample_x1: int,
) -> None:
    """Restore the score's antialiased staff-line profile across an erased area."""
    for y in lines:
        for dy in range(-2, 3):
            yy = y + dy
            profile = np.median(original[yy, sample_x0:sample_x1], axis=0).astype(np.uint8)
            edited[yy, x0:x1] = profile


def remove_key_signatures(edited: np.ndarray, original: np.ndarray, systems: list[list[int]]) -> None:
    gray = cv2.cvtColor(original, cv2.COLOR_BGR2GRAY)
    for lines in systems:
        top_line = lines[0]
        # At an unobstructed staff row, the first dark pixel is the system's
        # left edge. In this engraving the key signature begins 64 px later.
        x_left = int(np.where(gray[top_line] < 128)[0].min())
        x0 = x_left + 61
        x1 = x_left + 163

        for staff in (lines[:5], lines[5:]):
            y0 = staff[0] - 31
            y1 = staff[-1] + 31
            edited[y0:y1, x0:x1] = 255
            restore_line_profiles(
                edited,
                original,
                x0,
                x1,
                staff,
                x1 + 5,
                min(x1 + 25, original.shape[1]),
            )


def load_flat_glyph() -> np.ndarray:
    rendered = cv2.imread("/tmp/bravura-flat.png", cv2.IMREAD_GRAYSCALE)
    if rendered is None:
        raise RuntimeError("Run tools/render_flat.swift before this script")
    ys, xs = np.where(rendered < 250)
    return rendered[ys.min() : ys.max() + 1, xs.min() : xs.max() + 1]


def nearest_staff_lines(y: int, systems: list[list[int]]) -> list[int]:
    staffs = [lines[:5] for system in systems for lines in (system,)] + [
        lines[5:] for system in systems for lines in (system,)
    ]
    return min(staffs, key=lambda staff: abs(np.mean(staff) - y))


def natural_to_flat(
    edited: np.ndarray,
    original: np.ndarray,
    natural_xy: tuple[int, int],
    systems: list[list[int]],
    glyph: np.ndarray,
) -> None:
    x, y = natural_xy
    staff = nearest_staff_lines(y + 17, systems)

    # Remove only the natural glyph, then put back any staff lines it crossed.
    erase_x0, erase_x1 = x - 2, x + 12
    erase_y0, erase_y1 = y - 2, y + 38
    edited[erase_y0:erase_y1, erase_x0:erase_x1] = 255
    restore_line_profiles(
        edited,
        original,
        erase_x0,
        erase_x1,
        staff,
        max(0, erase_x0 - 18),
        max(1, erase_x0 - 4),
    )

    # The flat's bulb is aligned to the same note position as the natural.
    gx = x - 4
    gy = y - 10
    h, w = glyph.shape
    area = edited[gy : gy + h, gx : gx + w]
    for channel in range(3):
        area[:, :, channel] = np.minimum(area[:, :, channel], glyph)


def main() -> None:
    OUT.mkdir(exist_ok=True)
    flat = load_flat_glyph()

    for page_index, (source_path, output_name) in enumerate(PAGES):
        original = cv2.imread(source_path, cv2.IMREAD_COLOR)
        if original is None:
            raise RuntimeError(f"Unable to open {source_path}")
        edited = original.copy()
        systems = staff_lines(original)

        remove_key_signatures(edited, original, systems)
        for natural in NATURALS[page_index]:
            natural_to_flat(edited, original, natural, systems, flat)

        output_path = OUT / output_name
        if not cv2.imwrite(str(output_path), edited):
            raise RuntimeError(f"Unable to write {output_path}")
        print(output_path)


if __name__ == "__main__":
    main()
