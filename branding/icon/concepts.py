"""Renders the app icon concepts to PNG with headless Chromium. Usage: python3 concepts.py <outdir>"""
import sys, os
from playwright.sync_api import sync_playwright
out = sys.argv[1] if len(sys.argv) > 1 else 'out'; os.makedirs(out, exist_ok=True)

TILE = """<div class="tile">%s</div>"""
CSS = """
html,body{margin:0;background:transparent}
.wrap{width:1024px;height:1024px;position:relative}
.tile{position:absolute;left:100px;top:100px;width:824px;height:824px;border-radius:185px;overflow:hidden;
  box-shadow:0 18px 40px rgba(0,0,0,.45);}
.abs{position:absolute}
"""
concepts = {}

# A: Halo screen. A glass screen with colored light spilling out of every edge.
concepts['A_halo'] = """
<div class="tile" style="background:radial-gradient(120% 120% at 50% 0%,#1d1d2b 0%,#0a0a12 70%)">
  <div class="abs" style="left:112px;top:222px;width:600px;height:380px;border-radius:70px;background:conic-gradient(from 200deg at 50% 50%,#ff8a2a,#ff2a8a,#7a3cff,#18b8ff,#27e0a4,#ff8a2a);filter:blur(70px);opacity:.95"></div>
  <div class="abs" style="left:182px;top:262px;width:460px;height:300px;border-radius:52px;background:linear-gradient(180deg,#f4f6ff,#cfd6ee);box-shadow:0 0 0 10px rgba(255,255,255,.25)"></div>
  <div class="abs" style="left:182px;top:262px;width:460px;height:300px;border-radius:52px;background:linear-gradient(135deg,rgba(255,255,255,0) 40%,rgba(120,140,255,.35))"></div>
</div>"""

# B: Spectrum ring. A bold conic ring around a dark screen void with one white light.
concepts['B_ring'] = """
<div class="tile" style="background:radial-gradient(100% 100% at 50% 100%,#1b1624 0%,#09080e 75%)">
  <div class="abs" style="left:112px;top:112px;width:600px;height:600px;border-radius:50%;background:conic-gradient(from 210deg,#ff9b2f,#ff2f6d,#9b3dff,#2f7bff,#18d6c4,#7cf05a,#ff9b2f);
       -webkit-mask:radial-gradient(circle at 50% 50%,transparent 0,transparent 188px,#000 190px);mask:radial-gradient(circle at 50% 50%,transparent 0,transparent 188px,#000 190px)"></div>
  <div class="abs" style="left:112px;top:112px;width:600px;height:600px;border-radius:50%;background:conic-gradient(from 210deg,#ff9b2f,#ff2f6d,#9b3dff,#2f7bff,#18d6c4,#7cf05a,#ff9b2f);filter:blur(60px);opacity:.55;
       -webkit-mask:radial-gradient(circle at 50% 50%,transparent 0,transparent 200px,#000 202px);mask:radial-gradient(circle at 50% 50%,transparent 0,transparent 200px,#000 202px)"></div>
  <div class="abs" style="left:322px;top:322px;width:180px;height:180px;border-radius:50%;background:radial-gradient(circle,#fff 0,#fff 35%,rgba(255,255,255,0) 72%)"></div>
</div>"""

# C: L monogram built from light bars, warm to cool, with a light dot.
concepts['C_mono'] = """
<div class="tile" style="background:linear-gradient(160deg,#14121c,#07060b)">
  <svg class="abs" viewBox="0 0 824 824" width="824" height="824">
   <defs><linearGradient id="g" gradientUnits="userSpaceOnUse" x1="200" y1="680" x2="620" y2="150"><stop offset="0" stop-color="#ff7a1a"/><stop offset=".5" stop-color="#ff2f7a"/><stop offset="1" stop-color="#3a7bff"/></linearGradient>
   <filter id="b" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="28"/></filter></defs>
   <g filter="url(#b)" opacity=".8" stroke="url(#g)" stroke-width="96" stroke-linecap="round" stroke-linejoin="round" fill="none"><path d="M290 190 V634 H560"/></g>
   <g stroke="url(#g)" stroke-width="92" stroke-linecap="round" stroke-linejoin="round" fill="none"><path d="M290 190 V634 H560"/></g>
   <circle cx="552" cy="250" r="64" fill="#3a7bff" filter="url(#b)" opacity=".7"/><circle cx="552" cy="250" r="42" fill="url(#g)"/>
  </svg>
</div>"""

# D: Additive light. Three light discs mixing to white, on a dark tile.
concepts['D_mix'] = """
<div class="tile" style="background:radial-gradient(110% 110% at 50% 20%,#1a1a22,#06060a)">
  <div class="abs" style="left:150px;top:130px;width:360px;height:360px;border-radius:50%;background:#ff3b3b;mix-blend-mode:screen;filter:blur(2px)"></div>
  <div class="abs" style="left:314px;top:130px;width:360px;height:360px;border-radius:50%;background:#2bd45a;mix-blend-mode:screen;filter:blur(2px)"></div>
  <div class="abs" style="left:232px;top:268px;width:360px;height:360px;border-radius:50%;background:#2b6bff;mix-blend-mode:screen;filter:blur(2px)"></div>
</div>"""

# E: Edge light. A bold rounded-rect display outline lit by a spectrum gradient, one bright spark inside.
concepts['E_edge'] = """
<div class="tile" style="background:linear-gradient(180deg,#17151f,#08070c)">
  <div class="abs" style="left:70px;top:160px;width:684px;height:504px;border-radius:140px;background:conic-gradient(from 90deg,#ff8a1f,#ff2f6f,#8a3cff,#2f84ff,#17d3c0,#ff8a1f);filter:blur(60px);opacity:.55"></div>
  <div class="abs" style="left:130px;top:215px;width:564px;height:394px;border-radius:96px;background:conic-gradient(from 90deg,#ff8a1f,#ff2f6f,#8a3cff,#2f84ff,#17d3c0,#ff8a1f);filter:blur(30px);opacity:.8"></div>
  <div class="abs" style="left:150px;top:235px;width:524px;height:354px;border-radius:78px;background:conic-gradient(from 90deg,#ff8a1f,#ff2f6f,#8a3cff,#2f84ff,#17d3c0,#ff8a1f);"></div>
  <div class="abs" style="left:206px;top:291px;width:412px;height:242px;border-radius:30px;background:linear-gradient(180deg,#0c0b12,#15131e)"></div>
  <div class="abs" style="left:296px;top:326px;width:232px;height:172px;border-radius:50%;background:radial-gradient(circle,rgba(255,255,255,.95) 0,rgba(255,255,255,.55) 22%,rgba(255,255,255,0) 68%)"></div>
</div>"""

page = lambda body: f"<html><head><style>{CSS}</style></head><body><div class='wrap'>{body}</div></body></html>"
with sync_playwright() as p:
    b = p.chromium.launch(); pg = b.new_page(viewport={'width': 1024, 'height': 1024})
    for k, v in concepts.items():
        pg.set_content(page(v)); pg.wait_for_timeout(150)
        pg.screenshot(path=f'{out}/{k}.png', omit_background=True)
    b.close()
print('ok')
