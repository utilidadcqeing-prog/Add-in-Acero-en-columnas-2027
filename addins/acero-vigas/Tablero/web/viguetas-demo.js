// Solo para ver viguetas.html fuera de Revit (en un navegador): un piso de ejemplo de 2 × 2 paneles rectangulares y
// una copia simple del reparto del Excel. En Revit no se carga: el cálculo de verdad es el de C# (Calculo/Viguetas).
window.DEMO_VIGUETAS = () => {
  const xs = [0, 3870, 9070], ys = [0, 2300, 6280], anchoViga = 300;
  const armazon = [];
  ys.forEach((y, i) => armazon.push({ id: `h${i}`, desde: [xs[0], y], hasta: [xs[2], y], ancho: anchoViga, tipo: 'V30X40', esVigueta: false, creada: false }));
  xs.forEach((x, i) => armazon.push({ id: `v${i}`, desde: [x, ys[0]], hasta: [x, ys[2]], ancho: anchoViga, tipo: 'V30X40', esVigueta: false, creada: false }));
  const columnas = [];
  xs.forEach((x) => ys.forEach((y) => columnas.push({ id: `c${x}-${y}`, min: [x - 200, y - 200], max: [x + 200, y + 200] })));
  const paneles = [];
  [[0, 1], [1, 1], [0, 0], [1, 0]].forEach(([i, j], k) => {
    const [x0, x1, y0, y1] = [xs[i], xs[i + 1], ys[j], ys[j + 1]], m = anchoViga / 2;
    paneles.push({ id: `P${k + 1}`, eje: [[x0, y0], [x1, y0], [x1, y1], [x0, y1]], libre: [[x0 + m, y0 + m], [x1 - m, y0 + m], [x1 - m, y1 - m], [x0 + m, y1 - m]], creadas: 0 });
  });
  const piso = { tipo: 'piso', id: '1', nombre: 'N+3.70', cota: 3700, viguetasComoBorde: false, armazon, columnas, paneles };

  function propuesta(m) {
    const pan = paneles.find((p) => p.id === m.panel), enY = m.direccion === 'X', d = m.ejeAEje, b = m.ancho;
    const us = pan.libre.map((p) => (enY ? p[1] : p[0])), ws = pan.libre.map((p) => (enY ? p[0] : p[1]));
    const u0 = Math.min(...us), L = Math.max(...us) - u0, w0 = Math.min(...ws), w1 = Math.max(...ws);
    const ext = (n) => (L - ((n - 1) * d + b)) / 2;
    let n = Math.max(1, Math.floor(L / d));
    if (ext(n) > d - b) n++;
    const e = ext(n), punto = (c, w) => (enY ? [w, c] : [c, w]);
    const viguetas = [];
    for (let i = 0; i < n; i++) {
      const c = u0 + e + b / 2 + i * d, id = `${i + 1}`;
      viguetas.push({ id, cota: c, desde: punto(c, w0), hasta: punto(c, w1), largo: w1 - w0, tipo: 1, borrada: m.borradas.includes(id) });
    }
    const vivas = viguetas.filter((v) => !v.borrada).length;
    return { tipo: 'propuesta', panel: m.panel, propuesta: { direccion: m.direccion, ancho: b, libre: L, cantidad: n, ejeAEje: d, libreExtremos: e, libreMaximo: d - b, viguetas, tipos: vivas ? [{ numero: 1, largo: w1 - w0, cantidad: vivas }] : [] } };
  }

  return (m) => {
    if (m.tipo === 'listo') return [{ tipo: 'pisos', documento: 'Ejemplo (fuera de Revit)', pisos: [{ id: '1', nombre: 'N+3.70', cota: 3700, armazon: armazon.length }], tipos: [{ id: '10', nombre: 'Vigueta : VT_10X35', sugerido: true }] }];
    if (m.tipo === 'piso') return [piso];
    if (m.tipo === 'propuesta') return [propuesta(m)];
    if (m.tipo === 'crear') return [{ tipo: 'aviso', texto: 'Ejemplo: en Revit aquí se crearían las viguetas.', bien: true }];
    return [];
  };
};
