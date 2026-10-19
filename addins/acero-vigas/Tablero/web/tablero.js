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
  // "Esto llega con la #NN." (decisión 7): los botones de otras issues se ven como en la maqueta, sin su acción.
  const LLEGA = (n) => 'Esto llega con la #' + n + '.';

  // Lo que el usuario elige para un pórtico: el `state` de la maqueta que cambia el cálculo (OpcionesTablero).
  const OPCIONES0 = {
    tipo: undefined, side: undefined, sup: [{ n: 3, d: '#5' }], inf: [{ n: 3, d: '#5' }],
    rec: '4', recUp: '', recDown: '', recSide: '', sepMode: 'pegada', sepCm: '1', maxCapas: '3',
    bastDef: '2.00', bastHook: false, bastLen: {}, bastSupN: '1', bastSupD: '', bastInfN: '1', bastInfD: '',
    est: { estD: '#3', estG: '135°', conf: '0.80', s1: '0.10', s2: '0.20' }, estMarc: null, tramosSel: [],
    // corte 1-1 (#91): cutX vacío = la vista tipo que elige C#
    cutX: '', depth: '0.10', mira: 'derecha',
    // edición (#84): CambiosManuales en mm (barras y traslapos ya; movidos y ganchos, fases B y C) y bastones arrastrados
    cambios: { barras: {}, traslapos: {}, movidos: {}, ganchos: {} }, bastPuntas: {},
  };
  // Ancho de la ventana del corte que sale al arrastrar la línea 1-1 (decisión 10 del plan docs/planes/2026-10-17-corte-vigas-91.md).
  const ANCHO_CORTE = 260;
  const CLAVES_OPCIONES = Object.keys(OPCIONES0);

  // Las opciones de la página con la forma de OpcionesTablero (tarea 2); null si algún campo no es número: así no se
  // manda y el último cálculo válido sigue en pantalla, como el num(x, d) de la maqueta.
  function opcionesDe(s) {
    const n = (x) => { const v = num(x, NaN); if (!Number.isFinite(v)) throw new Error(x); return v; };
    const opcional = (x) => (String(x).trim() === '' ? null : n(x));
    // la fila a mano de cada tipo (#84): '' = Auto
    const grupos = (rows) => rows.map((r) => ({ cantidad: r.n, diametro: diametro(r.d), fila: r.f ? Number(r.f) : null }));
    const estribos = (e) => ({ diametro: diametro(e.estD), gancho: e.estG === '135°' ? 'De135' : 'De90',
      largoZona: mm(n(e.conf)), separacionZona: mm(n(e.s1)), separacionCentro: mm(n(e.s2)) });
    const baston = (cant, d, filas) => ({ cantidad: Math.max(0, Math.round(n(cant))), diametro: diametro(d || filas[0].d) });
    try {
      const largos = {};
      Object.keys(s.bastLen).forEach((id) => { largos[id] = mm(n(s.bastLen[id])); });
      return {
        tipo: s.tipo ? (s.tipo === 'Cimentación' ? 'Cimentacion' : 'Aerea') : null,
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

  // La sección del corte 1-1 (#91), con el factor de `sec` (ver seccionA en valores): la usan la pestaña y la ventana.
  const dibujoSeccion = (sec) => html`
    <div style="position: relative; width: ${sec.wpx}; height: ${sec.hpx}; flex-shrink: 0; background: #F1F3F5; border: 1px solid #9AA3AD; box-sizing: border-box">
      <div style="position: absolute; left: ${sec.stL}; top: ${sec.stT}; right: ${sec.stR}; bottom: ${sec.stB}; border: 2px solid #8A939D; border-radius: 4px"></div>
      ${sec.adds.map((sh) => html`
        <div style="position: absolute; left: ${sh.l}; top: ${sh.t}; width: ${sh.w}; height: ${sh.h}; border: ${sh.bd}; border-radius: ${sh.r}; background: ${sh.bg}; box-sizing: border-box; pointer-events: none"></div>`)}
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

  class Tablero extends Component {
    constructor(props) {
      super(props);
      this.state = Object.assign({ tab: 'alzado', toast: '', q: '', ver: 'todo', listOpen: false, more: false, rulesWarn: false, rulesSaved: null,
        cutLive: null, cutWin: false, cutWinPos: null, draft: '', locked: true, rules: null, mult: null, gap: null, bar12: null, guardando: false,
        vw: window.innerWidth, vh: window.innerHeight },
      OPCIONES0, props.inicial || {});
      this.opcionesPorPortico = {};
      this.alCambiarTamano = () => this.setState({ vw: window.innerWidth, vh: window.innerHeight });
    }

    componentDidMount() {
      window.addEventListener('resize', this.alCambiarTamano);
    }

    componentWillUnmount() {
      window.removeEventListener('resize', this.alCambiarTamano);
    }

    // La página mide como mínimo 1440 × 900, como la maqueta (más chica, C# la achica con el zoom). Si la ventana es
    // más grande, la llena: la columna del centro crece y el alzado y el corte toman el espacio de más (Juan, 16-oct:
    // "no se usa el espacio bien").
    medidas() {
      const W = Math.max(1440, this.state.vw), H = Math.max(900, this.state.vh);
      return { pageW: W + 'px', pageH: H + 'px', dibujoW: 788 + W - 1440, dibujoH: 300 + H - 900 };
    }

    // Lo elegido en el alzado o la lista; sin elegir, el traslapo del aviso largo o S1.
    seleccion() {
      const s = this.state, P = this.props.vista && this.props.vista.actual;
      if (s.sel) return s.sel;
      const largos = P ? P.avisos.filter((a) => a.tipo === 'TraslapoLargo') : [];
      const avisoLargo = largos.find((a) => a.cambiarCara) || largos[0] || null;
      return avisoLargo ? { k: 'lap', id: avisoLargo.elemento } : { k: 'bar', id: 'S1' };
    }

    // Pide a C# la vista previa de lo elegido (#84): Subir / Bajar, o el largo escrito del traslapo o del bastón. La
    // respuesta llega a previa(); las que traen otro número son viejas y se descartan.
    pedirPrevia() {
      const v = this.props.vista, opciones = opcionesDe(this.state);
      if (!v || !v.actual || !opciones) return;
      const sel = this.seleccion();
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
      if (!v || !v.actual || !opciones || sel.k === 'lap') return;
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
      // Cada pórtico guarda sus opciones, como SesionTablero en C#: al cambiar de pórtico se recuperan las suyas.
      if (antes !== ahora) {
        const guardadas = ahora ? this.opcionesPorPortico[ahora] : null;
        if (antes) {
          this.opcionesPorPortico[antes] = CLAVES_OPCIONES.reduce((o, k) => ({ ...o, [k]: this.state[k] }), {});
          this.setState({ ...OPCIONES0, ...guardadas });
        } else if (guardadas) {
          this.setState(guardadas);
        }
        // Si el pórtico se abrió desde Revit (En vivo o "Seleccionar en Revit"), la lista baja hasta él (Juan, 16-oct).
        const tarjeta = ahora && document.querySelector('[data-portico="' + CSS.escape(ahora) + '"]');
        if (tarjeta) tarjeta.scrollIntoView({ block: 'nearest' });
      }
      // Al soltar la línea 1-1, se sigue viendo donde quedó hasta que llega el cálculo de C#.
      if (prev.vista !== this.props.vista && !this.arrastrandoCorte && this.state.cutLive != null) this.setState({ cutLive: null });
      if (this.state.guardando && prev.vista !== this.props.vista) {
        this.say('Reglas guardadas para todos los proyectos');
        this.setState({ guardando: false, locked: true, rulesWarn: false, rulesSaved: null, rules: null, mult: null, gap: null, bar12: null,
          ...(this.salirDeReglas ? { step: 'barras' } : null) });
        this.salirDeReglas = false;
      }
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
      this.props.enviar({ tipo: 'guardarReglas', reglas: { porDiametro: reglas.rules.map((r) => ({ d: r.d, t: r.t, g: r.g, e: r.e, k: r.k })),
        multiplo: reglas.mult, distanciaLibreCm: reglas.gap, varilla12: reglas.bar12, problema: null } });
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
      const mult = s.mult ?? R.multiplo, gap = s.gap ?? R.distanciaLibreCm, bar12 = s.bar12 ?? R.varilla12;
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

      let sb = { id: '', where: '', qty: '', len: '', tag: '', tagCol: '#1E7A46', downVal: '', upVal: '', downOff: true, downWhy: '', downCol: '#9AA3AD',
        upOff: true, upWhy: '', upCol: '#9AA3AD', notManual: true, resetCol: '#9AA3AD', note: '', down: () => {}, up: () => {}, reset: () => {} };
      if (SB) {
        const sbLen = SB.largo;
        const subir = opcion('subir'), bajar = opcion('bajar');
        const aplicar = (o) => () => {
          if (!o || !o.sePuede) return;
          this.cambiar({ cambios: conCambio('barras', SB.id, mm(o.valor)) });
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
          reset: () => this.cambiar({ cambios: conCambio('barras', SB.id, null) }),
        };
      }

      let sl = { id: '', min: '', diam: '', cur: '', eff: '', effCol: '#5E6670', off: true, applyBg: '#9AA3AD', notManual: true, resetCol: '#9AA3AD', apply: () => {}, reset: () => {} };
      if (SL) {
        const o = opcion('aplicar');
        const ok = !!o && o.sePuede;
        // en rojo solo si lo escrito es otro largo y no se puede; con el largo actual es solo la indicación
        const otro = Math.abs(num(s.draft, lapLen(SL)) - lapLen(SL)) > 1e-9;
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
      const cerrado = secc.figuras.find((f) => f.id === 'E');
      const recCm = num(s.rec, 4);
      const seccionA = (K) => ({
        wpx: secc.anchoCm * K + 'px', hpx: secc.altoCm * K + 'px',
        dots: secBars.map((b) => ({ x: (b.x - b.d / 2) * K + 'px', y: (b.y - b.d / 2) * K + 'px', s: b.d * K + 'px',
          c: b.color || '#3F4A57', ring: 'none', cur: 'default', label: (b.baston ? '' : 'Barra ') + b.nombre, pick: () => {} })),
        // lo que se ve detrás, hasta la profundidad: círculos vacíos con borde punteado
        proy: (C ? C.proyeccion : []).map((b) => ({ x: (b.x - b.d / 2) * K + 'px', y: (b.y - b.d / 2) * K + 'px', s: b.d * K + 'px', label: b.nombre })),
        adds: secc.figuras.filter((f) => f.id !== 'E').map((f) => (f.tipo === 'Cerrado'
          ? { l: f.izquierda * K + 'px', t: f.arriba * K + 'px', w: (f.derecha - f.izquierda) * K + 'px', h: (f.abajo - f.arriba) * K + 'px', bd: '2px solid #0F7B6C', r: '3px', bg: 'transparent' }
          : { l: (f.izquierda * K - 1) + 'px', t: f.arriba * K + 'px', w: '2px', h: (f.abajo - f.arriba) * K + 'px', bd: 'none', r: '0', bg: '#0F7B6C' })),
        stL: (cerrado ? cerrado.izquierda : recCm) * K + 'px', stT: (cerrado ? cerrado.arriba : recCm) * K + 'px',
        stR: (cerrado ? secc.anchoCm - cerrado.derecha : recCm) * K + 'px', stB: (cerrado ? secc.altoCm - cerrado.abajo : recCm) * K + 'px' });
      const sec = seccionA(Math.min(5, 270 / secc.altoCm, 340 / secc.anchoCm));
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
          pick: () => { this.setState({ ver: 'todo', sel: undefined }); enviar({ tipo: 'elegirPortico', id: p.id }); },
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
      const rulesNow = { rules: rulesData, mult, gap, bar12 };
      const rulesDirty = !s.locked && !!s.rulesSaved && JSON.stringify(rulesNow) !== JSON.stringify(s.rulesSaved);
      const leaveRules = (extra) => this.setState({ step: 'barras', locked: true, rulesWarn: false, rulesSaved: null, rules: null, mult: null, gap: null, bar12: null, ...extra });
      const bastD = (d, rows) => d || rows[0].d;
      const nEstAll = P ? P.estribos : 0;
      const avisoTexto = (avisoLargo || avisoOtro || { texto: '' }).texto;
      const corte = avisoTexto.indexOf('. ');
      return {
        pors, porCount: elegidos ? enLista.length + ' de ' + PORS.length : PORS.length + ' en el modelo', filtrado: !!elegidos,
        verTodos: () => enviar({ tipo: 'verTodos' }), q: s.q, proyecto: vista.proyecto, por: por ? { name: por.nombre, sub: por.sub } : { name: '', sub: '' }, hasPor: !!P, noPor: !P,
        tipo, isCim, isAviso, summary: (bars.length + BAST.length) + ' barras · ' + nEstAll + ' estribos · ' + lapsAll.length + ' traslapos · ' + (P ? P.kg : 0).toFixed(1) + ' kg',
        avisoNegrita: corte > 0 ? avisoTexto.slice(0, corte + 1) : avisoTexto, avisoResto: corte > 0 ? avisoTexto.slice(corte + 1) : '', avisoCambiar: !!avisoLargo && avisoLargo.cambiarCara,
        warnLap: avisoLargo ? avisoLargo.elemento : '',
        ver: s.ver, tramos: TRAMOS, rangeFrom: verT ? verT.desde : (P ? P.inicio : 0), rangeTo: verT ? verT.hasta : (P ? P.fin : 0),
        isAlzado: s.tab === 'alzado', isCorte: s.tab === 'corte', tabA: s.tab === 'alzado' ? on : off, tabC: s.tab === 'corte' ? on : off,
        side, faceQuestion: '¿Dónde traslapan las barras de ' + faceName + '?', izq: sideBtn('izq', P && P.izquierda), der: sideBtn('der', P && P.derecha),
        barSel: kind === 'lap' ? '' : selEl.id, lapSel: kind === 'lap' ? selEl.id : '', lapsManual: lapsAll.filter((l) => l.manual).map((l) => l.id).join(','),
        isSelBar: kind === 'bar' && !!SB, isSelLap: kind === 'lap' && !!SL, isSelBast: kind === 'bast' && !!SBa, sb, sl, st: stv, draft: s.draft,
        bastDef: s.bastDef, bastSupN: s.bastSupN, bastInfN: s.bastInfN, bastSupD: bastD(s.bastSupD, s.sup), bastInfD: bastD(s.bastInfD, s.inf),
        rulesWarn: s.rulesWarn,
        // abierta, la lista de barras toma todo el alto que sobra en la columna (Juan, 19-oct: "que la tabla llegue al límite")
        listOpen: s.listOpen, listLabel: s.listOpen ? 'Ocultar' : 'Ver lista', rows: rowsList.concat(estRows),
        estAddList: adicionales.map((ea, i) => ({ name: 'Adicional ' + (i + 1), desc: ea.tipo.replace('Adicional ', '').replace(/^./, (c) => c.toUpperCase()) + ' · ' + ea.detalle,
          removeLabel: 'Quitar adicional ' + (i + 1), remove: () => this.say(LLEGA(91)) })),
        isPicking: false, noPick: true, pickMsg: '', pickNotDone: true,
        confTxt: est.conf + ' m', s1Txt: 'cada ' + est.s1, s2Txt: 'cada ' + est.s2, bastTxt: s.bastDef + ' m',
        secAddText: adicionales.length ? ' · ' + adicionales.length + (adicionales.length === 1 ? ' adicional' : ' adicionales') : '',
        startCerrado: () => this.say(LLEGA(91)), startAbierto: () => this.say(LLEGA(91)), cancelPick: () => {}, addPick: () => {},
        corte: C, cutLive: s.cutLive, cutX: s.cutX !== '' ? s.cutX : (C ? f2(C.x) : ''), cutPos: C ? f2(s.cutLive ?? C.x) : '',
        cutAjustado: !!C && C.ajustado, cutBd: C && C.ajustado ? '#B26B00' : '#C9CFD6', cutTipo: !!C && C.esTipo,
        cutTramo: C ? C.tramo : '', cutAviso: C ? C.aviso : null,
        cutMira: C ? C.mirando : s.mira, cutProf: C ? f2(C.profundidad) : s.depth, depth: s.depth, sec, secMini,
        cutWin: s.cutWin && !!C, cutWinPos: s.cutWinPos, hideCutWin: () => this.setState({ cutWin: false }),
        moveCutWin: (pos) => this.setState({ cutWinPos: pos }),
        segMiraIzq: s.mira === 'izquierda' ? on : off, segMiraDer: s.mira === 'izquierda' ? off : on,
        secTop: qtyOf(s.sup) + (supI.filas > 1 ? ' en ' + supI.filas + ' filas' : ''), secBot: qtyOf(s.inf) + (infI.filas > 1 ? ' en ' + infI.filas + ' filas' : ''),
        estD: est.estD, estG: est.estG, conf: est.conf, s1: est.s1, s2: est.s2, secEstD: s.est.estD,
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
        tipoHint: !isCim ? 'Arriba traslapa al centro de la luz; abajo, junto a la columna.' : 'Abajo traslapa al centro de la luz; arriba, junto a la columna.',
        segHookNo: s.bastHook ? off : on, segHookYes: s.bastHook ? on : off,
        locked: lockedNow, mult, gap, seg6: bar12 ? off : on, seg12: bar12 ? on : off,
        lockBg: lockedNow ? '#F4F6F8' : '#FDF6EA', lockText: lockedNow ? 'Bloqueadas para evitar cambios por error.' : 'Desbloqueadas: los cambios valen para todos los proyectos.',
        lockBtn: lockedNow ? 'Desbloquear' : 'Guardar y bloquear', lockPath: lockedNow ? 'M4.5 6V4.2a2.5 2.5 0 015 0V6' : 'M4.5 6V4.2a2.5 2.5 0 014.6-1.3',
        rules: rulesData.map((r, i) => {
          const set = (key) => (e) => this.setState({ rules: rulesData.map((x, j) => (j === i ? { ...x, [key]: e.target.value } : x)) });
          return { d: r.d, t: r.t, g: r.g, e: r.e, k: r.k, labT: 'Traslapo ' + r.d, labG: 'Gancho 90° ' + r.d, labE: 'Gancho de estribo 135° ' + r.d, labK: 'Peso por metro ' + r.d, setT: set('t'), setG: set('g'), setE: set('e'), setK: set('k') };
        }),
        liveDot: vista.enVivo ? '#1E7A46' : '#9AA3AD', liveText: vista.enVivo ? 'En vivo con Revit' : 'Sin enlace con Revit',
        genOff: !P || !P.sePuedeGenerar, genBg: P && P.sePuedeGenerar ? '#1F5FAF' : '#9AA3AD', hasToast: !!s.toast, toast: s.toast,
        pickEl: (id) => selectBar(id), pickLap: (id) => { const L = lapsAll.find((x) => x.id === id); if (L) selectLap(L); },
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
        useIzq: () => { this.setState({ sel: { k: 'bar', id: 'S1' } }); this.cambiar({ side: 'izq' }); },
        useDer: () => { this.setState({ sel: { k: 'bar', id: 'S1' } }); this.cambiar({ side: 'der' }); },
        toggleList: () => this.setState({ listOpen: !s.listOpen }), toggleMore: () => this.setState({ more: !s.more }),
        toggleRecDraw: () => this.setState({ recDraw: !s.recDraw }),
        setRec: (e) => this.cambiar({ rec: e.target.value }, true), setRecUp: (e) => this.cambiar({ recUp: e.target.value }, true),
        setRecDown: (e) => this.cambiar({ recDown: e.target.value }, true), setRecSide: (e) => this.cambiar({ recSide: e.target.value }, true),
        sepPegada: () => this.cambiar({ sepMode: 'pegada' }), sepSeparada: () => this.cambiar({ sepMode: 'separada' }),
        setSepCm: (e) => this.cambiar({ sepCm: e.target.value }, true), setMaxCapas: (e) => this.cambiar({ maxCapas: e.target.value }, true),
        setAerea: () => { this.setState({ sel: { k: 'bar', id: 'S1' } }); this.cambiar({ tipo: 'Aérea' }); },
        setCim: () => { this.setState({ sel: { k: 'bar', id: 'S1' } }); this.cambiar({ tipo: 'Cimentación' }); },
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
        toggleLive: () => enviar({ tipo: 'cambiarEnVivo', activo: !vista.enVivo }),
        pickInRevit: () => enviar({ tipo: 'seleccionarEnRevit' }),
        showInRevit: () => { if (P) enviar({ tipo: 'verEnRevit', id: P.id }); },
        open3d: () => { if (P) enviar({ tipo: 'abrir3d', id: P.id }); },
        generate: () => this.say(LLEGA(85)),
      };
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

    <div style="width: 260px; flex-shrink: 0; display: flex; flex-direction: column; gap: 10px; padding: 14px; background: #FFFFFF; border-right: 1px solid #D9DDE2; box-sizing: border-box">
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
                <span style="flex-shrink: 0; font-size: 11px; padding: 1px 7px; border-radius: 10px; background: #EEF0F2; color: #3F4A57">${p.tipo}</span>
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
      </div>
      <p style="margin: 0; font-size: 12px; color: #5E6670; line-height: 1.45">Un pórtico es la viga continua de un eje. Marca tramos para darles estribos distintos.</p>
    </div>

    ${v.hasPor ? html`
      <div style="flex: 1; min-width: 0; min-height: 0; display: flex; flex-direction: column; gap: 12px; padding: 16px">
        <div style="display: flex; align-items: center; gap: 12px; white-space: nowrap">
          <div style="flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 2px">
            <span style="font-size: 18px; font-weight: 600">${v.por.name}</span>
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
            <${Alzado} portico=${P} w=${m.dibujoW} h=${m.dibujoH} sel=${v.barSel} selLap=${v.lapSel} lapsManual=${v.lapsManual} invert=${v.isCim} warn=${v.isAviso} warnLap=${v.warnLap} showStirrups=${true} corte=${v.corte} cutLive=${v.cutLive} onCut=${v.onCut} rangeFrom=${v.rangeFrom} rangeTo=${v.rangeTo} onPick=${v.pickEl} onPickLap=${v.pickLap} onHook=${v.onHook} arrastre=${v.arrastre} onDrop=${v.onDrop} />
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
              <span>Estribo cerrado ${v.secEstD} en las esquinas${v.secAddText}</span>
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
          <span style="font-size: 16px; font-weight: 600">Elige un pórtico para empezar</span>
          <span style="color: #5E6670; line-height: 1.5">Haz clic en un pórtico de la lista o selecciona una viga en el modelo. Aquí verás sus barras, traslapos, bastones y estribos.</span>
          <button type="button" onClick=${v.pickInRevit} style="height: 36px; padding: 0 16px; border: 1px solid #1F5FAF; border-radius: 6px; background: #1F5FAF; color: #FFFFFF; font: inherit; font-weight: 600; cursor: pointer">Seleccionar en Revit</button>
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
              <span style="font-weight: 600">Tipo de viga</span>
              <div style="display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); border: 1px solid #C9CFD6; border-radius: 6px; overflow: hidden">
                <button type="button" onClick=${v.setAerea} style="height: 32px; border: 0; font: inherit; cursor: pointer; ${v.segAerea}">Aérea</button>
                <button type="button" onClick=${v.setCim} style="height: 32px; border: 0; border-left: 1px solid #C9CFD6; font: inherit; cursor: pointer; ${v.segCim}">Cimentación</button>
              </div>
              <span style="font-size: 12px; color: #5E6670">Detectado por el nivel. ${v.tipoHint}</span>
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
                      <option value="#3">#3 · 3/8"</option><option value="#4">#4 · 1/2"</option><option value="#5">#5 · 5/8"</option><option value="#6">#6 · 3/4"</option><option value="#7">#7 · 7/8"</option><option value="#8">#8 · 1"</option>
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
          <div style="display: flex; flex-direction: column; gap: 16px">
            <span style="color: #3F4A57; line-height: 1.5">Barras sueltas de refuerzo. <b>Arriba</b> van centradas en cada columna, también en las de borde; <b>abajo</b>, en el centro de cada luz.</span>
            <svg width="308" height="104" viewBox="0 0 308 104" role="img" aria-label="Bastones arriba sobre las columnas y abajo en el centro de cada luz">
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
                  <option value="#3">#3</option><option value="#4">#4</option><option value="#5">#5</option><option value="#6">#6</option><option value="#7">#7</option><option value="#8">#8</option>
                </select>
              </div>
              <div style="display: flex; align-items: center; gap: 8px">
                <label for="bast-inf-n" style="flex: 1; color: #3F4A57">Abajo</label>
                <input id="bast-inf-n" type="text" inputmode="numeric" value=${v.bastInfN} onInput=${v.setBastInfN} style="width: 48px; height: 32px; padding: 0 8px; border: 1px solid #C9CFD6; border-radius: 6px; font-family: 'IBM Plex Mono', monospace; font-size: 13px; color: #16191D" />
                <select aria-label="Diámetro de los bastones de abajo" value=${v.bastInfD} onChange=${v.setBastInfD} style="height: 32px; border: 1px solid #C9CFD6; border-radius: 6px; font: inherit; background: #FFFFFF; color: #16191D; padding: 0 6px">
                  <option value="#3">#3</option><option value="#4">#4</option><option value="#5">#5</option><option value="#6">#6</option><option value="#7">#7</option><option value="#8">#8</option>
                </select>
              </div>
              <span style="font-size: 12px; color: #5E6670; line-height: 1.5">Barras por bastón. Por defecto, 1 del diámetro de las esquinas; con 0, esa cara no lleva bastones.</span>
            </div>
            <div style="display: flex; align-items: center; gap: 8px">
              <span style="font-weight: 600; flex: 1">Gancho</span>
              <div style="display: flex; border: 1px solid #C9CFD6; border-radius: 6px; overflow: hidden">
                <button type="button" onClick=${v.hookNo} style="height: 30px; padding: 0 14px; border: 0; font: inherit; cursor: pointer; ${v.segHookNo}">Sin gancho</button>
                <button type="button" onClick=${v.hookYes} style="height: 30px; padding: 0 14px; border: 0; border-left: 1px solid #C9CFD6; font: inherit; cursor: pointer; ${v.segHookYes}">Con gancho</button>
              </div>
            </div>
            <span style="font-size: 12px; color: #5E6670; line-height: 1.5; margin-top: -8px">El gancho solo va en la punta que llega al borde de la viga o del voladizo.</span>
            <span style="font-size: 12px; color: #5E6670; line-height: 1.5">Su largo no se redondea y queda mitad a cada lado de su eje; si llega al borde de la viga, se corta en la punta. Haz clic en un bastón del dibujo para cambiarle el largo solo a él. Van en el primer puesto libre de la fila, empezando por la esquina.</span>
          </div>` : null}

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
              <span style="font-weight: 600; flex: 1">Estribo</span>
              <select aria-label="Diámetro del estribo" value=${v.estD} onChange=${v.setEstD} style="height: 32px; border: 1px solid #C9CFD6; border-radius: 6px; font: inherit; background: #FFFFFF; color: #16191D; padding: 0 6px">
                <option value="#3">#3 · 3/8"</option><option value="#4">#4 · 1/2"</option>
              </select>
              <select aria-label="Gancho del estribo" value=${v.estG} onChange=${v.setEstG} style="height: 32px; border: 1px solid #C9CFD6; border-radius: 6px; font: inherit; background: #FFFFFF; color: #16191D; padding: 0 6px">
                <option value="135°">135°</option><option value="90°">90°</option>
              </select>
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
                  </div>
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
              <span style="font-size: 12px; color: #5E6670; line-height: 1.5">Se eligen en el corte 1-1 con un clic en cada barra. El cerrado necesita 2 barras arriba y 2 abajo; el abierto, 1 y 1. En cada vertical manda la barra que se elige primero; si una vecina queda muy cerca, también se corre. Las esquinas no se mueven. Las barras que se mueven para alinearse lo hacen en todo el pórtico; el adicional va donde se aplica este paso.</span>
            </div>
            <span style="font-size: 12px; color: #5E6670; line-height: 1.5">El estribo cerrado que abraza las barras de las esquinas se pone solo. El primero va a 5 cm de la cara de la columna. Si una luz es más corta que las dos zonas juntas, toda la luz lleva la separación de columnas. El voladizo lleva la zona junto a su columna y el resto con la separación del centro.</span>
          </div>` : null}

        ${v.isCorteStep ? html`
          <div style="display: flex; flex-direction: column; gap: 14px">
            <span style="color: #3F4A57; line-height: 1.5">El corte 1-1 muestra la sección de la viga en ese punto. Por defecto se ubica donde la sección es la más común. Arrastra la línea 1-1 en el alzado para moverlo de estribo en estribo, sin pasar por traslapos ni apoyos: el corte se ve en una ventana que puedes ocultar.</span>
            <div style="display: flex; align-items: center; gap: 8px">
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
              <span style="font-weight: 600; font-size: 15px">Reglas generales</span>
              <span style="font-size: 12px; color: #5E6670">Son la base para todos los proyectos.</span>
            </div>
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
            <span style="font-size: 12px; color: #5E6670; margin-top: -8px">Si un tramo no se puede resolver con 6 m, se usa una de 12 m.</span>
            <div style="display: flex; flex-direction: column; gap: 6px">
              <div style="display: grid; grid-template-columns: 30px repeat(4, minmax(0, 1fr)); gap: 5px; font-size: 11px; color: #5E6670">
                <span></span><span>Traslapo</span><span>Gancho 90°</span><span>Estribo 135°</span><span>kg/m</span>
              </div>
              ${v.rules.map((ru) => html`
                <div style="display: grid; grid-template-columns: 30px repeat(4, minmax(0, 1fr)); gap: 5px; align-items: center">
                  <span style="color: #3F4A57">${ru.d}</span>
                  <input type="text" aria-label=${ru.labT} value=${ru.t} onInput=${ru.setT} disabled=${v.locked} placeholder="—" style="width: 100%; box-sizing: border-box; height: 28px; padding: 0 5px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; font-size: 12px; color: #16191D" />
                  <input type="text" aria-label=${ru.labG} value=${ru.g} onInput=${ru.setG} disabled=${v.locked} placeholder="—" style="width: 100%; box-sizing: border-box; height: 28px; padding: 0 5px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; font-size: 12px; color: #16191D" />
                  <input type="text" aria-label=${ru.labE} value=${ru.e} onInput=${ru.setE} disabled=${v.locked} placeholder="—" style="width: 100%; box-sizing: border-box; height: 28px; padding: 0 5px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; font-size: 12px; color: #16191D" />
                  <input type="text" aria-label=${ru.labK} value=${ru.k} onInput=${ru.setK} disabled=${v.locked} style="width: 100%; box-sizing: border-box; height: 28px; padding: 0 6px; border: 1px solid #C9CFD6; border-radius: 5px; font-family: 'IBM Plex Mono', monospace; font-size: 12px; color: #16191D" />
                </div>`)}
              <span style="font-size: 12px; color: #5E6670; line-height: 1.45">Valores en metros, de los cuadros de ganchos y traslapos. El traslapo depende solo del diámetro. Las barras terminan con gancho de 90°; el estribo de 135° sale del cuadro de dimensiones mínimas.</span>
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
  ${v.hasToast ? html`
    <div role="status" style="position: absolute; left: 50%; bottom: 20px; transform: translateX(-50%); padding: 10px 16px; border-radius: 8px; background: #16191D; color: #FFFFFF; font-size: 13px; white-space: nowrap">${v.toast}</div>` : null}
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
