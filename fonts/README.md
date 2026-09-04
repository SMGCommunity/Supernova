# Bundled fonts

Drop font files here to override the fonts Supernova loads, on any platform:

| File | Used for |
|------|----------|
| `ui.ttf` / `ui.otf` / `ui.ttc` | The main interface font |
| `cjk.otf` / `cjk.ttf` / `cjk.ttc` | Japanese glyphs (BCSV `PosName`, object text, etc.) |

Anything placed here is copied next to the built executable and checked first.

If these are absent, Supernova falls back to system fonts:

- **Windows** - Segoe UI + Yu Gothic (always present).
- **Linux** - DejaVu / Liberation / Noto Sans for the UI, and Noto Sans CJK for
  Japanese. Install `fonts-noto-cjk` (Debian/Ubuntu) or `noto-fonts-cjk` (Arch) if
  Japanese text shows as boxes.
- **macOS** - Arim/SF for the UI, Hiragino for Japanese.

Font files are not committed to the repository; add your own here if you want a
consistent look across machines.
