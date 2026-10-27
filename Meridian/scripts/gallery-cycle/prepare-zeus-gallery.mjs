import fs from 'node:fs';
import path from 'node:path';

// This assembles reviewed local authoring inputs only. It never calls a host
// write endpoint or changes a saved canvas. Images were obtained from the
// museum's official Open Access API for the requested local gallery.
const directory = path.resolve('output/gallery-qa-20261008/content-prep');
const source = JSON.parse(fs.readFileSync(path.join(directory, 'source-project.json'), 'utf8'));
const parentId = '005fe68a-bc30-4b11-af1d-4dadec799b1c';
const original = source.nodes.find(node => node.id === parentId);
if (!original || original.assetId !== 'b13d952e-7f7c-42d5-b449-20c3816ef565') throw new Error('Original Zeus image identity changed.');

const items = [
  {
    key: 'probable-zeus', objectId: 252511, file: 'zeus-original-252511.jpg',
    title: original.title, caption: original.text,
    catalogTitle: 'Marble head of a god, probably Zeus',
    context: 'Greek, Hellenistic; marble; 3rd or 2nd century BCE. The museum qualifies the identification as probably Zeus.',
    existingAssetId: original.assetId,
  },
  {
    key: 'zeus-and-giant', objectId: 248093, file: 'met-248093-view-3.jpg',
    title: 'Zeus and a giant',
    caption: 'Greek, Attic, ca. 450–440 BCE. Zeus appears in the lower band on the reverse of this red-figure calyx-krater. Attributed to the Nekyia Painter. Met 08.258.21.',
    catalogTitle: 'Terracotta calyx-krater (bowl for mixing wine and water)',
    context: 'The selected photograph shows the reverse. The museum identifies the lower reverse scene as Zeus and giant; the upper band depicts heroes in Hades.',
    selectedImageURL: 'https://images.metmuseum.org/CRDImages/gr/web-large/DP278309.jpg',
  },
  {
    key: 'roman-jupiter', objectId: 251215, file: 'met-251215.jpg',
    title: 'Jupiter in Roman bronze',
    caption: 'Roman, late 1st century CE. A bronze Jupiter holds a thunderbolt in his right hand; his raised left hand held a staff. Met 22.139.37.',
    catalogTitle: 'Bronze statuette of Jupiter',
    context: 'The museum describes the statuette as nude with thunderbolt in right hand and staff in left. The staff itself is not visible in the photograph.',
  },
  {
    key: 'ganymede-woodcut', objectId: 360017, file: 'met-360017.jpg',
    title: 'Zeus as an eagle',
    caption: 'Giovanni Battista Palumba, 1500–1510. Zeus carries Ganymede away in an eagle’s form. Woodcut; the museum notes this may be a later impression. Met 25.2.9.',
    catalogTitle: 'Zeus as an eagle, abducting Ganymede',
    context: 'The museum connects the scene to Virgil’s description of the abduction on Mount Ida in Aeneid 5.250–57.',
  },
  {
    key: 'renaissance-jupiter', objectId: 195182, file: 'met-195182.jpg',
    title: 'Jupiter with a thunderbolt',
    caption: 'Italian bronze, late 16th–early 17th century. Thunderbolt and eagle identify Jupiter. Possibly from Joseph de Levis’s workshop; attribution remains uncertain. Met 24.212.8.',
    catalogTitle: 'Jupiter with a thunderbolt',
    context: 'The museum catalog attributes it possibly to the workshop of Joseph de Levis, Italian, possibly Verona. This is a later interpretation, not an ancient Greek object.',
  },
];

const rightsURL = 'https://www.metmuseum.org/hubs/open-access';
const assets = [];
for (const item of items) {
  const metadata = JSON.parse(fs.readFileSync(path.join(directory, `met-${item.objectId}.json`), 'utf8'));
  if (metadata.isPublicDomain !== true) throw new Error(`Object ${item.objectId} is not marked public domain.`);
  const imagePath = path.join(directory, item.file);
  const bytes = fs.readFileSync(imagePath);
  if (bytes[0] !== 0xff || bytes[1] !== 0xd8) throw new Error(`Expected JPEG: ${imagePath}`);
  Object.assign(item, {
    imagePath, mime: 'image/jpeg', bytes: bytes.length,
    sourceURL: metadata.objectURL, accession: metadata.accessionNumber,
    imageURL: item.selectedImageURL ?? metadata.primaryImageSmall,
    creditLine: metadata.creditLine,
    rights: 'Public domain / The Met Open Access (CC0)', rightsURL,
    metadataPath: path.join(directory, `met-${item.objectId}.json`),
  });
  if (!item.existingAssetId) assets.push({key: `zeus-asset-${item.objectId}`, name: `zeus-met-${item.objectId}.jpg`, mime: item.mime, base64: bytes.toString('base64')});
}

const recipe = {
  version: 1,
  assets,
  nodes: items.map(item => ({
    key: `zeus-image-${item.objectId}`, canvas: original.canvasId, parent: parentId,
    kind: 'image', title: item.title, text: item.caption,
    asset: item.existingAssetId ?? `zeus-asset-${item.objectId}`,
    hyperlinks: [{url: item.sourceURL, label: 'Museum catalog'}, {url: rightsURL, label: 'The Met Open Access · CC0'}],
    x: original.x, y: original.y, width: original.width, height: original.height,
    style: {...original.style, imageFit: 'contain'},
  })),
};

const manifest = {
  version: 1, projectId: source.id, observedRevision: source.revision,
  canvasId: original.canvasId, nodeId: parentId, originalNode: original,
  originalAssetId: original.assetId, items,
  authoringNote: 'Use these inputs after converting the existing parent to a Gallery container while retaining its GUID and external relationships. Recipe alone only appends image children and does not convert parent or save. First item reuses the exact original asset and caption. Gallery display avoids plotting all children simultaneously.',
};
fs.writeFileSync(path.join(directory, 'zeus-gallery-manifest.json'), JSON.stringify(manifest, null, 2));
fs.writeFileSync(path.join(directory, 'zeus-gallery-items.recipe.json'), JSON.stringify(recipe, null, 2));
console.log(JSON.stringify({items: items.length, newAssets: assets.length, newImageBytes: items.filter(item => !item.existingAssetId).reduce((sum,item)=>sum+item.bytes,0), manifest: path.join(directory, 'zeus-gallery-manifest.json'), recipe: path.join(directory, 'zeus-gallery-items.recipe.json')}));
