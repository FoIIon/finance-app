// Génère les icônes PWA à partir d'un dessin unique (fond plein + « F » blanc).
// Rejouable : node scripts/make-icons.mjs
// Les PNG produits sont commités, le build du Pi n'a pas à les régénérer.
import sharp from 'sharp';
import { writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const publicDir = resolve(dirname(fileURLToPath(import.meta.url)), '../public');

// Couleurs lues dans l'app : #1a1a3e (Layout.tsx, tiroir mobile et fond
// atmosphérique). Le glyphe est blanc pour rester lisible à 48 px.
const BACKGROUND = '#1a1a3e';
const GLYPH = '#ffffff';

// Un « F » géométrique dessiné en rectangles : aucune police à embarquer,
// le rendu est identique quel que soit le poste qui rasterise.
// `glyphScale` réduit le glyphe (zone de sécurité des icônes maskable),
// `radius` arrondit le carré (icônes « any », qu'Android affiche telles quelles).
function svg({ size = 512, radius = 0, glyphScale = 1 } = {}) {
  const half = size / 2;
  const bar = 0.14 * size; // épaisseur des traits
  const h = 0.56 * size;   // hauteur du F
  const w = 0.46 * size;   // largeur de la barre haute
  const x = half - w / 2;
  const y = half - h / 2;
  const glyph = `
    <g transform="translate(${half} ${half}) scale(${glyphScale}) translate(${-half} ${-half})" fill="${GLYPH}">
      <rect x="${x}" y="${y}" width="${bar}" height="${h}" />
      <rect x="${x}" y="${y}" width="${w}" height="${bar}" />
      <rect x="${x}" y="${y + 0.42 * h - bar / 2}" width="${0.78 * w}" height="${bar}" />
    </g>`;
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${size} ${size}" width="${size}" height="${size}">
  <rect width="${size}" height="${size}" rx="${radius}" fill="${BACKGROUND}" />${glyph}
</svg>
`;
}

const targets = [
  // Source vectorielle (favicon) : plein bord, glyphe à taille normale.
  { file: 'icon.svg', svg: svg() },
  // purpose "any" : coins arrondis, le lanceur n'y touche pas.
  { file: 'icon-192.png', svg: svg({ radius: 512 * 0.18 }), size: 192 },
  { file: 'icon-512.png', svg: svg({ radius: 512 * 0.18 }), size: 512 },
  // purpose "maskable" : fond jusqu'aux bords, glyphe dans les 80 % centraux.
  { file: 'icon-512-maskable.png', svg: svg({ glyphScale: 0.8 }), size: 512 },
  // iOS : coins carrés, c'est le système qui arrondit.
  { file: 'apple-touch-icon.png', svg: svg(), size: 180 },
];

for (const { file, svg: markup, size } of targets) {
  const out = resolve(publicDir, file);
  if (!size) {
    writeFileSync(out, markup);
  } else {
    await sharp(Buffer.from(markup)).resize(size, size).png().toFile(out);
  }
  console.log(`${file} écrit`);
}
