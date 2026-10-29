// Datos de demostración del tablero (#83, decisión 8 del plan docs/planes/2026-10-16-tablero-vigas-83.md): una
// VistaTablero con los números que pinta la maqueta (versión 19), para ver y comparar la página fuera de Revit. Solo se
// carga sin window.chrome.webview. Las cuentas son las de la maqueta (Main.dc.html y Beam.dc.html), para que un cambio
// en los pasos 1 a 3 responda como allá; en Revit todo esto lo calcula el motor.
(function () {
  'use strict';
  const f2 = (n) => (Math.round(n * 100) / 100).toFixed(2);
  const DIAM = { 3: 0.95, 4: 1.27, 5: 1.59, 6: 1.91, 7: 2.22, 8: 2.54 };
  const RULES0 = [
    { d: '#3', t: '0.55', g: '0.166', g180: '0.142', e: '0.113', k: '0.560' }, { d: '#4', t: '0.75', g: '0.222', g180: '0.190', e: '0.151', k: '0.994' },
    { d: '#5', t: '0.90', g: '0.278', g180: '0.238', e: '0.189', k: '1.552' }, { d: '#6', t: '1.10', g: '0.334', g180: '0.286', e: '0.272', k: '2.235' },
    { d: '#7', t: '1.35', g: '0.388', g180: '0.333', e: '0.316', k: '3.042' }, { d: '#8', t: '1.70', g: '0.444', g180: '0.381', e: '0.362', k: '3.973' },
  ];
  // un color por diámetro, como ColoresDiametro en C# (#100)
  const COLOR = { 3: '#FBBC04', 4: '#4285F4', 5: '#EA4335', 6: '#34A853', 7: '#9334E6', 8: '#8B4513', 9: '#E91E63', 10: '#000000', 11: '#FF6D01' };
  const colorDe = (d) => COLOR[d] || '#5E6670';
  const OPCIONES0 = {
    tipo: null, elemento: null, superior: [{ cantidad: 3, diametro: 5 }], inferior: [{ cantidad: 3, diametro: 5 }], recubrimientoCm: 4,
    recArribaCm: null, recAbajoCm: null, recLadosCm: null, separacionFilasCm: null, maximoFilas: 3, cara: null,
    bastones: { superior: null, inferior: null, largo: 2000, conGancho: false, largos: {} },
    estribos: { diametro: 3, gancho: 'De135', largoZona: 800, separacionZona: 100, separacionCentro: 200 },
    tramosMarcados: [], estribosMarcados: null,
  };

  // El pórtico de la maqueta: ejes A a D y voladizo, en m desde el eje A (Beam.dc.html: C, F, st y g).
  const MAQUETA = {
    inicio: -0.20, fin: 14.0,
    apoyos: [['A', 0, -0.20, 0.20], ['B', 4.40, 4.20, 4.60], ['C', 7.90, 7.70, 8.10], ['D', 12.30, 12.10, 12.50]],
    tramos: [['A-B', 'Viga A-B', 0, 4.40], ['B-C', 'Viga B-C', 4.40, 7.90], ['C-D', 'Viga C-D', 7.90, 12.30], ['Voladizo D', 'Voladizo D', 12.30, 14.0]],
    luces: [['A-B', 0.20, 4.20], ['B-C', 4.60, 7.70], ['C-D', 8.10, 12.10], ['Voladizo D', 12.50, 13.95, true]],
    c: [{ a: -0.15, b: 2.65, hs: true, len: 3.0, recta: '2.80 + gancho' }, { a: 1.65, b: 6.65, len: 5.0, recta: 'recta' },
      { a: 5.65, b: 11.15, len: 5.5, recta: 'recta' }, { a: 10.15, b: 13.95, he: true, len: 4.0, recta: '3.80 + gancho', vol: true }],
    f: {
      izq: [{ a: -0.15, b: 5.65, hs: true, len: 6.0, recta: '5.80 + gancho' }, { a: 4.65, b: 9.15, len: 4.5, recta: 'recta' },
        { a: 8.15, b: 13.95, he: true, len: 6.0, recta: '5.80 + gancho', vol: true }],
      der: [{ a: -0.15, b: 4.15, hs: true, len: 4.5, recta: '4.30 + gancho' }, { a: 3.15, b: 7.65, len: 4.5, recta: 'recta' },
        { a: 6.55, b: 12.05, len: 5.5, recta: 'recta' }, { a: 10.65, b: 13.95, he: true, len: 3.5, recta: '3.30 + gancho', vol: true }],
    },
    sumF: { izq: 16.5, der: 18.0 },
    columnas: [[0, 'A'], [4.40, 'B'], [7.90, 'C'], [12.30, 'D']],
    centros: [[2.20, 'A-B'], [6.15, 'B-C'], [10.10, 'C-D']],
    zonas: [[0.25, 1.00, 1], [1.00, 3.40, 0], [3.40, 4.15, 1], [4.65, 5.40, 1], [5.40, 6.90, 0], [6.90, 7.65, 1],
      [8.15, 8.90, 1], [8.90, 11.30, 0], [11.30, 12.05, 1], [12.55, 13.25, 1], [13.25, 13.95, 0]],
  };

  // ?estado=largo: 8 luces de 5 m sin voladizo, para revisar que nada se encime (punto 2 de "Lo que más puede fallar").
  function largo() {
    const ejes = 'ABCDEFGHI'.split('');
    const apoyos = ejes.map((e, i) => [e, 5 * i, 5 * i - 0.20, 5 * i + 0.20]);
    const tramos = ejes.slice(1).map((e, i) => [ejes[i] + '-' + e, 'Viga ' + ejes[i] + '-' + e, 5 * i, 5 * (i + 1)]);
    const luces = tramos.map((t, i) => [t[0], 5 * i + 0.20, 5 * (i + 1) - 0.20]);
    // arriba traslapa al centro de cada luz; abajo, junto a la columna izquierda de cada luz; ninguna pasa de 6 m
    const capa = (laps) => {
      const puntas = [-0.15].concat(...laps.map(([a, b]) => [b, a]), 39.95);
      const n = puntas.length / 2;
      return Array.from({ length: n }, (_, i) => {
        const a = puntas[2 * i], b = puntas[2 * i + 1], borde = i === 0 || i === n - 1;
        return { a, b, hs: i === 0, he: i === n - 1, len: b - a + (borde ? 0.2 : 0), recta: borde ? f2(b - a) + ' + gancho' : 'recta' };
      });
    };
    const c = capa(Array.from({ length: 8 }, (_, i) => [5 * i + 2.0, 5 * i + 3.0]));
    const f = capa(Array.from({ length: 8 }, (_, i) => [5 * i + 0.25, 5 * i + 1.25]));
    const sum = f.reduce((s, x) => s + x.len, 0);
    const zonas = [];
    luces.forEach((l) => { zonas.push([l[1] + 0.05, l[1] + 0.80, 1], [l[1] + 0.80, l[2] - 0.80, 0], [l[2] - 0.80, l[2] - 0.05, 1]); });
    return { inicio: -0.20, fin: 40.20, apoyos, tramos, luces, c, f: { izq: f, der: f }, sumF: { izq: sum, der: sum },
      columnas: ejes.map((e, i) => [5 * i, e]), centros: tramos.map((t, i) => [5 * i + 2.5, t[0]]), zonas };
  }

  window.DEMO_TABLERO = function (estado) {
    const G = estado === 'largo' ? largo() : MAQUETA;
    const PORS = estado === 'largo'
      ? [{ id: 'Pórtico 9', nombre: 'Pórtico 9', sub: 'Eje 9 · A a I', tipo: 'Aérea' }]
      : [{ id: 'Pórtico 1', nombre: 'Pórtico 1', sub: 'Eje 1 · A a D + voladizo', tipo: 'Aérea' },
        { id: 'Pórtico 2', nombre: 'Pórtico 2', sub: 'Eje 2 · A a D', tipo: 'Aérea' },
        { id: 'Pórtico 3a', nombre: 'Pórtico 3a', sub: 'Eje 3 · A a B', tipo: 'Aérea' },
        { id: 'Pórtico 3b', nombre: 'Pórtico 3b', sub: 'Eje 3 · C a D', tipo: 'Aérea' },
        { id: 'Pórtico C1', nombre: 'Pórtico C1', sub: 'Cimentación · eje 1', tipo: 'Cimentación' }];
    // Planta (opción D, 27-oct): viguetas de la demostración, dos grupos de iguales (3 en el paño B–C / 1–2 y 2 en el
    // paño C–D / 2–3), y la planta de dos niveles con los ids de la lista. En 'largo' no hay planta (muestra el aviso).
    if (estado !== 'largo') {
      [['Vigueta 1', 'VT_10X35 · 1', 'Paño B–C / 1–2 · 10×35'], ['Vigueta 2', 'VT_10X35 · 1', 'Paño B–C / 1–2 · 10×35'],
        ['Vigueta 3', 'VT_10X35 · 1', 'Paño B–C / 1–2 · 10×35'], ['Vigueta 4', 'VT_10X30 · 2', 'Paño C–D / 2–3 · 10×30'],
        ['Vigueta 5', 'VT_10X30 · 2', 'Paño C–D / 2–3 · 10×30']]
        .forEach(([id, grupo, sub]) => PORS.push({ id, nombre: id, sub, tipo: 'Aérea', grupo }));
    }
    const EJES_DEMO = [['A', 0], ['B', 6], ['C', 12], ['D', 18]].map(([nombre, x]) => ({ nombre, x1: x, y1: -1, x2: x, y2: 12 }))
      .concat([['1', 0], ['2', 6], ['3', 11]].map(([nombre, y]) => ({ nombre, x1: -1, y1: y, x2: 19, y2: y })));
    const COLUMNAS_DEMO = [0, 6, 12, 18].flatMap((x) => [0, 6, 11].map((y) =>
      ({ puntos: [[x - 0.2, y - 0.2], [x + 0.2, y - 0.2], [x + 0.2, y + 0.2], [x - 0.2, y + 0.2]] })));
    const viga = (portico, x1, y1, x2, y2) => ({ portico, tipo: 'viga', x1, y1, x2, y2, ancho: 0.30, grupo: '' });
    const vigueta = (portico, grupo, x, y1, y2) => ({ portico, tipo: 'vigueta', x1: x, y1, x2: x, y2, ancho: 0.10, grupo });
    const PLANTA = estado === 'largo' ? null : { niveles: [
      { nombre: 'Cimentación', elevacion: 0, ejes: EJES_DEMO, columnas: COLUMNAS_DEMO,
        elementos: [viga('Pórtico C1', 0, 0, 18, 0)] },
      { nombre: 'Nivel 2', elevacion: 3.0, ejes: EJES_DEMO, columnas: COLUMNAS_DEMO,
        elementos: [viga('Pórtico 1', 0, 0, 20, 0), viga('Pórtico 2', 0, 6, 18, 6), viga('Pórtico 3a', 0, 11, 6, 11),
          viga('Pórtico 3b', 12, 11, 18, 11),
          vigueta('Vigueta 1', 'VT_10X35 · 1', 7.5, 0, 6), vigueta('Vigueta 2', 'VT_10X35 · 1', 9, 0, 6),
          vigueta('Vigueta 3', 'VT_10X35 · 1', 10.5, 0, 6),
          vigueta('Vigueta 4', 'VT_10X30 · 2', 14, 6, 11), vigueta('Vigueta 5', 'VT_10X30 · 2', 16, 6, 11)] }] };
    const TRAMOS = G.tramos.map(([id, nombre, desde, hasta]) => ({ id, nombre, desde, hasta }));
    let actual = estado === 'vacio' ? null : (estado === 'cimentacion' ? 'Pórtico C1' : PORS[0].id);
    let enVivo = true;
    let elegidos = null;
    let reglas = { porDiametro: RULES0, multiplo: '0.50', distanciaLibreCm: '2.5', varilla12: false, redondeoGanchoCm: '5', problema: null };
    const opciones = {};
    const inicial = estado === 'reglas' ? { step: 'reglas' }
      : estado === 'varias' ? { step: 'estribos', tramosSel: ['A-B', 'B-C', 'C-D'] } : {};
    if (estado === 'varias') opciones[actual] = { ...OPCIONES0, tramosMarcados: inicial.tramosSel, estribosMarcados: OPCIONES0.estribos };

    const num = (x, d) => { const v = parseFloat(String(x).replace(',', '.')); return Number.isFinite(v) ? v : d; };
    const regla = (d) => reglas.porDiametro.find((r) => r.d === '#' + d) || {};

    function armar(id, o) {
      const por = PORS.find((p) => p.id === id);
      const tipo = o.tipo ? (o.tipo === 'Cimentacion' ? 'Cimentación' : 'Aérea') : por.tipo;
      const isCim = tipo === 'Cimentación';
      const side = o.cara ? (o.cara === 'Inicio' ? 'izq' : 'der') : (estado === 'aviso' ? 'der' : 'izq');
      const kgOf = (d) => num(regla(d).k, 0);
      const kgRow = (rows) => rows.reduce((a, r) => a + r.cantidad * kgOf(r.diametro), 0);
      const qtyOf = (rows) => rows.map((r) => r.cantidad + ' #' + r.diametro).join(' + ');
      const pc = isCim ? 'I' : 'S', pf = isCim ? 'S' : 'I';
      const cRows = isCim ? o.inferior : o.superior, fRows = isCim ? o.superior : o.inferior;
      const capaC = isCim ? 'abajo' : 'arriba', capaF = isCim ? 'arriba' : 'abajo';
      const barras = [], traslapos = [];
      const cm0 = o.cambios || {}, gch = cm0.ganchos || {};
      const grados = (tipo) => ({ De90: '90°', De135: '135°', De180: '180°' })[tipo] || '';
      // corridas (#84): el gancho de cada punta del borde; sin "Sin" (vuelve a 90°) y, en la demostración, con el mismo largo
      const tipoCorrida = (clave) => (gch[clave] && gch[clave] !== 'Sin' ? grados(gch[clave]) : '90°');
      // rótulo de DC-CAD, como Rotulo en C#: "3#5L= 2.80/3.00" (recto / total) o un solo largo sin gancho
      const PULG = { 2: '¼"', 3: '⅜"', 4: '½"', 5: '⅝"', 6: '¾"', 7: '⅞"', 8: '1"', 9: '1⅛"', 10: '1¼"', 11: '1⅜"' };
      const rotulo = (rows, recto, total, pulg) => rows.map((r) => r.cantidad + (pulg ? 'Ø' + PULG[r.diametro] : '#' + r.diametro)).join('+')
        + 'L= ' + f2(recto) + (Math.abs(total - recto) > 1e-6 ? '/' + f2(total) : '');
      const capa = (lista, pre, rows, nombre) => {
        lista.forEach((b, i) => barras.push({ id: pre + (i + 1), capa: nombre, desde: b.a, hasta: b.b, ganchoInicio: !!b.hs, ganchoFin: !!b.he,
          rotulo: rotulo(rows, b.b - b.a, b.len, false), rotuloPulgadas: rotulo(rows, b.b - b.a, b.len, true),
          largo: b.len, recta: b.recta, manual: false, llegaAlVoladizo: !!b.vol, cantidad: qtyOf(rows), kg: b.len * kgRow(rows),
          ganchoInicioTipo: b.hs ? tipoCorrida(pre + '.inicio') : '', ganchoFinTipo: b.he ? tipoCorrida(pre + '.fin') : '',
          puntaInicioEnBorde: !!b.hs, puntaFinEnBorde: !!b.he, color: colorDe(Math.max(...rows.map((r) => r.diametro))) }));
        for (let i = 0; i + 1 < lista.length; i++) {
          traslapos.push({ id: pre + (i + 1) + '–' + pre + (i + 2), barraIzquierda: pre + (i + 1), barraDerecha: pre + (i + 2), capa: nombre,
            desde: lista[i + 1].a, hasta: lista[i].b, minimo: num(regla(rows[0].diametro).t, 0), diametro: '#' + rows[0].diametro, manual: false,
            rotulo: f2(lista[i].b - lista[i + 1].a) });
        }
      };
      capa(G.c, pc, cRows, capaC);
      capa(G.f[side], pf, fRows, capaF);
      // cambios manuales (#84), con las cuentas de la maqueta: Subir / Bajar fija el largo; un traslapo escrito crece
      // al múltiplo y alarga la barra de la derecha
      const cm = o.cambios || {};
      Object.keys(cm.barras || {}).forEach((id) => {
        const b = barras.find((x) => x.id === id);
        if (b) Object.assign(b, { largo: cm.barras[id] / 1000, manual: true, kg: b.kg / b.largo * (cm.barras[id] / 1000) });
      });
      Object.keys(cm.traslapos || {}).forEach((id) => {
        const t = traslapos.find((x) => x.id === id), M = num(reglas.multiplo, 0.5);
        if (!t) return;
        const inc = Math.max(0, Math.ceil((cm.traslapos[id] / 1000 - (t.hasta - t.desde)) / M - 1e-9) * M);
        const b = barras.find((x) => x.id === t.barraDerecha);
        Object.assign(t, { desde: t.desde - inc, manual: true, rotulo: f2(t.hasta - t.desde + inc) });
        Object.assign(b, { desde: b.desde - inc, kg: b.kg / b.largo * (b.largo + inc), largo: b.largo + inc });
      });
      // traslapos arrastrados: se corren enteros; crece la barra de su izquierda y se acorta la de su derecha
      Object.keys(cm.movidos || {}).forEach((id) => {
        const t = traslapos.find((x) => x.id === id);
        if (!t) return;
        const d = cm.movidos[id].desde / 1000 - t.desde;
        const izq = barras.find((x) => x.id === t.barraIzquierda), der = barras.find((x) => x.id === t.barraDerecha);
        Object.assign(t, { desde: t.desde + d, hasta: t.hasta + d, manual: true });
        Object.assign(izq, { hasta: izq.hasta + d, kg: izq.kg / izq.largo * (izq.largo + d), largo: izq.largo + d, manual: true });
        Object.assign(der, { desde: der.desde + d, kg: der.kg / der.largo * (der.largo - d), largo: der.largo - d, manual: true });
      });

      // bastones: arriba en todos los apoyos y abajo en el centro de cada luz; se cortan en la punta del pórtico
      const ob = o.bastones;
      const bTop = isCim ? 'BI' : 'BS', bBot = isCim ? 'BS' : 'BI';
      const acero = (pre) => {
        const g = pre === 'BS' ? ob.superior : ob.inferior;
        return g || { cantidad: 1, diametro: (pre === 'BS' ? o.superior : o.inferior)[0].diametro };
      };
      const a0 = G.inicio + 0.05, z0 = G.fin - 0.05;
      const bastones = G.columnas.map(([x, e], i) => ({ id: bTop + (i + 1), eje: x, donde: 'Centrado en la columna ' + e }))
        .concat(G.centros.map(([x, t], i) => ({ id: bBot + (i + 1), eje: x, donde: 'Centro de la luz ' + t })))
        .map((b) => {
          const L = (ob.largos && ob.largos[b.id] != null ? ob.largos[b.id] : ob.largo) / 1000, st = acero(b.id.slice(0, 2));
          // arrastrado (#84): sus puntas; si no, centrado en su eje
          const pu = ob.puntas && ob.puntas[b.id];
          let a = pu ? Math.max(a0, pu.desde / 1000) : Math.max(a0, b.eje - L / 2);
          let z = pu ? Math.min(z0, pu.hasta / 1000) : Math.min(z0, b.eje + L / 2);
          const cutA = pu ? a <= a0 + 1e-9 : a > b.eje - L / 2, cutZ = pu ? z >= z0 - 1e-9 : z < b.eje + L / 2;
          // gancho por punta (#84): el elegido con los botones o el general del paso 2; con uno elegido, el total sube al
          // múltiplo y el sobrante va a la otra punta (como CalculadorBastones en C#)
          const punta = (clave, borde) => (!borde ? 'Sin' : gch[clave] || (ob.conGancho ? 'De90' : 'Sin'));
          const ti = punta(b.id + '.inicio', cutA), tf = punta(b.id + '.fin', cutZ);
          const largoGancho = (tipo) => (tipo === 'De90' ? num(regla(st.diametro).g, 0) : tipo === 'De135' ? num(regla(st.diametro).e, 0) : tipo === 'De180' ? num(regla(st.diametro).g180, 0) : 0);
          const gA = largoGancho(ti), gZ = largoGancho(tf);
          let len = z - a + gA + gZ;
          const elegido = (cutA && gch[b.id + '.inicio'] && gch[b.id + '.inicio'] !== 'Sin') || (cutZ && gch[b.id + '.fin'] && gch[b.id + '.fin'] !== 'Sin');
          if (elegido) {
            const M = num(reglas.multiplo, 0.5), sobra = Math.ceil(len / M - 1e-9) * M - len;
            if (cutA && !cutZ) z = Math.min(z0, z + sobra); else if (cutZ && !cutA) a = Math.max(a0, a - sobra);
            len = z - a + gA + gZ;
          }
          return { id: b.id, eje: b.eje, desde: a, hasta: z, ganchoInicio: gA > 0, ganchoFin: gZ > 0, largo: len, donde: b.donde,
            cantidad: st.cantidad + ' #' + st.diametro, kg: len * st.cantidad * kgOf(st.diametro), cortado: cutA || cutZ, n: st.cantidad,
            ganchoInicioTipo: grados(ti), ganchoFinTipo: grados(tf), puntaInicioEnBorde: cutA, puntaFinEnBorde: cutZ,
            color: colorDe(st.diametro), diametro: st.diametro,
            rotulo: rotulo([st], z - a, len, false), rotuloPulgadas: rotulo([st], z - a, len, true) };
        }).filter((b) => b.n > 0);

      // sección y reparto en filas (#88)
      const rec = o.recubrimientoCm, recUp = o.recArribaCm ?? rec, recDown = o.recAbajoCm ?? rec, recSide = o.recLadosCm ?? rec;
      const dE = DIAM[o.estribos.diametro];
      const gap = num(reglas.distanciaLibreCm, 2.5);
      const maxCapas = o.maximoFilas;
      // los diámetros de cada fila, como RepartidorFilas en C#: primero los tipos con fila fija (#84), en su fila; los
      // Auto llenan lo libre desde la fila 1
      const enFilas = (rows, per) => {
        const filas = [], at = (i) => { while (filas.length <= i) filas.push([]); return filas[i]; };
        rows.filter((r) => r.fila >= 1).forEach((r) => { for (let i = 0; i < r.cantidad; i++) at(r.fila - 1).push(r.diametro); });
        let f = 0;
        rows.filter((r) => !(r.fila >= 1)).forEach((r) => {
          for (let i = 0; i < r.cantidad; i++) { while (at(f).length >= per) f++; at(f).push(r.diametro); }
        });
        return filas;
      };
      const cara = (rows) => {
        const total = rows.reduce((a, r) => a + r.cantidad, 0);
        const dmax = Math.max(...rows.map((r) => DIAM[r.diametro]));
        const per = Math.max(0, Math.floor((30 - 2 * recSide - 2 * dE + gap) / (dmax + gap)));
        const lista = per ? enFilas(rows, per) : [];
        const fijas = rows.some((r) => r.fila != null);
        // en una vigueta (#941) basta una barra por cara, como en RepartidorFilas
        const minimo = o.elemento === 'Vigueta' ? 1 : 2;
        const error = total < minimo ? 'MenosDeDosBarras'
          : !per || lista.length > maxCapas || lista.some((x) => x.length > per) || rows.some((r) => r.fila != null && r.fila < 1) ? 'NoCaben'
          : fijas && lista[0].length < minimo ? 'MenosDeDosBarras' : 'Ninguno';
        const filas = lista.filter((x) => x.length);
        return { cabenPorFila: per, porFila: error === 'Ninguno' ? filas.map((x) => x.length) : [], error, maximo: per * maxCapas, dmax, filas };
      };
      const sup = cara(o.superior), inf = cara(o.inferior);
      const barrasSec = [];
      const place = (rows, info, fromTop) => {
        const enFila = info.filas, list = [].concat(...enFila);
        const per = Math.max(1, info.cabenPorFila);
        const sepV = o.separacionFilasCm ?? 0;
        const recV = fromTop ? recUp : recDown;
        let idx = 0, fila = 0;
        const salen = [];
        while (fila < enFila.length && fila < 4) {
          const n = enFila[fila].length;
          const d = DIAM[list[idx]];
          const yc = recV + dE + d / 2 + fila * (info.dmax + sepV);
          const xL = recSide + dE + d / 2, xR = 30 - xL;
          const slotsAll = [];
          for (let i = 0; i < per; i++) slotsAll.push(per === 1 ? 15 : xL + i * (xR - xL) / (per - 1));
          let order;
          if (fila === 0) order = n === per ? slotsAll : Array.from({ length: n }, (_, i) => (n === 1 ? 15 : xL + i * (xR - xL) / (n - 1)));
          else { order = []; let a = 0, z = per - 1; while (order.length < n && a <= z) { order.push(slotsAll[a++]); if (order.length < n && a <= z) order.push(slotsAll[z--]); } }
          order.forEach((xc, j) => salen.push({ fila, x: xc, y: fromTop ? yc : 50 - yc, d: DIAM[list[idx + j]], color: colorDe(list[idx + j]) }));
          idx += n; fila++;
        }
        salen.sort((p, q) => p.fila - q.fila || p.x - q.x);
        const f1 = salen.filter((b) => b.fila === 0);
        salen.forEach((b, i) => barrasSec.push({ id: (fromTop ? 'a' : 'b') + (i + 1), nombre: (fromTop ? 'arriba ' : 'abajo ') + (i + 1), arriba: fromTop,
          fila: b.fila, x: b.x, y: b.y, d: b.d, esquina: b.fila === 0 && (b === f1[0] || b === f1[f1.length - 1]), color: b.color }));
      };
      place(o.superior, sup, true);
      place(o.inferior, inf, false);

      // estribos: el primero a 5 cm de la cara; zonas junto a las columnas y centro; el voladizo, zona junto a su columna
      const marcados = o.tramosMarcados || [];
      const confDe = (t) => (o.estribosMarcados && marcados.includes(t) ? o.estribosMarcados : o.estribos);
      const nEst = ([t, a, b, vol]) => {
        const e = confDe(t);
        const confL = e.largoZona / 1000, s1v = Math.max(0.01, e.separacionZona / 1000), s2v = Math.max(0.01, e.separacionCentro / 1000);
        const countAt = (L) => Math.floor((L + 1e-9) / s1v) + 1;
        if (vol) {
          if (b + 0.05 - a < confL) return countAt(b - a - 0.05);
          const nzv = countAt(confL - 0.05);
          return nzv + Math.max(0, Math.ceil(((b - a) - (0.05 + (nzv - 1) * s1v)) / s2v - 1e-9));
        }
        if (b - a < 2 * confL) return countAt(b - a - 0.10);
        const nz = countAt(confL - 0.05);
        return 2 * nz + Math.max(0, Math.ceil(((b - a) - 2 * (0.05 + (nz - 1) * s1v)) / s2v - 1e-9) - 1);
      };
      const nMain = G.luces.reduce((s, l) => s + nEst(l), 0);
      const re = regla(o.estribos.diametro);
      const hookE = o.estribos.gancho === 'De180' ? num(re.g180, 0) : num(o.estribos.gancho === 'De135' ? re.e : re.g, 0);
      const hE = (50 - recUp - recDown - dE) / 100;
      const lenMain = 2 * ((30 - 2 * recSide - dE) / 100 + hE) + 2 * hookE;
      const kgE = nMain * lenMain * num(re.k, 0);
      const e0 = o.estribos;
      const listaEstribos = [{ id: 'E', tipo: 'Estribo cerrado', cantidad: nMain, diametro: '#' + e0.diametro, largo: lenMain,
        detalle: 'zonas ' + f2(e0.largoZona / 1000) + ' cada ' + f2(e0.separacionZona / 1000) + ' · centro cada ' + f2(e0.separacionCentro / 1000), kg: kgE }];

      const kgFace = { izq: G.sumF.izq * kgRow(fRows), der: G.sumF.der * kgRow(fRows) };
      const cheaper = kgFace.izq <= kgFace.der ? 'izq' : 'der';
      const avisos = estado === 'aviso' && side === 'der' && !isCim
        ? [{ tipo: 'TraslapoLargo', elemento: pf + '3–' + pf + '4', texto: 'El traslapo ' + pf + '3–' + pf + '4 queda en 1.40, 0.40 por encima del mínimo. ' + pf + '4 termina en el voladizo y solo puede crecer. Si el traslapo va al inicio de la luz no pasa, y además se ahorran 7 kg.', cambiarCara: true }]
        : [];
      // corte 1-1 (#91): lo de CalculadoraCorte en C#, simplificado (sin vista tipo ni proyección). Se puede en los tramos,
      // sin apoyos ni traslapos y con 0.01 de margen; fuera de ahí salta al punto permitido más cercano (empate: derecha).
      const MARGEN = 0.01;
      const enTraslapo = traslapos.map((t) => [t.desde, t.hasta]).sort((p, q) => p[0] - q[0]);
      const permitidos = [];
      const agregar = (d, h) => { if (h - d > 2 * MARGEN) permitidos.push({ desde: d + MARGEN, hasta: h - MARGEN }); };
      G.luces.forEach(([, a, b, vol]) => {
        const z = vol ? G.fin : b;
        let d = a;
        enTraslapo.filter(([p, q]) => q > a && p < z).forEach(([p, q]) => { agregar(d, Math.min(p, z)); d = Math.max(d, q); });
        agregar(d, z);
      });
      // los estribos: en la demostración, cada 0.10 desde 5 cm de la cara (en C# salen del motor); el corte puede ir en
      // los que caen en lo permitido
      const posicionesEstribos = [];
      G.luces.forEach(([, a, b, vol]) => {
        for (let x = a + 0.05; x <= (vol ? G.fin : b) - 0.05 + 1e-9; x += 0.10) posicionesEstribos.push(Math.round(x * 1000) / 1000);
      });
      const puntos = posicionesEstribos.filter((r) => permitidos.some((i) => i.desde - 1e-9 <= r && r <= i.hasta + 1e-9));
      const oc = o.corte || {};
      const pedido = oc.x == null ? 2.20 : oc.x / 1000;
      let cx = pedido, dist = Infinity;
      puntos.forEach((p) => {
        const d = Math.abs(p - pedido);
        if (d < dist - 1e-9 || (Math.abs(d - dist) <= 1e-9 && p > cx)) { cx = p; dist = d; }
      });
      const tramoCorte = G.luces.find(([, a, b, vol]) => a - 1e-9 <= cx && cx <= (vol ? G.fin : b) + 1e-9);
      const corte = { x: cx, esTipo: oc.x == null, ajustado: oc.x != null && !permitidos.some((i) => i.desde - 1e-9 <= pedido && pedido <= i.hasta + 1e-9),
        profundidad: Math.max(0, (oc.profundidad ?? 100) / 1000), mirando: oc.mirando === 'Izquierda' ? 'izquierda' : 'derecha',
        tramo: tramoCorte ? tramoCorte[0] : '', permitidos, proyeccion: [], aviso: null, puntos };

      const kg = barras.reduce((s, b) => s + b.kg, 0) + bastones.reduce((s, b) => s + b.kg, 0) + kgE;
      // el rótulo de cada zona de estribos, como ArmadorVista en C#: las zonas se quedan con el estribo que comparten con
      // el centro; la L va de la cara de la columna al último estribo de la zona
      const libres = posicionesEstribos.slice();
      const cuenta = new Map();
      G.zonas.map((z, i) => [z, i]).sort((p, q) => q[0][2] - p[0][2]).forEach(([[d, h], i]) => {
        const suyos = libres.filter((x) => x >= d - 0.051 && x <= h + 0.051);
        suyos.forEach((x) => libres.splice(libres.indexOf(x), 1));
        cuenta.set(i, suyos.length);
      });
      const sep = (m) => '@' + f2(m).replace(/^0/, '');
      const zonas = G.zonas.map(([desde, hasta, junta], i) => {
        const luz = G.luces.find(([, a, b]) => desde >= a - 1e-9 && hasta <= b + 0.05);
        const pegadaIzq = luz && Math.abs(desde - luz[1]) < 0.1;
        return { desde, hasta, junta: !!junta, cantidad: cuenta.get(i) + 'e#' + e0.diametro, cantidadPulgadas: cuenta.get(i) + 'eØ' + PULG[e0.diametro],
          separacion: sep((junta ? e0.separacionZona : e0.separacionCentro) / 1000),
          largo: junta && luz ? 'L=' + f2(pegadaIzq ? hasta - luz[1] : luz[2] - desde) : '' };
      });
      const secciones = G.luces.map(([, a, b, vol]) => ({ x: (a + (vol ? G.fin : b)) / 2, texto: '30×50' }));
      // los de la leyenda: barras y bastones, del más grueso al más delgado
      const diametros = [...new Set(o.superior.concat(o.inferior).map((r) => r.diametro).concat(bastones.map((b) => b.diametro)))]
        .sort((p, q) => q - p).map((d) => ({ nombre: '#' + d, color: colorDe(d), nombrePulgadas: PULG[d] }));
      return {
        // los pórticos de la demostración son vigas: la casilla "Es vigueta" (#940) solo cambia si se corrige a mano
        id, tipo, inicio: G.inicio, fin: G.fin, esVigueta: o.elemento === 'Vigueta', detectadaVigueta: false,
        apoyos: G.apoyos.map(([eje, x, caraIzquierda, caraDerecha]) => ({ eje, x, caraIzquierda, caraDerecha,
          seccion: Math.round((caraDerecha - caraIzquierda) * 100) + '×40' })),
        barras, traslapos, bastones: bastones.map(({ n, diametro, ...b }) => b), diametros,
        zonas, secciones,
        superior: sup, inferior: inf,
        izquierda: { kg: kgFace.izq, existe: true }, derecha: { kg: kgFace.der, existe: true }, caraUsada: side, caraMasEconomica: cheaper,
        seccion: { anchoCm: 30, altoCm: 50, barras: barrasSec,
          figuras: [{ id: 'E', tipo: 'Cerrado', izquierda: recSide, derecha: 30 - recSide, arriba: recUp, abajo: 50 - recDown }] },
        listaEstribos, avisos, zonasTraslapo: traslapos.map((t) => ({ desde: t.desde, hasta: t.hasta })),
        kg, estribos: nMain, sePuedeGenerar: sup.error === 'Ninguno' && inf.error === 'Ninguno', corte, posicionesEstribos,
      };
    }

    function vista() {
      return {
        proyecto: '[Nombre del proyecto]', enVivo, reglas, elegidos,
        // los que tienen tipo de barra en el modelo (#85): en la demostración, del #3 al #8
        diametrosDisponibles: [3, 4, 5, 6, 7, 8],
        porticos: PORS.map((p) => ({ ...p, tramos: TRAMOS })),
        actual: actual ? armar(actual, opciones[actual] || OPCIONES0) : null,
        planta: PLANTA,
      };
    }
    const msgVista = () => ({ tipo: 'vista', vista: vista() });

    // La vista previa de la edición (#84) con las cuentas de la maqueta; en Revit la arma EditorTablero en C#.
    function previa(m) {
      const P = armar(m.id, m.opciones), M = num(reglas.multiplo, 0.5), varilla = reglas.varilla12 ? 12 : 6;
      const pe = m.pedido, id = pe.elemento, v = pe.valor;
      const op = (accion, valor, sePuede, texto) => ({ accion, valor, sePuede, texto });
      let opciones = [];
      if (pe.accion === 'Barra') {
        const b = P.barras.find((x) => x.id === id);
        opciones = !b ? [] : [
          b.largo + M > varilla ? op('subir', b.largo + M, false, 'No sale de una varilla de ' + varilla + ' m.') : op('subir', b.largo + M, true, id + ' pasa a ' + f2(b.largo + M) + ' m.'),
          op('bajar', b.largo - M, false, 'Bajarla dejaría un traslapo por debajo del mínimo.'),
        ];
      } else if (pe.accion === 'Traslapo') {
        const t = P.traslapos.find((x) => x.id === id);
        if (t) {
          const cur = t.hasta - t.desde, der = P.barras.find((x) => x.id === t.barraDerecha);
          const inc = v == null ? 0 : Math.max(0, Math.ceil((v - cur) / M - 1e-9) * M);
          opciones = [v == null ? op('aplicar', 0, false, 'Escribe un largo en metros.')
            : v < t.minimo - 1e-9 ? op('aplicar', v, false, 'No puede ser menor que el mínimo (' + f2(t.minimo) + ' m).')
            : inc === 0 ? op('aplicar', v, false, 'Escribe un largo mayor que el actual (' + f2(cur) + ' m).')
            : der.largo + inc > varilla + 1e-9 ? op('aplicar', v, false, t.barraDerecha + ' pasaría a ' + f2(der.largo + inc) + ' m y no sale de una varilla de ' + varilla + ' m.')
            : op('aplicar', v, true, t.barraDerecha + ' pasa a ' + f2(der.largo + inc) + ' m (múltiplo de ' + f2(M) + ') y el traslapo queda en ' + f2(cur + inc) + ' m.')];
        }
      } else if (pe.accion === 'Baston') {
        const b = P.bastones.find((x) => x.id === id);
        if (b) {
          opciones = [v == null ? op('aplicar', 0, false, 'Escribe un largo en metros.')
            : v <= 0 ? op('aplicar', v, false, 'El largo debe ser mayor que 0.')
            : op('aplicar', v, true, b.cortado ? 'Llega al borde de la viga y se corta en la punta. No se redondea.' : 'Queda ' + f2(v / 2) + ' m a cada lado de su eje. No se redondea.')];
        }
      }
      const saltos = pe.accion === 'Arrastre' ? saltosDe(P, m.opciones, id, pe.punto, M, varilla) : [];
      return { tipo: 'previa', previa: { n: pe.n, elemento: id, opciones, saltos } };
    }

    // Los saltos del arrastre (#84) con módulo fijo; en Revit los arma EditorArrastre en C#. Un traslapo se queda en su
    // luz, a 5 cm de las caras, o pasa al voladizo que toca su luz (pide confirmar); ninguna barra pasa de la varilla.
    function saltosDe(P, o, id, punto, M, varilla) {
      const r3 = (x) => Math.round(x * 1000) / 1000;
      const rotulo = (cant, recto, total) => cant.split(' + ').map((c) => c.replace(' ', '')).join('+') + 'L= ' + f2(recto)
        + (Math.abs(total - recto) > 1e-6 ? '/' + f2(total) : '');
      const salto = (x, recto, total, cant, enVoladizo, cambios) => ({ x: r3(x), recto: r3(recto), total: r3(total), rotulo: rotulo(cant, recto, total), enVoladizo, cambios });
      const b = P.barras.find((x) => x.id === id);
      if (b) {
        const laps = P.traslapos.filter((t) => t.capa === b.capa);
        const izq = laps.find((t) => t.barraDerecha === id), der = laps.find((t) => t.barraIzquierda === id);
        const mueve = punto === 'Izquierda' ? [izq] : punto === 'Derecha' ? [der] : izq && der ? [izq, der] : [];
        if (!mueve.length || !mueve[0]) return [];
        const luces = G.luces.filter((l) => !l[3]), vol = G.luces.find((l) => l[3]);
        // la luz donde está un traslapo (índice), 'V' en el voladizo o −1 en ninguna
        const zona = (lo, hi) => {
          const i = luces.findIndex((l) => lo >= l[1] + 0.05 - 1e-9 && hi <= l[2] - 0.05 + 1e-9);
          return i >= 0 ? i : vol && lo >= vol[1] + 0.05 - 1e-9 && hi < vol[2] - 1e-9 ? 'V' : -1;
        };
        const casa = (t) => { const z = zona(t.desde, t.hasta); return z === 'V' ? luces.length - 1 : z; };
        const crece = P.barras.find((x) => x.id === mueve[0].barraIzquierda), acorta = P.barras.find((x) => x.id === mueve[mueve.length - 1].barraDerecha);
        const ganchos = b.largo - (b.hasta - b.desde), out = [];
        for (let k = -30; k <= 30; k++) {
          const d = k * M;
          if (crece.largo + d > varilla + 1e-9 || acorta.largo - d > varilla + 1e-9) continue;
          const zonas = mueve.map((t) => zona(t.desde + d, t.hasta + d));
          if (zonas.some((z, j) => z !== casa(mueve[j]) && !(z === 'V' && casa(mueve[j]) === luces.length - 1))) continue;
          const enVol = zonas.includes('V'), pregunta = mueve.some((t, j) => zonas[j] === 'V' && zona(t.desde, t.hasta) !== 'V');
          const desde = b.desde + (punto === 'Derecha' ? 0 : d), hasta = b.hasta + (punto === 'Izquierda' ? 0 : d);
          const x = punto === 'Izquierda' ? desde : punto === 'Derecha' ? hasta : (desde + hasta) / 2;
          const cambios = k === 0 ? [] : mueve.map((t) => ({ tipo: 'movido', id: t.id, desde: Math.round((t.desde + d) * 1000), hasta: 0, enVoladizo: enVol }));
          out.push(salto(x, hasta - desde, hasta - desde + ganchos, b.cantidad, pregunta, cambios));
        }
        return out;
      }
      const bs = P.bastones.find((x) => x.id === id);
      if (!bs) return [];
      const a0 = G.inicio + 0.05, z0 = G.fin - 0.05, d = parseInt(bs.cantidad.split('#')[1], 10);
      const gch = (o.cambios && o.cambios.ganchos) || {};
      // gancho en una punta pegada al borde (en la demostración, siempre con el largo del de 90°)
      const gancho = (punta, enBorde) => (enBorde && ((gch[id + '.' + punta] && gch[id + '.' + punta] !== 'Sin') || (!gch[id + '.' + punta] && o.bastones.conGancho)) ? num(regla(d).g, 0) : 0);
      const total = (a, z) => z - a + gancho('inicio', a <= a0 + 1e-9) + gancho('fin', z >= z0 - 1e-9);
      const en = (a, z, x) => salto(x, z - a, total(a, z), bs.cantidad, false,
        Math.abs(a - bs.desde) < 1e-9 && Math.abs(z - bs.hasta) < 1e-9 ? [] : [{ tipo: 'puntas', id, desde: Math.round(a * 1000), hasta: Math.round(z * 1000), enVoladizo: false }]);
      if (punto === 'Centro') {
        const out = [];
        for (let c = Math.ceil((a0 - bs.desde) / 0.05 - 1e-9); bs.hasta + c * 0.05 <= z0 + 1e-9; c++) {
          const a = bs.desde + c * 0.05, z = bs.hasta + c * 0.05;
          out.push(en(a, z, (a + z) / 2));
        }
        return out;
      }
      const izqP = punto === 'Izquierda', fija = izqP ? bs.hasta : bs.desde;
      const gFija = izqP ? gancho('fin', bs.hasta >= z0 - 1e-9) : gancho('inicio', bs.desde <= a0 + 1e-9);
      const puntas = [[bs.desde, bs.hasta]];
      for (let t = M; ; t += M) {
        const recto = t - gFija;
        if (recto < M - 1e-9) continue;
        const suelta = izqP ? fija - recto : fija + recto;
        if (izqP ? suelta <= a0 + 0.05 + 1e-9 : suelta >= z0 - 0.05 - 1e-9) {
          // imán: se pega al borde, el total sube al módulo y el sobrante va a la otra punta
          let a = izqP ? a0 : bs.desde, z = izqP ? bs.hasta : z0;
          const sobra = Math.ceil(total(a, z) / M - 1e-9) * M - total(a, z);
          if (izqP) z = Math.min(z0, z + sobra); else a = Math.max(a0, a - sobra);
          puntas.push([a, z]);
          break;
        }
        puntas.push(izqP ? [suelta, fija] : [fija, suelta]);
      }
      return puntas.sort((p, q) => (izqP ? p[0] - q[0] : p[1] - q[1])).map(([a, z]) => en(a, z, izqP ? a : z));
    }
    const aviso = (texto) => ({ tipo: 'aviso', texto });
    const nombre = (id) => (PORS.find((p) => p.id === id) || { nombre: '' }).nombre;

    // Responde como SesionTablero en C#, con los avisos de la maqueta para lo que en Revit hace Revit.
    function manejar(m) {
      switch (m.tipo) {
        case 'listo': return [msgVista()];
        case 'elegirPortico':
          actual = m.id;
          if (elegidos && !elegidos.includes(m.id)) elegidos = null;
          return [msgVista()];
        case 'calcular': opciones[m.id] = m.opciones; actual = m.id; return [msgVista()];
        case 'previsualizar': return [previa(m)];
        case 'cambiarEnVivo': enVivo = m.activo; return [msgVista()];
        case 'verTodos': elegidos = null; return [msgVista()];
        // clic y Ctrl+clic en la planta: con dos o más quedan como elegidos
        case 'elegirVarios': {
          const ids = m.ids.filter((id, i) => PORS.some((p) => p.id === id) && m.ids.indexOf(id) === i);
          if (!ids.length) return [msgVista()];
          elegidos = ids.length > 1 ? ids : null;
          actual = ids.includes(m.abrir) ? m.abrir : ids[0];
          return [msgVista()];
        }
        case 'guardarReglas':
          if (!(num(m.reglas.multiplo, 0) > 0)) return [aviso('El múltiplo de corte debe ser mayor que 0.')];
          reglas = { ...m.reglas, problema: null };
          return [msgVista()];
        // En Revit, la barra del add-in deja elegir vigas; aquí se simula que eligió vigas del primer y del último pórtico.
        case 'seleccionarEnRevit':
          elegidos = [PORS[0].id, PORS[PORS.length - 1].id];
          actual = elegidos[0];
          return [msgVista()];
        case 'verEnRevit': return [aviso(nombre(m.id) + ' seleccionado y centrado en Revit')];
        case 'abrir3d': return [aviso('Vista 3D aislada de ' + nombre(m.id) + ' abierta en Revit')];
        case 'generar': return [{ tipo: 'generado', texto: 'Se generó el acero de ' + (m.ids.length === 1 ? '1 pórtico' : m.ids.length + ' pórticos')
          + '.\nEn la demostración no se crea nada: en Revit aquí salen las barras, bastones y estribos creados, y lo que no se pudo.' }];
        default: return [];
      }
    }

    return { vista: vista(), inicial, manejar };
  };

  window.VISTA_DEMO = (estado) => window.DEMO_TABLERO(estado).vista;
})();
