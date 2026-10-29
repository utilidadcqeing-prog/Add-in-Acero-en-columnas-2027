// Uso: node tools/PruebasTableroColumnas/comparar-forma.js addins/armado-columnas/Tablero/web/demo.js tools/PruebasTableroColumnas/Datos/vista-c12.json
// Tarea 3 del plan docs/planes/2026-10-29-interfaz-web-columnas.md: que la maqueta y C# no se separen.
// Compara la forma (nombres de campo y tipos) de la vista de demo.js con la que arma C#.
const fs = require('fs'), path = require('path');
const [, , demoJs, vistaJson] = process.argv;
global.window = {};
eval(fs.readFileSync(demoJs, 'utf8'));
const demo = window.DemoColumnas.crear('normal').vista();
const cs = JSON.parse(fs.readFileSync(vistaJson, 'utf8'));
const tipo = (v) => (v === null ? 'null' : Array.isArray(v) ? 'array' : typeof v);
const dif = [];
function comparar(a, b, ruta) {
  const ta = tipo(a), tb = tipo(b);
  if (ta === 'null' || tb === 'null') return;
  if (ta !== tb) { dif.push(`${ruta}: demo ${ta}, C# ${tb}`); return; }
  if (ta === 'array') { if (a.length && b.length) comparar(a[0], b[0], ruta + '[0]'); return; }
  if (ta !== 'object') return;
  if (ruta.endsWith('.excepciones')) return;
  for (const k of Object.keys(a)) if (!(k in b)) dif.push(`${ruta}.${k}: falta en C#`);
  for (const k of Object.keys(b)) if (!(k in a)) dif.push(`${ruta}.${k}: sobra en C# (no está en demo)`);
  for (const k of Object.keys(a)) if (k in b) comparar(a[k], b[k], `${ruta}.${k}`);
}
comparar(demo, cs, 'vista');
console.log(dif.length ? dif.join('\n') : 'Misma forma.');
