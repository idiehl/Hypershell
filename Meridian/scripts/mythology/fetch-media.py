"""Fetch selected Met Open Access images without changing their original bytes."""
from pathlib import Path
import hashlib, json, urllib.request

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'content' / 'greek-mythology' / 'media'
OUT.mkdir(parents=True, exist_ok=True)
objects = [
    ('zeus', 252511, 'A probable identification, not a certain portrait of Zeus. The museum preserves that uncertainty.'),
    ('circe', 253627, 'An Attic vase shows Odysseus pursuing Circe. Ancient images are interpretations of myth, not photographs of events.'),
    ('jupiter', 246539, 'A Roman statuette catalogued as Zeus or Jupiter makes the shared visual vocabulary and uncertain naming visible.'),
    ('persephone', 252973, 'Persephone rises from the earth, accompanied by Hermes and Hekate, toward Demeter. Compare the image with the Homeric Hymn to Demeter.'),
    ('thetis', 239234, 'William Theed the Elder imagined Thetis carrying arms for Achilles centuries after antiquity. Read it as modern reception of the epic.'),
    ('athena', 244457, 'A small Roman bronze Athena wears a helmet and aegis. Museum culture and depicted Greek identity need not have identical labels.'),
]
records = []
for key, oid, caption in objects:
    metadata_file = OUT / f'met-{oid}.json'
    if metadata_file.exists():
        data = json.loads(metadata_file.read_text(encoding='utf-8'))
    else:
        request = urllib.request.Request(f'https://collectionapi.metmuseum.org/public/collection/v1/objects/{oid}', headers={'User-Agent':'AtlasMeridianResearch/0.1'})
        with urllib.request.urlopen(request, timeout=40) as response: data=json.load(response)
        metadata_file.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
    if not data.get('isPublicDomain') or not data.get('primaryImageSmall'):
        raise RuntimeError(f'Object {oid} is not a verified available public-domain image')
    filename = f'{key}-met-{oid}.jpg'
    destination = OUT / filename
    if not destination.exists():
        with urllib.request.urlopen(data['primaryImageSmall'], timeout=60) as response: destination.write_bytes(response.read())
    records.append({
        'key':key, 'file':filename, 'mime':'image/jpeg', 'title':data['title'],
        'caption':caption, 'date':data.get('objectDate',''), 'culture':data.get('culture',''),
        'artist':data.get('artistDisplayName',''), 'accession':data.get('accessionNumber',''),
        'credit':data.get('creditLine',''), 'url':data['objectURL'],
        'imageUrl':data['primaryImageSmall'], 'rights':'The Metropolitan Museum of Art Open Access; public domain / CC0.',
        'sha256':hashlib.sha256(destination.read_bytes()).hexdigest(), 'bytes':destination.stat().st_size,
    })
(OUT.parent/'media.json').write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps([{'file':r['file'],'bytes':r['bytes'],'rights':r['rights']} for r in records],indent=2))
