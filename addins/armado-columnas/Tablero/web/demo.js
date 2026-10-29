// Datos de demostración de la ventana de columnas (#205, tarea 1 del plan docs/planes/2026-10-29-interfaz-web-columnas.md).
// Hace el papel de SesionColumnas en C#: guarda el estado, atiende los mensajes de la página y devuelve la VistaColumnas
// entera. Solo se carga sin window.chrome.webview (fuera de Revit), para la maqueta que aprueba Carlos. Las columnas son
// las de ProveedorContextoFalso (C-12, C-1, C-7) más copias para tener una planta con varias columnas iguales. Las
// cuentas son simplificadas: en Revit todo esto lo calcula C# con SeccionPlanta, RepartoEstribos y PlanTraslapos.
(function () {
  'use strict';

  // Cuadro de varillas de la empresa (CuadroVarillas.Cuadro), en m. Gancho 90°: la pata B y el D del cuadro 2, que es
  // lo que muestra VentanaCuadroVarillas y lo que se usa al generar (#177). 180° y estribo 135°: E y D del cuadro 2.
  const CUADRO = [
    { n: 2, mm: 6.4, traslapo: null, b90: 0.112, d90: 0.038, e180: 0.065, d180: 0.038, e135: 0.038, d135: 0.026, kg: 0.250 },
    { n: 3, mm: 9.5, traslapo: 0.55, b90: 0.166, d90: 0.057, e180: 0.065, d180: 0.057, e135: 0.057, d135: 0.038, kg: 0.560 },
    { n: 4, mm: 12.7, traslapo: 0.75, b90: 0.222, d90: 0.076, e180: 0.065, d180: 0.076, e135: 0.076, d135: 0.051, kg: 0.994 },
    { n: 5, mm: 15.9, traslapo: 0.90, b90: 0.278, d90: 0.095, e180: 0.065, d180: 0.095, e135: 0.095, d135: 0.064, kg: 1.552 },
    { n: 6, mm: 19.1, traslapo: 1.10, b90: 0.334, d90: 0.115, e180: 0.076, d180: 0.115, e135: null, d135: null, kg: 2.235 },
    { n: 7, mm: 22.2, traslapo: 1.35, b90: 0.388, d90: 0.133, e180: 0.089, d180: 0.133, e135: null, d135: null, kg: 3.042 },
    { n: 8, mm: 25.4, traslapo: 1.70, b90: 0.444, d90: 0.152, e180: 0.102, d180: 0.152, e135: null, d135: null, kg: 3.973 },
  ];
  // ColoresVarilla.cs: el color de cada número en los filtros de vista de Revit.
  const COLOR = { 2: '#5E6670', 3: '#FBBC04', 4: '#4285F4', 5: '#EA4335', 6: '#34A853', 7: '#9334E6', 8: '#8B4513' };
  const num = (nombre) => parseInt(String(nombre || '#4').replace(/\D/g, ''), 10) || 4;
  const varilla = (nombre) => CUADRO.find((v) => v.n === num(nombre)) || CUADRO[2];
  const r2 = (x) => Math.round(x * 100) / 100;
  const copia = (o) => JSON.parse(JSON.stringify(o));

  // ---- Las columnas del modelo de ejemplo

  function edificio() {
    // ColumnaEdificioWilliam: arranca 0.20 m sobre la zapata, 4 niveles con viga de 45 cm y un último tramo libre.
    const tramos = [];
    let z = 0;
    const agregar = (alto, tipo) => { tramos.push({ indice: tramos.length, tipo, zInf: r2(z), zSup: r2(z + alto) }); z += alto; };
    agregar(0.20, 'ContraZapata');
    for (let nivel = 1; nivel <= 4; nivel++) {
      agregar(3.20 - 0.45 - (nivel === 1 ? 0.20 : 0), 'Libre');
      agregar(0.45, 'DentroDeViga');
    }
    agregar(3.20 - 0.45, 'Libre');
    return {
      tramos, zSuperior: r2(z), zInferiorLongitudinal: -0.30, zapata: { zInf: -0.50, zSup: 0, ancho: 1.20 },
      niveles: ['PB', 'P1', 'P2', 'P3', 'P4', 'Techo'].map((nombre, i) => ({ nombre, z: r2(i * 3.20) })),
      avisos: ['Arranca sobre la zapata: el acero longitudinal entra 0.30 m en ella.'],
    };
  }
  function sinVigas() {
    return {
      tramos: [{ indice: 0, tipo: 'Libre', zInf: 0, zSup: 3.00 }], zSuperior: 3.00, zInferiorLongitudinal: 0, zapata: null,
      niveles: [{ nombre: 'PB', z: 0 }, { nombre: 'P1', z: 3.00 }],
      avisos: ['Ninguna viga toca esta columna: queda un solo tramo libre de punta a punta.'],
    };
  }
  function tresTramos() {
    return {
      tramos: [
        { indice: 0, tipo: 'Libre', zInf: 0, zSup: 2.60 },
        { indice: 1, tipo: 'DentroDeViga', zInf: 2.60, zSup: 3.00 },
        { indice: 2, tipo: 'Libre', zInf: 3.00, zSup: 5.60 },
      ],
      zSuperior: 5.60, zInferiorLongitudinal: 0, zapata: null,
      niveles: [{ nombre: 'PB', z: 0 }, { nombre: 'P1', z: 3.00 }, { nombre: 'P2', z: 6.00 }],
      avisos: [],
    };
  }

  const COLUMNAS = [
    { id: 'C-12', marca: 'C-12', ubicacion: 'A-1', x: 0, y: 0, ladoX: 0.40, ladoY: 0.40, forma: edificio },
    { id: 'C-1', marca: 'C-1', ubicacion: 'B-1', x: 5, y: 0, ladoX: 0.30, ladoY: 0.30, forma: sinVigas },
    { id: 'C-14', marca: 'C-14', ubicacion: 'C-1', x: 10, y: 0, ladoX: 0.40, ladoY: 0.40, forma: edificio },
    { id: 'C-7', marca: 'C-7', ubicacion: 'A-2', x: 0, y: 5, ladoX: 0.35, ladoY: 0.35, forma: tresTramos },
    { id: 'C-13', marca: 'C-13', ubicacion: 'B-2', x: 5, y: 5, ladoX: 0.40, ladoY: 0.40, forma: edificio },
    { id: 'C-15', marca: 'C-15', ubicacion: 'C-2', x: 10, y: 5, ladoX: 0.35, ladoY: 0.35, forma: tresTramos },
  ];
  const REJILLAS = [
    { nombre: 'A', x1: 0, y1: -1.5, x2: 0, y2: 6.5 }, { nombre: 'B', x1: 5, y1: -1.5, x2: 5, y2: 6.5 },
    { nombre: 'C', x1: 10, y1: -1.5, x2: 10, y2: 6.5 },
    { nombre: '1', x1: -1.5, y1: 0, x2: 11.5, y2: 0 }, { nombre: '2', x1: -1.5, y1: 5, x2: 11.5, y2: 5 },
  ];
  const contexto = (id) => { const c = COLUMNAS.find((k) => k.id === id); return { ...c, ...c.forma() }; };
  // Misma estructura (la regla #63 de ColumnasCompatibles): mismos tipos de tramo y misma sección.
  const firma = (id) => { const c = contexto(id); return [c.ladoX, c.ladoY, c.tramos.map((t) => t.tipo + (t.zSup - t.zInf).toFixed(2)).join()].join('|'); };

  // ---- Opciones (ConfigLongitudinal y ConfigEstribosColumna, en camelCase como las serializa C#)

  const OPCIONES0 = {
    recubrimiento: 0.04,
    longitudinal: {
      barrasPorCaraX: 2, barrasPorCaraY: 2,
      tipoBarraEsquinasNombre: '#5', tipoBarraCaraXNombre: '#4', tipoBarraCaraYNombre: '#4',
      longitudComercialBarra: 12, redondeoVarillas: 0.50, solapeMayorDeLosDos: false,
      estribosMenores: [],
    },
    estribos: {
      tramosLibres: { modo: 'Confinado', tipoBarraNombre: '#3', espaciadoUniforme: 0.20, longitudConfinamiento: 0.50, espaciadoConfinado: 0.07, espaciadoNoConfinado: 0.14 },
      nudoEspaciado: 0.14,
      excepciones: {},
    },
  };

  const GUARDADAS0 = {
    'Columna tipo 40×40': OPCIONES0,
    'Esquineras con grapas': (() => {
      const o = copia(OPCIONES0);
      o.longitudinal.barrasPorCaraX = 3; o.longitudinal.barrasPorCaraY = 3; o.longitudinal.tipoBarraCaraXNombre = '#5'; o.longitudinal.tipoBarraCaraYNombre = '#5';
      o.longitudinal.estribosMenores = [
        { forma: 'GrapaC', eje: 'Fila', desde: 1, hasta: 1, volteado: false, anguloInicio: 180, anguloFin: 180, tipoBarraNombre: null },
        { forma: 'GrapaC', eje: 'Columna', desde: 1, hasta: 1, volteado: false, anguloInicio: 180, anguloFin: 180, tipoBarraNombre: null },
      ];
      return o;
    })(),
  };

  // ---- La forma en planta de estribos y grapas como los deja Revit: port de PlantaArmado, GanchoGrapa y
  // EstriboMenor.CajaQueAbraza (en cm). En Revit esto lo manda C# con las mismas funciones.

  const PASO = 7.5;
  const rad = (g) => (g * Math.PI) / 180;
  const arco = (c, r, desde, hasta) => {
    const n = Math.max(1, Math.ceil(Math.abs(hasta - desde) / PASO)), out = [];
    for (let i = 0; i <= n; i++) { const g = rad(desde + ((hasta - desde) * i) / n); out.push([c[0] + r * Math.cos(g), c[1] + r * Math.sin(g)]); }
    return out;
  };
  const doblezDe = (v) => (v.d135 != null ? v.d135 * 100 : (4 * v.mm) / 10); // diámetro de doblado de estribo, cm
  const cola135 = (v) => (v.e135 != null ? v.e135 * 100 : (6 * v.mm) / 10);
  const cola180 = (v) => (v.e180 != null ? v.e180 * 100 : Math.max(6.5, (4 * v.mm) / 10));
  const colaDe = (angulo, v) => (angulo === 135 ? cola135(v) : angulo === 90 ? (6 * v.mm) / 10 : cola180(v));

  // PlantaArmado.EstriboCerrado: medidas por fuera, ganchos de 135° en la esquina de abajo a la izquierda.
  function estriboCerrado(x1, x2, y1, y2, d, doblez, cola) {
    let r = doblez / 2 + d / 2;
    const ex1 = x1 + d / 2, ex2 = x2 - d / 2, ey1 = y1 + d / 2, ey2 = y2 - d / 2;
    r = Math.max(0, Math.min(r, Math.min(ex2 - ex1, ey2 - ey1) / 2));
    const ai = [ex1 + r, ey1 + r], ad = [ex2 - r, ey1 + r], ard = [ex2 - r, ey2 - r], ari = [ex1 + r, ey2 - r];
    const k = Math.SQRT2 / 2;
    const cierre = arco(ai, r, 180, 315);
    const u = cierre[cierre.length - 1];
    cierre.push([u[0] + k * cola, u[1] + k * cola]);
    const arranque = cierre.map((p) => [ai[0] + (p[1] - ai[1]), ai[1] + (p[0] - ai[0])]).reverse();
    return [...arranque, ...arco(ad, r, 270, 360), ...arco(ard, r, 0, 90), ...arco(ari, r, 90, 180), ...cierre];
  }

  // GanchoGrapa.Lados: la C abre hacia el centro de la sección; Voltear invierte las dos puntas y la S, la del fin.
  function lados(desde, hasta, centro, forma, volteado) {
    const L = Math.hypot(hasta[0] - desde[0], hasta[1] - desde[1]), ux = (hasta[0] - desde[0]) / L, uy = (hasta[1] - desde[1]) / L;
    const haciaCentro = -uy * (centro[0] - desde[0]) + ux * (centro[1] - desde[1]);
    const ladoCentro = haciaCentro >= -1e-6 * L ? 1 : -1;
    return [volteado ? -ladoCentro : ladoCentro, (forma === 'GrapaS') !== volteado ? -ladoCentro : ladoCentro];
  }

  // GanchoGrapa.Linea con lo medido (0, r) en las dos puntas: el tramo recto, tangente a los dos dobleces.
  function linea(ci, cf, r, li, lf) {
    let a = ci, b = cf;
    for (let i = 0; i < 20; i++) {
      const L = Math.hypot(b[0] - a[0], b[1] - a[1]), ux = (b[0] - a[0]) / L, uy = (b[1] - a[1]) / L, lx = -uy, ly = ux;
      const na = [ci[0] - lx * li * r, ci[1] - ly * li * r], nb = [cf[0] - lx * lf * r, cf[1] - ly * lf * r];
      const quieto = Math.abs(na[0] - a[0]) + Math.abs(na[1] - a[1]) + Math.abs(nb[0] - b[0]) + Math.abs(nb[1] - b[1]) < 1e-9;
      a = na; b = nb;
      if (quieto) break;
    }
    return [a, b];
  }

  // PlantaArmado.Grapa: de la punta de la cola del inicio a la del fin, cada punta con su ángulo y su cola.
  function grapa(ci, cf, li, lf, angIni, angFin, d, doblez, colaIni, colaFin) {
    const r = doblez / 2 + d / 2;
    const [a, b] = linea(ci, cf, r, li, lf);
    const L = Math.hypot(b[0] - a[0], b[1] - a[1]), u = [(b[0] - a[0]) / L, (b[1] - a[1]) / L], l = [-u[1], u[0]];
    const gancho = (c, lado, fuera, angulo, cola) => {
      const punto = (g) => { const co = Math.cos(rad(g)), si = Math.sin(rad(g)); return [c[0] + r * (co * lado * l[0] + si * fuera[0]), c[1] + r * (co * lado * l[1] + si * fuera[1])]; };
      const desde = 180 - angulo, out = [];
      for (let g = desde; g <= 180 + 1e-9; g += PASO) out.push(punto(g));
      const cd = Math.cos(rad(desde)), sd = Math.sin(rad(desde));
      const dir = [sd * lado * l[0] - cd * fuera[0], sd * lado * l[1] - cd * fuera[1]];
      out.unshift([out[0][0] + dir[0] * cola, out[0][1] + dir[1] * cola]);
      return out;
    };
    const ini = gancho(ci, li, [-u[0], -u[1]], angIni, colaIni);
    const fin = gancho(cf, lf, u, angFin, colaFin).reverse();
    return [...ini, ...fin];
  }

  // EstriboMenor.CajaQueAbraza: la caja por fuera que deja todas las barras adentro, también en las curvas.
  function cajaQueAbraza(piezas, d, rInt) {
    let x1 = Math.min(...piezas.map((p) => p.x - p.r)) - d, x2 = Math.max(...piezas.map((p) => p.x + p.r)) + d;
    let y1 = Math.min(...piezas.map((p) => p.y - p.r)) - d, y2 = Math.max(...piezas.map((p) => p.y + p.r)) + d;
    let abrir = 0;
    for (const [sx, sy] of [[-1, -1], [1, -1], [-1, 1], [1, 1]]) {
      const cx = (sx < 0 ? x1 + d : x2 - d) - sx * rInt, cy = (sy < 0 ? y1 + d : y2 - d) - sy * rInt;
      for (const p of piezas) {
        const wx = p.x - cx, wy = p.y - cy;
        if (wx * sx <= 0 || wy * sy <= 0) continue;
        const libre = rInt - p.r;
        if (libre <= 0 || wx * wx + wy * wy <= libre * libre) continue;
        const ws = wx * sx + wy * sy, disc = ws * ws - 2 * (wx * wx + wy * wy - libre * libre);
        if (disc < 0) continue;
        abrir = Math.max(abrir, (ws - Math.sqrt(disc)) / 2);
      }
    }
    return [x1 - abrir, x2 + abrir, y1 - abrir, y2 + abrir];
  }

  // ---- La sección (SeccionPlanta): barras y piezas en cm, origen abajo a la izquierda, vista desde arriba

  function seccion(ctx, o) {
    const L = o.longitudinal;
    const B = ctx.ladoX * 100, H = ctx.ladoY * 100, rec = o.recubrimiento * 100;
    const dEst = varilla(o.estribos.tramosLibres.tipoBarraNombre).mm / 10;
    const dEsq = varilla(L.tipoBarraEsquinasNombre).mm / 10;
    const dX = varilla(L.tipoBarraCaraXNombre).mm / 10, dY = varilla(L.tipoBarraCaraYNombre).mm / 10;
    // Del borde al eje de la barra de esquina: metida en la curva del estribo, tocando su cara interior, como el generador.
    const vE = varilla(o.estribos.tramosLibres.tipoBarraNombre), dob = doblezDe(vE);
    const centroCurva = rec + dEst / 2 + dob / 2 + dEst / 2;
    const a = Math.max(rec + dEst + dEsq / 2, centroCurva - (dob / 2 - dEsq / 2) / Math.SQRT2);
    const barras = [];
    const barra = (x, y, nombre, posicion, eje, indice) => barras.push({ x: r2(x), y: r2(y), diametroMm: varilla(nombre).mm, color: COLOR[num(nombre)], nombre: '#' + num(nombre), posicion, eje, indice });
    barra(a, a, L.tipoBarraEsquinasNombre, 'esquina'); barra(B - a, a, L.tipoBarraEsquinasNombre, 'esquina');
    barra(a, H - a, L.tipoBarraEsquinasNombre, 'esquina'); barra(B - a, H - a, L.tipoBarraEsquinasNombre, 'esquina');
    // Intermedias, repartidas parejo entre las esquinas. Columna i: caras de abajo y arriba (caras X); fila i: izquierda y derecha (caras Y).
    const yF = (i) => a + ((H - 2 * a) * (i + 1)) / (L.barrasPorCaraY + 1);
    const xC = (i) => a + ((B - 2 * a) * (i + 1)) / (L.barrasPorCaraX + 1);
    for (let i = 0; i < L.barrasPorCaraX; i++) {
      barra(xC(i), rec + dEst + dX / 2, L.tipoBarraCaraXNombre, 'abajo', 'Columna', i);
      barra(xC(i), H - rec - dEst - dX / 2, L.tipoBarraCaraXNombre, 'arriba', 'Columna', i);
    }
    for (let i = 0; i < L.barrasPorCaraY; i++) {
      barra(rec + dEst + dY / 2, yF(i), L.tipoBarraCaraYNombre, 'izquierda', 'Fila', i);
      barra(B - rec - dEst - dY / 2, yF(i), L.tipoBarraCaraYNombre, 'derecha', 'Fila', i);
    }
    const e = rec + dEst / 2;
    const vEst = varilla(o.estribos.tramosLibres.tipoBarraNombre);
    // Las dos barras que engancha la fila o columna k: la de la izquierda (o abajo) y la de enfrente.
    const barrasDe = (eje, k) => {
      const ini = barras.find((b) => b.eje === eje && b.indice === k && (b.posicion === 'izquierda' || b.posicion === 'abajo'));
      const fin = barras.find((b) => b.eje === eje && b.indice === k && (b.posicion === 'derecha' || b.posicion === 'arriba'));
      return ini && fin ? [ini, fin] : null;
    };
    const menores = L.estribosMenores.map((m, indice) => {
      const v = varilla(m.tipoBarraNombre || o.estribos.tramosLibres.tipoBarraNombre);
      const dm = v.mm / 10, doblez = doblezDe(v);
      const rango = []; for (let k = m.desde; k <= m.hasta; k++) rango.push(barrasDe(m.eje, k));
      const base = { indice, forma: m.forma, eje: m.eje, desde: m.desde, hasta: m.hasta, color: COLOR[num(m.tipoBarraNombre || o.estribos.tramosLibres.tipoBarraNombre)] };
      if (rango.some((p) => !p)) return { ...base, puntos: [], puntas: null, valida: false, motivo: 'Esa barra ya no existe con la cantidad de barras actual.' };
      let puntos, valida = true, motivo = null;
      if (m.forma === 'Cerrado') {
        const piezas = rango.flat().map((b) => ({ x: b.x, y: b.y, r: b.diametroMm / 20 }));
        const [x1, x2, y1, y2] = cajaQueAbraza(piezas, dm, doblez / 2);
        puntos = estriboCerrado(x1, x2, y1, y2, dm, doblez, cola135(v));
        // EstriboMenor.AnchoMinimoCerradoMm: en el lado corto entran dos dobleces de 90°.
        if (Math.min(x2 - x1, y2 - y1) - dm < doblez + dm) { valida = false; motivo = 'Muy angosto: Revit no puede doblar este estribo cerrado. Abarca una barra más.'; }
      } else {
        const [bi, bf] = rango[0];
        const L0 = Math.hypot(bf.x - bi.x, bf.y - bi.y), u = [(bf.x - bi.x) / L0, (bf.y - bi.y) / L0];
        const libre = Math.max(0, doblez / 2 - bi.diametroMm / 20);
        const ci = [bi.x + u[0] * libre, bi.y + u[1] * libre], cf = [bf.x - u[0] * libre, bf.y - u[1] * libre];
        const [li, lf] = lados([bi.x, bi.y], [bf.x, bf.y], [B / 2, H / 2], m.forma, m.volteado);
        puntos = grapa(ci, cf, li, lf, m.anguloInicio, m.anguloFin, dm, doblez, colaDe(m.anguloInicio, v), colaDe(m.anguloFin, v));
      }
      const puntas = m.forma === 'Cerrado' ? null : rango[0].map((b) => [b.x, b.y]);
      return { ...base, puntos: puntos.map(([x, y]) => [r2(x), r2(y)]), puntas, grosor: r2(dm), valida, motivo };
    });
    const puntosEstribo = estriboCerrado(rec, B - rec, rec, H - rec, dEst, doblezDe(vEst), cola135(vEst)).map(([x, y]) => [r2(x), r2(y)]);
    return {
      ancho: r2(B), alto: r2(H), recubrimiento: r2(rec),
      estribo: { x1: r2(e), y1: r2(e), x2: r2(B - e), y2: r2(H - e), puntos: puntosEstribo, color: COLOR[num(o.estribos.tramosLibres.tipoBarraNombre)], grosor: r2(dEst) },
      barras, menores,
    };
  }

  // ---- Zonas de estribos (RepartoEstribos)

  const configDe = (o, t) => o.estribos.excepciones[t.indice] || null;
  function zonas(ctx, o) {
    const E = o.estribos, libres = E.tramosLibres;
    const out = [];
    for (const t of ctx.tramos) {
      const h = t.zSup - t.zInf, exc = configDe(o, t);
      if (t.tipo === 'ContraZapata') { out.push({ indice: t.indice, zInf: t.zInf, zSup: t.zSup, zona: 'zapata', cada: null }); continue; }
      if (t.tipo === 'DentroDeViga') {
        const cada = exc ? exc.espaciadoUniforme : (libres.modo === 'Uniforme' ? libres.espaciadoUniforme : E.nudoEspaciado);
        out.push({ indice: t.indice, zInf: t.zInf, zSup: t.zSup, zona: 'nudo', cada });
        continue;
      }
      const c = exc || libres;
      if (c.modo === 'Uniforme') { out.push({ indice: t.indice, zInf: t.zInf, zSup: t.zSup, zona: 'uniforme', cada: c.espaciadoUniforme }); continue; }
      const l1 = Math.min(c.longitudConfinamiento, h / 2);
      out.push({ indice: t.indice, zInf: t.zInf, zSup: r2(t.zInf + l1), zona: 'confinada', cada: c.espaciadoConfinado });
      if (h - 2 * l1 > 0.01) out.push({ indice: t.indice, zInf: r2(t.zInf + l1), zSup: r2(t.zSup - l1), zona: 'central', cada: c.espaciadoNoConfinado });
      out.push({ indice: t.indice, zInf: r2(t.zSup - l1), zSup: t.zSup, zona: 'confinada', cada: c.espaciadoConfinado });
    }
    return out.map((z) => ({ ...z, cantidad: z.cada ? Math.max(1, Math.floor((z.zSup - z.zInf) / z.cada + 1e-6)) : 0 }));
  }

  // ---- Traslapos (PlanTraslapos): plan A y plan B, cada varilla a lo sumo del largo comercial

  // Como PlanTraslapos.Calcular: las barras del anillo se alternan (pares A, impares B, ConfigLongitudinal.Anillo);
  // A traslapa en los tramos libres pares y B en los impares (si un grupo no tiene tramos propios, usa los del otro), y
  // dentro de cada grupo hay un plan por número de varilla, porque cada uno tiene su solape. Simplificado: el cálculo
  // real busca el plan con menos traslapos entre todos los válidos.
  function traslapos(ctx, o) {
    const L = o.longitudinal;
    const anillo = [];
    for (let vuelta = 0; vuelta < 2; vuelta++) {
      anillo.push(L.tipoBarraEsquinasNombre);
      for (let i = 0; i < L.barrasPorCaraX; i++) anillo.push(L.tipoBarraCaraXNombre);
      anillo.push(L.tipoBarraEsquinasNombre);
      for (let i = 0; i < L.barrasPorCaraY; i++) anillo.push(L.tipoBarraCaraYNombre);
    }
    const mayor = Math.max(...anillo.map((n) => varilla(n).traslapo || 0));
    const solapeDe = (nombre) => (L.solapeMayorDeLosDos ? mayor : varilla(nombre).traslapo || 0);
    const zIni = ctx.zInferiorLongitudinal, zFin = r2(ctx.zSuperior - o.recubrimiento);
    const libres = ctx.tramos.filter((t) => t.tipo === 'Libre');
    const redondear = (x) => Math.ceil(x / L.redondeoVarillas - 1e-9) * L.redondeoVarillas;
    const tramosDe = (grupo) => {
      const propios = libres.filter((t, i) => (i % 2 === 0) === (grupo === 'A'));
      return propios.length ? { tramos: propios, delOtro: false } : { tramos: libres, delOtro: true };
    };
    function plan(grupo, barra, cantidad) {
      const s = solapeDe(barra), { tramos, delOtro } = tramosDe(grupo);
      const base = { nombre: grupo, barra: '#' + num(barra), color: COLOR[num(barra)], cantidad, solape: s, enTramosDelOtro: delOtro };
      // El traslapo, centrado en el tramo libre; si usa los tramos del otro grupo, corrido para no pisar sus traslapos.
      const candidatos = tramos.map((t) => {
        const m = (t.zInf + t.zSup) / 2 + (delOtro ? s + 0.05 : 0);
        return { zInf: m - s / 2, zSup: m + s / 2, tramo: t };
      });
      // Como el cálculo real (gana el de menos exceso de solape): la varilla se redondea y el traslapo se corre dentro
      // del tramo libre para quedar con el solape justo; solo si no entra en el tramo, se alarga.
      const ubicar = (c, fin) => (fin - s >= c.tramo.zInf + 0.05 && fin <= c.tramo.zSup - 0.05 ? fin - s : Math.min(c.zInf, fin - s));
      const varillas = [], laps = [];
      let inicio = zIni;
      for (let vueltas = 0; vueltas < 20; vueltas++) {
        if (zFin - inicio <= L.longitudComercialBarra + 1e-9) {
          varillas.push({ zInf: r2(inicio), zSup: zFin, largo: r2(redondear(zFin - inicio)) });
          return { ...base, varillas, traslapos: laps, sinSolucion: false };
        }
        const cabe = candidatos.filter((c) => c.zInf > inicio + 0.5 && redondear(c.zSup - inicio) <= L.longitudComercialBarra);
        if (!cabe.length) break;
        const c = cabe[cabe.length - 1];
        const largo = redondear(c.zSup - inicio), fin = inicio + largo, desde = ubicar(c, fin);
        varillas.push({ zInf: r2(inicio), zSup: r2(fin), largo: r2(largo) });
        laps.push({ zInf: r2(desde), zSup: r2(fin), largo: r2(fin - desde) });
        inicio = desde;
      }
      return { ...base, varillas, traslapos: laps, sinSolucion: true };
    }
    const planes = [];
    for (const grupo of ['A', 'B']) {
      const cuenta = {};
      anillo.forEach((n, i) => { if ((i % 2 === 0) === (grupo === 'A')) cuenta[n] = (cuenta[n] || 0) + 1; });
      Object.keys(cuenta).sort((x, y) => num(y) - num(x)).forEach((n) => planes.push(plan(grupo, n, cuenta[n])));
    }
    return { planes, zInicio: zIni, zFin };
  }

  // ---- Choques (ChoquesSeccion): dos piezas en la misma fila o columna se enciman

  function choques(o) {
    const vistos = {}, out = [];
    for (const m of o.longitudinal.estribosMenores) {
      for (let i = m.desde; i <= m.hasta; i++) {
        const k = m.eje + i;
        if (vistos[k]) out.push(`Dos piezas pasan por la ${m.eje === 'Fila' ? 'fila' : 'columna'} ${i + 1}: en Revit quedan una encima de la otra.`);
        vistos[k] = true;
      }
    }
    return [...new Set(out)];
  }

  // ---- La sesión

  function crear(estado) {
    const s = {
      porColumna: {}, // id -> opciones propias
      estados: { 'C-1': 'generada' }, // id -> 'lista' | 'generada'
      elegidas: ['C-12'],
      actuales: copia(OPCIONES0), // lo que muestra el panel si la columna no tiene opciones propias
      guardadas: copia(GUARDADAS0),
      configuracion: null,
      generacion: { choques: [], pendiente: false, resultado: null, error: null },
      aviso: null,
    };
    if (estado === 'simple') s.elegidas = ['C-1'];
    if (estado === 'varias') { s.elegidas = ['C-12', 'C-13', 'C-7']; s.estados['C-14'] = 'lista'; s.porColumna['C-14'] = copia(OPCIONES0); }
    if (estado !== 'simple' && estado !== 'varias') { s.estados['C-14'] = 'lista'; s.porColumna['C-14'] = copia(OPCIONES0); }

    const activa = () => s.elegidas[0] || null;
    const opcionesDe = (id) => s.porColumna[id] || s.actuales;
    const compatibles = () => { const f = activa() && firma(activa()); return s.elegidas.filter((id) => firma(id) === f); };

    // Lo que cambia el usuario cae en todas las compatibles y las deja "listas" (decisiones 7 y 8 del plan).
    function cambiar(fn) {
      const id = activa();
      if (!id) return;
      const nueva = copia(opcionesDe(id));
      fn(nueva);
      s.actuales = nueva;
      for (const c of compatibles()) { s.porColumna[c] = copia(nueva); s.estados[c] = 'lista'; }
      s.generacion.resultado = null;
    }

    function vista() {
      const id = activa(), ctx = id ? contexto(id) : null, o = id ? opcionesDe(id) : s.actuales;
      let columna = null;
      if (ctx) {
        const z = zonas(ctx, o), sec = seccion(ctx, o);
        columna = {
          id, marca: ctx.marca, ubicacion: ctx.ubicacion, baseX: ctx.ladoX, baseY: ctx.ladoY, recubrimiento: o.recubrimiento,
          zSuperior: ctx.zSuperior, zInferiorLongitudinal: ctx.zInferiorLongitudinal, zapata: ctx.zapata, niveles: ctx.niveles,
          tramos: ctx.tramos.map((t) => ({ ...t, excepcion: !!o.estribos.excepciones[t.indice] })),
          seccion: sec, zonasEstribos: z, traslapos: traslapos(ctx, o), avisos: ctx.avisos,
          resumen: {
            barras: sec.barras.length,
            estribos: z.reduce((n, k) => n + k.cantidad, 0),
            grapas: o.longitudinal.estribosMenores.filter((m) => m.forma !== 'Cerrado').length,
            cerrados: o.longitudinal.estribosMenores.filter((m) => m.forma === 'Cerrado').length,
            pisos: ctx.tramos.filter((t) => t.tipo === 'Libre').length,
          },
          // Lo que trae la columna en Revit: en la maqueta, uno distinto de los 4 cm para que se vea el enlace.
          recubrimientosRevit: [0.025],
        };
      }
      const comp = compatibles();
      return {
        planta: {
          rejillas: REJILLAS,
          columnas: COLUMNAS.map((c) => ({ id: c.id, marca: c.marca, ubicacion: c.ubicacion, x: c.x, y: c.y, ladoX: c.ladoX, ladoY: c.ladoY, estado: s.estados[c.id] || 'sinConfigurar' })),
        },
        elegidas: s.elegidas.slice(),
        compatibles: comp,
        descartadas: s.elegidas.filter((e) => !comp.includes(e)).map((e) => contexto(e).marca),
        columna,
        opciones: copia(o),
        catalogos: {
          barrasLongitudinales: [4, 5, 6, 7, 8].map((n) => ({ nombre: '#' + n, diametroMm: varilla('#' + n).mm, color: COLOR[n] })),
          barrasEstribo: [2, 3, 4, 5].map((n) => ({ nombre: '#' + n, diametroMm: varilla('#' + n).mm, color: COLOR[n] })),
          largosComerciales: [6, 12], redondeos: [0.10, 0.20, 0.25, 0.50, 1.00],
          cuadro: CUADRO.map((v) => ({ ...v, nombre: '#' + v.n, color: COLOR[v.n] })),
          configuraciones: Object.keys(s.guardadas),
        },
        configuracion: s.configuracion,
        generacion: { ...s.generacion, listas: Object.keys(s.estados).filter((k) => s.estados[k] === 'lista').map((k) => contexto(k).marca) },
        aviso: s.aviso,
      };
    }

    function atender(m) {
      s.aviso = null;
      const L = (fn) => cambiar((o) => fn(o.longitudinal));
      switch (m.tipo) {
        case 'elegirColumnas': s.elegidas = m.ids.slice(); s.configuracion = null; break;
        case 'quitarDeLista': if (s.estados[m.id] === 'lista') { delete s.estados[m.id]; delete s.porColumna[m.id]; } break;
        case 'seleccionarEnRevit': s.aviso = 'En Revit: elige columnas en el modelo y pulsa «Terminar». En esta maqueta no hay Revit.'; break;
        case 'cambiarLongitudinal': L((l) => Object.assign(l, m.config)); break;
        case 'cambiarRecubrimiento': cambiar((o) => { o.recubrimiento = m.valor; }); break;
        case 'cambiarEstribos': cambiar((o) => {
          const e = o.estribos;
          if (m.tramosLibres) Object.assign(e.tramosLibres, m.tramosLibres);
          if (m.nudoEspaciado != null) e.nudoEspaciado = m.nudoEspaciado;
          if (m.excepcion) {
            const { indice, config } = m.excepcion;
            if (config) e.excepciones[indice] = config; else delete e.excepciones[indice];
          }
        }); break;
        case 'menor': L((l) => {
          const lista = l.estribosMenores;
          if (m.accion === 'agregar') lista.push({ forma: m.forma, eje: m.eje, desde: Math.min(m.desde, m.hasta), hasta: Math.max(m.desde, m.hasta), volteado: false, anguloInicio: 180, anguloFin: 180, tipoBarraNombre: null });
          if (m.accion === 'eliminar') lista.splice(m.indice, 1);
          if (m.accion === 'voltear') lista[m.indice].volteado = !lista[m.indice].volteado;
          if (m.accion === 'varilla') lista[m.indice].tipoBarraNombre = m.nombre || null;
          if (m.accion === 'angulo') lista[m.indice][m.punta === 'inicio' ? 'anguloInicio' : 'anguloFin'] = m.angulo;
        }); break;
        case 'ver3D': s.aviso = `En Revit se abre la vista 3D «CQEING Columnas 3D» mostrando: ${m.filtro}. Se actualiza sola con cada cambio.`; break;
        case 'aplicarConfiguracion': { const g = s.guardadas[m.nombre]; if (g) { cambiar((o) => Object.assign(o, copia(g))); s.configuracion = m.nombre; s.aviso = `Se aplicó «${m.nombre}».`; } break; }
        case 'guardarConfiguracion': { const id = activa(); s.guardadas[m.nombre] = copia(opcionesDe(id)); s.configuracion = m.nombre; s.aviso = m.comoNueva ? `Se guardó «${m.nombre}» en el modelo.` : `Se guardaron los cambios de «${m.nombre}».`; break; }
        case 'borrarConfiguracion': delete s.guardadas[m.nombre]; if (s.configuracion === m.nombre) s.configuracion = null; s.aviso = `Se eliminó «${m.nombre}» del modelo.`; break;
        case 'generar': {
          const listas = Object.keys(s.estados).filter((k) => s.estados[k] === 'lista');
          const ch = [...new Set(listas.flatMap((k) => choques(opcionesDe(k))))];
          if (ch.length && !m.aunConChoques) { s.generacion = { choques: ch, pendiente: true, resultado: null, error: null }; break; }
          for (const k of listas) s.estados[k] = 'generada';
          const marcas = listas.map((k) => contexto(k).marca);
          s.generacion = {
            choques: [], pendiente: false, error: null,
            resultado: { detalles: [
              marcas.length === 1 ? `Se armó la columna ${marcas[0]}.` : `Se armaron ${marcas.length} columnas: ${marcas.join(', ')}.`,
              m.borrarAjeno ? 'Se reemplazó todo el acero que había en esas columnas.' : 'Se reemplazó el acero que el add-in había generado antes en esas columnas.',
            ] },
          };
          break;
        }
        case 'cancelarGenerar': s.generacion = { choques: [], pendiente: false, resultado: null, error: null }; break;
        default: break;
      }
      return vista();
    }

    return { vista, atender };
  }

  window.DemoColumnas = { crear };
})();
