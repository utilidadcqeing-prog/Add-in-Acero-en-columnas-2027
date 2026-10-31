// Los tres pasos del despiece (#220 y #221). Las imágenes son vistas reales de Revit, que manda PuenteDespiece.
(function () {
  const { html, render, Component } = window.htmPreact;
  const enviar = (m) => window.chrome?.webview?.postMessage(m);

  const VERDE = '#1E8E3E', AZUL = '#1F5FAF';
  // Los límites del zoom: 1 es la imagen entera en su recuadro.
  const ZOOM_MIN = 0.35, ZOOM_MAX = 16;
  const TIPOS = [
    { n: 'Vigas', k: 'viga', icon: 'M3 14h30v8H3zM8 14v8M28 14v8' },
    { n: 'Columnas · pronto', off: true, icon: 'M13 3h10v30H13zM13 12h10M13 24h10' },
    { n: 'Viguetas · pronto', off: true, icon: 'M3 12h30M3 18h30M3 24h30' },
    { n: 'Zapatas · pronto', off: true, icon: 'M6 22h24v9H6zM14 22V6h8v16' }
  ];
  const TEXTOS = {
    1: ['¿Qué vas a despiezar?', 'Muévete entre plantas y haz clic en una viga. Las que llevan el mismo acero se eligen solas, también en otras plantas.'],
    2: ['Revisa el despiece', 'Arrastra las barras y los textos para acomodarlos. Todavía no se creó nada en el proyecto.'],
    3: ['Vista previa', 'Las vistas quedan en el navegador de Revit, con etiquetas y colores. Tú las arrastras a tu plano.']
  };

  /**
   * Una imagen de Revit que se acerca con la rueda y se mueve arrastrando. El zoom y el desplazamiento viven aquí y se
   * aplican directo al dibujo, sin volver a pintar la página: así siguen el dedo sin retraso (Juan, 30-oct: "que sea muy
   * fluido"). <c>movio</c> dice si lo último fue un arrastre, para no tomarlo por un clic en lo que hay dentro.
   */
  class Visor extends Component {
    constructor(props) {
      super(props);
      this.vista = { k: 1, tx: 0, ty: 0 };
      this.capa = null;
      this.lienzo = null;
      this.arrastrando = null;
      this.movio = false;
      this.alMoverPuntero = (e) => this.moverPuntero(e);
      this.alSoltarPuntero = () => this.soltarPuntero();
    }

    componentWillUnmount() {
      this.soltarPuntero();
    }

    aplicar() {
      const { k, tx, ty } = this.vista;
      this.capa?.setAttribute('transform', `translate(${tx} ${ty}) scale(${k})`);
    }

    reiniciar() {
      this.vista = { k: 1, tx: 0, ty: 0 };
      this.aplicar();
    }

    /** De un punto de la pantalla a coordenadas del dibujo (píxeles de la imagen). */
    aDibujo(x, y) {
      const m = this.lienzo?.getScreenCTM();
      if (!m) return null;
      const p = this.lienzo.createSVGPoint();
      p.x = x;
      p.y = y;
      return p.matrixTransform(m.inverse());
    }

    /**
     * La imagen se mueve libre en cualquier sentido y a cualquier zoom (Juan, 30-oct: "no me deja arrastrar hacia
     * arriba"); solo no se deja perder del todo: siempre queda a la vista.
     */
    limitar(v) {
      const { ancho, alto } = this.props;
      const k = Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, v.k));
      const rango = (largo, t) => Math.min(Math.max(t, Math.min(0, largo - k * largo) - largo * 0.5), Math.max(0, largo - k * largo) + largo * 0.5);
      return { k, tx: rango(ancho, v.tx), ty: rango(alto, v.ty) };
    }

    acercar(factor, x, y) {
      const c = this.aDibujo(x, y);
      if (!c) return;
      const v = this.vista;
      const k = Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, v.k * factor));
      const r = k / v.k;
      this.vista = this.limitar({ k, tx: c.x - (c.x - v.tx) * r, ty: c.y - (c.y - v.ty) * r });
      this.aplicar();
    }

    /** Los botones acercan hacia el centro de lo que se ve. */
    acercarAlCentro(factor) {
      const caja = this.lienzo?.getBoundingClientRect();
      if (caja) this.acercar(factor, caja.left + caja.width / 2, caja.top + caja.height / 2);
    }

    alRueda(e) {
      e.preventDefault();
      // Con un touchpad, pellizcar manda la rueda con Ctrl; arrastrar con dos dedos, también la rueda.
      this.acercar(Math.exp(-e.deltaY * (e.ctrlKey ? 0.01 : 0.0015)), e.clientX, e.clientY);
    }

    alPulsar(e) {
      if (e.button !== 0) return;
      this.arrastrando = { x: e.clientX, y: e.clientY };
      this.movio = false;
      window.addEventListener('pointermove', this.alMoverPuntero);
      window.addEventListener('pointerup', this.alSoltarPuntero);
    }

    moverPuntero(e) {
      const a = this.arrastrando;
      if (!a) return;
      const dx = e.clientX - a.x, dy = e.clientY - a.y;
      if (!this.movio && Math.hypot(dx, dy) < 4) return;
      this.movio = true;
      const m = this.lienzo.getScreenCTM();
      this.vista = this.limitar({ ...this.vista, tx: this.vista.tx + dx / m.a, ty: this.vista.ty + dy / m.d });
      this.arrastrando = { x: e.clientX, y: e.clientY };
      this.aplicar();
    }

    soltarPuntero() {
      this.arrastrando = null;
      window.removeEventListener('pointermove', this.alMoverPuntero);
      window.removeEventListener('pointerup', this.alSoltarPuntero);
    }

    render({ ancho, alto, etiqueta, children }) {
      const estilo = 'width: 36px; height: 36px; border: 1px solid #C9CFD6; border-radius: 6px; background: #FFFFFF; color: #16191D; font: inherit; font-size: 18px; cursor: pointer';
      return html`
        <div style="position: relative; width: 100%; height: 100%">
          <svg ref=${(el) => (this.lienzo = el)} viewBox="0 0 ${ancho} ${alto}" style="width: 100%; height: 100%; display: block; touch-action: none; cursor: grab; user-select: none" role="img" aria-label=${etiqueta}
            onWheel=${(e) => this.alRueda(e)} onPointerDown=${(e) => this.alPulsar(e)} onDblClick=${() => this.reiniciar()}>
            <g ref=${(el) => { this.capa = el; this.aplicar(); }}>${children}</g>
          </svg>
          <div style="position: absolute; right: 12px; top: 12px; display: flex; flex-direction: column; gap: 6px">
            <button type="button" aria-label="Acercar" title="Acercar" onClick=${() => this.acercarAlCentro(1.5)} style=${estilo}>+</button>
            <button type="button" aria-label="Alejar" title="Alejar" onClick=${() => this.acercarAlCentro(1 / 1.5)} style=${estilo}>−</button>
            <button type="button" aria-label="Ajustar a la ventana" title="Ajustar a la ventana" onClick=${() => this.reiniciar()} style=${estilo + '; font-size: 12px'}>Todo</button>
          </div>
        </div>`;
    }
  }

  /** Un tubo por cada eje de barra, todos en una sola geometría: un objeto por barra giraría peor con cientos de estribos. */
  function tubos(barras) {
    const T = window.THREE, LADOS = 6;
    const posiciones = [], normales = [], indices = [];
    barras.forEach(({ r, p }) => {
      const puntos = [];
      for (let i = 0; i + 2 < p.length; i += 3) puntos.push(new T.Vector3(p[i], p[i + 1], p[i + 2]));
      if (puntos.length < 2) return;
      const inicio = posiciones.length / 3;
      let n = null;
      puntos.forEach((q, i) => {
        const t = puntos[Math.min(i + 1, puntos.length - 1)].clone().sub(puntos[Math.max(i - 1, 0)]).normalize();
        // La normal se arrastra de un punto al siguiente, para que el tubo no se retuerza en los dobleces.
        n = n ? n.sub(t.clone().multiplyScalar(n.dot(t))) : new T.Vector3(Math.abs(t.z) < 0.9 ? 0 : 1, 0, Math.abs(t.z) < 0.9 ? 1 : 0).cross(t);
        n.normalize();
        const b = t.clone().cross(n);
        for (let l = 0; l < LADOS; l++) {
          const a = (l / LADOS) * Math.PI * 2;
          const d = n.clone().multiplyScalar(Math.cos(a)).add(b.clone().multiplyScalar(Math.sin(a)));
          posiciones.push(q.x + d.x * r, q.y + d.y * r, q.z + d.z * r);
          normales.push(d.x, d.y, d.z);
        }
      });
      for (let i = 0; i < puntos.length - 1; i++) {
        for (let l = 0; l < LADOS; l++) {
          const a = inicio + i * LADOS + l, c = inicio + i * LADOS + (l + 1) % LADOS;
          indices.push(a, c, a + LADOS, c, c + LADOS, a + LADOS);
        }
      }
    });
    const g = new T.BufferGeometry();
    g.setAttribute('position', new T.Float32BufferAttribute(posiciones, 3));
    g.setAttribute('normal', new T.Float32BufferAttribute(normales, 3));
    g.setIndex(indices);
    return g;
  }

  /** El acero real de la viga, para girarlo dentro del add-in. Solo se dibuja cuando la cámara se mueve: en reposo no gasta nada. */
  class Vista3D extends Component {
    componentDidMount() {
      const T = window.THREE, { datos } = this.props, caja = this.caja;
      try {
        this.pintor = new T.WebGLRenderer({ antialias: true });
      } catch (e) {
        this.setState({ sinWebgl: true });
        return;
      }
      this.pintor.setPixelRatio(Math.min(window.devicePixelRatio, 2));
      this.pintor.setClearColor(0xffffff);
      caja.appendChild(this.pintor.domElement);

      const escena = new T.Scene();
      const grupo = new T.Group();
      const porColor = {};
      datos.barras.forEach((b) => (porColor[b.c] ??= []).push(b));
      Object.entries(porColor).forEach(([color, barras]) => grupo.add(new T.Mesh(tubos(barras), new T.MeshLambertMaterial({ color }))));
      const viga = new T.BufferGeometry();
      viga.setAttribute('position', new T.Float32BufferAttribute(datos.viga, 3));
      viga.computeVertexNormals();
      grupo.add(new T.Mesh(viga, new T.MeshLambertMaterial({ color: 0x9aa3ad, transparent: true, opacity: 0.22, depthWrite: false, side: T.DoubleSide })));
      escena.add(grupo);

      const camara = new T.PerspectiveCamera(35, 1, 0.05, 2000);
      camara.up.set(0, 0, 1);
      camara.add(new T.DirectionalLight(0xffffff, 0.7));
      escena.add(camara, new T.AmbientLight(0xffffff, 0.65));

      const limites = new T.Box3().setFromObject(grupo);
      const centro = limites.getCenter(new T.Vector3()), radio = limites.getSize(new T.Vector3()).length() / 2;
      camara.position.copy(centro).add(new T.Vector3(-0.55, -1, 0.45).normalize().multiplyScalar(radio * 2.3));
      camara.far = radio * 40;

      this.camara = camara;
      this.pintar = () => this.pintor.render(escena, camara);
      this.controles = new T.OrbitControls(camara, this.pintor.domElement);
      this.controles.target.copy(centro);
      this.controles.addEventListener('change', this.pintar);
      this.controles.update();

      const ajustar = () => {
        const w = caja.clientWidth, h = caja.clientHeight;
        if (!w || !h) return;
        this.pintor.setSize(w, h);
        camara.aspect = w / h;
        camara.updateProjectionMatrix();
        this.pintar();
      };
      this.observador = new ResizeObserver(ajustar);
      this.observador.observe(caja);
      ajustar();
    }

    componentWillUnmount() {
      this.observador?.disconnect();
      this.controles?.dispose();
      this.pintor?.dispose();
    }

    render(_, { sinWebgl }) {
      return html`
        <div ref=${(el) => (this.caja = el)} style="position: absolute; inset: 0; overflow: hidden">
          ${sinWebgl ? html`<div style="display: grid; place-items: center; height: 100%; color: #5E6670">Este equipo no puede dibujar el 3D.</div>` : null}
        </div>`;
    }
  }

  class Pasos extends Component {
    constructor(props) {
      super(props);
      this.state = { paso: 1, modelo: null, plantaId: null, plantas: {}, grupo: null, previas: {}, negros: false, movimientos: {}, modo3d: false, modelos3d: {}, hechos: {}, error: null, hecho: false, ...(props.inicial ?? {}) };
      this.alCambiarTamano = () => this.forceUpdate();
      this.alRecibir = (e) => this.recibir(e.data);
    }

    /** Un clic en una viga solo cuenta si no fue el final de un arrastre. */
    elegirViga(g) {
      if (this.visor?.movio) {
        this.visor.movio = false;
        return;
      }
      if (g) this.setState({ grupo: g.indice, hecho: false });
    }

    componentDidMount() {
      window.addEventListener('resize', this.alCambiarTamano);
      window.chrome?.webview?.addEventListener('message', this.alRecibir);
      enviar({ tipo: 'iniciar' });
    }

    componentWillUnmount() {
      window.removeEventListener('resize', this.alCambiarTamano);
      window.chrome?.webview?.removeEventListener('message', this.alRecibir);
    }

    recibir(m) {
      if (m.tipo === 'modelo' && m.refresco && this.state.modelo) {
        this.refrescar(m);
      } else if (m.tipo === 'modelo') {
        this.setState({ modelo: m, error: null });
        if (m.plantas.length > 0) this.verPlanta(m.plantas[0].id);
      } else if (m.tipo === 'planta') {
        this.setState((s) => ({ plantas: { ...s.plantas, [m.id]: m }, error: null }));
      } else if (m.tipo === 'previa') {
        this.setState((s) => ({ previas: { ...s.previas, [m.clave]: m }, error: null }));
      } else if (m.tipo === 'modelo3d') {
        this.setState((s) => ({ modelos3d: { ...s.modelos3d, [m.grupo]: m.modelo }, error: null }));
      } else if (m.tipo === 'vistas') {
        // En verde solo cuando Revit ya las tiene.
        this.setState((s) => ({ creando: false, hecho: m.yaExistian ? 'Esas vistas ya existen: no se duplicaron.' : `Vistas creadas: ${m.longitudinal} y ${m.seccion}. Arrástralas a tu plano.`, hechos: { ...s.hechos, [m.grupo]: true }, error: null }));
      } else if (m.tipo === 'error') {
        this.setState({ error: m.texto, creando: false });
      }
    }

    /**
     * El modelo cambió en Revit: se sigue en el mismo paso, planta, zoom y viga, y se piden primero las imágenes que se están
     * viendo. Los grupos pueden cambiar de número y de nombre, así que se buscan por sus vigas; si la elegida ya no existe,
     * vuelve al paso 1.
     */
    refrescar(m) {
      const { modelo, grupo, hechos, plantaId, paso } = this.state;
      const elegida = modelo.grupos[grupo]?.vigas[0];
      const nuevo = m.grupos.find((g) => g.vigas.includes(elegida)) ?? null;
      const hechosNuevos = {};
      m.grupos.forEach((g) => {
        const antes = modelo.grupos.find((a) => a.vigas.some((v) => g.vigas.includes(v)));
        if (antes && hechos[antes.indice]) hechosNuevos[g.indice] = true;
      });
      const sigue = m.plantas.some((p) => p.id === plantaId);
      const planta = sigue ? plantaId : m.plantas[0]?.id ?? null;
      const pasoNuevo = nuevo?.tieneAcero ? paso : 1;
      if (!sigue) this.visor?.reiniciar();
      this.setState({ modelo: m, grupo: nuevo?.indice ?? null, hechos: hechosNuevos, previas: {}, modelos3d: {}, plantaId: planta, paso: pasoNuevo, error: null });
      if (planta) enviar({ tipo: 'verPlanta', id: planta });
      if (pasoNuevo > 1) enviar(this.mensajePrevia(nuevo, this.state.negros, pasoNuevo));
      if (pasoNuevo > 1 && this.state.modo3d) enviar({ tipo: 'ver3d', grupo: nuevo.indice });
    }

    cambiar3d(g) {
      const modo3d = !this.state.modo3d;
      this.setState({ modo3d });
      if (modo3d && !this.state.modelos3d[g.indice]) enviar({ tipo: 'ver3d', grupo: g.indice });
    }

    vistaGrande(g) {
      const { modo3d, modelos3d } = this.state;
      const datos = modelos3d[g.indice];
      return html`
        ${!modo3d ? this.imagenes(g) : datos ? html`<${Vista3D} key=${g.indice} datos=${datos} />` : html`<div style="display: grid; place-items: center; height: 100%; color: #5E6670">Leyendo el acero en Revit…</div>`}
        <button type="button" onClick=${() => this.cambiar3d(g)} style="position: absolute; left: 12px; top: 12px; height: 36px; padding: 0 14px; border: 1px solid #C9CFD6; border-radius: 6px; background: #FFFFFF; color: #16191D; font: inherit; cursor: pointer">${modo3d ? 'Plano' : '3D'}</button>`;
    }

    verPlanta(id) {
      this.visor?.reiniciar();
      this.setState({ plantaId: id, error: null });
      if (!this.state.plantas[id]) enviar({ tipo: 'verPlanta', id });
    }

    irA(paso) {
      const g = this.grupoElegido();
      if (paso > 1 && !g?.tieneAcero) return;
      this.setState({ paso, hecho: false });
      if (paso > 1) this.pedirPrevia(g, this.state.negros, paso);
    }

    /** Lo movido en una viga, en metros: <c>{ id: { dz, tx, tz } }</c>. Va por viga: el número del grupo cambia con el modelo. */
    movimientos(g) {
      return this.state.movimientos[g.vigas[0]] ?? {};
    }

    /** El paso 2 trae lo movible aparte y no depende de lo movido; el 3 es la imagen real con todo aplicado. */
    mensajePrevia(g, negros, paso) {
      const separar = paso === 2;
      const movimientos = separar ? {} : this.movimientos(g);
      return { tipo: 'previa', grupo: g.indice, negros, separar, movimientos, clave: `${g.indice}|${negros}|${separar}|${JSON.stringify(movimientos)}` };
    }

    previa(g) {
      return this.state.previas[this.mensajePrevia(g, this.state.negros, this.state.paso).clave];
    }

    pedirPrevia(g, negros, paso) {
      const m = this.mensajePrevia(g, negros, paso);
      if (!this.state.previas[m.clave]) enviar(m);
    }

    /** Cada modo se genera una vez en Revit; volver a uno ya visto es inmediato. */
    elegirTextos(g, negros) {
      this.setState({ negros });
      this.pedirPrevia(g, negros, this.state.paso);
    }

    selectorTextos(g) {
      const movido = this.state.paso === 2 && Object.keys(this.movimientos(g)).length > 0;
      return html`
        <div style="display: flex; align-items: center; justify-content: space-between; gap: 8px; flex-shrink: 0; font-size: 12px; color: #5E6670">
          <label style="display: flex; align-items: center; gap: 6px; cursor: pointer">
            <input type="checkbox" checked=${this.state.negros} onChange=${(e) => this.elegirTextos(g, e.target.checked)} style="margin: 0; accent-color: ${AZUL}" />Textos en negro
          </label>
          ${movido ? html`<button type="button" onClick=${() => this.setState((s) => ({ movimientos: { ...s.movimientos, [g.vigas[0]]: {} } }))} style="padding: 0; border: 0; background: transparent; color: ${AZUL}; font: inherit; cursor: pointer">Volver a la posición automática</button>` : null}
        </div>`;
    }

    /** Arrastra una barra (solo arriba y abajo, con su texto y su gancho) o un texto (libre). El zoom del visor ya viene en la matriz. */
    arrastrar(e, g, id, esBarra, escala) {
      if (e.button !== 0) return;
      e.stopPropagation();
      const k = e.currentTarget.getScreenCTM().a;
      const clave = g.vigas[0];
      let x = e.clientX, y = e.clientY;
      const mover = (ev) => {
        const dx = (ev.clientX - x) / k / escala, dz = -(ev.clientY - y) / k / escala;
        x = ev.clientX;
        y = ev.clientY;
        this.setState((s) => {
          const deViga = s.movimientos[clave] ?? {};
          const m = deViga[id] ?? { dz: 0, tx: 0, tz: 0 };
          const nuevo = esBarra ? { ...m, dz: m.dz + dz } : { ...m, tx: m.tx + dx, tz: m.tz + dz };
          return { movimientos: { ...s.movimientos, [clave]: { ...deViga, [id]: nuevo } } };
        });
      };
      const soltar = () => {
        window.removeEventListener('pointermove', mover);
        window.removeEventListener('pointerup', soltar);
      };
      window.addEventListener('pointermove', mover);
      window.addEventListener('pointerup', soltar);
    }

    /** Lo movible de la vista larga, encima de la imagen: mismas medidas que da Revit, en píxeles de la imagen. */
    capaMovible(g, capa) {
      const { escala, altoLetra, piezas } = capa;
      const movs = this.movimientos(g), negros = this.state.negros;
      const texto = (t, color, extra) => html`
        <text x=${t.x} y=${t.y} font-size=${altoLetra} font-family="Arial, sans-serif" text-anchor="middle" dominant-baseline="central" fill=${negros ? '#000000' : color} transform=${t.girado ? `rotate(-90 ${t.x} ${t.y})` : null} ...${extra}>${t.texto}</text>`;
      return piezas.map((p) => {
        const m = movs[p.id] ?? { dz: 0, tx: 0, tz: 0 };
        const puntos = p.puntos.map((q) => q.join(',')).join(' ');
        const alBarra = (e) => this.arrastrar(e, g, p.id, true, escala);
        return html`
          <g key=${p.id} transform="translate(0 ${-m.dz * escala})">
            ${puntos ? html`
              <polyline points=${puntos} fill="none" stroke=${p.color} stroke-width="6"></polyline>
              <polyline points=${puntos} fill="none" stroke="transparent" stroke-width="18" pointer-events="stroke" style="cursor: ns-resize" onPointerDown=${alBarra}></polyline>` : null}
            ${p.anexos.map((t) => texto(t, p.color, { style: 'cursor: ns-resize', onPointerDown: alBarra }))}
            <g transform="translate(${m.tx * escala} ${-m.tz * escala})">
              ${texto(p.etiqueta, p.color, { style: 'cursor: move', onPointerDown: (e) => this.arrastrar(e, g, p.id, false, escala) })}
            </g>
          </g>`;
      });
    }

    grupoElegido() {
      return this.state.modelo?.grupos[this.state.grupo] ?? null;
    }

    grupoDeViga(id) {
      return this.state.modelo.grupos.find((g) => g.vigas.includes(id));
    }

    planta() {
      const { modelo, plantaId, plantas, grupo, hechos } = this.state;
      const p = plantas[plantaId];
      if (!modelo) return html`<span style="color: #5E6670">Leyendo el modelo…</span>`;
      if (modelo.plantas.length === 0) return html`<span style="color: #5E6670">El proyecto no tiene plantas estructurales.</span>`;
      if (!p) return html`<span style="color: #5E6670">Generando la planta en Revit…</span>`;
      const elegidas = new Set(this.grupoElegido()?.vigas ?? []);
      return html`
        <div style="position: absolute; inset: 0">
          <${Visor} ref=${(v) => (this.visor = v)} ancho=${p.ancho} alto=${p.alto} etiqueta="Planta de Revit con las vigas para elegir">
            <image href=${p.url} width=${p.ancho} height=${p.alto} draggable="false"></image>
            ${p.cajas.map((c) => {
              const g = this.grupoDeViga(c.viga);
              const color = g && hechos[g.indice] ? VERDE : elegidas.has(c.viga) ? AZUL : 'transparent';
              return html`<rect x=${c.x} y=${c.y} width=${c.w} height=${c.h} fill=${color} fill-opacity="0.55" pointer-events="all" style="cursor: pointer" onClick=${() => this.elegirViga(g)}><title>${g?.nombre ?? ''}</title></rect>`;
            })}
          <//>
        </div>`;
    }

    imagenes(g) {
      const pv = this.previa(g);
      if (!pv) return html`<div style="display: grid; place-items: center; height: 100%; color: #5E6670">Generando la vista en Revit…</div>`;
      const i = pv.longitudinal;
      return html`
        <${Visor} ancho=${i.ancho} alto=${i.alto} etiqueta="Vista longitudinal de Revit">
          <image href=${i.url} width=${i.ancho} height=${i.alto} draggable="false"></image>
          ${pv.capa ? this.capaMovible(g, pv.capa) : null}
        <//>`;
    }

    seccion(g) {
      const i = this.previa(g)?.seccion;
      if (!i) return null;
      return html`
        <div style="width: 100%; height: 360px; flex-shrink: 0; box-sizing: border-box; background: #FFFFFF; border: 1px solid #D9DDE2; border-radius: 6px; overflow: hidden">
          <${Visor} ancho=${i.ancho} alto=${i.alto} etiqueta="Sección de Revit">
            <image href=${i.url} width=${i.ancho} height=${i.alto} draggable="false"></image>
          <//>
        </div>`;
    }

    render() {
      const { paso, modelo, plantaId, plantas, hechos, error, hecho, creando } = this.state;
      const g = this.grupoElegido();
      const ancho = window.innerWidth, alto = window.innerHeight;
      const set = (o) => this.setState(o);
      const pasos = ['Elegir', 'Revisar', 'Crear vistas'].map((n, i) => {
        const k = i + 1, on = k === paso, ya = k < paso;
        return { n, num: ya ? '✓' : String(k), color: on ? '#16191D' : '#5E6670', fw: on ? 600 : 400, fondo: on || ya ? AZUL : '#DDE1E6', numC: on || ya ? '#FFFFFF' : '#3F4A57', pick: () => (k === 1 ? set({ paso: 1 }) : this.irA(k)) };
      });
      const tipos = TIPOS.map((t) => ({ ...t, off: !!t.off, borde: t.k ? AZUL : '#C9CFD6', fondo: t.k ? '#E4ECF6' : '#FFFFFF', color: t.k ? '#174A8A' : '#6B737C', cursor: t.off ? 'not-allowed' : 'default' }));
      const titulo = TEXTOS[paso][0], ayuda = TEXTOS[paso][1];
      const enEstaPlanta = g ? g.vigas.filter((v) => plantas[plantaId]?.cajas.some((c) => c.viga === v)).length : 0;
      const hechas = modelo ? modelo.grupos.filter((x) => hechos[x.indice]).length : 0;
      const puedeSeguir = !creando && (paso === 3 || !!g?.tieneAcero);
      const back = () => {
        if (paso > 1) set({ paso: paso - 1, hecho: false });
        else enviar({ tipo: 'cerrar' });
      };
      const next = () => {
        if (paso < 3) return this.irA(paso + 1);
        if (hecho) return set({ paso: 1, hecho: false, grupo: null });
        set({ creando: true, error: null });
        enviar({ tipo: 'crearVistas', grupo: g.indice, negros: this.state.negros, movimientos: this.movimientos(g) });
      };

      return html`
        <div style="width: ${ancho}px; height: ${alto}px; display: flex; flex-direction: column; background: #F4F6F8; font-family: 'IBM Plex Sans', 'Segoe UI', sans-serif; font-size: 14px; color: #16191D; overflow: hidden">

          <div style="height: 64px; flex-shrink: 0; display: flex; align-items: center; gap: 24px; padding: 0 24px; background: #FFFFFF; border-bottom: 1px solid #D9DDE2">
            <span style="font-size: 16px; font-weight: 600">Despiece</span>
            <div style="flex: 1; display: flex; justify-content: center; align-items: center; gap: 12px">
              ${pasos.map((p) => html`
                <button type="button" onClick=${p.pick} style="display: flex; align-items: center; gap: 8px; height: 40px; padding: 0 12px; border: 0; background: transparent; font: inherit; color: ${p.color}; font-weight: ${p.fw}; cursor: pointer">
                  <span style="width: 26px; height: 26px; display: grid; place-items: center; border-radius: 13px; background: ${p.fondo}; color: ${p.numC}; font-size: 13px; font-weight: 600">${p.num}</span>${p.n}
                </button>`)}
            </div>
            <div style="width: 70px"></div>
          </div>

          <div style="flex: 1; min-height: 0; padding: 20px 24px; display: flex; flex-direction: column; gap: 14px">
            <div style="display: flex; flex-direction: column; gap: 2px">
              <span style="font-size: 22px; font-weight: 600">${titulo}</span>
              <span style="color: #5E6670">${ayuda}</span>
            </div>

            ${paso === 1 ? html`
              <div style="flex: 1; min-height: 0; display: grid; grid-template-columns: 360px minmax(0, 1fr); gap: 16px">
                <div style="display: flex; flex-direction: column; gap: 12px; min-height: 0; overflow: auto">
                  <div style="display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px">
                    ${tipos.map((t) => html`
                      <button type="button" disabled=${t.off} style="display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 6px; height: 92px; border: 1px solid ${t.borde}; border-radius: 6px; background: ${t.fondo}; color: ${t.color}; font: inherit; font-weight: 500; cursor: ${t.cursor}">
                        <svg width="36" height="36" viewBox="0 0 36 36" aria-hidden="true"><path d=${t.icon} stroke="currentColor" stroke-width="2" fill="none" stroke-linejoin="round"></path></svg>${t.n}
                      </button>`)}
                  </div>
                  <span style="font-weight: 600">Plantas</span>
                  <div style="display: flex; flex-wrap: wrap; gap: 6px; max-height: 232px; overflow-y: auto; padding: 1px">
                    ${(modelo?.plantas ?? []).map((p) => html`
                      <button type="button" onClick=${() => this.verPlanta(p.id)} style="height: 36px; padding: 0 12px; border: 1px solid ${p.id === plantaId ? AZUL : '#C9CFD6'}; border-radius: 6px; background: ${p.id === plantaId ? '#E4ECF6' : '#FFFFFF'}; color: ${p.id === plantaId ? '#174A8A' : '#16191D'}; font: inherit; cursor: pointer">${p.nombre}</button>`)}
                  </div>
                  ${g ? html`
                    <div style="display: flex; flex-direction: column; gap: 4px; padding: 10px 12px; background: #FFFFFF; border: 1px solid ${AZUL}; border-radius: 6px">
                      <span style="font-weight: 600">${g.nombre}</span>
                      ${g.tieneAcero ? html`
                        <span style="font-size: 12px; color: #5E6670">${g.cantidad === 1 ? 'Acero único' : `${g.cantidad} vigas con el mismo acero`}</span>
                        <span style="font-size: 12px; color: #5E6670">${enEstaPlanta} tramos en esta planta, ${g.vigas.length - enEstaPlanta} en otras</span>` : html`
                        <span style="font-size: 12px; color: #B3261E">Esta viga no tiene acero: no se puede despiezar.</span>`}
                    </div>` : html`<span style="font-size: 12px; color: #5E6670">Ninguna viga elegida.</span>`}
                  <div style="display: flex; gap: 14px; font-size: 12px; color: #5E6670">
                    <span><span style="display: inline-block; width: 10px; height: 10px; background: ${AZUL}; margin-right: 4px"></span>Elegida</span>
                    <span><span style="display: inline-block; width: 10px; height: 10px; background: ${VERDE}; margin-right: 4px"></span>Ya tiene despiece (${hechas})</span>
                  </div>
                  ${g && (g.vistaCreada || hechos[g.indice]) ? html`
                    <div style="flex-shrink: 0; margin-top: auto; display: flex; flex-direction: column; gap: 6px">
                      <span style="font-weight: 600">Ver en Revit</span>
                      <div style="display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px">
                        ${[['Vista larga', false], ['Sección', true]].map(([n, seccion]) => html`
                          <button type="button" onClick=${() => enviar({ tipo: 'irAVista', grupo: g.indice, seccion })} style="height: 36px; border: 1px solid #C9CFD6; border-radius: 6px; background: #FFFFFF; color: #16191D; font: inherit; cursor: pointer">${n}</button>`)}
                      </div>
                    </div>` : null}
                </div>
                <div style="position: relative; min-width: 0; min-height: 0; display: grid; place-items: center; background: #FFFFFF; border: 1px solid #D9DDE2; border-radius: 6px; overflow: hidden">
                  ${this.planta()}
                </div>
              </div>` : null}

            ${paso > 1 && g ? html`
              <div style="flex: 1; min-height: 0; display: grid; grid-template-columns: minmax(0, 1fr) 460px; gap: 16px">
                <div style="position: relative; min-width: 0; min-height: 0; background: #FFFFFF; border: 1px solid #D9DDE2; border-radius: 6px; overflow: hidden">
                  ${this.vistaGrande(g)}
                </div>
                <div style="display: flex; flex-direction: column; gap: 8px; min-width: 0; min-height: 0; overflow: hidden auto">
                  ${this.seccion(g)}
                  ${this.selectorTextos(g)}
                  ${paso === 2 ? html`
                    <span style="font-weight: 600">Barras de ${g.nombre}</span>
                    ${g.filas.map((f) => html`
                      <div style="display: grid; grid-template-columns: 14px minmax(0, 1fr); gap: 10px; align-items: center; padding: 10px; background: #FFFFFF; border: 1px solid #D9DDE2; border-radius: 6px">
                        <span style="width: 14px; height: 14px; border-radius: 3px; background: ${f.c}"></span>
                        <span style="display: flex; flex-direction: column; gap: 2px; min-width: 0"><span style="font-family: 'IBM Plex Mono', monospace; font-size: 13px">${f.et}</span><span style="font-size: 12px; color: #5E6670">${f.d}</span></span>
                      </div>`)}` : html`
                    <span style="font-weight: 600">Vistas que se crearán</span>
                    ${[g.nombre, 'Sección ' + g.nombre].map((n) => html`<div style="padding: 10px; background: #FFFFFF; border: 1px solid #D9DDE2; border-radius: 6px">${n}</div>`)}`}
                </div>
              </div>` : null}
          </div>

          <div style="height: 68px; flex-shrink: 0; display: flex; align-items: center; justify-content: space-between; gap: 16px; padding: 0 24px; background: #FFFFFF; border-top: 1px solid #D9DDE2">
            <button type="button" onClick=${back} style="height: 44px; padding: 0 20px; border: 1px solid #C9CFD6; border-radius: 6px; background: #FFFFFF; font: inherit; color: #16191D; cursor: pointer">${paso === 1 ? 'Cerrar' : 'Atrás'}</button>
            ${error ? html`<span style="color: #B3261E; font-weight: 500">${error}</span>` : null}
            ${hecho && !error ? html`<span style="color: #1E7A3C; font-weight: 500">${hecho}</span>` : null}
            <button type="button" disabled=${!puedeSeguir} onClick=${next} style="height: 44px; padding: 0 24px; border: 0; border-radius: 6px; background: ${puedeSeguir ? AZUL : '#AEB5BD'}; color: #FFFFFF; font: inherit; font-weight: 600; cursor: pointer">${paso === 3 ? (hecho ? 'Despiezar otro' : 'Crear vistas') : 'Siguiente'}</button>
          </div>
        </div>`;
    }
  }

  window.Pasos = Pasos;
  const destino = document.getElementById('app');
  if (destino) render(html`<${Pasos} />`, destino);
})();
