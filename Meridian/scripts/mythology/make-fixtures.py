"""Original, clearly labelled media fixtures for the Meridian mythology collection."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import json, math, struct, wave

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'content'/'greek-mythology'/'media'
OUT.mkdir(parents=True,exist_ok=True)
FONT=Path('C:/Windows/Fonts/segoeui.ttf')
BOLD=Path('C:/Windows/Fonts/segoeuib.ttf')
def font(size,bold=False): return ImageFont.truetype(str(BOLD if bold else FONT),size)
def base(title,subtitle):
    image=Image.new('RGB',(1200,800),'#101c2b');draw=ImageDraw.Draw(image)
    for x in range(30,1200,30):
        for y in range(30,800,30):draw.ellipse((x,y,x+1,y+1),fill='#25384c')
    draw.text((58,42),'ATLAS MERIDIAN  /  MYTHOLOGY',font=font(19,True),fill='#71dbc7')
    draw.text((58,91),title,font=font(43,True),fill='#f1e9d8')
    draw.text((60,155),subtitle,font=font(21),fill='#b1bdcd')
    draw.text((58,753),'Original teaching diagram, 2026. Schematic, not a geographic or historical reconstruction.',font=font(15),fill='#8298af')
    return image,draw

img,d=base('Four ways into the collection','Read across traditions, then return to the source and its particular version.')
boxes=[(65,230,'GENEALOGY','Descent, succession and variants'),(665,230,'EPIC','Anger, homecoming and narration'),(65,470,'HESIOD','Divine order, work and justice'),(665,470,'ROME','Correspondence and local identity')]
for x,y,title,sub in boxes:
    d.rounded_rectangle((x,y,x+465,y+175),radius=18,fill='#1b2e43',outline='#4e8f91',width=2)
    d.text((x+28,y+30),title,font=font(30,True),fill='#7ae1cf')
    d.text((x+28,y+88),sub,font=font(22),fill='#e0e7ef')
d.line((530,317,665,317),fill='#bba77f',width=4);d.line((530,557,665,557),fill='#bba77f',width=4)
d.line((297,405,297,470),fill='#bba77f',width=4);d.line((897,405,897,470),fill='#bba77f',width=4)
img.save(OUT/'reading-compass.png')

img,d=base('The Iliad has a deliberate boundary','Its story ends with the burial of Hector, before the sack of Troy.')
stages=[('ANGER','Books 1-8'),('NEGOTIATION','Books 9-15'),('LOSS','Books 16-19'),('RANSOM','Books 20-24')]
for i,(title,books) in enumerate(stages):
    x=55+i*287
    d.rounded_rectangle((x,295,x+250,545),radius=15,fill='#1b2e43',outline='#c3a971',width=2)
    d.text((x+22,330),str(i+1).zfill(2),font=font(49,True),fill='#68cfbf')
    d.text((x+22,419),title,font=font(23,True),fill='#f0e6d2')
    d.text((x+22,467),books,font=font(23),fill='#b9c8d8')
d.text((58,623),'The wooden horse and Achilles\' death belong to other parts of the Trojan tradition.',font=font(25),fill='#d0dce9')
img.save(OUT/'iliad-four-movements.webp',lossless=True)

frames=[]
for step in range(4):
    image,d=base('Odyssey reading order','The poem reveals the journey through delayed information and remembered adventures.')
    items=[('1-4','Telemachus searches'),('5-8','Odysseus reaches Phaeacia'),('9-12','Odysseus narrates earlier travels'),('13-24','Return, recognition and settlement')]
    for i,(books,text) in enumerate(items):
        y=225+i*112
        d.rounded_rectangle((65,y,1125,y+90),radius=12,fill='#255c57' if i==step else '#1b2d41',outline='#77dcca' if i==step else '#365168',width=2)
        d.text((92,y+24),books,font=font(26,True),fill='#eae1cb')
        d.text((252,y+24),text,font=font(26),fill='#e6edf3')
    frames.append(image)
frames[0].save(OUT/'odyssey-reading-order.gif',save_all=True,append_images=frames[1:],duration=1400,loop=0)

svg='''<svg xmlns="http://www.w3.org/2000/svg" width="900" height="480" viewBox="0 0 900 480">
<rect width="900" height="480" fill="#101c2b"/><g fill="#e5e5d9" font-family="sans-serif">
<text x="45" y="68" font-size="32">A source is a version, not a universal canon</text>
<text x="45" y="115" font-size="18">Original editable vector fixture for Atlas Meridian</text></g>
<g fill="#1c3847" stroke="#6ddac5" stroke-width="2"><rect x="45" y="175" width="240" height="145" rx="16"/>
<rect x="330" y="175" width="240" height="145" rx="16"/><rect x="615" y="175" width="240" height="145" rx="16"/></g>
<g fill="#e5e5d9" font-family="sans-serif" font-size="24" text-anchor="middle"><text x="165" y="240">Ancient passage</text>
<text x="450" y="240">Variant tradition</text><text x="735" y="240">Modern retelling</text></g>
<path d="M285 250H330M570 250H615" fill="none" stroke="#b5a172" stroke-width="4"/>
<text x="45" y="393" font-size="18" fill="#a8b9ce" font-family="sans-serif">No scripts, external resources, or embedded active content.</text></svg>'''
(OUT/'source-versions.svg').write_text(svg,encoding='utf-8')

rate=22050
notes=[220,261.626,293.665,329.628,392,329.628,293.665,220]
samples=[]
for frequency in notes:
    for n in range(int(rate*.42)):
        t=n/rate;env=min(1,t/.012)*math.exp(-6*t)
        value=.24*env*(math.sin(2*math.pi*frequency*t)+.25*math.sin(4*math.pi*frequency*t))
        samples.append(struct.pack('<h',round(max(-1,min(1,value))*32767)))
with wave.open(str(OUT/'original-plucked-motif.wav'),'wb') as audio:
    audio.setnchannels(1);audio.setsampwidth(2);audio.setframerate(rate);audio.writeframes(b''.join(samples))
(OUT/'reading-journal.md').write_text('''# Greek mythology reading journal

## A passage and its version
Record the ancient work and book or line range. What does this particular telling claim?

## A comparison
Describe one agreement and one difference between a Greek and Roman treatment.

## An image
Record the museum object number, date, culture and proposed identification. Does the image belong to antiquity or later reception?

## A Meridian observation
Canvas and node:
Steps taken:
Expected behavior:
Actual behavior:
Browser and viewport:

This original Markdown attachment is a reusable worksheet, not an ancient source.
''',encoding='utf-8')
fixtures=[
 ('reading-compass.png','image/png','Four reading paths','Original diagram. Native PNG image preview.'),
 ('iliad-four-movements.webp','image/webp','The Iliad boundary','Original lossless WebP diagram. Source: Iliad books 1-24; grouping is editorial.'),
 ('odyssey-reading-order.gif','image/gif','Odyssey reading order','Original animated GIF. Four narrative frames; not a geographic route.'),
 ('source-versions.svg','image/svg+xml','Source versions vector','Original script-free SVG. Download-only attachment in this release.'),
 ('original-plucked-motif.wav','audio/wav','Original audio motif','Original 3.36-second mono WAV. A media fixture, not reconstructed ancient music; download-only in this release.'),
 ('reading-journal.md','text/markdown','Editable reading journal','Original Markdown worksheet. Viewer text preview; editor may use download fallback.'),
]
(OUT.parent/'fixtures.json').write_text(json.dumps([{'file':f,'mime':m,'title':t,'description':s,'rights':'Original material created for this Meridian collection.'} for f,m,t,s in fixtures],indent=2),encoding='utf-8')
print('Created PNG, lossless WebP, animated GIF, SVG, WAV and Markdown fixtures.')
