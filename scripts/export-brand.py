#!/usr/bin/env python3
"""Generate local Llumi icon resources. Requires macOS/AppKit; no external requests."""
from pathlib import Path
import shutil, struct, subprocess, tempfile
root = Path(__file__).resolve().parents[1]
with tempfile.TemporaryDirectory() as temporary:
    iconset = Path(temporary)/'Llumi.iconset'
    subprocess.run(['swift',str(root/'macos/Scripts/make-icon.swift'),str(iconset)],check=True)
    subprocess.run(['iconutil','-c','icns',str(iconset),'-o',str(root/'macos/AgentMeter/Resources/Llumi.icns')],check=True)
    sizes=[16,20,24,32,40,48,64,128,256]
    frames=[(iconset/f'windows-{size}.png').read_bytes() for size in sizes]
    offset=6+16*len(sizes)
    data=struct.pack('<HHH',0,1,len(sizes))
    for size,png in zip(sizes,frames):
        data+=struct.pack('<BBBBHHII',size%256,size%256,0,0,1,32,len(png),offset)
        offset+=len(png)
    (root/'windows/src/AgentMeter/Assets/Llumi.ico').write_bytes(data+b''.join(frames))
    for variant in ('dark','light','mono-dark','mono-light'):
        shutil.copyfile(iconset/f'{variant}.png',root/f'assets/brand/llumi-{variant}.png')
