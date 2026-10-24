# Color fidelity report

## What made the output dull (found in the v1 code)
1. Output was raised to a power of 1.2 by default, which darkens every mid-tone (0.5 becomes 0.43).
2. Auto-gain could only dim. Its boost (up to 1.6x) was cut off by the brightness cap, so dark scenes stayed dark and bright scenes got dimmer.
3. Colors were averaged, then blended with the dominant color by at most 50%. A zone of gray with red accents came out brown-gray.
4. Saturation was multiplied by 1.0 by default. No lift for muted colors.
5. Smoothing worked on R, G and B separately. A fade from red to cyan passes through gray.
6. Frames were captured in the display's color space (Display P3 on most Macs) and treated as sRGB.
7. In Mirror mode the dominant colors of several zones were averaged, which mixes hues.

## What changed
- **Pick**: the zone average is blended toward its dominant hue by an amount that depends on how much colorful content the zone has (none for white or gray zones, strong for vivid ones). The dominant hue now merges neighboring hue bins, so a color on a bin edge isn't split.
- **Shape**: a saturation curve (fades out near gray so white stays white), a brightness curve that lifts dark tones, a mild S-curve for contrast, true black stays black, and auto-gain that can raise dark scenes.
- **Smooth in HSV**: hue moves along the shortest arc and is frozen in darkness; brightness keeps the strobe guard (rate limit); rise is faster than fall.
- **Output**: white balance (`warmth`), night and low-light caps, brightness ceiling.
- **Capture**: frames are requested in sRGB.

## Styles
| Style | Intent | Dominant weight | Saturation curve | Dark-tone lift | Warmth | Response |
|---|---|---|---|---|---|---|
| Accurate | True colors | 0.15 | none | none | 0 | normal |
| Enhanced (default) | Richer, more vivid | 0.65 | strong | medium | 0 | fast |
| Cinematic | Dramatic, slightly warm | 0.50 | mild | mild, more contrast | +0.22 | slow, long fall |
| Gaming | Aggressive, immediate | 0.85 | strongest | strong | 0 | very fast |

Sliders (Color intensity, Smoothness, Warmth, Dominant color, Tone curve) adjust any style. Profiles store the style. The Home page shows all four styles side by side on the live screen.

## Side-by-side evaluation
`docs/colorlab-board.png` shows 11 synthetic scenes (sunset, teal and orange film frame, neon in the dark, forest, explosion, document, ocean, game, mixed gray and red, muted skin tones, night city). Rebuild it with `swift run ColorLab <dir>` and `python3 tools/colorlab_board.py <dir>`.

Mean over the 11 scenes (3x3 zones, settled output):

| Pipeline | Saturation of lit lights | Brightness | Hue error vs. most prominent hue |
|---|---|---|---|
| Before (v1) | 0.62 | 0.40 | 5.4 deg |
| Accurate | 0.57 | 0.47 | 7.4 deg |
| Enhanced | 0.76 | 0.58 | 4.2 deg |
| Cinematic | 0.68 | 0.51 | 4.7 deg |
| Gaming | 0.84 | 0.62 | 3.5 deg |

Reading it: Enhanced raises saturation by about 23% and brightness by about 45% over v1 while keeping hue error below v1. Gaming is the most saturated and tracks the prominent hue best, at the cost of looking less natural on skin tones and documents. Accurate is the truest and, by design, the least vivid; it has the largest hue error because it averages mixed zones.

Limits of this evaluation: the scenes are procedural, not footage; swatches are plain sRGB, not what a bulb emits; "most immersive" is a perceptual judgment these numbers can't make. Enhanced is the default because it improves saturation, brightness and hue accuracy together. Final judgment needs your lights, which is why the comparison is live in the app.

## Requested items, status
| Item | Status |
|---|---|
| Sampling algorithm, per-zone dominant color | Done (neighbor-bin dominant, evidence-weighted pick) |
| Saturation preservation, vibrancy | Done (curve, no gray tinting) |
| Brightness scaling, dynamic range mapping | Done (tone curve, auto-gain both ways, black gate) |
| Gamma | Done (default 1.0, exposed as Tone curve) |
| White balance | Done as a warm/cool shift (`warmth`). Not calibrated per bulb model |
| Modes | Done (Accurate, Enhanced, Cinematic, Gaming) |
| HDR | Not implemented. Capture is SDR and macOS tone-maps HDR content. ScreenCaptureKit has `SCCaptureDynamicRange` from macOS 15, which could capture HDR values; mapping them to bulbs needs a separate tone-mapping stage and testing on an HDR display |
| OLED compensation | Not implemented separately. True blacks stay black (black gate), and wide-gamut primaries are handled by the sRGB capture. Tuning for a specific panel needs measurements from that panel |
| Wide gamut | Frames are converted to sRGB by macOS before analysis. Colors outside sRGB are clipped there |
