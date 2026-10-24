"""Renders the ColorLab comparison board. Usage: python3 tools/colorlab_board.py <colorlab-out-dir>"""
import json, sys, os
from PIL import Image, ImageDraw, ImageFont
out = sys.argv[1]; os.chdir(out)
d = json.load(open('results.json')); algos = d['algos']
def F(s, b=False):
    for f in ('/usr/share/fonts/opentype/inter/Inter-%s.otf' % ('Bold' if b else 'Regular'),
              '/usr/share/fonts/truetype/dejavu/DejaVuSans%s.ttf' % ('-Bold' if b else ''),
              '/System/Library/Fonts/Helvetica.ttc'):
        try: return ImageFont.truetype(f, s)
        except Exception: pass
    return ImageFont.load_default()
names = {'legacy': 'Before (v1)', 'accurate': 'Accurate', 'enhanced': 'Enhanced', 'cinematic': 'Cinematic', 'gaming': 'Gaming'}
TW, TH, SW, PAD = 256, 144, 144, 16; ROWH = TH + 40
width = PAD + TW + len(algos) * (SW + PAD) + PAD; height = 90 + len(d['scenes']) * (ROWH + PAD) + 70
img = Image.new('RGB', (width, height), (18, 18, 20)); dr = ImageDraw.Draw(img)
dr.text((PAD, 16), 'Color pipeline comparison on synthetic scenes', font=F(20, True), fill=(240, 240, 240))
dr.text((PAD, 46), 'Each 3x3 block shows the color sent to the light covering that part of the screen. Swatches are plain sRGB; real bulbs differ.', font=F(12), fill=(160, 160, 165))
x0 = PAD + TW + PAD
for i, a in enumerate(algos): dr.text((x0 + i * (SW + PAD), 68), names[a], font=F(14, True), fill=(255, 200, 90) if a == 'enhanced' else (255, 255, 255))
y = 92
for s in d['scenes']:
    img.paste(Image.open(f"scenes/{s['key']}.ppm").convert('RGB').resize((TW, TH)), (PAD, y))
    for k in (1, 2):
        dr.line([(PAD + k * TW // 3, y), (PAD + k * TW // 3, y + TH)], fill=(255, 255, 255)); dr.line([(PAD, y + k * TH // 3), (PAD + TW, y + k * TH // 3)], fill=(255, 255, 255))
    dr.text((PAD, y + TH + 4), s['title'], font=F(12, True), fill=(220, 220, 225))
    for i, a in enumerate(algos):
        m = s['algos'][a]; x = x0 + i * (SW + PAD)
        for z, c in enumerate(m['colors']):
            cx, cy = x + (z % 3) * SW // 3, y + (z // 3) * TH // 3
            dr.rectangle([cx, cy, cx + SW // 3 - 2, cy + TH // 3 - 2], fill=tuple(int(round(max(0, min(1, v)) * 255)) for v in c))
        dr.text((x, y + TH + 4), f"sat {m['meanSat']:.2f}   bright {m['meanV']:.2f}", font=F(11), fill=(170, 170, 175))
    y += ROWH + PAD
dr.text((PAD, y), 'sat = mean HSV saturation of lit lights, bright = mean HSV value over all nine. Scenes are procedurally generated, not real footage.', font=F(11), fill=(140, 140, 145))
img.save('board.png'); print(img.size)
