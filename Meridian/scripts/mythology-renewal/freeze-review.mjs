/** Bind the final visually reviewed documents to a concrete, locally stored manifest. */
import {writeJSON,OUT,readJSON} from './shared.mjs';
import {createReviewManifest,canonicalSha256} from './apply-reviewed.mjs';
import path from 'node:path';
const manifest=await createReviewManifest();
const proof=await readJSON(path.join(OUT,'content-verification-final.json'));
await writeJSON(path.join(OUT,'reviewed-content.json'),manifest);
await writeJSON(path.join(OUT,'reviewed-content-hash.json'),{canonicalization:manifest.canonicalization,sha256:canonicalSha256(manifest)});
console.log(JSON.stringify({manifest:path.join(OUT,'reviewed-content.json'),sha256:canonicalSha256(manifest),projects:manifest.projects.map(p=>({key:p.key,revision:p.baselineRevision,candidateCanonicalSha256:p.candidateSha256})),proofKeys:Object.keys(proof)}));
