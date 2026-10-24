"""Builds an .icns (PNG payloads) from a 1024 px PNG. Usage: python3 make_icns.py AppIcon-1024.png ../../Resources/AppIcon.icns"""
import sys, io, struct
from PIL import Image
src = Image.open(sys.argv[1]).convert('RGBA')
# (type, pixel size): the @2x types reuse the next size up
entries = [('icp4', 16), ('icp5', 32), ('icp6', 64), ('ic07', 128), ('ic08', 256), ('ic09', 512), ('ic10', 1024),
           ('ic11', 32), ('ic12', 64), ('ic13', 256), ('ic14', 512)]
body = b''
for t, px in entries:
    buf = io.BytesIO(); src.resize((px, px), Image.LANCZOS).save(buf, 'PNG', optimize=True)
    body += t.encode() + struct.pack('>I', len(buf.getvalue()) + 8) + buf.getvalue()
open(sys.argv[2], 'wb').write(b'icns' + struct.pack('>I', len(body) + 8) + body)
print(len(body) + 8, 'bytes')
