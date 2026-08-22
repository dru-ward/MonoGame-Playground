"""Stitch play-test frames into one contact sheet (and optionally a GIF) for review.

usage: python tools/contact_sheet.py <frames_dir> [--cols 4] [--width 480] [--gif out.gif]
"""
import sys, os, glob
from PIL import Image, ImageDraw

def main():
    args = sys.argv[1:]
    if not args: print(__doc__); return
    d = args[0]; cols = 4; width = 480; gif = None
    i = 1
    while i < len(args):
        if args[i] == '--cols': cols = int(args[i + 1]); i += 2
        elif args[i] == '--width': width = int(args[i + 1]); i += 2
        elif args[i] == '--gif': gif = args[i + 1]; i += 2
        else: i += 1
    files = sorted(glob.glob(os.path.join(d, 'f*.png')))
    if not files: print('no frames'); return
    ims = []
    for f in files:
        im = Image.open(f).convert('RGB')
        # Crop away the HUD band at the top and the help text at the bottom; keep the centre of the frame.
        w, h = im.size
        im = im.crop((0, int(h * 0.10), w, int(h * 0.91)))
        im = im.resize((width, int(im.size[1] * width / w)))
        dr = ImageDraw.Draw(im)
        label = os.path.basename(f)[:-4].split('_', 1)[-1]
        dr.rectangle((0, 0, 8 + 7 * len(label), 14), fill=(0, 0, 0))
        dr.text((4, 1), label, fill=(255, 255, 255))
        ims.append(im)
    rows = (len(ims) + cols - 1) // cols
    sheet = Image.new('RGB', (cols * width, rows * ims[0].size[1]), (20, 20, 24))
    for k, im in enumerate(ims):
        sheet.paste(im, ((k % cols) * width, (k // cols) * im.size[1]))
    out = os.path.join(d, 'sheet.png')
    sheet.save(out)
    print('wrote', out, f'({len(ims)} frames)')
    if gif:
        ims[0].save(gif, save_all=True, append_images=ims[1:], duration=250, loop=0)
        print('wrote', gif)

if __name__ == '__main__':
    main()
