import { build } from 'esbuild';
import { readFile, writeFile, readdir } from 'node:fs/promises';
import path from 'node:path';

await build({entryPoints:['case-notes.js'], bundle:true, format:'esm', minify:true,
  outfile:'../wwwroot/js/case-notes.js', target:['es2022'], legalComments:'eof'});

// Preserve upstream notices for all installed packages, including build-only dependencies.
const notices = ['LandErp C-01 editor — bundled open-source dependency notices\n'];
async function visit(dir) {
  for (const item of (await readdir(dir, {withFileTypes:true})).sort((a,b)=>a.name.localeCompare(b.name))) {
    if (!item.isDirectory() || item.name.startsWith('.')) continue;
    const root=path.join(dir,item.name);
    if (item.name.startsWith('@')) { await visit(root); continue; }
    const pkg=JSON.parse(await readFile(path.join(root,'package.json'),'utf8'));
    if (pkg.license !== 'MIT') throw new Error(`Unreviewed license: ${pkg.name} ${pkg.license}`);
    notices.push(`\n=== ${pkg.name} ${pkg.version} (${pkg.license}) ===\n`);
    const licenses=(await readdir(root)).filter(n=>/^(license|copying|notice)(\.|$)/i.test(n));
    if (!licenses.length && pkg.name.startsWith("@esbuild/")) { notices.push(await readFile("node_modules/esbuild/LICENSE.md","utf8")); continue; }
    if (!licenses.length) throw new Error(`Missing notice: ${pkg.name}`);
    for (const license of licenses) notices.push(await readFile(path.join(root,license),'utf8'));
  }
}
await visit('node_modules');
await writeFile('../wwwroot/js/case-notes.LICENSE.txt',notices.join('\n'));
