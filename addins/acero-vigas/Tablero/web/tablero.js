// El tablero de vigas: el port de Main.dc.html de la maqueta (versión 19) para Revit (#83), más el puente con
// window.chrome.webview. Mismo DOM y mismos estilos que la maqueta (decisión 1 del plan
// docs/planes/2026-10-16-tablero-vigas-83.md). La página no calcula acero (decisión 5): pinta `props.vista`, que arma el
// motor en C#, y le manda lo que el usuario cambia con `props.enviar`.
(function () {
  'use strict';
  const { html, render, Component } = window.htmPreact;
  const Alzado = window.Alzado;

  const num = (x, d) => { const v = parseFloat(String(x).replace(',', '.')); return Number.isFinite(v) ? v : d; };
  const f2 = (n) => (Math.round(n * 100) / 100).toFixed(2);
  const mm = (m) => Math.round(m * 1000);
  const diametro = (d) => parseInt(String(d).slice(1), 10);
  // Los avisos con los que el motor descarta un estribo adicional (TipoAviso en C#).
  const DESCARTA = ['EstriboIncompleto', 'BarraNoExiste', 'MueveUnaEsquina', 'NoSePuedeAlinear', 'IgualAlCerrado', 'RepiteOtroAdicional'];
  const PULGADAS = { 3: '3/8"', 4: '1/2"', 5: '5/8"', 6: '3/4"', 7: '7/8"', 8: '1"' };

  // Las opciones de una lista de diámetros (#85, tarea 10): solo los que tienen tipo de barra en el modelo
  // (`diametrosDisponibles`); el elegido se queda aunque no esté, para que la lista no cambie sola lo que dice.
  function opcionesDiametro(disponibles, desde, hasta, actual, conPulgadas) {
    const ns = [];
    for (let n = desde; n <= hasta; n++) if (!disponibles || disponibles.includes(n) || '#' + n === actual) ns.push(n);
    return ns.map((n) => html`<option value=${'#' + n}>${'#' + n + (conPulgadas ? ' · ' + PULGADAS[n] : '')}</option>`);
  }

  // Lo que el usuario elige para un pórtico: el `state` de la maqueta que cambia el cálculo (OpcionesTablero).
  const OPCIONES0 = {
    // vigueta (#940): undefined = la que detectó el add-in; true / false = corregida a mano con la casilla
    tipo: undefined, vigueta: undefined, side: undefined, sup: [{ n: 3, d: '#5' }], inf: [{ n: 3, d: '#5' }],
    rec: '4', recUp: '', recDown: '', recSide: '', sepMode: 'pegada', sepCm: '1', maxCapas: '3',
    bastDef: '2.00', bastHook: false, bastLen: {}, bastSupN: '1', bastSupD: '', bastInfN: '1', bastInfD: '',
    // adicionales (#85, hallazgo E8): [{ tipo: 'Cerrado' | 'Abierto', barras: [{ cara, fila, numero, nombre }] }]
    est: { estD: '#3', estG: '135°', conf: '0.80', s1: '0.10', s2: '0.20', adicionales: [], forma: 'S' }, estMarc: null, tramosSel: [],
    // corte 1-1 (#91): cutX vacío = la vista tipo que elige C#
    cutX: '', depth: '0.10', mira: 'derecha',
    // edición (#84): CambiosManuales en mm (barras y traslapos ya; movidos y ganchos, fases B y C) y bastones arrastrados
    cambios: { barras: {}, traslapos: {}, movidos: {}, ganchos: {} }, bastPuntas: {},
  };
  // Con lo que arranca un pórtico que el usuario no tocó, como OpcionesTablero.Para en C# (#942): una vigueta de 10 cm
  // no admite las barras ni el recubrimiento de una viga.
  const VIGUETA0 = { sup: [{ n: 1, d: '#4' }], inf: [{ n: 1, d: '#4' }], rec: '3' };
  const arranqueDe = (vigueta) => (vigueta ? VIGUETA0 : { sup: OPCIONES0.sup, inf: OPCIONES0.inf, rec: OPCIONES0.rec });
  const sinTocar = (s, vigueta) => {
    const a = arranqueDe(vigueta);
    return JSON.stringify([s.sup, s.inf, s.rec]) === JSON.stringify([a.sup, a.inf, a.rec]);
  };
  // El estado de la página con que se generó el pórtico (VistaPortico.Estado, guardado en sus vigas): solo las claves de
  // OPCIONES0. null si no hay o no se entiende.
  const estadoGuardado = (actual) => {
    if (!actual || !actual.estado) return null;
    try {
      const e = JSON.parse(actual.estado);
      return Object.keys(e).filter((k) => k in OPCIONES0).reduce((o, k) => ({ ...o, [k]: e[k] }), {});
    } catch (err) {
      return null;
    }
  };
  // Ancho de la ventana del corte que sale al arrastrar la línea 1-1 (decisión 10 del plan docs/planes/2026-10-17-corte-vigas-91.md).
  const ANCHO_CORTE = 260;
  const CLAVES_OPCIONES = Object.keys(OPCIONES0);
  // Sin nada elegido en el alzado: no sale el panel de barra, traslapo ni bastón, ni se pide vista previa.
  const NADA = Object.freeze({ k: 'nada' });

  // Las opciones de la página con la forma de OpcionesTablero (tarea 2); null si algún campo no es número: así no se
  // manda y el último cálculo válido sigue en pantalla, como el num(x, d) de la maqueta.
  function opcionesDe(s) {
    const n = (x) => { const v = num(x, NaN); if (!Number.isFinite(v)) throw new Error(x); return v; };
    const opcional = (x) => (String(x).trim() === '' ? null : n(x));
    // la fila a mano de cada tipo (#84): '' = Auto
    const grupos = (rows) => rows.map((r) => ({ cantidad: r.n, diametro: diametro(r.d), fila: r.f ? Number(r.f) : null }));
    const estribos = (e) => ({ diametro: diametro(e.estD), gancho: ({ '135°': 'De135', '180°': 'De180' })[e.estG] || 'De90',
      largoZona: mm(n(e.conf)), separacionZona: mm(n(e.s1)), separacionCentro: mm(n(e.s2)), forma: e.forma || 'S',
      adicionales: (e.adicionales || []).map((a) => ({ tipo: a.tipo, forma: a.forma || 'C',
        barras: a.barras.map((b) => ({ cara: b.cara, fila: b.fila, numero: b.numero })) })) });
    const baston = (cant, d, filas) => ({ cantidad: Math.max(0, Math.round(n(cant))), diametro: diametro(d || filas[0].d) });
    try {
      const largos = {};
      Object.keys(s.bastLen).forEach((id) => { largos[id] = mm(n(s.bastLen[id])); });
      return {
        tipo: s.tipo ? (s.tipo === 'Cimentación' ? 'Cimentacion' : 'Aerea') : null,
        elemento: s.vigueta === undefined ? null : (s.vigueta ? 'Vigueta' : 'Viga'),
        superior: grupos(s.sup), inferior: grupos(s.inf),
        recubrimientoCm: n(s.rec), recArribaCm: opcional(s.recUp), recAbajoCm: opcional(s.recDown), recLadosCm: opcional(s.recSide),
        separacionFilasCm: s.sepMode === 'pegada' ? null : n(s.sepCm), maximoFilas: Math.max(1, Math.round(n(s.maxCapas))),
        cara: s.side ? (s.side === 'izq' ? 'Inicio' : 'Final') : null,
        bastones: { superior: baston(s.bastSupN, s.bastSupD, s.sup), inferior: baston(s.bastInfN, s.bastInfD, s.inf),
          largo: mm(n(s.bastDef)), conGancho: s.bastHook, largos, puntas: s.bastPuntas },
        estribos: estribos(s.est),
        tramosMarcados: s.tramosSel.slice(),
        estribosMarcados: s.tramosSel.length && s.estMarc ? estribos(s.estMarc) : null,
        cambios: { barras: s.cambios.barras, traslapos: s.cambios.traslapos, ganchos: s.cambios.ganchos, movidos: s.cambios.movidos },
        corte: { x: String(s.cutX).trim() === '' ? null : mm(n(s.cutX)), profundidad: mm(n(s.depth)),
          mirando: s.mira === 'izquierda' ? 'Izquierda' : 'Derecha' },
      };
    } catch (e) {
      return null;
    }
  }

  // El texto de la confirmación del voladizo (#84): dónde quedaría cada traslapo corrido y en qué voladizo.
  const confirmVolTexto = (salto, P) => {
    if (!salto || !P) return '';
    const ap = P.apoyos;
    const partes = salto.cambios.filter((c) => c.tipo === 'movido').map((c) => {
      const t = P.traslapos.find((x) => x.id === c.id), desde = c.desde / 1000;
      return { id: c.id, desde, hasta: desde + (t ? t.hasta - t.desde : 0) };
    });
    if (!partes.length) return '';
    const eje = partes[0].desde > ap[ap.length - 1].x ? ap[ap.length - 1].eje : ap[0].eje;
    return 'El traslapo ' + partes.map((x) => x.id + ' quedaría de ' + f2(x.desde) + ' a ' + f2(x.hasta) + ' m').join(' y ')
      + ', dentro del voladizo ' + eje + '. El add-in nunca lo pone ahí: solo queda si lo confirmas.';
  };

  // "Hay 7 choques entre barras en Pórtico 1 (y 3 en Pórtico 2)." con los nombres de la lista.
  const textoChoquesGen = (porticos, lista) => {
    const nombre = (id) => (lista.find((p) => p.id === id) || { nombre: id }).nombre;
    const cuantos = (n) => n + (n === 1 ? ' choque' : ' choques');
    const [primero, ...resto] = porticos;
    return 'Hay ' + cuantos(primero.cantidad) + ' entre barras en ' + nombre(primero.id)
      + (resto.length ? ' (y ' + resto.map((p) => p.cantidad + ' en ' + nombre(p.id)).join(', ') + ')' : '')
      + '. Puedes revisarlos antes o generar de todos modos.';
  };

  // La sección del corte 1-1 (#91), con el factor de `sec` (ver seccionA en valores): la usan la pestaña y la ventana.
  const dibujoSeccion = (sec) => html`
    <div style="position: relative; width: ${sec.wpx}; height: ${sec.hpx}; flex-shrink: 0; background: #F1F3F5; border: 1px solid #9AA3AD; box-sizing: border-box">
      ${sec.stOn ? html`<div style="position: absolute; left: ${sec.stL}; top: ${sec.stT}; right: ${sec.stR}; bottom: ${sec.stB}; border: ${sec.stW} solid ${sec.stC}; border-radius: ${sec.stRadio}"></div>` : null}
      ${sec.adds.map((sh) => html`
        <div style="position: absolute; left: ${sh.l}; top: ${sh.t}; width: ${sh.w}; height: ${sh.h}; border: ${sh.bd}; border-radius: ${sh.r}; background: ${sh.bg}; box-sizing: border-box; pointer-events: none"></div>`)}
      ${sec.trazos.length ? html`
        <svg aria-hidden="true" width=${sec.wpx} height=${sec.hpx} style="position: absolute; left: 0; top: 0; overflow: visible; pointer-events: none">
          ${sec.trazos.map((t) => html`<polyline points=${t.pts} fill="none" stroke=${t.c} stroke-width=${t.w} stroke-linejoin="round" stroke-linecap="butt" />`)}
        </svg>` : null}
      ${sec.proy.map((d) => html`
        <div role="img" aria-label=${d.label} style="position: absolute; left: ${d.x}; top: ${d.y}; width: ${d.s}; height: ${d.s}; border: 1.5px dashed #5E6670; border-radius: 50%; box-sizing: border-box"></div>`)}
      ${sec.dots.map((d) => html`
        <button type="button" aria-label=${d.label} onClick=${d.pick} style="position: absolute; left: ${d.x}; top: ${d.y}; width: ${d.s}; height: ${d.s}; padding: 0; border: 0; border-radius: 50%; background: ${d.c}; outline: ${d.ring}; outline-offset: 2px; cursor: ${d.cur}"></button>`)}
    </div>`;

  // La ventana del corte que sale encima del alzado al tomar la línea 1-1 (decisión 10): se mueve por su barra, sin
  // salirse del alzado, y se oculta con ×.
  class VentanaCorte extends Component {
    render() {
      const { v, W, H } = this.props;
      const pos = v.cutWinPos || { x: W - ANCHO_CORTE - 12, y: 40 };
      const x = Math.max(0, Math.min(pos.x, W - ANCHO_CORTE)), y = Math.max(0, Math.min(pos.y, H - 40));
      const down = (e) => {
        if (e.button !== 0 || e.target.closest('button')) return;
        e.currentTarget.setPointerCapture(e.pointerId);
        this.drag = { cx: e.clientX, cy: e.clientY, x, y };
      };
      const move = (e) => { const d = this.drag; if (d) v.moveCutWin({ x: d.x + e.clientX - d.cx, y: d.y + e.clientY - d.cy }); };
      const up = () => { this.drag = null; };
      return html`
<div role="dialog" aria-label="Corte 1-1" style="position: absolute; left: ${x}px; top: ${y}px; width: ${ANCHO_CORTE}px; display: flex; flex-direction: column; background: #FFFFFF; border: 1px solid #C9CFD6; border-radius: 6px; box-shadow: 0 6px 18px rgba(22, 25, 29, 0.18); z-index: 5">
  <div onPointerDown=${down} onPointerMove=${move} onPointerUp=${up} onPointerCancel=${up} style="display: flex; align-items: center; gap: 8px; height: 30px; padding: 0 4px 0 10px; border-bottom: 1px solid #D9DDE2; cursor: move; user-select: none; touch-action: none; white-space: nowrap">
    <span style="font-weight: 600">Corte 1-1</span>
    <span style="font-family: 'IBM Plex Mono', monospace; font-size: 12px">${v.cutPos} m</span>
    <span style="font-size: 12px; color: #5E6670">${v.cutTramo}</span>
    <div style="flex: 1"></div>
    ${v.cutTipo ? html`<span title="La sección que más se repite en el pórtico" style="font-size: 11px; font-weight: 600; color: #1E7A46">Más común</span>` : null}
    <button type="button" aria-label="Ocultar el corte" onClick=${v.hideCutWin} style="width: 24px; height: 24px; padding: 0; border: 0; border-radius: 4px; background: transparent; color: #3F4A57; cursor: pointer; display: grid; place-items: center">
      <svg width="12" height="12" viewBox="0 0 12 12" aria-hidden="true"><path d="M2.5 2.5l7 7M9.5 2.5l-7 7" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"></path></svg>
    </button>
  </div>
  <div style="display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 10px">
    ${dibujoSeccion(v.secMini)}
    <span style="font-size: 12px; color: #5E6670">Mirando a la ${v.cutMira} · proyección ${v.cutProf} m</span>
    ${v.cutAviso ? html`<span role="status" style="font-size: 12px; color: #B42318">${v.cutAviso}</span>` : null}
  </div>
</div>`;
    }
  }

  // Planta (opción D, 27-oct): la barra izquierda tiene dos modos, Lista (la de siempre) y Planta, donde crece a 440 px
  // y muestra el nivel en planta para abrir un pórtico con un clic. Los datos llegan en `vista.planta` (C#).
  const ANCHO_LISTA = 260, ANCHO_PLANTA = 440, ALTO_PLANTA = 400, MARGEN_PLANTA = 30;
  const TAB_ON = 'background: #1F5FAF; color: #FFFFFF', TAB_OFF = 'background: #FFFFFF; color: #16191D';
  const CHIP_ON = 'border: 1px solid #1F5FAF; background: #E8F0FA; color: #1F5FAF; font-weight: 600';
  const CHIP_OFF = 'border: 1px solid #C9CFD6; background: #FFFFFF; color: #5E6670';

  // Los elementos del nivel que se ven elegidos: el del pórtico abierto y, si es una vigueta con grupo, todo su grupo.
  function elegidosEnPlanta(nivel, id) {
    const els = nivel.elementos || [];
    const el = id == null ? null : els.find((e) => e.portico === id);
    if (!el) return [];
    return el.tipo === 'vigueta' && el.grupo ? els.filter((e) => e.grupo === el.grupo) : [el];
  }

  // La escala del dibujo: el rectángulo de todo el nivel (elementos, columnas y ejes) cabe en la caja con 30 px de
  // margen; el zoom escala desde el centro y `pan` lo corre en px (arrastrar). El modelo tiene la Y hacia arriba: en
  // pantalla se invierte.
  function escalaDePlanta(nivel, W, H, zoom, pan = { x: 0, y: 0 }) {
    const xs = [], ys = [];
    const pon = (x, y) => { if (Number.isFinite(x) && Number.isFinite(y)) { xs.push(x); ys.push(y); } };
    (nivel.elementos || []).forEach((e) => { pon(e.x1, e.y1); pon(e.x2, e.y2); });
    (nivel.ejes || []).forEach((e) => { pon(e.x1, e.y1); pon(e.x2, e.y2); });
    (nivel.columnas || []).forEach((c) => (c.puntos || []).forEach((p) => pon(p[0], p[1])));
    if (!xs.length) return null;
    const menor = (a) => a.reduce((m, x) => Math.min(m, x), Infinity), mayor = (a) => a.reduce((m, x) => Math.max(m, x), -Infinity);
    const x0 = menor(xs), x1 = mayor(xs), y0 = menor(ys), y1 = mayor(ys);
    const s = Math.min((W - 2 * MARGEN_PLANTA) / Math.max(x1 - x0, 1), (H - 2 * MARGEN_PLANTA) / Math.max(y1 - y0, 1)) * zoom;
    const cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
    return { s, X: (x) => W / 2 + pan.x + (x - cx) * s, Y: (y) => H / 2 + pan.y - (y - cy) * s };
  }

  class Tablero extends Component {
    constructor(props) {
      super(props);
      this.state = Object.assign({ tab: 'alzado', toast: '', q: '', ver: 'todo', listOpen: false, more: false, rulesWarn: false, rulesSaved: null,
        cutLive: null, cutWin: false, cutWinPos: null, draft: '', locked: true, rules: null, mult: null, gap: null, bar12: null, hookR: null, guardando: false,
        vw: window.innerWidth, vh: window.innerHeight, choqueAbierto: -1, arreglos: null, deshacer: null,
        // la barra izquierda: 'lista' o 'planta', el nivel elegido (null = el del pórtico abierto), lo que se muestra y el zoom
        barra: 'lista', plNivel: null, plVer: { viga: true, vigueta: true }, plZoom: 1, plPan: { x: 0, y: 0 } },
      OPCIONES0, props.inicial || {});
      this.opcionesPorPortico = {};
      this.alCambiarTamano = () => this.setState({ vw: window.innerWidth, vh: window.innerHeight });
    }

    componentDidMount() {
      window.addEventListener('resize', this.alCambiarTamano);
    }

    // Lo que el usuario eligió para el pórtico abierto (las claves de OPCIONES0).
    estadoDeLaPagina() {
      return CLAVES_OPCIONES.reduce((o, k) => ({ ...o, [k]: this.state[k] }), {});
    }

    // El estado de la página de cada pórtico que se genera, como texto, para guardarlo en sus vigas: el abierto, como
    // está ahora; los demás, el que quedó en memoria (los que nunca se abrieron no mandan nada y C# usa el guardado).
    estadosParaGenerar(ids) {
      const actual = this.props.vista && this.props.vista.actual ? this.props.vista.actual.id : null;
      return ids.reduce((o, id) => {
        const e = id === actual ? this.estadoDeLaPagina() : this.opcionesPorPortico[id];
        return e ? { ...o, [id]: JSON.stringify(e) } : o;
      }, {});
    }

    componentWillUnmount() {
      window.removeEventListener('resize', this.alCambiarTamano);
    }

    // La página mide como mínimo 1440 × 900, como la maqueta (más chica, C# la achica con el zoom). Si la ventana es
    // más grande, la llena: la columna del centro crece y el alzado y el corte toman el espacio de más (Juan, 16-oct:
    // "no se usa el espacio bien").
    medidas() {
      const W = Math.max(1440, this.state.vw), H = Math.max(900, this.state.vh);
      // En Planta la barra izquierda crece 180 px: el centro los resta para que el alzado no crezca ni se desborde.
      const extra = this.state.barra === 'planta' ? ANCHO_PLANTA - ANCHO_LISTA : 0;
      return { pageW: W + 'px', pageH: H + 'px', dibujoW: 788 + W - 1440 - extra, dibujoH: 300 + H - 900 };
    }

    // Lo elegido en el alzado o la lista; sin elegir, el traslapo del aviso largo o nada (Carlos, 21-oct, min 13:20:
    // al abrir no debe quedar una barra elegida).
    seleccion() {
      const s = this.state, P = this.props.vista && this.props.vista.actual;
      if (s.sel) return s.sel;
      const largos = P ? P.avisos.filter((a) => a.tipo === 'TraslapoLargo') : [];
      const avisoLargo = largos.find((a) => a.cambiarCara) || largos[0] || null;
      return avisoLargo ? { k: 'lap', id: avisoLargo.elemento } : NADA;
    }

    // Pide a C# la vista previa de lo elegido (#84): Subir / Bajar, o el largo escrito del traslapo o del bastón. La
    // respuesta llega a previa(); las que traen otro número son viejas y se descartan.
    pedirPrevia() {
      const v = this.props.vista, opciones = opcionesDe(this.state);
      if (!v || !v.actual || !opciones) return;
      const sel = this.seleccion();
      if (sel === NADA) return;
      const valor = num(this.state.draft, NaN);
      const accion = sel.k === 'lap' ? 'Traslapo' : sel.k === 'bast' ? 'Baston' : 'Barra';
      this.n = this.siguiente();
      this.props.enviar({ tipo: 'previsualizar', id: v.actual.id, opciones,
        pedido: { n: this.n, elemento: sel.id, accion, valor: accion === 'Barra' || !Number.isFinite(valor) ? null : valor } });
    }

    // Pide a C# los saltos de los 3 puntos de la barra o el bastón elegido (#84, tarea 7). Van con su propio número,
    // así no pisan la vista previa del panel.
    pedirSaltos() {
      const v = this.props.vista, opciones = opcionesDe(this.state), sel = this.seleccion();
      this.saltosPedidos = {};
      if (!v || !v.actual || !opciones || sel.k === 'lap' || sel === NADA) return;
      ['Izquierda', 'Centro', 'Derecha'].forEach((punto) => {
        const n = this.siguiente();
        this.saltosPedidos[n] = punto;
        this.props.enviar({ tipo: 'previsualizar', id: v.actual.id, opciones, pedido: { n, elemento: sel.id, accion: 'Arrastre', punto } });
      });
    }

    siguiente() {
      this.pedidos = (this.pedidos || 0) + 1;
      return this.pedidos;
    }

    previa(p) {
      if (!p) return;
      if (p.n === this.n) this.setState({ previa: p });
      const punto = this.saltosPedidos && this.saltosPedidos[p.n];
      if (punto) {
        this.setState((st) => ({ saltos: { ...(st.saltos && st.saltos.elemento === p.elemento ? st.saltos : { elemento: p.elemento }), [punto]: p.saltos } }));
      }
    }

    // Al soltar un punto en un salto: los traslapos corridos van a cambios.movidos y las puntas del bastón a bastPuntas,
    // en mm, como los manda C#.
    aplicarSalto(salto) {
      const s = this.state, movidos = { ...s.cambios.movidos }, puntas = { ...s.bastPuntas }, ganchos = { ...s.cambios.ganchos };
      const P = this.props.vista && this.props.vista.actual;
      salto.cambios.forEach((c) => {
        if (c.tipo === 'movido') { movidos[c.id] = { desde: c.desde, enVoladizo: c.enVoladizo }; return; }
        // la punta que se movió pierde el gancho elegido: puede haber dejado el borde
        const b = P && P.bastones.find((x) => x.id === c.id);
        if (!b || mm(b.desde) !== c.desde) delete ganchos[c.id + '.inicio'];
        if (!b || mm(b.hasta) !== c.hasta) delete ganchos[c.id + '.fin'];
        puntas[c.id] = { desde: c.desde, hasta: c.hasta };
      });
      this.cambiar({ cambios: { ...s.cambios, movidos, ganchos }, bastPuntas: puntas, confirmVol: null });
    }

    // "Generar en Revit" (#85). Sin forzar, si algún pórtico tiene choques C# no genera y contesta "choques" (decisión 8
    // del plan de choques); "Generar de todos modos" vuelve a mandar los mismos con forzar.
    generar(ids, forzar) {
      this.idsGenerar = ids;
      this.props.enviar({ tipo: 'generar', ids, estados: this.estadosParaGenerar(ids), forzar });
    }

    choquesAlGenerar(porticos) {
      if (porticos && porticos.length && this.idsGenerar) this.setState({ choquesGen: { porticos, ids: this.idsGenerar } });
    }

    // Los arreglos de un choque (plan docs/planes/2026-10-22-choques-entre-barras-vigas.md, decisión 7): C# los prueba
    // antes de ofrecerlos. Se piden al abrir el punto, con las opciones de ahora; los que traen otro número son viejos.
    abrirChoque(i) {
      const v = this.props.vista, opciones = opcionesDe(this.state);
      if (!v || !v.actual || !opciones) return;
      this.nArreglos = this.siguiente();
      this.setState({ choqueAbierto: i, arreglos: null });
      this.props.enviar({ tipo: 'arreglos', id: v.actual.id, opciones, n: this.nArreglos, choque: i });
    }

    arreglosRecibidos(a) {
      if (a && a.n === this.nArreglos && a.choque === this.state.choqueAbierto) this.setState({ arreglos: a.arreglos });
    }

    // Aplica el arreglo como lo probó C# (ArreglosChoque.Aplicar) y guarda la página como estaba, para "Deshacer".
    aplicarArreglo(arreglo) {
      const s = this.state, c = arreglo.cambio, antes = this.estadoDeLaPagina();
      if (c.tipo === 'salto') {
        this.aplicarSalto({ cambios: c.saltos });
      } else {
        const clave = c.donde === 'estMarc' && s.estMarc ? 'estMarc' : 'est', e = s[clave], adicionales = e.adicionales || [];
        const nuevo = c.tipo === 'gancho' ? { ...e, estG: c.valor }
          : c.tipo === 'forma' ? { ...e, adicionales: adicionales.map((a, j) => (j === c.indice ? { ...a, forma: c.valor } : a)) }
          : { ...e, adicionales: adicionales.filter((a, j) => j !== c.indice) };
        this.cambiar({ [clave]: nuevo });
      }
      this.setState({ choqueAbierto: -1, arreglos: null, deshacer: { antes, texto: arreglo.texto } });
      clearTimeout(this._deshacer);
      this._deshacer = setTimeout(() => this.setState({ deshacer: null }), 10000);
    }

    deshacerArreglo() {
      const d = this.state.deshacer;
      if (!d) return;
      clearTimeout(this._deshacer);
      this.setState({ deshacer: null });
      this.cambiar(d.antes);
    }

    say(t) {
      this.setState({ toast: t });
      clearTimeout(this._t);
      this._t = setTimeout(() => this.setState({ toast: '' }), 2600);
    }

    // Un mensaje de C# que no es la vista: un aviso, o la respuesta a "Guardar y bloquear".
    aviso(texto) {
      if (this.state.guardando) this.setState({ guardando: false });
      this.say(texto);
    }

    componentDidUpdate(prev, prevState) {
      // la vista previa se vuelve a pedir al elegir otra cosa, al escribir un largo (150 ms) o al llegar otra vista
      const s = this.state;
      if (this.llenarLargo && prev.vista !== this.props.vista) {
        this.llenarLargo = false;
        const sel = this.seleccion(), P = this.props.vista && this.props.vista.actual;
        const L = sel.k === 'lap' && P ? P.traslapos.find((x) => x.id === sel.id) : null;
        if (L) this.setState({ draft: f2(L.hasta - L.desde) });
      }
      if (prev.vista !== this.props.vista || prevState.sel !== s.sel || prevState.draft !== s.draft) {
        clearTimeout(this._previa);
        this._previa = setTimeout(() => this.pedirPrevia(), prevState.draft !== s.draft ? 150 : 0);
      }
      if (prev.vista !== this.props.vista || prevState.sel !== s.sel) {
        clearTimeout(this._saltos);
        this._saltos = setTimeout(() => this.pedirSaltos(), 0);
      }
      const antes = prev.vista && prev.vista.actual ? prev.vista.actual.id : null;
      const ahora = this.props.vista && this.props.vista.actual ? this.props.vista.actual.id : null;
      // Cada pórtico guarda sus opciones, como SesionTablero en C#: al cambiar de pórtico se recuperan las suyas. Sin
      // ninguna en memoria, las que se guardaron en sus vigas al generarlo (plan de 22-oct, recordar el pórtico).
      if (antes !== ahora) {
        const actual = this.props.vista && this.props.vista.actual;
        const guardadas = ahora ? this.opcionesPorPortico[ahora] || estadoGuardado(actual) : null;
        // sin nada guardado, arranca como C#: con las de vigueta si se detectó vigueta (#942)
        const arranque = actual && actual.detectadaVigueta ? VIGUETA0 : {};
        if (antes) {
          this.opcionesPorPortico[antes] = this.estadoDeLaPagina();
          this.setState({ ...OPCIONES0, ...arranque, ...guardadas });
        } else if (guardadas || ahora) {
          this.setState({ ...arranque, ...guardadas });
        }
        // Si el pórtico se abrió desde Revit (En vivo o "Seleccionar en Revit"), la lista baja hasta él (Juan, 16-oct).
        const tarjeta = ahora && document.querySelector('[data-portico="' + CSS.escape(ahora) + '"]');
        if (tarjeta) tarjeta.scrollIntoView({ block: 'nearest' });
      }
      // Con otra vista los choques pueden ser otros: el globo abierto se cierra.
      if (prev.vista !== this.props.vista && s.choqueAbierto >= 0) this.setState({ choqueAbierto: -1, arreglos: null });
      // Al soltar la línea 1-1, se sigue viendo donde quedó hasta que llega el cálculo de C#.
      if (prev.vista !== this.props.vista && !this.arrastrandoCorte && this.state.cutLive != null) this.setState({ cutLive: null });
      if (this.state.guardando && prev.vista !== this.props.vista) {
        this.say('Reglas guardadas para todos los proyectos');
        this.setState({ guardando: false, locked: true, rulesWarn: false, rulesSaved: null, rules: null, mult: null, gap: null, bar12: null, hookR: null,
          ...(this.salirDeReglas ? { step: 'barras' } : null) });
        this.salirDeReglas = false;
      }
    }

    // Viga o vigueta (#940): elegir lo que detectó el add-in borra la corrección (undefined), como si nunca se hubiera tocado.
    // Si las barras y el recubrimiento siguen como arrancaron, pasan a los del otro (#942): una viga corregida a vigueta
    // no queda con 3 #5 que no caben.
    ponerVigueta(P, vigueta) {
      const era = !!(P && P.esVigueta);
      const cambio = { vigueta: P && vigueta === P.detectadaVigueta ? undefined : vigueta };
      if (vigueta !== era && sinTocar(this.state, era)) Object.assign(cambio, arranqueDe(vigueta));
      this.setState({ sel: NADA });
      this.cambiar(cambio);
    }

    // Cambia algo de OpcionesTablero y pide el cálculo; en los campos de texto, con 150 ms de espera.
    cambiar(cambio, texto) {
      this.setState(cambio, () => {
        clearTimeout(this._calc);
        const ir = () => {
          const v = this.props.vista;
          const opciones = opcionesDe(this.state);
          if (v && v.actual && opciones) this.props.enviar({ tipo: 'calcular', id: v.actual.id, opciones });
        };
        if (texto) this._calc = setTimeout(ir, 150); else ir();
      });
    }

    guardarReglas(reglas, salir) {
      this.salirDeReglas = !!salir;
      this.setState({ guardando: true });
      this.props.enviar({ tipo: 'guardarReglas', reglas: { porDiametro: reglas.rules.map((r) => ({ d: r.d, t: r.t, g: r.g, g180: r.g180, e: r.e, k: r.k })),
        multiplo: reglas.mult, distanciaLibreCm: reglas.gap, varilla12: reglas.bar12, redondeoGanchoCm: reglas.hookR, problema: null } });
    }

    valores() {
      const s = this.state;
      const vista = this.props.vista;
      const P = vista.actual;
      const enviar = this.props.enviar;

      const PORS = vista.porticos;
      const porId = P ? P.id : null;
      const por = PORS.find((p) => p.id === porId) || null;
      const TRAMOS = por ? por.tramos : [];
      const step = s.step || 'barras';
      const tramosSel = s.tramosSel;
      // Primero el traslapo largo que se evita cambiando de cara: es el que trae "Cambiar".
      const largos = P ? P.avisos.filter((a) => a.tipo === 'TraslapoLargo') : [];
      const avisoLargo = largos.find((a) => a.cambiarCara) || largos[0] || null;
      const avisoOtro = P ? P.avisos.find((a) => a.tipo !== 'TraslapoLargo') : null;
      const selEl = this.seleccion();
      const tipo = P ? P.tipo : 'Aérea';
      const isCim = tipo === 'Cimentación';
      const side = P ? P.caraUsada : 'izq';
      const isAviso = !!(avisoLargo || avisoOtro);

      // reglas: las de la vista, o lo que se está escribiendo en la pantalla Reglas
      const R = vista.reglas;
      const rulesData = s.rules || R.porDiametro;
      const mult = s.mult ?? R.multiplo, gap = s.gap ?? R.distanciaLibreCm, bar12 = s.bar12 ?? R.varilla12, hookR = s.hookR ?? R.redondeoGanchoCm ?? '5';
      const M = num(mult, 0.5);

      // capas (#88): el reparto en filas sale del motor
      const maxCapas = Math.max(1, Math.round(num(s.maxCapas, 3)));
      // las bolitas de cada fila, con el color del diámetro de las barras del corte en esa fila (#100)
      const coloresFila = (arriba, i) => (P ? P.seccion.barras : []).filter((b) => !b.baston && b.arriba === arriba && b.fila === i)
        .sort((p, q) => p.x - q.x).map((b) => b.color || '#3F4A57');
      const layerInfo = (rows, cara, arriba) => {
        const total = rows.reduce((a, r) => a + r.n, 0);
        const per = cara ? cara.cabenPorFila : 0;
        const filas = cara && cara.porFila.length ? cara.porFila.length : (per ? Math.ceil(total / per) : 99);
        let msg, col = '#5E6670';
        if (!cara) msg = '';
        else if (cara.error === 'MenosDeDosBarras') { msg = 'El estribo cerrado necesita al menos 2 barras en esta cara.'; col = '#B42318'; }
        else if (cara.error === 'NoCaben') { msg = 'No caben: máximo ' + cara.maximo + ' barras en ' + maxCapas + ' filas.'; col = '#B42318'; }
        else if (cara.error === 'NoCabenEnElAlto') { msg = 'No caben en el alto de la viga.'; col = '#B42318'; }
        else if (filas === 1) { msg = 'Todas van en una fila.'; }
        else { msg = cara.porFila.map((n, i) => 'fila ' + (i + 1) + ': ' + n).join(' · ') + '. La segunda fila va debajo de las esquinas.'; col = '#9A5B00'; }
        const filasVis = [];
        if (cara && cara.error === 'Ninguno' && cara.porFila.length) {
          cara.porFila.forEach((n, i) => {
            const cs = coloresFila(arriba, i);
            filasVis.push({ label: 'Fila ' + (i + 1), dots: Array.from({ length: n }, (_, j) => ({ c: cs[j] || '#3F4A57' })) });
          });
        } else {
          let left = total;
          const nF = Math.min(Math.max(filas, 1), Math.max(maxCapas, 1) + 1);
          for (let i = 0; i < nF && left > 0; i++) {
            const n = per ? Math.min(per, left) : left;
            const over = i >= maxCapas;
            filasVis.push({ label: 'Fila ' + (i + 1), dots: Array.from({ length: n }, () => ({ c: over ? '#B42318' : '#9AA3AD' })) });
            left -= n;
          }
        }
        return { total, per, filas, msg, col, filasVis };
      };
      const supI = layerInfo(s.sup, P && P.superior, true), infI = layerInfo(s.inf, P && P.inferior, false);
      // Fila: Auto · 1 … el máximo de filas del paso 1 (decisión 11 del plan de #84)
      const filasOpc = Array.from({ length: Math.max(1, Math.round(num(s.maxCapas, 3))) }, (_, i) => String(i + 1));
      // el color de un diámetro, de los que mandó C# para la leyenda (uno recién elegido no lo trae hasta recalcular)
      const colorDe = (d) => { const x = P && P.diametros ? P.diametros.find((y) => y.nombre === d) : null; return x ? x.color : '#C9CFD6'; };
      const mkRows = (key, rows, label) => rows.map((r, i) => ({
        n: r.n, d: r.d, c: colorDe(r.d), f: r.f || '', filasOpc, canRemove: rows.length > 1, filaLabel: 'Fila de las ' + r.n + ' ' + r.d + ' ' + label,
        setF: (e) => this.cambiar({ [key]: rows.map((x, j) => (j === i ? { ...x, f: e.target.value } : x)) }),
        minusLabel: 'Quitar barra ' + label, plusLabel: 'Añadir barra ' + label, selLabel: 'Diámetro ' + label, removeLabel: 'Quitar este diámetro',
        minus: () => this.cambiar({ [key]: rows.map((x, j) => (j === i ? { ...x, n: Math.max(1, x.n - 1) } : x)) }),
        plus: () => this.cambiar({ [key]: rows.map((x, j) => (j === i ? { ...x, n: Math.min(20, x.n + 1) } : x)) }),
        setD: (e) => this.cambiar({ [key]: rows.map((x, j) => (j === i ? { ...x, d: e.target.value } : x)) }),
        remove: () => this.cambiar({ [key]: rows.filter((x, j) => j !== i) }),
      }));
      const layers = [
        { title: 'Barras arriba', per: supI.per, msg: supI.msg, msgCol: supI.col, filas: supI.filasVis, rows: mkRows('sup', s.sup, 'arriba'), add: () => this.cambiar({ sup: s.sup.concat({ n: 2, d: '#4' }) }) },
        { title: 'Barras abajo', per: infI.per, msg: infI.msg, msgCol: infI.col, filas: infI.filasVis, rows: mkRows('inf', s.inf, 'abajo'), add: () => this.cambiar({ inf: s.inf.concat({ n: 2, d: '#4' }) }) },
      ];
      // al eje de la barra: el alto menos la y de la primera barra de abajo, que viene del motor
      const secBars = P ? P.seccion.barras : [];
      const b1 = secBars.find((b) => !b.arriba && b.fila === 0);
      const alEje = b1 ? f2(Math.round((P.seccion.altoCm - b1.y) * 1000) / 1000).replace(/0$/, '') : '';

      // barras, traslapos y bastones del motor
      const bars = P ? P.barras : [];
      const lapsAll = P ? P.traslapos : [];
      const BAST = P ? P.bastones : [];
      const lapLen = (L) => L.hasta - L.desde;
      const qtyOf = (rows) => rows.map((r) => r.n + ' ' + r.d).join(' + ');
      const cheaper = P ? P.caraMasEconomica : 'izq';
      const sideBtn = (k, o) => ({ kg: o && o.existe ? o.kg.toFixed(1) + ' kg' : '—', tag: k === cheaper ? 'más económica' : '', bd: side === k ? '#1F5FAF' : '#C9CFD6', bg: side === k ? '#E8F0FA' : '#FFFFFF' });

      // selección
      const selectLap = (L) => this.setState({ sel: { k: 'lap', id: L.id }, draft: f2(lapLen(L)) });
      const selectBar = (id) => {
        const B = BAST.find((b) => b.id === id);
        if (B) this.setState({ sel: { k: 'bast', id }, draft: f2(B.hasta - B.desde) });
        else this.setState({ sel: { k: 'bar', id } });
      };
      const kind = selEl.k;
      const SB = bars.find((b) => b.id === selEl.id) || bars[0] || null;
      const SL = lapsAll.find((l) => l.id === selEl.id) || null;
      const SBa = BAST.find((b) => b.id === selEl.id) || null;

      // vista previa de C# (#84) para lo elegido; mientras no llega, los botones quedan desactivados
      const PV = s.previa && s.previa.elemento === selEl.id ? s.previa : null;
      const opcion = (accion) => (PV ? PV.opciones.find((o) => o.accion === accion) : null);
      const conCambio = (clave, id, valor) => {
        const c = { ...s.cambios, [clave]: { ...s.cambios[clave] } };
        if (valor == null) delete c[clave][id]; else c[clave][id] = valor;
        return c;
      };
      // El largo de la barra puesto a mano (o Automático) quita el corrido de sus traslapos: el corrido es una posición fija
      // y la devolvería a otro largo. C# previsualiza igual (MotorVigas.SinCorridosDe).
      const barraSinCorridos = (id, valor) => {
        const c = conCambio('barras', id, valor), movidos = { ...c.movidos };
        lapsAll.filter((l) => l.barraIzquierda === id || l.barraDerecha === id).forEach((l) => delete movidos[l.id]);
        return { ...c, movidos };
      };

      let sb = { id: '', where: '', qty: '', len: '', tag: '', tagCol: '#1E7A46', downVal: '', upVal: '', downOff: true, downWhy: '', downCol: '#9AA3AD',
        upOff: true, upWhy: '', upCol: '#9AA3AD', notManual: true, resetCol: '#9AA3AD', note: '', down: () => {}, up: () => {}, reset: () => {} };
      if (SB) {
        const sbLen = SB.largo;
        const subir = opcion('subir'), bajar = opcion('bajar');
        const aplicar = (o) => () => {
          if (!o || !o.sePuede) return;
          this.cambiar({ cambios: barraSinCorridos(SB.id, mm(o.valor)) });
          this.say(o.texto);
        };
        sb = {
          id: SB.id, where: SB.capa === 'arriba' ? 'Arriba' : 'Abajo', qty: SB.cantidad, len: f2(sbLen),
          tag: SB.manual ? 'Manual' : 'Automático', tagCol: SB.manual ? '#9A5B00' : '#1E7A46',
          downVal: f2(bajar ? bajar.valor : sbLen - M), upVal: f2(subir ? subir.valor : sbLen + M),
          downOff: !(bajar && bajar.sePuede), downWhy: bajar ? bajar.texto : '', downCol: bajar && bajar.sePuede ? '#16191D' : '#9AA3AD',
          upOff: !(subir && subir.sePuede), upWhy: subir ? subir.texto : '', upCol: subir && subir.sePuede ? '#16191D' : '#9AA3AD',
          notManual: !SB.manual, resetCol: SB.manual ? '#1F5FAF' : '#9AA3AD',
          note: (SB.llegaAlVoladizo ? 'Esta barra entra al voladizo de corrido: en el voladizo nunca se traslapa. ' : '')
            + (bajar ? (bajar.sePuede ? 'Bajar: ' : '') + bajar.texto + ' ' : '') + (subir ? 'Subir: ' + subir.texto : ''),
          down: aplicar(bajar), up: aplicar(subir),
          reset: () => this.cambiar({ cambios: barraSinCorridos(SB.id, null) }),
        };
      }

      let sl = { id: '', min: '', diam: '', cur: '', eff: '', effCol: '#5E6670', off: true, applyBg: '#9AA3AD', notManual: true, resetCol: '#9AA3AD', apply: () => {}, reset: () => {} };
      if (SL) {
        const o = opcion('aplicar');
        const ok = !!o && o.sePuede;
        // en rojo solo si lo escrito es otro largo y no se puede; con el largo actual es solo la indicación
        // (el largo se compara como se muestra: 1.098 se ve 1.10 y escribir 1.10 es el mismo)
        const otro = f2(num(s.draft, lapLen(SL))) !== f2(lapLen(SL));
        sl = { id: SL.id, min: f2(SL.minimo), diam: SL.diametro, cur: f2(lapLen(SL)),
          eff: o ? o.texto : 'Escribe un largo en metros.', effCol: ok ? '#1E5C38' : (o && otro ? '#B42318' : '#5E6670'),
          off: !ok, applyBg: ok ? '#1F5FAF' : '#9AA3AD', notManual: !SL.manual, resetCol: SL.manual ? '#1F5FAF' : '#9AA3AD',
          apply: () => {
            if (!ok) return;
            this.cambiar({ cambios: conCambio('traslapos', SL.id, mm(o.valor)) });
            this.say('Traslapo ' + SL.id + ': ' + o.texto);
          },
          // el largo automático llega con la vista siguiente; ahí se pone en el campo
          reset: () => { this.llenarLargo = true; this.cambiar({ cambios: conCambio('traslapos', SL.id, null) }); } };
      }
      let stv = { id: '', where: '', note: '', apply: () => {}, reset: () => {} };
      if (SBa) {
        const o = opcion('aplicar');
        stv = { id: SBa.id, where: SBa.donde + ' · ahora ' + f2(SBa.largo) + ' m',
          note: o ? o.texto : 'Escribe un largo en metros.',
          apply: () => {
            if (!o) return;
            if (!o.sePuede) { this.say(o.texto); return; }
            // el largo escrito lo vuelve a centrar: las puntas de un arrastre mandarían sobre él
            const puntas = { ...s.bastPuntas };
            delete puntas[SBa.id];
            this.cambiar({ bastLen: { ...s.bastLen, [SBa.id]: f2(o.valor) }, bastPuntas: puntas });
            this.say('Bastón ' + SBa.id + ' en ' + f2(o.valor) + ' m');
          },
          reset: () => {
            // vuelve a centrarse y puede dejar de llegar al borde: sus ganchos guardados se borran
            const largos = { ...s.bastLen }, puntas = { ...s.bastPuntas }, ganchos = { ...s.cambios.ganchos };
            delete largos[SBa.id]; delete puntas[SBa.id]; delete ganchos[SBa.id + '.inicio']; delete ganchos[SBa.id + '.fin'];
            this.cambiar({ bastLen: largos, bastPuntas: puntas, cambios: { ...s.cambios, ganchos }, draft: s.bastDef });
          } };
      }

      // lista
      const rowsList = bars.map((b) => {
        const L = lapsAll.find((x) => x.barraIzquierda === b.id);
        return { id: b.id, kind: 'Corrida ' + b.capa, qty: b.cantidad, len: f2(b.largo) + ' m', lap: L ? 'traslapo ' + f2(lapLen(L)) + ' con ' + L.barraDerecha : (b.llegaAlVoladizo ? 'llega al voladizo' : '—'),
          kg: b.kg.toFixed(1) + ' kg', bg: kind === 'bar' && b.id === selEl.id ? '#E8F0FA' : '#FFFFFF', pick: () => selectBar(b.id) };
      }).concat(BAST.map((b) => ({ id: b.id, kind: 'Bastón', qty: b.cantidad, len: f2(b.largo) + ' m', lap: b.donde, kg: b.kg.toFixed(1) + ' kg',
        bg: kind === 'bast' && b.id === selEl.id ? '#E8F0FA' : '#FFFFFF', pick: () => selectBar(b.id) })));
      const estRows = (P ? P.listaEstribos : []).map((e) => ({ id: e.id, kind: e.tipo, qty: e.cantidad + ' ' + e.diametro, len: f2(e.largo) + ' m', lap: e.detalle,
        kg: e.kg.toFixed(1) + ' kg', bg: '#FFFFFF', pick: () => this.setState({ step: 'estribos' }) }));

      // corte 1-1 (#91): la sección en el corte la arma C# (barras, bastones que pasan por ahí y estribos del tramo). Se
      // pinta en la pestaña Corte 1-1, a 5 px por cm como la maqueta, y más chica en la ventana que sale al arrastrar la
      // línea 1-1 en el alzado (decisión 10 del plan docs/planes/2026-10-17-corte-vigas-91.md).
      const C = P ? P.corte : null;
      const secc = P ? P.seccion : { anchoCm: 30, altoCm: 50, barras: [], figuras: [] };
      // el estribo obligatorio: el cerrado de las esquinas, o en una vigueta la grapa en S o en C (decisión 8 del plan de #938)
      const principal = secc.figuras.find((f) => f.id === 'E');
      const grapa = !!principal && principal.tipo !== 'Cerrado';
      const cerrado = grapa ? null : principal;
      const recCm = num(s.rec, 4);
      // Estribos adicionales (#85, hallazgo E8): con "+ Cerrado" o "+ Abierto" el corte queda eligiendo barras. Cada barra
      // es un PuntoBarra del motor: su cara, su fila desde 1 (la de la cara) y su número de izquierda a derecha en la fila.
      const pk = s.pick;
      const limite = pk ? (pk.tipo === 'Cerrado' ? 2 : 1) : 0;
      const punto = (b) => ({ cara: b.arriba ? 'Superior' : 'Inferior', fila: b.fila + 1, nombre: b.nombre,
        numero: 1 + secBars.filter((x) => !x.baston && x.arriba === b.arriba && x.fila === b.fila && x.x < b.x).length });
      const igual = (a, b) => a.cara === b.cara && a.fila === b.fila && a.numero === b.numero;
      const elegirBarra = (b) => {
        const p = punto(b);
        if (pk.barras.some((x) => igual(x, p))) { this.setState({ pick: { ...pk, barras: pk.barras.filter((x) => !igual(x, p)) } }); return; }
        if (pk.barras.filter((x) => x.cara === p.cara).length >= limite) {
          this.say('Ya elegiste ' + limite + (limite === 1 ? ' barra ' : ' barras ') + (p.cara === 'Superior' ? 'arriba' : 'abajo') + '. Haz clic en una para quitarla.');
          return;
        }
        this.setState({ pick: { ...pk, barras: pk.barras.concat(p) } });
      };
      // El grueso de un estribo en px: su diámetro a escala, nunca menos de 1 px para que se vea en el corte chico.
      const grueso = (f, K) => Math.max(1, f && f.diametroCm ? f.diametroCm * K : 2);
      // El color de un estribo es el de su diámetro, como las barras (23-oct); gris si C# no lo manda.
      const colorEstribo = (f) => (f && f.color) || '#5E6670';
      const seccionA = (K) => ({
        wpx: secc.anchoCm * K + 'px', hpx: secc.altoCm * K + 'px',
        dots: secBars.map((b) => {
          const elegible = !!pk && !b.baston;
          const elegida = elegible && pk.barras.some((x) => igual(x, punto(b)));
          return { x: (b.x - b.d / 2) * K + 'px', y: (b.y - b.d / 2) * K + 'px', s: b.d * K + 'px',
            c: b.color || '#3F4A57', ring: elegida ? '2px solid #0F7B6C' : 'none', cur: elegible ? 'pointer' : 'default',
            label: (b.baston ? '' : 'Barra ') + b.nombre, pick: elegible ? () => elegirBarra(b) : () => {} };
        }),
        // lo que se ve detrás, hasta la profundidad: círculos vacíos con borde punteado
        proy: (C ? C.proyeccion : []).map((b) => ({ x: (b.x - b.d / 2) * K + 'px', y: (b.y - b.d / 2) * K + 'px', s: b.d * K + 'px', label: b.nombre })),
        // los adicionales con su trazo de C# (PreviaAcero.EnElCorte): los ganchos según el ángulo y la forma C/S, como
        // salen en Revit (Juan, 22-oct). Sin trazo (no debería pasar), la figura simple de antes.
        // El grueso es el diámetro de la barra a escala (Juan, 22-oct), por el eje del trazo.
        trazos: secc.figuras.filter((f) => (f.id !== 'E' || grapa) && f.puntos).map((f) => ({
          pts: f.puntos.map((p) => p[0] * K + ',' + p[1] * K).join(' '), w: grueso(f, K), c: colorEstribo(f) })),
        adds: secc.figuras.filter((f) => f.id !== 'E' && !f.puntos).map((f) => (f.tipo === 'Cerrado'
          ? { l: f.izquierda * K + 'px', t: f.arriba * K + 'px', w: (f.derecha - f.izquierda) * K + 'px', h: (f.abajo - f.arriba) * K + 'px', bd: '2px solid ' + colorEstribo(f), r: '3px', bg: 'transparent' }
          : { l: (f.izquierda * K - 1) + 'px', t: f.arriba * K + 'px', w: '2px', h: (f.abajo - f.arriba) * K + 'px', bd: 'none', r: '0', bg: colorEstribo(f) })),
        stOn: !grapa, stL: (cerrado ? cerrado.izquierda : recCm) * K + 'px', stT: (cerrado ? cerrado.arriba : recCm) * K + 'px',
        stR: (cerrado ? secc.anchoCm - cerrado.derecha : recCm) * K + 'px', stB: (cerrado ? secc.altoCm - cerrado.abajo : recCm) * K + 'px',
        // El cerrado de las esquinas, con el grueso de su barra hacia adentro desde las caras exteriores y la esquina
        // redondeada por fuera como el doblado de estribo (4 d de diámetro interior: radio exterior 3 d).
        stW: grueso(cerrado, K) + 'px', stC: colorEstribo(cerrado), stRadio: (cerrado && cerrado.diametroCm ? 3 * cerrado.diametroCm * K : 4) + 'px' });
      // La pestaña usa todo el alto del área de dibujo y el ancho que deja el texto (320 + 48 de separación + márgenes):
      // a 5 px por cm una viga de 25 × 35 quedaba de 125 × 175 px.
      const md = this.medidas();
      const sec = seccionA(Math.min((md.dibujoH - 40) / secc.altoCm, (md.dibujoW - 320 - 48 - 80) / secc.anchoCm));
      const secMini = seccionA(Math.min(4, 180 / secc.altoCm, (ANCHO_CORTE - 40) / secc.anchoCm));
      const adicionales = P ? P.listaEstribos.filter((e) => e.id !== 'E') : [];

      // estribos: con tramos marcados, los campos son los de esos tramos (EstribosMarcados)
      const nSel = tramosSel.length;
      const est = nSel && s.estMarc ? s.estMarc : s.est;
      const setEst = (k, texto) => (e) => {
        const valor = e.target.value;
        if (nSel) this.cambiar({ estMarc: { ...(s.estMarc || s.est), [k]: valor } }, texto);
        else this.cambiar({ est: { ...s.est, [k]: valor } }, texto);
      };
      // los adicionales van donde se aplica este paso: los tramos marcados o todo el pórtico
      const misAdicionales = est.adicionales || [];
      const ponerAdicionales = (lista) => (nSel
        ? this.cambiar({ estMarc: { ...(s.estMarc || s.est), adicionales: lista } })
        : this.cambiar({ est: { ...s.est, adicionales: lista } }));
      const nArriba = pk ? pk.barras.filter((x) => x.cara === 'Superior').length : 0;
      const nAbajo = pk ? pk.barras.filter((x) => x.cara === 'Inferior').length : 0;
      const empezar = (tipo) => () => this.setState({ pick: { tipo, barras: [] }, tab: 'corte' });

      const on = 'background: #1F5FAF; color: #FFFFFF';
      const off = 'background: #FFFFFF; color: #16191D';
      const STEPS = [['barras', 'Barras'], ['bastones', 'Bastones'], ['estribos', 'Estribos'], ['corte', 'Corte']];
      const steps = STEPS.map(([k, name], i) => ({ n: String(i + 1), name, go: () => this.setState({ step: k }),
        style: step === k ? 'color: #1F5FAF; font-weight: 600; box-shadow: inset 0 -2px 0 #1F5FAF' : 'color: #5E6670' }));

      const q = s.q.trim().toLowerCase();
      // Con pórticos elegidos en Revit, la lista muestra solo esos; "Ver todos" vuelve a la completa.
      const elegidos = vista.elegidos;
      const enLista = elegidos ? PORS.filter((p) => elegidos.includes(p.id)) : PORS;
      const pors = enLista.filter((p) => !q || (p.nombre + ' ' + p.sub).toLowerCase().includes(q)).map((p) => {
        const active = p.id === porId;
        return { id: p.id, name: p.nombre, sub: p.sub, tipo: p.tipo, open: active, bg: active ? '#F3F7FC' : '#FFFFFF', bd: active ? '#1F5FAF' : '#FFFFFF',
          pick: () => { this.setState({ ver: 'todo', sel: undefined, pick: null }); enviar({ tipo: 'elegirPortico', id: p.id }); },
          // viguetas iguales (opción D): la etiqueta muestra el grupo en azul; sin grupo, el tipo como siempre
          grupo: p.grupo || '',
          tramos: (active ? p.tramos : []).map((t) => {
            const on2 = tramosSel.includes(t.id);
            return { name: t.nombre, chkLabel: 'Marcar ' + t.nombre, pressed: on2 ? 'true' : 'false', bg: s.ver === t.id && active ? '#E8F0FA' : 'transparent',
              boxBd: on2 ? '#1F5FAF' : '#9AA3AD', boxBg: on2 ? '#1F5FAF' : '#FFFFFF',
              toggle: () => this.cambiar({ tramosSel: on2 ? tramosSel.filter((x) => x !== t.id) : tramosSel.concat(t.id), estMarc: s.estMarc || s.est, step: 'estribos' }),
              view: () => this.setState({ ver: t.id }) };
          }) };
      });
      const verT = TRAMOS.find((t) => t.id === s.ver);
      const faceName = isCim ? 'arriba' : 'abajo';
      const lockedNow = s.locked;
      const rulesNow = { rules: rulesData, mult, gap, bar12, hookR };
      const rulesDirty = !s.locked && !!s.rulesSaved && JSON.stringify(rulesNow) !== JSON.stringify(s.rulesSaved);
      const leaveRules = (extra) => this.setState({ step: 'barras', locked: true, rulesWarn: false, rulesSaved: null, rules: null, mult: null, gap: null, bar12: null, hookR: null, ...extra });
      const bastD = (d, rows) => d || rows[0].d;
      const nEstAll = P ? P.estribos : 0;
      const avisoTexto = (avisoLargo || avisoOtro || { texto: '' }).texto;
      const corte = avisoTexto.indexOf('. ');
      // la palomita de terminado junto al nombre (punto 5 de Santiago, 28-oct); la marca vive en las vigas de Revit
      const terminado = !!P && (vista.terminados || []).includes(P.id);
      return {
        terminado, toggleTerminado: () => { if (P) enviar({ tipo: 'marcarTerminado', id: P.id, terminado: !terminado }); },
        pors, porCount: elegidos ? enLista.length + ' de ' + PORS.length : PORS.length + ' en el modelo', filtrado: !!elegidos,
        verTodos: () => enviar({ tipo: 'verTodos' }), q: s.q, proyecto: vista.proyecto, por: por ? { name: por.nombre, sub: por.sub } : { name: '', sub: '' }, hasPor: !!P, noPor: !P,
        tipo, isCim, isAviso, summary: (bars.length + BAST.length) + ' barras · ' + nEstAll + ' estribos · ' + lapsAll.length + ' traslapos · ' + (P ? P.kg : 0).toFixed(1) + ' kg',
        avisoNegrita: corte > 0 ? avisoTexto.slice(0, corte + 1) : avisoTexto, avisoResto: corte > 0 ? avisoTexto.slice(corte + 1) : '', avisoCambiar: !!avisoLargo && avisoLargo.cambiarCara,
        warnLap: avisoLargo ? avisoLargo.elemento : '',
        ver: s.ver, tramos: TRAMOS, rangeFrom: verT ? verT.desde : (P ? P.inicio : 0), rangeTo: verT ? verT.hasta : (P ? P.fin : 0),
        isAlzado: s.tab === 'alzado', isCorte: s.tab === 'corte', tabA: s.tab === 'alzado' ? on : off, tabC: s.tab === 'corte' ? on : off,
        side, faceQuestion: '¿Dónde traslapan las barras de ' + faceName + '?', izq: sideBtn('izq', P && P.izquierda), der: sideBtn('der', P && P.derecha),
        barSel: kind === 'bar' || kind === 'bast' ? selEl.id : '', lapSel: kind === 'lap' ? selEl.id : '', lapsManual: lapsAll.filter((l) => l.manual).map((l) => l.id).join(','),
        isSelBar: kind === 'bar' && !!SB, isSelLap: kind === 'lap' && !!SL, isSelBast: kind === 'bast' && !!SBa, sb, sl, st: stv, draft: s.draft,
        bastDef: s.bastDef, bastSupN: s.bastSupN, bastInfN: s.bastInfN, bastSupD: bastD(s.bastSupD, s.sup), bastInfD: bastD(s.bastInfD, s.inf),
        rulesWarn: s.rulesWarn,
        // abierta, la lista de barras toma todo el alto que sobra en la columna (Juan, 19-oct: "que la tabla llegue al límite")
        listOpen: s.listOpen, listLabel: s.listOpen ? 'Ocultar' : 'Ver lista', rows: rowsList.concat(estRows),
        // Hallazgo E9: el motor puede descartar un adicional (rama del cerrado, otro igual, no cabe…); la lista lo dice con
        // su motivo. Los ids son E1, E2… primero los del pórtico y después los de los tramos marcados.
        estAddList: misAdicionales.map((ea, i) => ({ name: 'Adicional ' + (i + 1),
          desc: ea.tipo + ' · ' + ea.barras.map((b) => b.nombre).join(', '),
          motivo: (() => {
            const id = 'E' + (i + 1 + (nSel ? (s.est.adicionales || []).length : 0));
            const a = P ? P.avisos.find((x) => x.elemento === id && DESCARTA.includes(x.tipo)) : null;
            return a ? 'No se agrega: ' + a.texto : '';
          })(),
          // la grapa en C o en S (Juan, 22-oct): solo el abierto
          isAbierto: ea.tipo === 'Abierto',
          formas: ['C', 'S'].map((f) => ({ f, pressed: (ea.forma || 'C') === f ? 'true' : 'false', label: 'Adicional ' + (i + 1) + ' en ' + f,
            style: (ea.forma || 'C') === f ? on : off,
            pick: () => ponerAdicionales(misAdicionales.map((x, j) => (j === i ? { ...x, forma: f } : x))) })),
          removeLabel: 'Quitar adicional ' + (i + 1), remove: () => ponerAdicionales(misAdicionales.filter((_, j) => j !== i)) })),
        isPicking: !!pk, noPick: !pk, pickNotDone: !pk || nArriba !== limite || nAbajo !== limite,
        pickMsg: pk ? 'Estribo ' + pk.tipo.toLowerCase() + ': haz clic en el corte en ' + limite + (limite === 1 ? ' barra' : ' barras') + ' arriba y '
          + limite + ' abajo. Llevas ' + nArriba + ' arriba y ' + nAbajo + ' abajo.' : '',
        confTxt: est.conf + ' m', s1Txt: 'cada ' + est.s1, s2Txt: 'cada ' + est.s2, bastTxt: s.bastDef + ' m',
        secEstTexto: grapa ? 'Grapa en ' + (principal.forma || 'S') + ' ' + s.est.estD : 'Estribo cerrado ' + s.est.estD + ' en las esquinas',
        secAddText: adicionales.length ? ' · ' + adicionales.length + (adicionales.length === 1 ? ' adicional' : ' adicionales') : '',
        startCerrado: empezar('Cerrado'), startAbierto: empezar('Abierto'), cancelPick: () => this.setState({ pick: null }),
        addPick: () => { this.setState({ pick: null }); ponerAdicionales(misAdicionales.concat({ tipo: pk.tipo, barras: pk.barras })); },
        corte: C, cutLive: s.cutLive, cutX: s.cutX !== '' ? s.cutX : (C ? f2(C.x) : ''), cutPos: C ? f2(s.cutLive ?? C.x) : '',
        cutAjustado: !!C && C.ajustado, cutBd: C && C.ajustado ? '#B26B00' : '#C9CFD6', cutTipo: !!C && C.esTipo,
        cutTramo: C ? C.tramo : '', cutAviso: C ? C.aviso : null,
        cutMira: C ? C.mirando : s.mira, cutProf: C ? f2(C.profundidad) : s.depth, depth: s.depth, sec, secMini,
        cutWin: s.cutWin && !!C, cutWinPos: s.cutWinPos, hideCutWin: () => this.setState({ cutWin: false }),
        moveCutWin: (pos) => this.setState({ cutWinPos: pos }),
        segMiraIzq: s.mira === 'izquierda' ? on : off, segMiraDer: s.mira === 'izquierda' ? off : on,
        secTop: qtyOf(s.sup) + (supI.filas > 1 ? ' en ' + supI.filas + ' filas' : ''), secBot: qtyOf(s.inf) + (infI.filas > 1 ? ' en ' + infI.filas + ' filas' : ''),
        estD: est.estD, estG: est.estG, conf: est.conf, s1: est.s1, s2: est.s2, secEstD: s.est.estD,
        // la grapa de la vigueta va en S o en C (26-oct), con los mismos botones que la de un adicional abierto
        esGrapa: grapa, formasGrapa: ['C', 'S'].map((f) => ({ f, pressed: (est.forma || 'S') === f ? 'true' : 'false', label: 'Grapa en ' + f,
          style: (est.forma || 'S') === f ? on : off, pick: () => setEst('forma')({ target: { value: f } }) })),
        estScope: nSel ? 'Se aplica a ' + nSel + (nSel === 1 ? ' tramo marcado' : ' tramos marcados') + ' en la lista.' : 'Se aplica a todo el pórtico. Marca tramos en la lista para darles otros valores.',
        estScopeBg: nSel ? '#E8F0FA' : '#F4F6F8',
        showSteps: step !== 'reglas' && !!P, panelEmpty: !P && step !== 'reglas',
        isBarras: step === 'barras' && !!P, isBastones: step === 'bastones' && !!P, isEstribos: step === 'estribos' && !!P, isCorteStep: step === 'corte' && !!P, isReglas: step === 'reglas', steps,
        layers, rec: s.rec, alEje, recUp: s.recUp, recDown: s.recDown, recSide: s.recSide,
        more: s.more, moreLabel: s.more ? '− Menos opciones' : '+ Más opciones',
        // el dibujo del recubrimiento (Carlos 1:00:08: "el azul oscuro sobre negro no se entiende"), como la maqueta:
        // fondo blanco, estribo gris claro, la barra con el color de su diámetro y, detrás de "Ver el dibujo", para no
        // alargar el panel
        recDraw: !!s.recDraw, recDrawLabel: s.recDraw ? 'Ocultar el dibujo' : 'Ver el dibujo',
        recBarCol: (b1 && b1.color) || '#3F4A57', recBarD: s.inf.length ? s.inf[0].d : '',
        leySec: (P && P.diametros) || [],
        isSep: s.sepMode === 'separada', sepCm: s.sepCm, maxCapas: s.maxCapas,
        segPeg: s.sepMode === 'pegada' ? on : off, segSep: s.sepMode === 'separada' ? on : off,
        segAerea: !isCim ? on : off, segCim: isCim ? on : off,
        // el chip "Vigueta" (#940, opción A): gris apagado, azul claro encendido, para no pesar como Aérea / Cimentación
        esVigueta: !!(P && P.esVigueta),
        chipVigueta: P && P.esVigueta ? 'border: 1px solid #B9CDEB; background: #EAF1FB; color: #1F5FAF; font-weight: 500'
          : 'border: 1px solid #C9CFD6; background: #FFFFFF; color: #5E6670',
        segHookNo: s.bastHook ? off : on, segHookYes: s.bastHook ? on : off,
        locked: lockedNow, mult, gap, seg6: bar12 ? off : on, seg12: bar12 ? on : off,
        segHook0: hookR === '0' ? on : off, segHook5: hookR === '5' ? on : off, segHook10: hookR === '10' ? on : off,
        lockBg: lockedNow ? '#F4F6F8' : '#FDF6EA', lockText: lockedNow ? 'Bloqueadas para evitar cambios por error.' : 'Desbloqueadas: los cambios valen para todos los proyectos.',
        lockBtn: lockedNow ? 'Desbloquear' : 'Guardar y bloquear', lockPath: lockedNow ? 'M4.5 6V4.2a2.5 2.5 0 015 0V6' : 'M4.5 6V4.2a2.5 2.5 0 014.6-1.3',
        rules: rulesData.map((r, i) => {
          const set = (key) => (e) => this.setState({ rules: rulesData.map((x, j) => (j === i ? { ...x, [key]: e.target.value } : x)) });
          return { d: r.d, t: r.t, g: r.g, g180: r.g180, e: r.e, k: r.k, labT: 'Traslapo ' + r.d, labG: 'Gancho 90° ' + r.d, labG180: 'Gancho 180° ' + r.d,
            labE: 'Gancho de estribo 135° ' + r.d, labK: 'Peso por metro ' + r.d, setT: set('t'), setG: set('g'), setG180: set('g180'), setE: set('e'), setK: set('k') };
        }),
        liveDot: vista.enVivo ? '#1E7A46' : '#9AA3AD', liveText: vista.enVivo ? 'En vivo con Revit' : 'Sin enlace con Revit',
        // #85, decisión 4: con la lista filtrada por "Seleccionar en Revit" se generan todos los de la lista (la sesión salta
        // y nombra los que tienen error); si no, el pórtico abierto.
        genOff: elegidos ? enLista.length === 0 : !P || !P.sePuedeGenerar,
        genBg: (elegidos ? enLista.length > 0 : P && P.sePuedeGenerar) ? '#1F5FAF' : '#9AA3AD',
        disponibles: vista.diametrosDisponibles || null, hasToast: !!s.toast, toast: s.toast,
        // choques: el globo del punto abierto, sus arreglos y "Aplicado · Deshacer"
        choqueAbierto: s.choqueAbierto, arreglos: s.arreglos, onChoque: (i) => this.abrirChoque(i), onArreglo: (a) => this.aplicarArreglo(a),
        onCerrarChoque: () => this.setState({ choqueAbierto: -1, arreglos: null }),
        deshacer: s.deshacer ? s.deshacer.texto : '', deshacerArreglo: () => this.deshacerArreglo(),
        hasConfirmGen: !!s.confirmGen,
        confirmGenText: s.confirmGen ? 'Se generará el acero de ' + (s.confirmGen.length === 1
          ? (PORS.find((p) => p.id === s.confirmGen[0]) || { nombre: s.confirmGen[0] }).nombre
          : s.confirmGen.length + ' pórticos') + '. Lo que el add-in generó antes en ' + (s.confirmGen.length === 1 ? 'él' : 'ellos') + ' se reemplaza.' : '',
        confirmGenYes: () => { this.generar(s.confirmGen, false); this.setState({ confirmGen: null }); },
        confirmGenNo: () => this.setState({ confirmGen: null }),
        // los choques al generar (decisión 8 del plan de choques): no bloquea, pregunta
        hasChoquesGen: !!s.choquesGen,
        choquesGenText: s.choquesGen ? textoChoquesGen(s.choquesGen.porticos, PORS) : '',
        choquesGenRevisar: () => {
          const primero = s.choquesGen.porticos[0].id;
          this.setState({ choquesGen: null, tab: 'alzado' });
          if (!P || P.id !== primero) enviar({ tipo: 'elegirPortico', id: primero });
        },
        choquesGenForzar: () => { this.generar(s.choquesGen.ids, true); this.setState({ choquesGen: null }); },
        // #85: el resumen de "Generar en Revit", una línea por cosa; se queda hasta cerrarlo
        resumen: s.resumen ? { titulo: s.resumen[0], lineas: s.resumen.slice(1) } : null, closeResumen: () => this.setState({ resumen: null }),
        pickEl: (id) => (id ? selectBar(id) : this.setState({ sel: NADA })), pickLap: (id) => { const L = lapsAll.find((x) => x.id === id); if (L) selectLap(L); },
        // gancho de una punta del borde (#84): "S.inicio"… en las corridas, "BS1.inicio"… en los bastones
        onHook: (clave, valor, texto) => {
          this.cambiar({ cambios: conCambio('ganchos', clave, valor) });
          const [quien, punta] = clave.split('.');
          const de = quien.length > 1 ? quien : 'las barras de ' + (quien === 'S' ? 'arriba' : 'abajo');
          this.say((valor === 'Sin' ? 'Sin gancho' : 'Gancho de ' + texto) + ' en la punta ' + (punta === 'inicio' ? 'izquierda' : 'derecha') + ' de ' + de);
        },
        // arrastre (#84): los saltos de los 3 puntos y, en el voladizo, la confirmación dentro de la página (decisión 7)
        arrastre: s.saltos, onDrop: (salto) => { if (salto.enVoladizo) this.setState({ confirmVol: salto }); else this.aplicarSalto(salto); },
        hasConfirm: !!s.confirmVol, confirmText: confirmVolTexto(s.confirmVol, P),
        confirmYes: () => { if (s.confirmVol) this.aplicarSalto(s.confirmVol); },
        confirmNo: () => this.setState({ confirmVol: null }),
        setQ: (e) => this.setState({ q: e.target.value }), setVer: (e) => this.setState({ ver: e.target.value }),
        setDraft: (e) => this.setState({ draft: e.target.value }),
        showAlzado: () => this.setState({ tab: 'alzado' }), showCorte: () => this.setState({ tab: 'corte', step: 'corte', cutWin: false }),
        useIzq: () => { this.setState({ sel: NADA }); this.cambiar({ side: 'izq' }); },
        useDer: () => { this.setState({ sel: NADA }); this.cambiar({ side: 'der' }); },
        toggleList: () => this.setState({ listOpen: !s.listOpen }), toggleMore: () => this.setState({ more: !s.more }),
        toggleRecDraw: () => this.setState({ recDraw: !s.recDraw }),
        setRec: (e) => this.cambiar({ rec: e.target.value }, true), setRecUp: (e) => this.cambiar({ recUp: e.target.value }, true),
        setRecDown: (e) => this.cambiar({ recDown: e.target.value }, true), setRecSide: (e) => this.cambiar({ recSide: e.target.value }, true),
        sepPegada: () => this.cambiar({ sepMode: 'pegada' }), sepSeparada: () => this.cambiar({ sepMode: 'separada' }),
        setSepCm: (e) => this.cambiar({ sepCm: e.target.value }, true), setMaxCapas: (e) => this.cambiar({ maxCapas: e.target.value }, true),
        setAerea: () => { this.setState({ sel: NADA }); this.cambiar({ tipo: 'Aérea' }); },
        setCim: () => { this.setState({ sel: NADA }); this.cambiar({ tipo: 'Cimentación' }); },
        // volver a lo detectado deja la casilla sin corregir (undefined), como si nunca se hubiera tocado
        alternarVigueta: () => this.ponerVigueta(P, !(P && P.esVigueta)),
        setBastSupN: (e) => this.cambiar({ bastSupN: e.target.value }, true), setBastInfN: (e) => this.cambiar({ bastInfN: e.target.value }, true),
        setBastSupD: (e) => this.cambiar({ bastSupD: e.target.value }), setBastInfD: (e) => this.cambiar({ bastInfD: e.target.value }),
        setBastDef: (e) => this.cambiar({ bastDef: e.target.value }, true), hookNo: () => this.cambiar({ bastHook: false }), hookYes: () => this.cambiar({ bastHook: true }),
        setEstD: setEst('estD'), setEstG: setEst('estG'), setConf: setEst('conf', true), setS1: setEst('s1', true), setS2: setEst('s2', true),
        setCutX: (e) => this.cambiar({ cutX: e.target.value }, true), setDepth: (e) => this.cambiar({ depth: e.target.value }, true),
        miraIzq: () => this.cambiar({ mira: 'izquierda' }), miraDer: () => this.cambiar({ mira: 'derecha' }),
        useTypical: () => this.cambiar({ cutX: '', cutLive: null }),
        // La línea 1-1 arrastrada en el alzado, ya pegada a un estribo: abre la ventana del corte; mientras se mueve
        // pide el cálculo con la espera de los campos de texto, y al soltarla, enseguida.
        onCut: (x, soltado) => {
          this.arrastrandoCorte = !soltado;
          this.cambiar({ cutX: f2(x), cutLive: x, cutWin: true }, !soltado);
        },
        openRules: () => this.setState({ step: 'reglas' }),
        closeRules: () => (rulesDirty ? this.setState({ rulesWarn: true }) : leaveRules({})),
        toggleLock: () => {
          if (s.locked) { this.setState({ locked: false, rulesSaved: rulesNow }); return; }
          this.guardarReglas(rulesNow, false);
        },
        warnSave: () => this.guardarReglas(rulesNow, true),
        warnDiscard: () => leaveRules({}),
        warnKeep: () => this.setState({ rulesWarn: false }),
        setMult: (e) => this.setState({ mult: e.target.value }), setGap: (e) => this.setState({ gap: e.target.value }),
        set6: () => this.setState({ bar12: false }), set12: () => this.setState({ bar12: true }),
        setHook: (cm) => () => this.setState({ hookR: cm }),
        toggleLive: () => enviar({ tipo: 'cambiarEnVivo', activo: !vista.enVivo }),
        pickInRevit: () => enviar({ tipo: 'seleccionarEnRevit' }),
        showInRevit: () => { if (P) enviar({ tipo: 'verEnRevit', id: P.id }); },
        // la vista 3D sale con los rombos de choques solo si la capa "Choques" del alzado está prendida (Carlos, 29-oct)
        open3d: () => { if (P) { enviar({ tipo: 'verChoques3d', ver: window.Alzado.verChoques() }); enviar({ tipo: 'abrir3d', id: P.id }); } },
        onChoques: (ver) => enviar({ tipo: 'verChoques3d', ver }),
        // #85, tarea 10: primero la tarjeta de confirmación; "Generar" en ella manda los pórticos.
        generate: () => {
          const ids = elegidos ? enLista.map((p) => p.id) : P ? [P.id] : [];
          if (ids.length) this.setState({ confirmGen: ids, resumen: null });
        },
      };
    }

    // Abre un pórtico desde la planta: lo mismo que el clic en la lista (mismo mensaje a C#); no cambia de modo.
    elegirDesdePlanta(id) {
      this.setState({ ver: 'todo', sel: undefined, pick: null });
      this.props.enviar({ tipo: 'elegirPortico', id });
    }

    // El modo Planta de la barra izquierda (opción D): nivel, filtros, el dibujo con zoom, leyenda y lo elegido.
    barraPlanta(P) {
      const s = this.state, vista = this.props.vista;
      const planta = vista.planta, niveles = planta && Array.isArray(planta.niveles) ? planta.niveles : [];
      if (!niveles.length) {
        return html`<div style="flex: 1; display: flex; align-items: center; justify-content: center; padding: 0 16px; text-align: center; font-size: 12px; color: #5E6670">No hay datos de planta para este modelo.</div>`;
      }
      const idP = P ? P.id : null;
      const delAbierto = idP == null ? null : niveles.find((n) => (n.elementos || []).some((e) => e.portico === idP));
      const nivel = niveles.find((n) => n.nombre === s.plNivel) || delAbierto || niveles[0];
      const W = ANCHO_PLANTA - 28, H = ALTO_PLANTA, iw = W - 2, ih = H - 2;
      const pan = s.plPan || { x: 0, y: 0 };
      const esc = escalaDePlanta(nivel, iw, ih, s.plZoom, pan);
      const ver = s.plVer;
      const elegidos = elegidosEnPlanta(nivel, idP);
      const porDe = (id) => (vista.porticos || []).find((p) => p.id === id);
      // zoom hacia el punto (mx, my) de la caja: lo que está bajo el mouse se queda quieto
      const zoom = (z, mx = iw / 2, my = ih / 2) => {
        const z0 = this.state.plZoom, z1 = Math.max(0.5, Math.min(8, Math.round(z * 100) / 100)), k = z1 / z0, p0 = this.state.plPan || { x: 0, y: 0 };
        this.setState({ plZoom: z1, plPan: { x: mx - iw / 2 - (mx - iw / 2 - p0.x) * k, y: my - ih / 2 - (my - ih / 2 - p0.y) * k } });
      };
      const ajustar = () => this.setState({ plZoom: 1, plPan: { x: 0, y: 0 } });
      const tog = (k) => () => this.setState({ plVer: { ...this.state.plVer, [k]: !this.state.plVer[k] } });
      const cota = (e) => (e >= 0 ? '+' : '−') + Math.abs(e).toFixed(2);
      const fin = (a) => Number.isFinite(a.x1) && Number.isFinite(a.y1) && Number.isFinite(a.x2) && Number.isFinite(a.y2);
      const f = (n) => n.toFixed(1);

      // Arrastrar mueve el dibujo; si el mouse se movió, el clic que sigue no abre nada.
      const arrastrar = (ev) => {
        if (ev.button !== 0) return;
        const x0 = ev.clientX, y0 = ev.clientY, p0 = this.state.plPan || { x: 0, y: 0 };
        this.plArrastro = false;
        const mover = (m) => {
          const dx = m.clientX - x0, dy = m.clientY - y0;
          if (!this.plArrastro && Math.hypot(dx, dy) < 3) return;
          this.plArrastro = true;
          this.setState({ plPan: { x: p0.x + dx, y: p0.y + dy }, plMoviendo: true });
        };
        const soltar = () => {
          window.removeEventListener('pointermove', mover);
          window.removeEventListener('pointerup', soltar);
          this.setState({ plMoviendo: false });
        };
        window.addEventListener('pointermove', mover);
        window.addEventListener('pointerup', soltar);
      };
      // Selección como en Revit: clic = solo ese; Ctrl+clic agrega o quita (una vigueta con grupo, todo su grupo). Con dos
      // o más quedan como los elegidos de "Seleccionar en Revit": la lista muestra esos y "Generar en Revit" los crea todos.
      const seleccion = vista.elegidos ? vista.elegidos : idP != null ? [idP] : [];
      const delGrupo = (e) => e.tipo === 'vigueta' && e.grupo ? (nivel.elementos || []).filter((o) => o.grupo === e.grupo).map((o) => o.portico) : [e.portico];
      // los terminados salen en verde (punto 5 de Santiago, 28-oct); el abierto y los seleccionados van encima
      const terminados = new Set(vista.terminados || []);
      const enSeleccion = new Set(seleccion.flatMap((id) => {
        const e = (nivel.elementos || []).find((o) => o.portico === id);
        return e ? delGrupo(e) : [id];
      }));
      const elegirVarios = (ids, id) => {
        this.setState({ ver: 'todo', sel: undefined, pick: null });
        this.props.enviar({ tipo: 'elegirVarios', ids, abrir: id });
      };
      const abrir = (e, ev) => {
        if (this.plArrastro) return;
        if (!(ev && (ev.ctrlKey || ev.metaKey))) return elegirVarios([e.portico], e.portico);
        const ids = delGrupo(e), quitar = ids.every((id) => seleccion.includes(id));
        const nuevos = quitar ? seleccion.filter((id) => !ids.includes(id)) : seleccion.concat(ids.filter((id) => !seleccion.includes(id)));
        if (!nuevos.length) return;
        elegirVarios(nuevos, quitar ? (nuevos.includes(idP) ? idP : nuevos[0]) : e.portico);
      };

      // Cada viga o vigueta es su rectángulo real en planta (ancho del modelo, con un mínimo para que se vea).
      const items = esc ? (nivel.elementos || []).filter(fin).map((e, i) => {
        const vig = e.tipo === 'vigueta', off = !ver[vig ? 'vigueta' : 'viga'], abierto = elegidos.includes(e);
        const on = abierto || enSeleccion.has(e.portico), listo = terminados.has(e.portico);
        const ax = esc.X(e.x1), ay = esc.Y(e.y1), bx = esc.X(e.x2), by = esc.Y(e.y2);
        const L = Math.hypot(bx - ax, by - ay) || 1, nx = -(by - ay) / L, ny = (bx - ax) / L;
        const t = Math.max(vig ? 2.5 : 3.5, (e.ancho > 0 ? e.ancho : vig ? 0.1 : 0.3) * esc.s) / 2;
        const por = porDe(e.portico);
        return { key: i, off, on, vig, label: (por ? por.nombre : String(e.portico)) + (e.grupo ? ' (' + e.grupo + ')' : ''),
          pick: (ev) => abrir(e, ev),
          tecla: (ev) => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); this.plArrastro = false; abrir(e, ev); } },
          a: [ax, ay], b: [bx, by],
          pts: [[ax + nx * t, ay + ny * t], [bx + nx * t, by + ny * t], [bx - nx * t, by - ny * t], [ax - nx * t, ay - ny * t]].map((p) => f(p[0]) + ',' + f(p[1])).join(' '),
          fill: off ? '#F1F3F5' : abierto ? '#1F5FAF' : on ? '#9DBBE3' : listo ? '#CDEBD5' : vig ? '#E4E7EB' : '#C9CFD6',
          stroke: off ? '#D9DDE2' : on ? '#174A8A' : listo ? '#3B8A57' : vig ? '#7C8692' : '#5E6670' };
      }) : [];
      // los elegidos encima, para que no los tape otro
      items.sort((p, q) => (p.on ? 1 : 0) - (q.on ? 1 : 0) || (p.vig ? 1 : 0) - (q.vig ? 1 : 0));
      const ejes = esc ? (nivel.ejes || []).filter(fin).map((a, i) => {
        let p = [esc.X(a.x1), esc.Y(a.y1)], q = [esc.X(a.x2), esc.Y(a.y2)];
        const vertical = Math.abs(q[1] - p[1]) > Math.abs(q[0] - p[0]);
        // la burbuja va en el extremo de arriba (eje vertical) o de la izquierda (horizontal), 16 px afuera
        if (vertical ? q[1] < p[1] : q[0] < p[0]) [p, q] = [q, p];
        const L = Math.hypot(p[0] - q[0], p[1] - q[1]) || 1, ux = (p[0] - q[0]) / L, uy = (p[1] - q[1]) / L;
        const cx = Math.max(11, Math.min(iw - 11, p[0] + ux * 16)), cy = Math.max(11, Math.min(ih - 11, p[1] + uy * 16));
        return { key: i, n: a.nombre, p, q, cx, cy, fx: p[0] + ux * 6, fy: p[1] + uy * 6 };
      }) : [];
      const cols = esc ? (nivel.columnas || []).map((c) => (c.puntos || []).map((pt) => f(esc.X(pt[0])) + ',' + f(esc.Y(pt[1]))).join(' ')) : [];

      const el0 = elegidos.find((e) => e.portico === idP);
      const porA = idP == null ? null : porDe(idP);
      const info = seleccion.length > 1 ? seleccion.length + ' pórticos seleccionados · Generar en Revit los crea todos'
        : !el0 ? 'Ningún pórtico abierto en este nivel'
        : el0.tipo === 'vigueta' && el0.grupo ? el0.grupo + ' · ' + elegidos.length + ' viguetas iguales seleccionadas'
        : (porA ? porA.nombre + ' · ' + porA.sub : String(idP));

      return html`
      <div style="display: flex; flex-direction: column; gap: 10px; flex: 1; min-height: 0">
        <div style="display: flex; align-items: center; gap: 8px">
          <label for="nivel-planta" style="color: #5E6670; font-size: 12px">Nivel</label>
          <select id="nivel-planta" value=${nivel.nombre} onChange=${(ev) => this.setState({ plNivel: ev.target.value, plZoom: 1, plPan: { x: 0, y: 0 } })} style="flex: 1; height: 32px; padding: 0 8px; border: 1px solid #C9CFD6; border-radius: 6px; font: inherit; color: #16191D; background: #FFFFFF">
            ${niveles.map((n) => html`<option value=${n.nombre}>${n.nombre + (Number.isFinite(n.elevacion) ? ' · ' + cota(n.elevacion) : '')}</option>`)}
          </select>
        </div>
        <div style="display: flex; align-items: center; gap: 6px">
          <span style="color: #5E6670; font-size: 12px; margin-right: 2px">Mostrar</span>
          <button type="button" aria-pressed=${ver.viga ? 'true' : 'false'} onClick=${tog('viga')} style="height: 24px; padding: 0 10px; border-radius: 999px; font: inherit; font-size: 12px; cursor: pointer; ${ver.viga ? CHIP_ON : CHIP_OFF}">Vigas</button>
          <button type="button" aria-pressed=${ver.vigueta ? 'true' : 'false'} onClick=${tog('vigueta')} style="height: 24px; padding: 0 10px; border-radius: 999px; font: inherit; font-size: 12px; cursor: pointer; ${ver.vigueta ? CHIP_ON : CHIP_OFF}">Viguetas</button>
        </div>
        <div onWheel=${(ev) => { ev.preventDefault(); const r = ev.currentTarget.getBoundingClientRect(); zoom(s.plZoom * (ev.deltaY < 0 ? 1.15 : 1 / 1.15), ev.clientX - r.left - 1, ev.clientY - r.top - 1); }} style="position: relative; width: ${W}px; height: ${H}px; flex-shrink: 0; overflow: hidden; background: #FFFFFF; border: 1px solid #D9DDE2; border-radius: 6px; box-sizing: border-box">
          <svg width=${iw} height=${ih} onPointerDown=${arrastrar} style="position: absolute; left: 0; top: 0; display: block; touch-action: none; user-select: none; cursor: ${s.plMoviendo ? 'grabbing' : 'grab'}">
            <style>${'.pl-el{cursor:pointer;outline:none}.pl-el:hover .pl-cuerpo,.pl-el:focus-visible .pl-cuerpo{stroke:#1F5FAF;stroke-width:1.6}.pl-off{cursor:default;pointer-events:none}'}</style>
            <g aria-hidden="true">
              ${ejes.map((a) => html`<line key=${a.key} x1=${f(a.fx)} y1=${f(a.fy)} x2=${f(a.q[0])} y2=${f(a.q[1])} stroke="#B5BCC4" stroke-width="0.8" stroke-dasharray="10 3 2 3"></line>`)}
            </g>
            ${items.map((e) => html`
              <g key=${e.key} class=${e.off ? 'pl-el pl-off' : 'pl-el'} role="button" tabindex=${e.off ? -1 : 0} aria-label=${e.label} aria-pressed=${e.on ? 'true' : 'false'} aria-disabled=${e.off ? 'true' : 'false'} onClick=${e.pick} onKeyDown=${e.tecla}>
                <title>${e.label}</title>
                <line x1=${f(e.a[0])} y1=${f(e.a[1])} x2=${f(e.b[0])} y2=${f(e.b[1])} stroke="transparent" stroke-width="12" stroke-linecap="round"></line>
                <polygon class="pl-cuerpo" points=${e.pts} fill=${e.fill} stroke=${e.stroke} stroke-width="0.8" stroke-linejoin="round"></polygon>
              </g>`)}
            <g aria-hidden="true" style="pointer-events: none">
              ${cols.map((pts, i) => html`<polygon key=${i} points=${pts} fill="#3F4A57" stroke="#16191D" stroke-width="0.6"></polygon>`)}
              ${ejes.map((a) => html`<g key=${'b' + a.key}>
                <circle cx=${f(a.cx)} cy=${f(a.cy)} r="9" fill="#FFFFFF" stroke="#7C8692" stroke-width="1"></circle>
                <text x=${f(a.cx)} y=${f(a.cy)} text-anchor="middle" dominant-baseline="central" font-size="10" font-weight="600" fill="#3F4A57" font-family="'IBM Plex Sans', 'Segoe UI', sans-serif">${a.n}</text>
              </g>`)}
            </g>
          </svg>
          <div style="position: absolute; right: 6px; top: 6px; z-index: 5; display: flex; flex-direction: column; border: 1px solid #C9CFD6; border-radius: 6px; overflow: hidden; background: #FFFFFF">
            <button type="button" onClick=${() => zoom(s.plZoom * 1.25)} aria-label="Acercar" style="width: 28px; height: 28px; border: 0; background: #FFFFFF; font: inherit; font-size: 15px; color: #16191D; cursor: pointer">+</button>
            <button type="button" onClick=${() => zoom(s.plZoom / 1.25)} aria-label="Alejar" style="width: 28px; height: 28px; border: 0; border-top: 1px solid #C9CFD6; background: #FFFFFF; font: inherit; font-size: 15px; color: #16191D; cursor: pointer">−</button>
            <button type="button" onClick=${ajustar} aria-label="Ver todo el nivel" title="Ver todo el nivel" style="width: 28px; height: 28px; border: 0; border-top: 1px solid #C9CFD6; background: #FFFFFF; cursor: pointer; display: grid; place-items: center">
              <svg width="13" height="13" viewBox="0 0 14 14" aria-hidden="true"><path d="M1.5 5V1.5H5M9 1.5h3.5V5M12.5 9v3.5H9M5 12.5H1.5V9" stroke="#16191D" stroke-width="1.4" fill="none"></path></svg>
            </button>
          </div>
        </div>
        <div style="display: flex; flex-wrap: wrap; gap: 6px 14px; font-size: 12px; color: #5E6670">
          <span style="display: flex; align-items: center; gap: 6px"><span style="width: 16px; height: 6px; background: #C9CFD6; border: 1px solid #5E6670; box-sizing: border-box"></span>Viga</span>
          <span style="display: flex; align-items: center; gap: 6px"><span style="width: 16px; height: 4px; background: #E4E7EB; border: 1px solid #7C8692; box-sizing: border-box"></span>Vigueta</span>
          <span style="display: flex; align-items: center; gap: 6px"><span style="width: 8px; height: 8px; background: #3F4A57"></span>Columna</span>
          <span style="display: flex; align-items: center; gap: 6px"><span style="width: 16px; height: 6px; background: #1F5FAF"></span>Abierto</span>
          <span style="display: flex; align-items: center; gap: 6px"><span style="width: 16px; height: 6px; background: #9DBBE3"></span>Seleccionado</span>
          <span style="display: flex; align-items: center; gap: 6px"><span style="width: 16px; height: 6px; background: #CDEBD5; border: 1px solid #3B8A57; box-sizing: border-box"></span>Terminado</span>
        </div>
        <div style="display: flex; align-items: center; gap: 8px; padding: 8px 10px; border: 1px solid #1F5FAF; border-radius: 6px; background: #F3F7FC">
          <span style="width: 10px; height: 10px; flex-shrink: 0; background: #1F5FAF"></span>
          <span title=${info} style="font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis">${info}</span>
        </div>      </div>`;
    }

    render() {
      const m = this.medidas();
      if (!this.props.vista) return html`<div style="width: ${m.pageW}; height: ${m.pageH}; background: #EEF0F2"></div>`;
      const v = this.valores();
      const P = this.props.vista.actual;
      return html`
<div style="position: relative; width: ${m.pageW}; height: ${m.pageH}; display: flex; flex-direction: column; background: #EEF0F2; font-family: 'IBM Plex Sans', 'Segoe UI', sans-serif; font-size: 13px; color: #16191D; overflow: hidden">

  <div style="height: 48px; flex-shrink: 0; display: flex; align-items: center; gap: 12px; padding: 0 16px; background: #FFFFFF; border-bottom: 1px solid #D9DDE2; white-space: nowrap">
    <svg width="18" height="18" viewBox="0 0 18 18" aria-hidden="true"><path d="M2 5h14M2 13h14M5 3v4M13 11v4" stroke="#1F5FAF" stroke-width="1.8" fill="none" stroke-linecap="round"></path></svg>
    <span style="font-weight: 600; font-size: 14px">Acero en vigas</span>
    <span style="color: #5E6670">${v.proyecto}</span>
    <div style="flex: 1"></div>
    <button type="button" onClick=${v.toggleLive} style="display: flex; align-items: center; gap: 8px; height: 32px; padding: 0 12px; border: 1px solid #C9CFD6; border-radius: 16px; background: #FFFFFF; font: inherit; color: #16191D; cursor: pointer">
      <span style="width: 8px; height: 8px; border-radius: 4px; background: ${v.liveDot}"></span>${v.liveText}
    </button>
    <button type="button" onClick=${v.openRules} style="display: flex; align-items: center; gap: 6px; height: 32px; padding: 0 12px; border: 1px solid #C9CFD6; border-radius: 6px; background: #FFFFFF; font: inherit; color: #16191D; cursor: pointer">
      <svg width="15" height="15" viewBox="0 0 16 16" aria-hidden="true"><circle cx="8" cy="8" r="2.3" stroke="currentColor" stroke-width="1.4" fill="none"></circle><path d="M8 1.5v2M8 12.5v2M1.5 8h2M12.5 8h2M3.4 3.4l1.4 1.4M11.2 11.2l1.4 1.4M3.4 12.6l1.4-1.4M11.2 4.8l1.4-1.4" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"></path></svg>
      Reglas
    </button>
    <button type="button" onClick=${v.generate} disabled=${v.genOff} style="height: 32px; padding: 0 16px; border: 1px solid ${v.genBg}; border-radius: 6px; background: ${v.genBg}; color: #FFFFFF; font: inherit; font-weight: 600; cursor: pointer">Generar en Revit</button>
  </div>

  <div style="flex: 1; min-height: 0; display: flex">

    <div style="width: ${this.state.barra === 'planta' ? ANCHO_PLANTA : ANCHO_LISTA}px; flex-shrink: 0; display: flex; flex-direction: column; gap: 10px; padding: 14px; background: #FFFFFF; border-right: 1px solid #D9DDE2; box-sizing: border-box; overflow: hidden; transition: width 180ms ease-out">
      <div style="flex-shrink: 0; display: flex; border: 1px solid #C9CFD6; border-radius: 6px; overflow: hidden">
        <button type="button" aria-pressed=${this.state.barra === 'planta' ? 'false' : 'true'} onClick=${() => this.setState({ barra: 'lista' })} style="flex: 1; height: 30px; border: 0; font: inherit; cursor: pointer; ${this.state.barra === 'planta' ? TAB_OFF : TAB_ON}">Lista</button>
        <button type="button" aria-pressed=${this.state.barra === 'planta' ? 'true' : 'false'} onClick=${() => this.setState({ barra: 'planta' })} style="flex: 1; height: 30px; border: 0; border-left: 1px solid #C9CFD6; font: inherit; cursor: pointer; ${this.state.barra === 'planta' ? TAB_ON : TAB_OFF}">Planta</button>
      </div>
      ${this.state.barra === 'planta' ? this.barraPlanta(P) : html`
      <div style="display: flex; flex-direction: column; gap: 10px; flex: 1; min-height: 0">
      <div style="display: flex; align-items: baseline; justify-content: space-between">
        <span style="font-weight: 600">Pórticos</span>
        <span style="color: #5E6670; font-size: 12px">${v.porCount}${v.filtrado ? html` · <button type="button" onClick=${v.verTodos} style="padding: 0; border: 0; background: transparent; font: inherit; color: #1F5FAF; cursor: pointer">Ver todos</button>` : null}</span>
      </div>
      <button type="button" onClick=${v.pickInRevit} style="display: flex; align-items: center; justify-content: center; gap: 8px; height: 36px; border: 1px solid #1F5FAF; border-radius: 6px; background: #FFFFFF; color: #1F5FAF; font: inherit; font-weight: 600; cursor: pointer">
        <svg width="15" height="15" viewBox="0 0 16 16" aria-hidden="true"><path d="M3 2l9 5-4 1.2L6.5 13z" stroke="currentColor" stroke-width="1.5" fill="none" stroke-linejoin="round"></path></svg>
        Seleccionar en Revit
      </button>
      <label for="q-main" style="position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0)">Buscar pórtico</label>
      <input id="q-main" type="search" value=${v.q} onInput=${v.setQ} placeholder="Buscar por nombre o eje" style="height: 32px; padding: 0 10px; border: 1px solid #C9CFD6; border-radius: 6px; font: inherit; color: #16191D; background: #FAFBFC" />
      <div style="display: flex; flex-direction: column; gap: 4px; overflow: auto; flex: 1; min-height: 0">
        ${v.pors.map((p) => html`
          <div data-portico=${p.id} style="display: flex; flex-direction: column; border: 1px solid ${p.bd}; border-radius: 6px; background: ${p.bg}">
            <button type="button" onClick=${p.pick} style="display: flex; flex-direction: column; gap: 2px; padding: 8px 10px; border: 0; background: transparent; font: inherit; color: #16191D; text-align: left; cursor: pointer">
              <span style="display: flex; align-items: center; justify-content: space-between; gap: 8px; width: 100%; white-space: nowrap">
                <span style="font-weight: 600">${p.name}</span>
                <span style="flex-shrink: 0; font-size: 11px; padding: 1px 7px; border-radius: 10px; background: ${p.grupo ? '#E8F0FA' : '#EEF0F2'}; color: ${p.grupo ? '#1F5FAF' : '#3F4A57'}">${p.grupo || p.tipo}</span>
              </span>
              <span title=${p.sub} style="font-size: 12px; color: #5E6670; white-space: nowrap; overflow: hidden; text-overflow: ellipsis">${p.sub}</span>
            </button>
            ${p.open ? html`
              <div style="display: flex; flex-direction: column; padding: 0 6px 6px">
                ${p.tramos.map((t) => html`
                  <div style="display: flex; align-items: center; gap: 8px; padding: 2px 4px; border-radius: 4px; background: ${t.bg}">
                    <button type="button" aria-label=${t.chkLabel} aria-pressed=${t.pressed} onClick=${t.toggle} style="width: 18px; height: 18px; flex-shrink: 0; padding: 0; border: 1.5px solid ${t.boxBd}; border-radius: 4px; background: ${t.boxBg}; cursor: pointer; display: grid; place-items: center">
                      <svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true"><path d="M1.5 5.2l2.2 2.2L8.5 2.6" stroke="#FFFFFF" stroke-width="1.6" fill="none" stroke-linecap="round" stroke-linejoin="round"></path></svg>
                    </button>
                    <button type="button" onClick=${t.view} style="flex: 1; padding: 4px 0; border: 0; background: transparent; font: inherit; font-size: 12px; color: #16191D; text-align: left; cursor: pointer; white-space: nowrap">${t.name}</button>
                  </div>`)}
              </div>` : null}
          </div>`)}
      </div>      </div>`}
    </div>

    ${v.hasPor ? html`
      <div style="flex: 1; min-width: 0; min-height: 0; display: flex; flex-direction: column; gap: 12px; padding: 16px">
        <div style="display: flex; align-items: center; gap: 12px; white-space: nowrap">
          <div style="flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 2px">
            <span style="display: flex; align-items: center; gap: 8px">
              <span style="font-size: 18px; font-weight: 600">${v.por.name}</span>
              <button type="button" onClick=${v.toggleTerminado} aria-pressed=${v.terminado ? 'true' : 'false'} title=${v.terminado ? 'Quitar la marca de terminado' : 'Marcar como terminado'} style="display: flex; align-items: center; gap: 5px; height: 22px; padding: 0 4px; border: 0; border-radius: 11px; background: transparent; font: inherit; font-size: 11px; font-weight: 600; color: #3B8A57; cursor: pointer">
                <span style="width: 16px; height: 16px; box-sizing: border-box; border-radius: 50%; border: 1.5px solid ${v.terminado ? '#3B8A57' : '#B5BCC4'}; background: ${v.terminado ? '#3B8A57' : 'transparent'}; display: grid; place-items: center; transition: background 120ms ease-out, border-color 120ms ease-out">
                  ${v.terminado ? html`<svg width="9" height="9" viewBox="0 0 10 10" aria-hidden="true"><path d="M1.5 5.2l2.2 2.2L8.5 2.6" stroke="#FFFFFF" stroke-width="1.7" fill="none" stroke-linecap="round" stroke-linejoin="round"></path></svg>` : null}
                </span>
                ${v.terminado ? 'Terminado' : null}
              </button>
            </span>
            <span style="font-size: 12px; color: #5E6670; overflow: hidden; text-overflow: ellipsis">${v.por.sub} · ${v.tipo} · ${v.summary}</span>
          </div>
          <label for="ver-main" style="color: #5E6670; font-size: 12px">Ver</label>
          <select id="ver-main" value=${v.ver} onChange=${v.setVer} style="flex-shrink: 0; height: 32px; padding: 0 8px; border: 1px solid #C9CFD6; border-radius: 6px; font: inherit; color: #16191D; background: #FFFFFF">
            <option value="todo">Todo el pórtico</option>${v.tramos.map((t) => html`<option value=${t.id}>${t.nombre}</option>`)}
          </select>
          <div style="flex-shrink: 0; display: flex; border: 1px solid #C9CFD6; border-radius: 6px; overflow: hidden">
            <button type="button" onClick=${v.showAlzado} style="height: 30px; padding: 0 12px; border: 0; font: inherit; cursor: pointer; ${v.tabA}">Alzado</button>
            <button type="button" onClick=${v.showCorte} style="height: 30px; padding: 0 12px; border: 0; border-left: 1px solid #C9CFD6; font: inherit; cursor: pointer; ${v.tabC}">Corte 1-1</button>
          </div>
          <button type="button" onClick=${v.showInRevit} style="flex-shrink: 0; display: flex; align-items: center; gap: 6px; height: 32px; padding: 0 10px; border: 1px solid #C9CFD6; border-radius: 6px; background: #FFFFFF; font: inherit; color: #16191D; cursor: pointer">
            <svg width="15" height="15" viewBox="0 0 16 16" aria-hidden="true"><circle cx="8" cy="8" r="5" stroke="currentColor" stroke-width="1.4" fill="none"></circle><path d="M8 1v3M8 12v3M1 8h3M12 8h3" stroke="currentColor" stroke-width="1.4" fill="none" stroke-linecap="round"></path></svg>
            Ver en Revit
          </button>
          <button type="button" onClick=${v.open3d} aria-label="Abrir 3D aislado en Revit" style="flex-shrink: 0; display: flex; align-items: center; gap: 6px; height: 32px; padding: 0 10px; border: 1px solid #C9CFD6; border-radius: 6px; background: #FFFFFF; font: inherit; color: #16191D; cursor: pointer">
            <svg width="15" height="15" viewBox="0 0 16 16" aria-hidden="true"><path d="M8 1.5l5.5 3v7L8 14.5l-5.5-3v-7zM8 8l5.5-3.5M8 8v6.5M8 8L2.5 4.5" stroke="currentColor" stroke-width="1.3" fill="none" stroke-linejoin="round"></path></svg>
            3D
          </button>
        </div>

        ${v.isAlzado ? html`
          <div style="position: relative; width: ${m.dibujoW}px; height: ${m.dibujoH}px">
            <${Alzado} onChoques=${v.onChoques} portico=${P} w=${m.dibujoW} h=${m.dibujoH} sel=${v.barSel} selLap=${v.lapSel} lapsManual=${v.lapsManual} invert=${v.isCim} warn=${v.isAviso} warnLap=${v.warnLap} showStirrups=${true} corte=${v.corte} cutLive=${v.cutLive} onCut=${v.onCut} rangeFrom=${v.rangeFrom} rangeTo=${v.rangeTo} onPick=${v.pickEl} onPickLap=${v.pickLap} onHook=${v.onHook} arrastre=${v.arrastre} onDrop=${v.onDrop} choqueAbierto=${v.choqueAbierto} arreglos=${v.arreglos} onChoque=${v.onChoque} onArreglo=${v.onArreglo} onCerrarChoque=${v.onCerrarChoque} />
            ${v.cutWin ? html`<${VentanaCorte} v=${v} W=${m.dibujoW} H=${m.dibujoH} />` : null}
          </div>` : null}
        ${v.isCorte ? html`
          <div style="width: ${m.dibujoW}px; height: ${m.dibujoH}px; box-sizing: border-box; display: flex; align-items: center; justify-content: center; gap: 48px; background: #FAFBFC; border: 1px solid #D9DDE2; border-radius: 6px">
            ${dibujoSeccion(v.sec)}
            <div style="display: flex; flex-direction: column; gap: 8px; font-size: 13px; max-width: 320px">
              <span style="display: flex; align-items: baseline; gap: 8px; white-space: nowrap">
                <span style="font-weight: 600; font-size: 15px">Corte 1-1</span>
                ${v.cutTipo ? html`<span style="font-size: 11px; font-weight: 600; color: #1E7A46">Sección más común</span>` : null}
              </span>
              <span style="display: flex; gap: 12px; font-family: 'IBM Plex Mono', monospace; font-size: 12px; color: #3F4A57">
                ${v.leySec.map((d) => html`<span style="display: flex; align-items: center; gap: 5px"><span style="width: 10px; height: 10px; border-radius: 50%; background: ${d.color}"></span>${d.nombre}</span>`)}
              </span>
              <span>A ${v.cutPos} m del eje A · ${v.cutTramo}</span>
              <span>Mirando a la ${v.cutMira} · proyección ${v.cutProf} m</span>
              <span><b>Arriba</b> · ${v.secTop}</span>
              <span><b>Abajo</b> · ${v.secBot}</span>
              <span>${v.secEstTexto}${v.secAddText}</span>
              ${v.isPicking ? html`
                <span style="color: #9A5B00; line-height: 1.45">${v.pickMsg}</span>` : null}
              <span style="color: #5E6670">Recubrimiento ${v.rec} cm · al eje de la barra ${v.alEje} cm</span>
              ${v.cutAviso ? html`<span role="status" style="color: #B42318">${v.cutAviso}</span>` : null}
            </div>
          </div>` : null}

        ${v.isAviso ? html`
          <div role="status" style="display: flex; gap: 10px; align-items: flex-start; padding: 10px 12px; border: 1px solid #E8C88F; border-radius: 6px; background: #FDF6EA; font-size: 12px; line-height: 1.45">
            <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true" style="flex-shrink: 0; margin-top: 1px"><path d="M8 2l6.5 11.5h-13z" stroke="#B26B00" stroke-width="1.4" fill="none" stroke-linejoin="round"></path><path d="M8 6.5v3.2M8 11.6v.1" stroke="#B26B00" stroke-width="1.6" stroke-linecap="round"></path></svg>
            <span style="flex: 1"><b>${v.avisoNegrita}</b>${v.avisoResto}</span>
            ${v.avisoCambiar ? html`<button type="button" onClick=${v.side === 'der' ? v.useIzq : v.useDer} style="flex-shrink: 0; padding: 0; border: 0; background: transparent; font: inherit; font-weight: 600; color: #1F5FAF; white-space: nowrap; cursor: pointer">Cambiar</button>` : null}
          </div>` : null}

        <div style="display: flex; align-items: center; gap: 12px; padding: 10px 12px; background: #FFFFFF; border: 1px solid #D9DDE2; border-radius: 6px; white-space: nowrap">
          <span style="font-weight: 600">${v.faceQuestion}</span>
          <div style="flex: 1"></div>
          <button type="button" onClick=${v.useIzq} style="display: flex; align-items: center; gap: 8px; height: 34px; padding: 0 12px; border: 1px solid ${v.izq.bd}; border-radius: 6px; background: ${v.izq.bg}; font: inherit; color: #16191D; cursor: pointer">
            Al inicio de la luz <span style="font-family: 'IBM Plex Mono', monospace">${v.izq.kg}</span><span style="font-size: 11px; font-weight: 600; color: #1E7A46">${v.izq.tag}</span>
          </button>
          <button type="button" onClick=${v.useDer} style="display: flex; align-items: center; gap: 8px; height: 34px; padding: 0 12px; border: 1px solid ${v.der.bd}; border-radius: 6px; background: ${v.der.bg}; font: inherit; color: #16191D; cursor: pointer">
            Al final de la luz <span style="font-family: 'IBM Plex Mono', monospace">${v.der.kg}</span><span style="font-size: 11px; font-weight: 600; color: #1E7A46">${v.der.tag}</span>
          </button>
        </div>

        <div style="display: flex; flex-direction: column; gap: 10px; padding: 12px 14px; background: #FFFFFF; border: 1px solid #D9DDE2; border-radius: 6px">
          ${v.isSelBar ? html`
            <div style="display: flex; align-items: center; gap: 16px; white-space: nowrap">
              <div style="display: flex; flex-direction: column; gap: 2px; min-width: 190px">
                <span style="font-weight: 600; font-size: 15px">Barra ${v.sb.id}</span>
                <span style="color: #5E6670; font-size: 12px">${v.sb.where} · ${v.sb.qty}</span>
              </div>
              <span style="font-family: 'IBM Plex Mono', monospace; font-size: 16px">${v.sb.len} m</span>
              <span style="font-size: 11px; font-weight: 600; color: ${v.sb.tagCol}">${v.sb.tag}</span>
              <div style="flex: 1"></div>
              <button type="button" onClick=${v.sb.down} disabled=${v.sb.downOff} title=${v.sb.downWhy} style="height: 32px; padding: 0 12px; border: 1px solid #C9CFD6; border-radius: 6px; background: #FFFFFF; font: inherit; color: ${v.sb.downCol}; cursor: pointer">Bajar a ${v.sb.downVal}</button>
              <button type="button" onClick=${v.sb.up} disabled=${v.sb.upOff} title=${v.sb.upWhy} style="height: 32px; padding: 0 12px; border: 1px solid #C9CFD6; border-radius: 6px; background: #FFFFFF; font: inherit; color: ${v.sb.upCol}; cursor: pointer">Subir a ${v.sb.upVal}</button>
              <button type="button" onClick=${v.sb.reset} disabled=${v.sb.notManual} style="height: 32px; padding: 0 12px; border: 0; background: transparent; font: inherit; color: ${v.sb.resetCol}; cursor: pointer">Automático</button>
            </div>
            <span style="font-size: 12px; color: #5E6670">${v.sb.note}</span>` : null}
          ${v.isSelLap ? html`
            <div style="display: flex; align-items: center; gap: 16px; white-space: nowrap">
              <div style="display: flex; flex-direction: column; gap: 2px; min-width: 190px">
                <span style="font-weight: 600; font-size: 15px">Traslapo ${v.sl.id}</span>
                <span style="color: #5E6670; font-size: 12px">Mínimo ${v.sl.min} m para ${v.sl.diam} · ahora ${v.sl.cur} m</span>
              </div>
              <label for="lap-len" style="color: #3F4A57">Largo</label>
              <input id="lap-len" type="text" inputmode="decimal" value=${v.draft} onInput=${v.setDraft} style="width: 70px; height: 32px; padding: 0 8px; border: 1px solid #1F5FAF; border-radius: 6px; font-family: 'IBM Plex Mono', monospace; font-size: 13px; color: #16191D" />
              <span style="color: #5E6670">m</span>
              <div style="flex: 1"></div>
              <button type="button" onClick=${v.sl.reset} disabled=${v.sl.notManual} style="height: 32px; padding: 0 12px; border: 0; background: transparent; font: inherit; color: ${v.sl.resetCol}; cursor: pointer">Volver al mínimo</button>
              <button type="button" onClick=${v.sl.apply} disabled=${v.sl.off} style="height: 32px; padding: 0 16px; border: 1px solid ${v.sl.applyBg}; border-radius: 6px; background: ${v.sl.applyBg}; font: inherit; font-weight: 600; color: #FFFFFF; cursor: pointer">Aplicar</button>
            </div>
            <span style="font-size: 12px; color: ${v.sl.effCol}">${v.sl.eff}</span>` : null}
          ${v.isSelBast ? html`
            <div style="display: flex; align-items: center; gap: 16px; white-space: nowrap">
              <div style="display: flex; flex-direction: column; gap: 2px; min-width: 190px">
                <span style="font-weight: 600; font-size: 15px">Bastón ${v.st.id}</span>
                <span style="color: #5E6670; font-size: 12px">${v.st.where}</span>
              </div>
              <label for="bast-len" style="color: #3F4A57">Largo</label>
              <input id="bast-len" type="text" inputmode="decimal" value=${v.draft} onInput=${v.setDraft} style="width: 70px; height: 32px; padding: 0 8px; border: 1px solid #1F5FAF; border-radius: 6px; font-family: 'IBM Plex Mono', monospace; font-size: 13px; color: #16191D" />
              <span style="color: #5E6670">m</span>
              <div style="flex: 1"></div>
              <button type="button" onClick=${v.st.reset} style="height: 32px; padding: 0 12px; border: 0; background: transparent; font: inherit; color: #1F5FAF; cursor: pointer">Usar el largo general</button>
              <button type="button" onClick=${v.st.apply} style="height: 32px; padding: 0 16px; border: 1px solid #1F5FAF; border-radius: 6px; background: #1F5FAF; font: inherit; font-weight: 600; color: #FFFFFF; cursor: pointer">Aplicar</button>
            </div>
            <span style="font-size: 12px; color: #5E6670">${v.st.note}</span>` : null}
        </div>

        <div style="display: flex; flex-direction: column; min-height: 0; ${v.listOpen ? 'flex: 1;' : ''} background: #FFFFFF; border: 1px solid #D9DDE2; border-radius: 6px; overflow: hidden">
          <button type="button" onClick=${v.toggleList} style="display: flex; align-items: center; gap: 8px; padding: 10px 12px; border: 0; background: transparent; font: inherit; color: #16191D; text-align: left; cursor: pointer; white-space: nowrap">
            <span style="font-weight: 600">Lista de barras</span>
            <span style="color: #5E6670">${v.summary}</span>
            <div style="flex: 1"></div>
            <span style="color: #1F5FAF">${v.listLabel}</span>
          </button>
          ${v.listOpen ? html`
            <div style="flex: 1; min-height: 90px; overflow: auto; border-top: 1px solid #D9DDE2">
              ${v.rows.map((r) => html`
                <button type="button" onClick=${r.pick} style="display: grid; grid-template-columns: 56px 90px 80px 70px minmax(0, 1fr) 70px; gap: 8px; width: 100%; padding: 6px 12px; border: 0; border-bottom: 1px solid #EEF0F2; background: ${r.bg}; font: inherit; font-size: 12px; color: #16191D; text-align: left; cursor: pointer; white-space: nowrap">
                  <span style="font-weight: 600">${r.id}</span>
                  <span style="color: #3F4A57">${r.kind}</span>
                  <span style="color: #3F4A57">${r.qty}</span>
                  <span style="font-family: 'IBM Plex Mono', monospace">${r.len}</span>
                  <span style="color: #3F4A57">${r.lap}</span>
                  <span style="font-family: 'IBM Plex Mono', monospace; text-align: right">${r.kg}</span>
                </button>`)}
            </div>` : null}
        </div>
      </div>` : null}

    ${v.noPor ? html`
      <div style="flex: 1; min-width: 0; display: flex; align-items: center; justify-content: center; padding: 16px">
        <div style="display: flex; flex-direction: column; align-items: center; gap: 12px; max-width: 360px; text-align: center">
          <svg width="56" height="56" viewBox="0 0 56 56" aria-hidden="true"><rect x="4" y="20" width="48" height="16" rx="2" stroke="#9AA3AD" stroke-width="1.6" fill="none"></rect><path d="M8 25h40M8 31h40" stroke="#C9CFD6" stroke-width="1.4" stroke-dasharray="3 3"></path><path d="M30 6l12 7-5 1.5-2.5 5.5z" stroke="#1F5FAF" stroke-width="1.6" fill="none" stroke-linejoin="round"></path></svg>
          <span style="font-size: 16px; font-weight: 600">Elige un pórtico para empezar</span>          <button type="button" onClick=${v.pickInRevit} style="height: 36px; padding: 0 16px; border: 1px solid #1F5FAF; border-radius: 6px; background: #1F5FAF; color: #FFFFFF; font: inherit; font-weight: 600; cursor: pointer">Seleccionar en Revit</button>
        </div>
      </div>` : null}

    <div style="width: 340px; flex-shrink: 0; display: flex; flex-direction: column; background: #FFFFFF; border-left: 1px solid #D9DDE2; box-sizing: border-box">
      ${v.showSteps ? html`
        <div style="display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); border-bottom: 1px solid #D9DDE2">
          ${v.steps.map((s) => html`
            <button type="button" onClick=${s.go} style="display: flex; flex-direction: column; align-items: center; gap: 2px; padding: 10px 0 8px; border: 0; background: transparent; font: inherit; cursor: pointer; ${s.style}">
              <span style="font-size: 11px; font-family: 'IBM Plex Mono', monospace">${s.n}</span>
              <span style="font-size: 13px">${s.name}</span>
            </button>`)}
        </div>` : null}

      <div style="flex: 1; min-height: 0; overflow: auto; padding: 16px; display: flex; flex-direction: column; gap: 16px">
        ${v.panelEmpty ? html`
          <span style="color: #5E6670">Elige un pórtico para configurar su acero.</span>` : null}

        ${v.isBarras ? html`
          <div style="display: flex; flex-direction: column; gap: 16px">
            <div style="display: flex; flex-direction: column; gap: 6px">
              <div style="display: flex; align-items: center; justify-content: space-between; gap: 8px">
                <span style="font-weight: 600">Tipo de viga</span>
                <button type="button" aria-pressed=${v.esVigueta} onClick=${v.alternarVigueta} style="height: 24px; padding: 0 10px; border-radius: 999px; font: inherit; font-size: 12px; cursor: pointer; ${v.chipVigueta}">Vigueta</button>
              </div>
              <div style="display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); border: 1px solid #C9CFD6; border-radius: 6px; overflow: hidden">
                <button type="button" onClick=${v.setAerea} style="height: 32px; border: 0; font: inherit; cursor: pointer; ${v.segAerea}">Aérea</button>
                <button type="button" onClick=${v.setCim} style="height: 32px; border: 0; border-left: 1px solid #C9CFD6; font: inherit; cursor: pointer; ${v.segCim}">Cimentación</button>
              </div>
            </div>

            ${v.layers.map((ly) => html`
              <div style="display: flex; flex-direction: column; gap: 8px">
                <div style="display: flex; align-items: baseline; justify-content: space-between">
                  <span style="font-weight: 600">${ly.title}</span>
                  <span style="font-size: 12px; color: #5E6670">caben ${ly.per} por fila</span>
                </div>
                ${ly.rows.map((r) => html`
                  <div style="display: flex; flex-direction: column; gap: 6px">
                  <div style="display: flex; align-items: center; gap: 8px">
                    <div style="display: flex; align-items: center; border: 1px solid #C9CFD6; border-radius: 6px; height: 32px">
                      <button type="button" aria-label=${r.minusLabel} onClick=${r.minus} style="width: 30px; height: 30px; border: 0; background: transparent; font: inherit; cursor: pointer">−</button>
                      <span style="width: 28px; text-align: center; font-family: 'IBM Plex Mono', monospace">${r.n}</span>
                      <button type="button" aria-label=${r.plusLabel} onClick=${r.plus} style="width: 30px; height: 30px; border: 0; background: transparent; font: inherit; cursor: pointer">+</button>
                    </div>
                    <span style="color: #5E6670">barras</span>
                    <span aria-hidden="true" style="width: 10px; height: 10px; flex-shrink: 0; border-radius: 50%; background: ${r.c}"></span>
                    <select aria-label=${r.selLabel} value=${r.d} onChange=${r.setD} style="flex: 1; height: 32px; border: 1px solid #C9CFD6; border-radius: 6px; font: inherit; background: #FFFFFF; color: #16191D; padding: 0 6px">
                      ${opcionesDiametro(v.disponibles, 3, 8, r.d, true)}
                    </select>
                    ${r.canRemove ? html`
                      <button type="button" aria-label=${r.removeLabel} onClick=${r.remove} style="width: 28px; height: 28px; border: 0; background: transparent; font: inherit; color: #5E6670; cursor: pointer">✕</button>` : null}
                  </div>
                  <div style="display: flex; align-items: center; gap: 8px; padding-left: 2px">
                    <span style="font-size: 12px; color: #5E6670">Van en</span>
                    <select aria-label=${r.filaLabel} value=${r.f} onChange=${r.setF} style="height: 28px; border: 1px solid #C9CFD6; border-radius: 6px; font: inherit; font-size: 12px; background: #FFFFFF; color: #16191D; padding: 0 6px">
                      <option value="">Auto</option>${r.filasOpc.map((x) => html`<option value=${x}>Fila ${x}</option>`)}
                    </select>
                  </div>
                  </div>`)}
                <div style="display: flex; flex-direction: column; gap: 4px; padding: 8px 10px; background: #F4F6F8; border-radius: 6px">
                  ${ly.filas.map((fi) => html`
                    <div style="display: flex; align-items: center; gap: 8px">
                      <span style="width: 44px; font-size: 11px; color: #5E6670">${fi.label}</span>
                      <div style="display: flex; gap: 5px">
                        ${fi.dots.map((dt) => html`
                          <span style="width: 12px; height: 12px; border-radius: 50%; background: ${dt.c}"></span>`)}
                      </div>
                    </div>`)}
                </div>
                <span style="font-size: 12px; color: ${ly.msgCol}; line-height: 1.45">${ly.msg}</span>
                <button type="button" onClick=${ly.add} style="align-self: flex-start; padding: 0; border: 0; background: transparent; font: inherit; color: #1F5FAF; cursor: pointer">+ Otro diámetro</button>
              </div>`)}

            <div style="display: flex; flex-direction: column; gap: 6px">
              <div style="display: flex; align-items: center; gap: 8px">
                <label for="rec-main" style="font-weight: 600; flex: 1">Recubrimiento</label>
                <input id="rec-main" type="text" inputmode="decimal" value=${v.rec} onInput=${v.setRec} style="width: 60px; height: 32px; padding: 0 8px; border: 1px solid #C9CFD6; border-radius: 6px; font-family: 'IBM Plex Mono', monospace; font-size: 13px; color: #16191D" />
                <span style="color: #5E6670">cm</span>
              </div>
              <span style="font-size: 12px; color: #5E6670">Libre, el que se pone en Revit. Al eje de la barra: ${v.alEje} cm (lo calcula el add-in). <button type="button" onClick=${v.toggleRecDraw} style="padding: 0; border: 0; background: transparent; font: inherit; color: #1F5FAF; cursor: pointer">${v.recDrawLabel}</button></span>
              ${v.recDraw ? html`
              <svg width="308" height="118" viewBox="0 0 308 118" role="img" aria-label="Recubrimiento libre hasta el estribo y recubrimiento al eje de la barra">
                <rect x="0" y="0" width="308" height="100" fill="#FFFFFF" stroke="#D9DDE2"></rect>
                <line x1="0" y1="100" x2="308" y2="100" stroke="#3F4A57" stroke-width="1.6"></line>
                <text x="4" y="113" font-size="10" fill="#5E6670" font-family="IBM Plex Mono, monospace">cara de la viga</text>
                <rect x="16" y="60" width="276" height="8" rx="2" fill="#C9CFD6" stroke="#8A939D"></rect>
                <text x="236" y="55" font-size="10" fill="#5E6670" font-family="IBM Plex Mono, monospace">estribo</text>
                <circle cx="96" cy="48" r="8" fill="${v.recBarCol}"></circle>
                <text x="40" y="36" font-size="10" fill="#16191D" font-family="IBM Plex Mono, monospace">barra ${v.recBarD}</text>
                <line x1="112" y1="48" x2="112" y2="100" stroke="#3F4A57" stroke-width="1.2"></line>
                <line x1="106" y1="48" x2="118" y2="48" stroke="#3F4A57" stroke-width="1.2" stroke-dasharray="2 2"></line>
                <text x="120" y="86" font-size="11" font-weight="600" fill="#3F4A57" font-family="IBM Plex Mono, monospace">al eje ${v.alEje} cm</text>
                <line x1="214" y1="68" x2="214" y2="100" stroke="#9A5B00" stroke-width="1.2"></line>
                <line x1="208" y1="68" x2="220" y2="68" stroke="#9A5B00" stroke-width="1.2"></line>
                <text x="222" y="88" font-size="11" font-weight="600" fill="#9A5B00" font-family="IBM Plex Mono, monospace">libre ${v.rec} cm</text>
              </svg>` : null}
            </div>

            <button type="button" onClick=${v.toggleMore} style="align-self: flex-start; padding: 0; border: 0; background: transparent; font: inherit; color: #1F5FAF; cursor: pointer">${v.moreLabel}</button>
            ${v.more ? html`
              <div style="display: flex; flex-direction: column; gap: 10px; padding: 12px; background: #F4F6F8; border-radius: 6px">
                <span style="font-size: 12px; color: #5E6670">Recubrimiento por cara (vacío = el general)</span>
                <div style="display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 8px">
                  <label style="display: flex; flex-direction: column; gap: 4px; font-size: 12px; color: #3F4A57">Arriba<input type="text" inputmode="decimal" value=${v.recUp} onInput=${v.setRecUp} placeholder=${v.rec} style="height: 30px; padding: 0 8px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; color: #16191D" /></label>
                  <label style="display: flex; flex-direction: column; gap: 4px; font-size: 12px; color: #3F4A57">Abajo<input type="text" inputmode="decimal" value=${v.recDown} onInput=${v.setRecDown} placeholder=${v.rec} style="height: 30px; padding: 0 8px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; color: #16191D" /></label>
                  <label style="display: flex; flex-direction: column; gap: 4px; font-size: 12px; color: #3F4A57">Lados<input type="text" inputmode="decimal" value=${v.recSide} onInput=${v.setRecSide} placeholder=${v.rec} style="height: 30px; padding: 0 8px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; color: #16191D" /></label>
                </div>
                <div style="display: flex; align-items: center; gap: 8px">
                  <span style="flex: 1; color: #3F4A57">Segunda fila</span>
                  <div style="display: flex; border: 1px solid #C9CFD6; border-radius: 6px; overflow: hidden">
                    <button type="button" onClick=${v.sepPegada} style="height: 28px; padding: 0 10px; border: 0; font: inherit; cursor: pointer; ${v.segPeg}">Pegada</button>
                    <button type="button" onClick=${v.sepSeparada} style="height: 28px; padding: 0 10px; border: 0; border-left: 1px solid #C9CFD6; font: inherit; cursor: pointer; ${v.segSep}">Separada</button>
                  </div>
                </div>
                ${v.isSep ? html`
                  <div style="display: flex; align-items: center; gap: 8px">
                    <label for="sep-cm" style="flex: 1; color: #3F4A57">Separación libre</label>
                    <input id="sep-cm" type="text" inputmode="decimal" value=${v.sepCm} onInput=${v.setSepCm} style="width: 60px; height: 30px; padding: 0 8px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; color: #16191D" />
                    <span style="color: #5E6670">cm</span>
                  </div>` : null}
                <div style="display: flex; align-items: center; gap: 8px">
                  <label for="max-capas" style="flex: 1; color: #3F4A57">Filas como máximo</label>
                  <input id="max-capas" type="text" inputmode="numeric" value=${v.maxCapas} onInput=${v.setMaxCapas} style="width: 60px; height: 30px; padding: 0 8px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; color: #16191D" />
                </div>
              </div>` : null}
          </div>` : null}

        ${v.isBastones ? html`
          <div style="display: flex; flex-direction: column; gap: 16px">            <svg width="308" height="104" viewBox="0 0 308 104" role="img" aria-label="Bastones arriba sobre las columnas y abajo en el centro de cada luz">
              <rect x="10" y="30" width="288" height="40" fill="#F1F3F5" stroke="#9AA3AD"></rect>
              <rect x="10" y="14" width="14" height="72" fill="#D5DAE0" stroke="#9AA3AD"></rect>
              <rect x="147" y="14" width="14" height="72" fill="#D5DAE0" stroke="#9AA3AD"></rect>
              <rect x="284" y="14" width="14" height="72" fill="#D5DAE0" stroke="#9AA3AD"></rect>
              <line x1="16" y1="36" x2="292" y2="36" stroke="#3F4A57" stroke-width="2"></line>
              <line x1="16" y1="64" x2="292" y2="64" stroke="#3F4A57" stroke-width="2"></line>
              <line x1="116" y1="42" x2="192" y2="42" stroke="#0F7B6C" stroke-width="3" stroke-linecap="round"></line>
              <line x1="16" y1="42" x2="54" y2="42" stroke="#0F7B6C" stroke-width="3" stroke-linecap="round"></line>
              <line x1="254" y1="42" x2="292" y2="42" stroke="#0F7B6C" stroke-width="3" stroke-linecap="round"></line>
              <line x1="48" y1="58" x2="124" y2="58" stroke="#0F7B6C" stroke-width="3" stroke-linecap="round"></line>
              <line x1="184" y1="58" x2="260" y2="58" stroke="#0F7B6C" stroke-width="3" stroke-linecap="round"></line>
              <line x1="154" y1="6" x2="154" y2="94" stroke="#8A939D" stroke-dasharray="3 3"></line>
              <text x="154" y="10" text-anchor="middle" font-size="10" fill="#0F7B6C" font-family="IBM Plex Mono, monospace">${v.bastTxt}</text>
              <text x="86" y="100" text-anchor="middle" font-size="10" fill="#0F7B6C" font-family="IBM Plex Mono, monospace">centro de luz</text>
              <text x="222" y="100" text-anchor="middle" font-size="10" fill="#0F7B6C" font-family="IBM Plex Mono, monospace">centro de luz</text>
            </svg>
            <div style="display: flex; align-items: center; gap: 8px">
              <label for="bast-def" style="font-weight: 600; flex: 1">Largo de todos</label>
              <input id="bast-def" type="text" inputmode="decimal" value=${v.bastDef} onInput=${v.setBastDef} style="width: 70px; height: 32px; padding: 0 8px; border: 1px solid #C9CFD6; border-radius: 6px; font-family: 'IBM Plex Mono', monospace; font-size: 13px; color: #16191D" />
              <span style="color: #5E6670">m</span>
            </div>
            <div style="display: flex; flex-direction: column; gap: 8px">
              <span style="font-weight: 600">Acero</span>
              <div style="display: flex; align-items: center; gap: 8px">
                <label for="bast-sup-n" style="flex: 1; color: #3F4A57">Arriba</label>
                <input id="bast-sup-n" type="text" inputmode="numeric" value=${v.bastSupN} onInput=${v.setBastSupN} style="width: 48px; height: 32px; padding: 0 8px; border: 1px solid #C9CFD6; border-radius: 6px; font-family: 'IBM Plex Mono', monospace; font-size: 13px; color: #16191D" />
                <select aria-label="Diámetro de los bastones de arriba" value=${v.bastSupD} onChange=${v.setBastSupD} style="height: 32px; border: 1px solid #C9CFD6; border-radius: 6px; font: inherit; background: #FFFFFF; color: #16191D; padding: 0 6px">
                  ${opcionesDiametro(v.disponibles, 3, 8, v.bastSupD, false)}
                </select>
              </div>
              <div style="display: flex; align-items: center; gap: 8px">
                <label for="bast-inf-n" style="flex: 1; color: #3F4A57">Abajo</label>
                <input id="bast-inf-n" type="text" inputmode="numeric" value=${v.bastInfN} onInput=${v.setBastInfN} style="width: 48px; height: 32px; padding: 0 8px; border: 1px solid #C9CFD6; border-radius: 6px; font-family: 'IBM Plex Mono', monospace; font-size: 13px; color: #16191D" />
                <select aria-label="Diámetro de los bastones de abajo" value=${v.bastInfD} onChange=${v.setBastInfD} style="height: 32px; border: 1px solid #C9CFD6; border-radius: 6px; font: inherit; background: #FFFFFF; color: #16191D; padding: 0 6px">
                  ${opcionesDiametro(v.disponibles, 3, 8, v.bastInfD, false)}
                </select>
              </div>            </div>
            <div style="display: flex; align-items: center; gap: 8px">
              <span style="font-weight: 600; flex: 1">Gancho</span>
              <div style="display: flex; border: 1px solid #C9CFD6; border-radius: 6px; overflow: hidden">
                <button type="button" onClick=${v.hookNo} style="height: 30px; padding: 0 14px; border: 0; font: inherit; cursor: pointer; ${v.segHookNo}">Sin gancho</button>
                <button type="button" onClick=${v.hookYes} style="height: 30px; padding: 0 14px; border: 0; border-left: 1px solid #C9CFD6; font: inherit; cursor: pointer; ${v.segHookYes}">Con gancho</button>
              </div>
            </div>          </div>` : null}

        ${v.isEstribos ? html`
          <div style="display: flex; flex-direction: column; gap: 14px">
            <div style="padding: 8px 10px; border-radius: 6px; background: ${v.estScopeBg}; font-size: 12px; color: #16191D">${v.estScope}</div>
            <svg width="308" height="96" viewBox="0 0 308 96" role="img" aria-label="Estribos más juntos cerca de las columnas y más separados en el centro">
              <rect x="22" y="20" width="264" height="44" fill="#F1F3F5" stroke="#9AA3AD"></rect>
              <rect x="8" y="8" width="14" height="68" fill="#D5DAE0" stroke="#9AA3AD"></rect>
              <rect x="286" y="8" width="14" height="68" fill="#D5DAE0" stroke="#9AA3AD"></rect>
              <line x1="24" y1="42" x2="84" y2="42" stroke="#3F4A57" stroke-width="40" stroke-dasharray="1.4 4.6"></line>
              <line x1="88" y1="42" x2="220" y2="42" stroke="#3F4A57" stroke-width="40" stroke-dasharray="1.4 11"></line>
              <line x1="224" y1="42" x2="284" y2="42" stroke="#3F4A57" stroke-width="40" stroke-dasharray="1.4 4.6"></line>
              <text x="54" y="80" text-anchor="middle" font-size="10" fill="#B26B00" font-family="IBM Plex Mono, monospace">${v.confTxt}</text>
              <text x="54" y="92" text-anchor="middle" font-size="10" fill="#5E6670" font-family="IBM Plex Mono, monospace">${v.s1Txt}</text>
              <text x="154" y="86" text-anchor="middle" font-size="10" fill="#5E6670" font-family="IBM Plex Mono, monospace">${v.s2Txt}</text>
              <text x="254" y="80" text-anchor="middle" font-size="10" fill="#B26B00" font-family="IBM Plex Mono, monospace">${v.confTxt}</text>
              <text x="254" y="92" text-anchor="middle" font-size="10" fill="#5E6670" font-family="IBM Plex Mono, monospace">${v.s1Txt}</text>
            </svg>
            <div style="display: flex; align-items: center; gap: 8px">
              <span style="font-weight: 600; flex: 1">${v.esGrapa ? 'Grapa' : 'Estribo'}</span>
              <select aria-label="Diámetro del estribo" value=${v.estD} onChange=${v.setEstD} style="height: 32px; border: 1px solid #C9CFD6; border-radius: 6px; font: inherit; background: #FFFFFF; color: #16191D; padding: 0 6px">
                ${opcionesDiametro(v.disponibles, 3, 4, v.estD, true)}
              </select>
              <select aria-label="Gancho del estribo" value=${v.estG} onChange=${v.setEstG} style="height: 32px; border: 1px solid #C9CFD6; border-radius: 6px; font: inherit; background: #FFFFFF; color: #16191D; padding: 0 6px">
                <option value="135°">135°</option><option value="90°">90°</option><option value="180°">180°</option>
              </select>
              ${v.esGrapa ? html`
                <div role="group" aria-label="Forma de la grapa" style="display: flex; border: 1px solid #C9CFD6; border-radius: 6px; overflow: hidden">
                  ${v.formasGrapa.map((fo) => html`
                    <button type="button" aria-label=${fo.label} aria-pressed=${fo.pressed} onClick=${fo.pick} style="height: 30px; width: 30px; padding: 0; border: 0; font: inherit; font-weight: 600; cursor: pointer; ${fo.style}">${fo.f}</button>`)}
                </div>` : null}
            </div>
            <div style="display: flex; flex-direction: column; gap: 8px">
              <span style="font-weight: 600">Junto a las columnas</span>
              <div style="display: flex; align-items: center; gap: 8px">
                <label for="conf-len" style="flex: 1; color: #3F4A57">Largo de la zona</label>
                <input id="conf-len" type="text" inputmode="decimal" value=${v.conf} onInput=${v.setConf} style="width: 60px; height: 30px; padding: 0 8px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; color: #16191D" />
                <span style="color: #5E6670; width: 16px">m</span>
              </div>
              <div style="display: flex; align-items: center; gap: 8px">
                <label for="conf-s" style="flex: 1; color: #3F4A57">Un estribo cada</label>
                <input id="conf-s" type="text" inputmode="decimal" value=${v.s1} onInput=${v.setS1} style="width: 60px; height: 30px; padding: 0 8px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; color: #16191D" />
                <span style="color: #5E6670; width: 16px">m</span>
              </div>
            </div>
            <div style="display: flex; flex-direction: column; gap: 8px">
              <span style="font-weight: 600">En el centro</span>
              <div style="display: flex; align-items: center; gap: 8px">
                <label for="mid-s" style="flex: 1; color: #3F4A57">Un estribo cada</label>
                <input id="mid-s" type="text" inputmode="decimal" value=${v.s2} onInput=${v.setS2} style="width: 60px; height: 30px; padding: 0 8px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; color: #16191D" />
                <span style="color: #5E6670; width: 16px">m</span>
              </div>
            </div>
            <div style="display: flex; flex-direction: column; gap: 8px">
              <span style="font-weight: 600">Estribos adicionales</span>
              ${v.estAddList.map((ea) => html`
                <div style="display: flex; align-items: center; gap: 8px; padding: 6px 8px; border: 1px solid #D9DDE2; border-radius: 6px; font-size: 12px">
                  <div style="flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 2px">
                    <span style="font-weight: 600">${ea.name}</span>
                    <span style="color: #5E6670">${ea.desc}</span>
                    ${ea.motivo ? html`<span role="status" style="color: #B42318">${ea.motivo}</span>` : null}
                  </div>
                  ${ea.isAbierto ? html`
                    <div role="group" aria-label="Forma de la grapa" style="display: flex; border: 1px solid #C9CFD6; border-radius: 6px; overflow: hidden">
                      ${ea.formas.map((fo) => html`
                        <button type="button" aria-label=${fo.label} aria-pressed=${fo.pressed} onClick=${fo.pick} style="height: 26px; width: 28px; padding: 0; border: 0; font: inherit; font-weight: 600; cursor: pointer; ${fo.style}">${fo.f}</button>`)}
                    </div>` : null}
                  <button type="button" aria-label=${ea.removeLabel} onClick=${ea.remove} style="height: 28px; padding: 0 8px; border: 0; background: transparent; font: inherit; color: #B42318; cursor: pointer">Quitar</button>
                </div>`)}
              ${v.noPick ? html`
                <div style="display: flex; gap: 8px">
                  <button type="button" onClick=${v.startCerrado} style="height: 32px; padding: 0 12px; border: 1px solid #1F5FAF; border-radius: 6px; background: #FFFFFF; font: inherit; color: #1F5FAF; cursor: pointer">+ Cerrado</button>
                  <button type="button" onClick=${v.startAbierto} style="height: 32px; padding: 0 12px; border: 1px solid #1F5FAF; border-radius: 6px; background: #FFFFFF; font: inherit; color: #1F5FAF; cursor: pointer">+ Abierto</button>
                </div>` : null}
              ${v.isPicking ? html`
                <div style="display: flex; flex-direction: column; gap: 8px; padding: 10px 12px; border: 1px solid #E8C88F; border-radius: 6px; background: #FDF6EA; font-size: 12px; line-height: 1.45">
                  <span>${v.pickMsg}</span>
                  <div style="display: flex; gap: 8px">
                    <button type="button" onClick=${v.addPick} disabled=${v.pickNotDone} style="height: 30px; padding: 0 12px; border: 1px solid #1F5FAF; border-radius: 6px; background: #1F5FAF; font: inherit; font-weight: 600; color: #FFFFFF; cursor: pointer">Agregar</button>
                    <button type="button" onClick=${v.cancelPick} style="height: 30px; padding: 0 8px; border: 0; background: transparent; font: inherit; color: #1F5FAF; cursor: pointer">Cancelar</button>
                  </div>
                </div>` : null}
            </div>
          </div>` : null}

        ${v.isCorteStep ? html`
          <div style="display: flex; flex-direction: column; gap: 14px">            <div style="display: flex; align-items: center; gap: 8px">
              <label for="cut-x" style="font-weight: 600; flex: 1">Distancia desde el eje A</label>
              <input id="cut-x" type="text" inputmode="decimal" value=${v.cutX} onInput=${v.setCutX} style="width: 70px; height: 32px; padding: 0 8px; border: 1px solid ${v.cutBd}; border-radius: 6px; font-family: 'IBM Plex Mono', monospace; font-size: 13px; color: #16191D" />
              <span style="color: #5E6670">m</span>
            </div>
            ${v.cutAjustado ? html`
              <span role="status" style="font-size: 12px; color: #9A5B00; line-height: 1.45">Ahí no se puede cortar (traslapo o apoyo): el corte se muestra en ${v.cutPos} m.</span>` : null}
            <div style="display: flex; align-items: center; gap: 8px">
              <span style="font-weight: 600; flex: 1">Mirando hacia</span>
              <div style="display: flex; border: 1px solid #C9CFD6; border-radius: 6px; overflow: hidden">
                <button type="button" onClick=${v.miraIzq} style="height: 30px; padding: 0 12px; border: 0; font: inherit; cursor: pointer; ${v.segMiraIzq}">Izquierda</button>
                <button type="button" onClick=${v.miraDer} style="height: 30px; padding: 0 12px; border: 0; border-left: 1px solid #C9CFD6; font: inherit; cursor: pointer; ${v.segMiraDer}">Derecha</button>
              </div>
            </div>
            <div style="display: flex; align-items: center; gap: 8px">
              <label for="cut-d" style="font-weight: 600; flex: 1">Profundidad de la vista</label>
              <input id="cut-d" type="text" inputmode="decimal" value=${v.depth} onInput=${v.setDepth} style="width: 70px; height: 32px; padding: 0 8px; border: 1px solid #C9CFD6; border-radius: 6px; font-family: 'IBM Plex Mono', monospace; font-size: 13px; color: #16191D" />
              <span style="color: #5E6670">m</span>
            </div>
            <button type="button" onClick=${v.useTypical} style="align-self: flex-start; padding: 0; border: 0; background: transparent; font: inherit; color: #1F5FAF; cursor: pointer">Volver a la sección más común</button>
          </div>` : null}

        ${v.isReglas ? html`
          <div style="display: flex; flex-direction: column; gap: 14px">
            <button type="button" onClick=${v.closeRules} style="align-self: flex-start; display: flex; align-items: center; gap: 6px; padding: 0; border: 0; background: transparent; font: inherit; color: #1F5FAF; cursor: pointer">
              <svg width="12" height="12" viewBox="0 0 12 12" aria-hidden="true"><path d="M7.5 2.5L4 6l3.5 3.5" stroke="currentColor" stroke-width="1.6" fill="none" stroke-linecap="round" stroke-linejoin="round"></path></svg>
              Volver al pórtico
            </button>
            <div style="display: flex; flex-direction: column; gap: 2px">
              <span style="font-weight: 600; font-size: 15px">Reglas generales</span>            </div>
            ${v.rulesWarn ? html`
              <div role="alert" style="display: flex; flex-direction: column; gap: 8px; padding: 10px 12px; border: 1px solid #E8C88F; border-radius: 6px; background: #FDF6EA; font-size: 12px; line-height: 1.45">
                <span style="font-weight: 600">Hay cambios sin guardar en las reglas generales.</span>
                <div style="display: flex; flex-wrap: wrap; gap: 8px; align-items: center">
                  <button type="button" onClick=${v.warnSave} style="height: 30px; padding: 0 12px; border: 1px solid #1F5FAF; border-radius: 6px; background: #1F5FAF; font: inherit; font-weight: 600; color: #FFFFFF; cursor: pointer">Guardar y bloquear</button>
                  <button type="button" onClick=${v.warnDiscard} style="height: 30px; padding: 0 12px; border: 1px solid #C9CFD6; border-radius: 6px; background: #FFFFFF; font: inherit; color: #16191D; cursor: pointer">Descartar</button>
                  <button type="button" onClick=${v.warnKeep} style="height: 30px; padding: 0 8px; border: 0; background: transparent; font: inherit; color: #1F5FAF; cursor: pointer">Seguir editando</button>
                </div>
              </div>` : null}
            <div style="display: flex; align-items: center; gap: 8px; padding: 8px 10px; border-radius: 6px; background: ${v.lockBg}">
              <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden="true"><rect x="2.5" y="6" width="9" height="6.5" rx="1.2" stroke="currentColor" stroke-width="1.3" fill="none"></rect><path d="${v.lockPath}" stroke="currentColor" stroke-width="1.3" fill="none" stroke-linecap="round"></path></svg>
              <span style="flex: 1; font-size: 12px">${v.lockText}</span>
              <button type="button" onClick=${v.toggleLock} style="padding: 0; border: 0; background: transparent; font: inherit; font-size: 12px; font-weight: 600; color: #1F5FAF; cursor: pointer">${v.lockBtn}</button>
            </div>
            <div style="display: grid; grid-template-columns: minmax(0, 1fr) 70px 24px; gap: 8px; align-items: center">
              <label for="r-mult" style="color: #3F4A57">Múltiplo de corte</label>
              <input id="r-mult" type="text" value=${v.mult} onInput=${v.setMult} disabled=${v.locked} style="height: 30px; padding: 0 8px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; color: #16191D" />
              <span style="color: #5E6670">m</span>
              <label for="r-gap" style="color: #3F4A57">Distancia libre entre barras</label>
              <input id="r-gap" type="text" value=${v.gap} onInput=${v.setGap} disabled=${v.locked} style="height: 30px; padding: 0 8px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; color: #16191D" />
              <span style="color: #5E6670">cm</span>
            </div>
            <div style="display: flex; align-items: center; gap: 8px">
              <span style="flex: 1; color: #3F4A57">Varilla</span>
              <div style="display: flex; border: 1px solid #C9CFD6; border-radius: 6px; overflow: hidden">
                <button type="button" onClick=${v.set6} disabled=${v.locked} style="height: 28px; padding: 0 12px; border: 0; font: inherit; cursor: pointer; ${v.seg6}">6 m</button>
                <button type="button" onClick=${v.set12} disabled=${v.locked} style="height: 28px; padding: 0 12px; border: 0; border-left: 1px solid #C9CFD6; font: inherit; cursor: pointer; ${v.seg12}">12 m</button>
              </div>
            </div>
            <div style="display: flex; flex-direction: column; gap: 4px">
              <div style="display: flex; align-items: center; gap: 8px">
                <span style="flex: 1; color: #3F4A57">Redondear ganchos</span>
                <div role="group" aria-label="Redondear ganchos" style="display: flex; border: 1px solid #C9CFD6; border-radius: 6px; overflow: hidden">
                  <button type="button" onClick=${v.setHook('0')} disabled=${v.locked} style="height: 28px; padding: 0 10px; border: 0; font: inherit; cursor: pointer; ${v.segHook0}">No</button>
                  <button type="button" onClick=${v.setHook('5')} disabled=${v.locked} style="height: 28px; padding: 0 10px; border: 0; border-left: 1px solid #C9CFD6; font: inherit; cursor: pointer; ${v.segHook5}">5 cm</button>
                  <button type="button" onClick=${v.setHook('10')} disabled=${v.locked} style="height: 28px; padding: 0 10px; border: 0; border-left: 1px solid #C9CFD6; font: inherit; cursor: pointer; ${v.segHook10}">10 cm</button>
                </div>
              </div>
              <span style="font-size: 11px; line-height: 1.4; color: #5E6670">El largo total del gancho de barras y bastones sube al siguiente múltiplo, para cortar y doblar con una medida estándar: #4 a 90° = 0.222 → 0.25 con 5 cm. Los estribos no cambian.</span>
            </div>            <div style="display: flex; flex-direction: column; gap: 6px">
              <div style="display: grid; grid-template-columns: 30px repeat(5, minmax(0, 1fr)); gap: 5px; font-size: 11px; color: #5E6670">
                <span></span><span>Traslapo</span><span>Gancho 90°</span><span>Gancho 180°</span><span>Estribo 135°</span><span>kg/m</span>
              </div>
              ${v.rules.map((ru) => html`
                <div style="display: grid; grid-template-columns: 30px repeat(5, minmax(0, 1fr)); gap: 5px; align-items: center">
                  <span style="color: #3F4A57">${ru.d}</span>
                  <input type="text" aria-label=${ru.labT} value=${ru.t} onInput=${ru.setT} disabled=${v.locked} placeholder="—" style="width: 100%; box-sizing: border-box; height: 28px; padding: 0 5px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; font-size: 12px; color: #16191D" />
                  <input type="text" aria-label=${ru.labG} value=${ru.g} onInput=${ru.setG} disabled=${v.locked} placeholder="—" style="width: 100%; box-sizing: border-box; height: 28px; padding: 0 5px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; font-size: 12px; color: #16191D" />
                  <input type="text" aria-label=${ru.labG180} value=${ru.g180} onInput=${ru.setG180} disabled=${v.locked} placeholder="—" style="width: 100%; box-sizing: border-box; height: 28px; padding: 0 5px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; font-size: 12px; color: #16191D" />
                  <input type="text" aria-label=${ru.labE} value=${ru.e} onInput=${ru.setE} disabled=${v.locked} placeholder="—" style="width: 100%; box-sizing: border-box; height: 28px; padding: 0 5px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; font-size: 12px; color: #16191D" />
                  <input type="text" aria-label=${ru.labK} value=${ru.k} onInput=${ru.setK} disabled=${v.locked} style="width: 100%; box-sizing: border-box; height: 28px; padding: 0 6px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; font-size: 12px; color: #16191D" />
                </div>`)}
              <span style="font-size: 11px; line-height: 1.4; color: #5E6670">Gancho y estribo: largo total del gancho (curva + tramo recto), el que se suma a la barra. A 90° la cota de la pata en Revit mide ese largo y las cotas suman el largo de la barra; en 135° y 180° la cota es solo la pata y sale menor.</span>
            </div>
          </div>` : null}
      </div>
    </div>
  </div>

  ${v.hasConfirm ? html`
    <div style="position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; background: rgba(22, 25, 29, 0.32)">
      <div role="alertdialog" aria-labelledby="vol-t" aria-describedby="vol-d" style="width: 440px; display: flex; flex-direction: column; gap: 14px; padding: 18px 20px; background: #FFFFFF; border: 1px solid #D9DDE2; border-radius: 8px; box-shadow: 0 12px 32px rgba(22, 25, 29, 0.22)">
        <div style="display: flex; gap: 10px; align-items: flex-start">
          <svg width="18" height="18" viewBox="0 0 16 16" aria-hidden="true" style="flex-shrink: 0; margin-top: 1px"><path d="M8 2l6.5 11.5h-13z" stroke="#B26B00" stroke-width="1.4" fill="none" stroke-linejoin="round"></path><path d="M8 6.5v3.2M8 11.6v.1" stroke="#B26B00" stroke-width="1.6" stroke-linecap="round"></path></svg>
          <div style="display: flex; flex-direction: column; gap: 6px">
            <span id="vol-t" style="font-weight: 600; font-size: 15px">En un voladizo nunca se traslapa. ¿Ponerlo igual?</span>
            <span id="vol-d" style="font-size: 12px; color: #5E6670; line-height: 1.5">${v.confirmText}</span>
          </div>
        </div>
        <div style="display: flex; justify-content: flex-end; gap: 8px">
          <button type="button" onClick=${v.confirmNo} style="height: 32px; padding: 0 14px; border: 1px solid #C9CFD6; border-radius: 6px; background: #FFFFFF; font: inherit; color: #16191D; cursor: pointer">Cancelar</button>
          <button type="button" onClick=${v.confirmYes} style="height: 32px; padding: 0 14px; border: 1px solid #9A5B00; border-radius: 6px; background: #9A5B00; font: inherit; font-weight: 600; color: #FFFFFF; cursor: pointer">Ponerlo igual</button>
        </div>
      </div>
    </div>` : null}
  ${v.hasConfirmGen ? html`
    <div style="position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; background: rgba(22, 25, 29, 0.32)">
      <div role="alertdialog" aria-labelledby="gen-t" aria-describedby="gen-d" style="width: 440px; display: flex; flex-direction: column; gap: 14px; padding: 18px 20px; background: #FFFFFF; border: 1px solid #D9DDE2; border-radius: 8px; box-shadow: 0 12px 32px rgba(22, 25, 29, 0.22)">
        <div style="display: flex; flex-direction: column; gap: 6px">
          <span id="gen-t" style="font-weight: 600; font-size: 15px">¿Generar el acero en Revit?</span>
          <span id="gen-d" style="font-size: 12px; color: #5E6670; line-height: 1.5">${v.confirmGenText}</span>
        </div>
        <div style="display: flex; justify-content: flex-end; gap: 8px">
          <button type="button" onClick=${v.confirmGenNo} style="height: 32px; padding: 0 14px; border: 1px solid #C9CFD6; border-radius: 6px; background: #FFFFFF; font: inherit; color: #16191D; cursor: pointer">Cancelar</button>
          <button type="button" onClick=${v.confirmGenYes} style="height: 32px; padding: 0 14px; border: 1px solid #1F5FAF; border-radius: 6px; background: #1F5FAF; font: inherit; font-weight: 600; color: #FFFFFF; cursor: pointer">Generar</button>
        </div>
      </div>
    </div>` : null}
  ${v.hasChoquesGen ? html`
    <div style="position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; background: rgba(22, 25, 29, 0.32)">
      <div role="alertdialog" aria-labelledby="chq-t" aria-describedby="chq-d" style="width: 440px; display: flex; flex-direction: column; gap: 14px; padding: 18px 20px; background: #FFFFFF; border: 1px solid #D9DDE2; border-radius: 8px; box-shadow: 0 12px 32px rgba(22, 25, 29, 0.22)">
        <div style="display: flex; flex-direction: column; gap: 6px">
          <span id="chq-t" style="font-weight: 600; font-size: 15px">Hay barras que se tocan</span>
          <span id="chq-d" style="font-size: 12px; color: #5E6670; line-height: 1.5">${v.choquesGenText}</span>
        </div>
        <div style="display: flex; justify-content: flex-end; gap: 8px">
          <button type="button" onClick=${v.choquesGenRevisar} style="height: 32px; padding: 0 14px; border: 1px solid #1F5FAF; border-radius: 6px; background: #1F5FAF; font: inherit; font-weight: 600; color: #FFFFFF; cursor: pointer">Revisar</button>
          <button type="button" onClick=${v.choquesGenForzar} style="height: 32px; padding: 0 14px; border: 1px solid #C9CFD6; border-radius: 6px; background: #FFFFFF; font: inherit; color: #16191D; cursor: pointer">Generar de todos modos</button>
        </div>
      </div>
    </div>` : null}
  ${v.hasToast ? html`
    <div role="status" style="position: absolute; left: 50%; bottom: 20px; transform: translateX(-50%); padding: 10px 16px; border-radius: 8px; background: #16191D; color: #FFFFFF; font-size: 13px; white-space: nowrap">${v.toast}</div>` : null}
  ${v.deshacer ? html`
    <div role="status" style="position: absolute; left: 50%; bottom: ${v.hasToast ? 64 : 20}px; transform: translateX(-50%); display: flex; align-items: center; gap: 14px; padding: 8px 10px 8px 16px; border-radius: 8px; background: #16191D; color: #FFFFFF; font-size: 13px; white-space: nowrap">
      <span>Aplicado: ${v.deshacer}</span>
      <button type="button" onClick=${v.deshacerArreglo} style="height: 28px; padding: 0 12px; border: 1px solid #5E6670; border-radius: 6px; background: transparent; color: #FFFFFF; font: inherit; font-weight: 600; cursor: pointer">Deshacer</button>
    </div>` : null}
  ${v.resumen ? html`
    <div role="status" style="position: absolute; left: 50%; bottom: 20px; transform: translateX(-50%); width: min(720px, calc(100% - 32px)); max-height: 45%; display: flex; flex-direction: column; gap: 8px; padding: 12px 16px; border-radius: 8px; background: #16191D; color: #FFFFFF; font-size: 13px; line-height: 1.4; box-shadow: 0 6px 20px rgba(0,0,0,.25)">
      <div style="display: flex; align-items: flex-start; gap: 12px">
        <div style="flex: 1; font-weight: 600">${v.resumen.titulo}</div>
        <button type="button" onClick=${v.closeResumen} style="height: 26px; padding: 0 10px; border: 1px solid #5E6670; border-radius: 6px; background: transparent; color: #FFFFFF; font: inherit; cursor: pointer">Cerrar</button>
      </div>
      ${v.resumen.lineas.length ? html`<ul style="margin: 0; padding-left: 18px; overflow: auto">${v.resumen.lineas.map((l) => html`<li style="margin: 2px 0">${l}</li>`)}</ul>` : null}
    </div>` : null}
</div>`;
    }
  }

  // La raíz guarda la última vista que mandó C# (o el demo) y le pasa los avisos al tablero.
  class Raiz extends Component {
    constructor(props) {
      super(props);
      this.state = { vista: props.vista || null };
    }

    recibir(m) {
      if (!m || typeof m !== 'object') return;
      if (m.tipo === 'vista') this.setState({ vista: m.vista });
      else if (m.tipo === 'aviso' && this.tablero) this.tablero.aviso(m.texto);
      else if (m.tipo === 'previa' && this.tablero) this.tablero.previa(m.previa);
      else if (m.tipo === 'arreglos' && this.tablero) this.tablero.arreglosRecibidos(m.arreglos);
      else if (m.tipo === 'choques' && this.tablero) this.tablero.choquesAlGenerar(m.porticos);
      // #85: el resumen de la generación; la primera línea es el título y las demás, una por falla o diferencia.
      else if (m.tipo === 'generado' && this.tablero) this.tablero.setState({ resumen: m.texto.split('\n') });
    }

    render() {
      return html`<${Tablero} ref=${(t) => { this.tablero = t; }} vista=${this.state.vista} enviar=${this.props.enviar} inicial=${this.props.inicial} />`;
    }
  }

  function montar(enviar, inicial) {
    let raiz = null;
    render(html`<${Raiz} ref=${(r) => { raiz = r; }} enviar=${enviar} inicial=${inicial} />`, document.getElementById('tablero'));
    return (m) => raiz && raiz.recibir(m);
  }

  const webview = window.chrome && window.chrome.webview;
  if (webview) {
    const recibir = montar((m) => webview.postMessage(m));
    webview.addEventListener('message', (e) => recibir(e.data));
    webview.postMessage({ tipo: 'listo' });
  } else {
    // Fuera de Revit: los datos de demostración con los números de la maqueta (decisión 8), ?estado=…
    const estado = new URLSearchParams(location.search).get('estado') || 'normal';
    const s = document.createElement('script');
    s.src = 'demo.js';
    s.onload = () => {
      const demo = window.DEMO_TABLERO(estado);
      const recibir = montar((m) => demo.manejar(m).forEach((r) => setTimeout(() => recibir(r), 0)), demo.inicial);
      recibir({ tipo: 'vista', vista: demo.vista });
    };
    document.head.appendChild(s);
  }

  window.Tablero = Tablero;
  window.opcionesDe = opcionesDe;
})();
