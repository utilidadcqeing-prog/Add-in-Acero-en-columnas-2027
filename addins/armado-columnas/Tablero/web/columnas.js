// La ventana de Acero en columnas (#205, plan docs/planes/2026-10-29-interfaz-web-columnas.md). Misma receta que el
// tablero de vigas: la página no calcula acero (decisión 2), pinta `vista` (VistaColumnas, que arma C#) y le manda lo que
// el usuario cambia. Dentro de Revit habla con C# por window.chrome.webview; fuera, con demo.js (la maqueta para Carlos).
(function () {
  'use strict';
  const { html, render, Component } = window.htmPreact;
  const { Planta, Seccion, Elevacion, Traslapos, escalaSeccion, ESTADO, C } = window.DibujosColumnas;

  const enRevit = !!(window.chrome && window.chrome.webview);
  const f2 = (x) => (Math.round(x * 100) / 100).toFixed(2);
  const num = (x) => { const v = parseFloat(String(x).replace(',', '.')); return Number.isFinite(v) ? v : NaN; };
  const PASOS = [
    { n: 1, nombre: 'Barras' }, { n: 2, nombre: 'Estribos' }, { n: 3, nombre: 'Grapas' }, { n: 4, nombre: 'Traslapos' },
  ];
  const FORMAS = [
    { forma: 'GrapaC', nombre: 'Grapa en C', icono: 'M6 6 L4 10 L52 10 L50 6' },
    { forma: 'GrapaS', nombre: 'Grapa en S', icono: 'M6 6 L4 10 L52 10 L50 14' },
    { forma: 'Cerrado', nombre: 'Estribo cerrado', icono: 'M8 3 H48 V17 H8 Z M8 17 L13 12' },
  ];
  const FILTROS_3D = ['Todo', 'Solo longitudinales', 'Solo estribos (con grapas)', 'Solo ganchos', 'Solo traslapos'];
  const NOMBRE_FORMA = { GrapaC: 'Grapa en C', GrapaS: 'Grapa en S', Cerrado: 'Estribo cerrado' };

  // ---- Piezas chicas de formulario

  function Varilla({ id, valor, opciones, onCambiar, conIgual }) {
    const actual = opciones.find((o) => o.nombre === valor);
    return html`<span class="varilla">
      <i style=${`background: ${actual ? actual.color : '#C9CFD6'}`}></i>
      <select id=${id} value=${valor || ''} onChange=${(e) => onCambiar(e.target.value || null)}>
        ${conIgual ? html`<option value="">Igual al estribo</option>` : null}
        ${opciones.map((o) => html`<option value=${o.nombre}>${o.nombre} · ${o.diametroMm} mm</option>`)}
      </select>
    </span>`;
  }

  function Contador({ id, valor, min, max, onCambiar, etiqueta }) {
    return html`<span class="contador" role="group" aria-label=${etiqueta}>
      <button type="button" aria-label=${`Menos ${etiqueta}`} disabled=${valor <= min} onClick=${() => onCambiar(valor - 1)}>−</button>
      <output id=${id} aria-live="polite">${valor}</output>
      <button type="button" aria-label=${`Más ${etiqueta}`} disabled=${valor >= max} onClick=${() => onCambiar(valor + 1)}>+</button>
    </span>`;
  }

  // Un largo en metros con − / +: se valida al salir del campo y el error se va apenas se corrige.
  class Medida extends Component {
    constructor(p) { super(p); this.state = { texto: null, error: null }; }
    confirmar(v) {
      const { min = 0.01, max = 99, onCambiar } = this.props;
      if (!Number.isFinite(v) || v < min || v > max) { this.setState({ error: `Escribe un valor entre ${f2(min)} y ${f2(max)}.` }); return; }
      this.setState({ texto: null, error: null });
      if (Math.abs(v - this.props.valor) > 1e-9) onCambiar(Math.round(v * 1000) / 1000);
    }
    render({ id, valor, paso = 0.01, unidad = 'm', decimales = 2 }, { texto, error }) {
      const mostrar = texto != null ? texto : valor.toFixed(decimales);
      return html`<span style="display: inline-flex; flex-direction: column; align-items: flex-end; gap: 3px">
        <span style="display: inline-flex; align-items: center">
          <span class="contador">
            <button type="button" aria-label="Menos" onClick=${() => this.confirmar(Math.round((valor - paso) * 1000) / 1000)}>−</button>
            <input id=${id} inputmode="decimal" value=${mostrar} aria-invalid=${error ? 'true' : 'false'}
              onInput=${(e) => this.setState({ texto: e.target.value, error: Number.isFinite(num(e.target.value)) ? null : this.state.error })}
              onBlur=${() => texto != null && this.confirmar(num(texto))}
              onKeyDown=${(e) => { if (e.key === 'Enter') { e.preventDefault(); if (texto != null) this.confirmar(num(texto)); } }} />
            <button type="button" aria-label="Más" onClick=${() => this.confirmar(Math.round((valor + paso) * 1000) / 1000)}>+</button>
          </span>
          <span class="unidad">${unidad}</span>
        </span>
        ${error ? html`<span class="error-campo" role="alert">${error}</span>` : null}
      </span>`;
    }
  }

  function Dato({ para, etiqueta, sub, children }) {
    return html`<div class="fila-dato">
      <label for=${para}>${etiqueta}${sub ? html`<span class="sub">${sub}</span>` : null}</label>
      <span>${children}</span>
    </div>`;
  }

  // El nombre de un tramo con los niveles que lo rodean, para que se entienda sin cotas.
  function nombreTramo(col, t) {
    const nivelAbajo = [...col.niveles].reverse().find((n) => n.z <= t.zInf + 0.25);
    const nivelArriba = col.niveles.find((n) => n.z >= t.zSup - 0.01);
    if (t.tipo === 'ContraZapata') return 'Contra la zapata';
    if (t.tipo === 'DentroDeViga') return `Nudo con la viga de ${nivelArriba ? nivelArriba.nombre : 'arriba'}`;
    return `Tramo libre ${nivelAbajo ? nivelAbajo.nombre : ''} → ${nivelArriba ? nivelArriba.nombre : 'arriba'}`;
  }

  // ---- La ventana

  class Ventana extends Component {
    constructor(p) {
      super(p);
      this.state = {
        vista: null, paso: 1, tramo: null, herramienta: null, primero: null, encima: null, pieza: null,
        ocultarEsquinas: false, zonaTraslapo: false, busqueda: '', menu: null, nombreNueva: '', borrar: null,
        borrarAjeno: false, cuadro: false, mas: false, avisoVisto: null, tam: { w: 700, h: 560 },
      };
      this.medida = null;
      this.onTecla = this.onTecla.bind(this);
    }

    componentDidMount() {
      if (enRevit) {
        window.chrome.webview.addEventListener('message', (e) => { if (e.data && e.data.tipo === 'vista') this.setState({ vista: e.data.vista }); });
        window.chrome.webview.postMessage({ tipo: 'listo' });
      } else {
        this.arrancarDemo();
        window.addEventListener('hashchange', () => this.arrancarDemo());
      }
      document.addEventListener('keydown', this.onTecla);
      document.addEventListener('pointerdown', (e) => { if (this.state.menu && !e.target.closest('.menu-ancla')) this.setState({ menu: null, borrar: null }); });
    }

    arrancarDemo() {
      const ejemplo = (location.hash || '#normal').slice(1);
      this.demo = window.DemoColumnas.crear(ejemplo);
      this.setState({ vista: this.demo.vista(), ejemplo, paso: ejemplo === 'simple' ? 1 : this.state.paso, tramo: null, pieza: null, primero: null });
    }

    enviar(m) {
      if (enRevit) window.chrome.webview.postMessage(m);
      else this.setState({ vista: this.demo.atender(m) });
    }

    onTecla(e) {
      if (e.target.closest && e.target.closest('input, select')) return;
      if (e.key === 'Escape') this.setState({ herramienta: null, primero: null, menu: null, cuadro: false });
      if ((e.key === 'Delete' || e.key === 'Supr') && this.state.paso === 3 && this.state.pieza != null) this.eliminarPieza();
    }

    medir(el) {
      if (!el || el === this.medida) return;
      this.medida = el;
      const ro = new ResizeObserver(() => {
        const r = el.getBoundingClientRect();
        const w = Math.max(280, Math.floor(r.width)), h = Math.max(320, Math.floor(r.height));
        if (w !== this.state.tam.w || h !== this.state.tam.h) this.setState({ tam: { w, h } });
      });
      ro.observe(el);
    }

    // ---- Planta y selección

    elegir(id, sumar) {
      const v = this.state.vista;
      let ids = sumar ? (v.elegidas.includes(id) ? v.elegidas.filter((x) => x !== id) : [...v.elegidas, id]) : [id];
      if (!ids.length) ids = [id];
      this.setState({ tramo: null, pieza: null, primero: null });
      this.enviar({ tipo: 'elegirColumnas', ids });
    }

    // ---- Grapas: lo que quedaría bajo el puntero (solo pantalla; la pieza real la arma C#)

    barraBajo(p, mapa) {
      const col = this.state.vista.columna;
      if (!p || !mapa) return null;
      let mejor = null, dmin = 16;
      for (const b of col.seccion.barras) {
        if (!b.eje) continue;
        const d = Math.hypot(mapa.X(b.x) - p.x, mapa.Y(b.y) - p.y);
        if (d < dmin) { dmin = d; mejor = b; }
      }
      return mejor;
    }

    fantasmaPara(forma, b, mapa) {
      if (!forma || !b) return null;
      const s = this.state.vista.columna.seccion, E = s.estribo;
      const { X, Y } = mapa;
      if (forma !== 'Cerrado') return b.eje === 'Fila' ? [[X(E.x1), Y(b.y)], [X(E.x2), Y(b.y)]] : [[X(b.x), Y(E.y1)], [X(b.x), Y(E.y2)]];
      const p = this.state.primero && this.state.primero.eje === b.eje ? this.state.primero : { indice: b.indice };
      const misma = s.barras.filter((k) => k.eje === b.eje && k.posicion === (b.eje === 'Fila' ? 'izquierda' : 'abajo'));
      const a = misma.find((k) => k.indice === Math.min(p.indice, b.indice)), z = misma.find((k) => k.indice === Math.max(p.indice, b.indice));
      if (b.eje === 'Fila') { const y1 = Y(a.y) + 7, y2 = Y(z.y) - 7; return [[X(E.x1), y1], [X(E.x2), y1], [X(E.x2), y2], [X(E.x1), y2], [X(E.x1), y1]]; }
      const x1 = X(a.x) - 7, x2 = X(z.x) + 7; return [[x1, Y(E.y1)], [x2, Y(E.y1)], [x2, Y(E.y2)], [x1, Y(E.y2)], [x1, Y(E.y1)]];
    }

    poner(forma, b) {
      if (!forma || !b) return;
      if (forma !== 'Cerrado') { this.enviar({ tipo: 'menor', accion: 'agregar', forma, eje: b.eje, desde: b.indice, hasta: b.indice }); this.setState({ pieza: this.state.vista.opciones.longitudinal.estribosMenores.length - 1 }); return; }
      const p = this.state.primero;
      if (!p || p.eje !== b.eje) { this.setState({ primero: { eje: b.eje, indice: b.indice } }); return; }
      this.enviar({ tipo: 'menor', accion: 'agregar', forma, eje: b.eje, desde: p.indice, hasta: b.indice });
      this.setState({ primero: null, pieza: this.state.vista.opciones.longitudinal.estribosMenores.length - 1 });
    }

    eliminarPieza() {
      this.enviar({ tipo: 'menor', accion: 'eliminar', indice: this.state.pieza });
      this.setState({ pieza: null });
    }

    // ---- Render

    render(_, s) {
      const v = s.vista;
      if (!v) return html`<div class="ventana"></div>`;
      const col = v.columna, o = v.opciones;
      const aviso = v.aviso && v.aviso !== s.avisoVisto ? v.aviso : null;
      if (aviso) setTimeout(() => this.setState({ avisoVisto: aviso }), 4200);
      return html`<div class="ventana">
        ${this.barra(v)}
        <div class="cuerpo">
          ${this.ladoIzquierdo(v)}
          ${this.centro(v, col, o)}
          ${this.ladoDerecho(v, col, o)}
        </div>
        ${aviso ? html`<div class="aviso-flotante" role="status">${aviso}</div>` : null}
        ${v.generacion.pendiente ? this.dialogoChoques(v) : null}
        ${s.cuadro ? this.dialogoCuadro(v, o) : null}
      </div>`;
    }

    barra(v) {
      const s = this.state, listas = v.generacion.listas;
      return html`<header class="barra">
        <svg width="18" height="18" viewBox="0 0 18 18" aria-hidden="true"><rect x="4.5" y="1.5" width="9" height="15" rx="1" stroke="#1F5FAF" stroke-width="1.6" fill="none"></rect><path d="M4.5 6h9M4.5 12h9" stroke="#1F5FAF" stroke-width="1.6"></path></svg>
        <span class="titulo">Acero en columnas</span>
        <span class="proyecto">Edificio de ejemplo</span>
        ${enRevit ? null : html`<span class="maqueta" aria-label="Ejemplos de la maqueta">Maqueta ·
          ${[['normal', 'Edificio'], ['simple', 'Una columna'], ['varias', 'Varias elegidas']].map(([h, t]) => html`<a href=${'#' + h} aria-current=${s.ejemplo === h ? 'true' : 'false'}>${t}</a>`)}
        </span>`}
        <div class="relleno"></div>
        <div class="menu-ancla">
          <button type="button" class="boton" aria-expanded=${s.menu === 'config' ? 'true' : 'false'} onClick=${() => this.setState({ menu: s.menu === 'config' ? null : 'config', borrar: null })}>
            <svg width="15" height="15" viewBox="0 0 16 16" aria-hidden="true"><path d="M3 2.5h10v11H3zM5.5 5.5h5M5.5 8h5M5.5 10.5h3" stroke="currentColor" stroke-width="1.3" fill="none"></path></svg>
            Configuraciones${v.configuracion ? html`<span style="color: #5E6670">· ${v.configuracion}</span>` : null}
          </button>
          ${s.menu === 'config' ? this.menuConfiguraciones(v) : null}
        </div>
        <button type="button" class="boton" onClick=${() => this.setState({ cuadro: true })}>
          <svg width="15" height="15" viewBox="0 0 16 16" aria-hidden="true"><path d="M2 3h12v10H2zM2 6.5h12M2 10h12M6 3v10" stroke="currentColor" stroke-width="1.3" fill="none"></path></svg>
          Cuadro de varillas
        </button>
        <div class="menu-ancla">
          <button type="button" class="boton primario" disabled=${!listas.length}
            title=${listas.length ? '' : 'Cambia algún dato de una columna para dejarla lista'}
            aria-expanded=${s.menu === 'generar' ? 'true' : 'false'} onClick=${() => this.setState({ menu: s.menu === 'generar' ? null : 'generar' })}>
            Generar en Revit${listas.length ? html`<span class="cuenta">${listas.length}</span>` : null}
          </button>
          ${s.menu === 'generar' ? html`<div class="menu" style="min-width: 320px">
            <div class="titulo-menu">Se van a armar</div>
            <div class="nota" style="color: #16191D; font-weight: 500">${listas.join(', ')}</div>
            <label class="nota" style="display: flex; gap: 8px; align-items: flex-start; color: #16191D">
              <input type="checkbox" id="borrar-ajeno" checked=${s.borrarAjeno} onChange=${(e) => this.setState({ borrarAjeno: e.target.checked })} />
              <span>Borrar también el acero que no generó el add-in en estas columnas</span>
            </label>
            <div class="nota">El acero que el add-in generó antes en estas columnas se reemplaza siempre.</div>
            <div class="fila" style="justify-content: flex-end">
              <button type="button" class="boton primario" onClick=${() => { this.setState({ menu: null }); this.enviar({ tipo: 'generar', borrarAjeno: s.borrarAjeno }); }}>
                Generar ${listas.length} ${listas.length === 1 ? 'columna' : 'columnas'}
              </button>
            </div>
          </div>` : null}
        </div>
      </header>`;
    }

    menuConfiguraciones(v) {
      const s = this.state, actual = v.configuracion;
      return html`<div class="menu" role="menu">
        <div class="titulo-menu">Guardadas en este modelo</div>
        ${v.catalogos.configuraciones.length ? v.catalogos.configuraciones.map((n) => html`
          <button type="button" role="menuitem" class=${'item' + (n === actual ? ' actual' : '')} disabled=${!v.columna}
            onClick=${() => { this.setState({ menu: null }); this.enviar({ tipo: 'aplicarConfiguracion', nombre: n }); }}>
            <span>${n}</span><span style="color: #1F5FAF; font-size: 12px">Aplicar</span>
          </button>`) : html`<div class="nota">Todavía no hay ninguna.</div>`}
        <div class="separador"></div>
        ${actual ? html`<button type="button" role="menuitem" class="item" onClick=${() => { this.setState({ menu: null }); this.enviar({ tipo: 'guardarConfiguracion', nombre: actual, comoNueva: false }); }}>
          Guardar los cambios en «${actual}»</button>` : null}
        <form class="fila" onSubmit=${(e) => { e.preventDefault(); const n = s.nombreNueva.trim(); if (!n) return; this.setState({ menu: null, nombreNueva: '' }); this.enviar({ tipo: 'guardarConfiguracion', nombre: n, comoNueva: true }); }}>
          <label for="nombre-config" class="oculto-lector">Nombre de la configuración nueva</label>
          <input id="nombre-config" placeholder="Nombre, por ejemplo Columna 40×40" value=${s.nombreNueva} onInput=${(e) => this.setState({ nombreNueva: e.target.value })} />
          <button type="submit" class="boton chico" disabled=${!s.nombreNueva.trim() || !v.columna}>Guardar como nueva</button>
        </form>
        <div class="nota">Guarda barras, estribos, grapas y traslapos dentro del modelo de Revit.</div>
        ${actual ? (s.borrar === actual ? html`<div class="fila" style="align-items: center">
            <span class="peligro" style="flex: 1">¿Eliminar «${actual}» del modelo?</span>
            <button type="button" class="boton chico" onClick=${() => this.setState({ borrar: null })}>No</button>
            <button type="button" class="boton chico" style="border-color: #B42318; color: #B42318" onClick=${() => { this.setState({ menu: null, borrar: null }); this.enviar({ tipo: 'borrarConfiguracion', nombre: actual }); }}>Eliminar</button>
          </div>` : html`<button type="button" role="menuitem" class="item peligro" onClick=${() => this.setState({ borrar: actual })}>Eliminar «${actual}»…</button>`) : null}
      </div>`;
    }

    ladoIzquierdo(v) {
      const s = this.state, q = s.busqueda.trim().toLowerCase();
      const coincide = q ? v.planta.columnas.filter((c) => (c.marca + ' ' + c.ubicacion).toLowerCase().includes(q)) : [];
      const listas = v.planta.columnas.filter((c) => c.estado === 'lista');
      return html`<aside class="lado izq" aria-label="Elegir columnas">
        <div class="cabeza-lado"><b>Columnas</b><span>${v.planta.columnas.length} en el modelo</span></div>
        <button type="button" class="boton contorno" style="justify-content: center; height: 36px" onClick=${() => this.enviar({ tipo: 'seleccionarEnRevit' })}>
          <svg width="15" height="15" viewBox="0 0 16 16" aria-hidden="true"><path d="M3 2l9 5-4 1.2L6.5 13z" stroke="currentColor" stroke-width="1.5" fill="none" stroke-linejoin="round"></path></svg>
          Seleccionar en Revit
        </button>
        <div class="buscador">
          <label for="buscar" class="oculto-lector">Buscar columna</label>
          <input id="buscar" type="search" placeholder="Buscar por marca o eje (C-12, A-1)" value=${s.busqueda}
            onInput=${(e) => this.setState({ busqueda: e.target.value })}
            onKeyDown=${(e) => { if (e.key === 'Enter' && coincide.length) { this.elegir(coincide[0].id, false); this.setState({ busqueda: '' }); } }} />
        </div>
        ${q ? html`<div class="resultados">
          ${coincide.length ? coincide.map((c) => html`<button type="button" onClick=${() => { this.elegir(c.id, false); this.setState({ busqueda: '' }); }}>
            <b>${c.marca}</b><span style="color: #5E6670">${c.ubicacion}</span></button>`) : html`<span class="vacio">Ninguna columna se llama así. Prueba con la marca (C-12) o con el cruce de ejes (A-1).</span>`}
        </div>` : null}
        <div class="planta">
          <${Planta} planta=${v.planta} elegidas=${v.elegidas} compatibles=${v.compatibles} coincide=${coincide.map((c) => c.id)} w=${270} h=${230} onElegir=${(id, sumar) => this.elegir(id, sumar)} />
        </div>
        <div class="leyenda">
          ${Object.keys(ESTADO).map((k) => html`<span><i style=${`background: ${ESTADO[k].relleno}; border-color: ${ESTADO[k].borde}`}></i>${ESTADO[k].texto}</span>`)}
        </div>
        <p class="ayuda" style="margin: 0">Clic para elegir una columna. Ctrl + clic para elegir varias: lo que cambies cae en todas las que tengan la misma forma.</p>
        <div class="listas">
          <div class="cabeza-lado"><b style="font-size: 13px">Listas para generar</b><span>${listas.length}</span></div>
          ${listas.length ? html`<div class="chips">
            ${listas.map((c) => html`<span class="chip">${c.marca}<button type="button" aria-label=${`Sacar ${c.marca} de la lista`} title="Sacar de la lista" onClick=${() => this.enviar({ tipo: 'quitarDeLista', id: c.id })}>
              <svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true"><path d="M2 2l6 6M8 2l-6 6" stroke="currentColor" stroke-width="1.5"></path></svg></button></span>`)}
          </div>` : html`<p class="ayuda" style="margin: 0">Una columna queda lista en cuanto le cambias un dato o le aplicas una configuración.</p>`}
        </div>
      </aside>`;
    }

    centro(v, col, o) {
      const s = this.state;
      if (!col) return html`<main class="centro"><div class="lienzo" style="align-items: center; justify-content: center"><p class="ayuda">Elige una columna en la planta para empezar.</p></div></main>`;
      const estado = v.planta.columnas.find((c) => c.id === col.id).estado;
      const otras = v.compatibles.length - 1;
      const R = col.resumen;
      return html`<main class="centro">
        ${v.generacion.resultado ? html`<div class="resultado" role="status">
          <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true" style="flex-shrink: 0; margin-top: 2px"><path d="M3 8.5l3 3 7-7.5" stroke="#1E7A46" stroke-width="1.8" fill="none" stroke-linecap="round"></path></svg>
          <span style="flex: 1">${v.generacion.resultado.detalles.map((d) => html`<span style="display: block">${d}</span>`)}</span>
        </div>` : null}
        <div class="cabeza-centro">
          <div class="nombre">
            <b>${col.marca}${otras > 0 ? html`<span style="font-weight: 400; color: #5E6670"> + ${otras} ${otras === 1 ? 'igual' : 'iguales'}</span>` : null}</b>
            <span>Eje ${col.ubicacion} · ${Math.round(col.baseX * 100)} × ${Math.round(col.baseY * 100)} cm · ${R.pisos} ${R.pisos === 1 ? 'tramo libre' : 'tramos libres'}${col.zapata ? ' · arranca sobre zapata' : ''}</span>
          </div>
          <span class=${'estado ' + estado}>${ESTADO[estado].texto}</span>
          <div class="menu-ancla">
            <button type="button" class="boton" aria-expanded=${s.menu === '3d' ? 'true' : 'false'} onClick=${() => this.setState({ menu: s.menu === '3d' ? null : '3d' })}>
              <svg width="15" height="15" viewBox="0 0 16 16" aria-hidden="true"><path d="M8 1.5l5.5 3v7L8 14.5l-5.5-3v-7zM8 8l5.5-3.5M8 8v6.5M8 8L2.5 4.5" stroke="currentColor" stroke-width="1.3" fill="none" stroke-linejoin="round"></path></svg>
              Ver en 3D
            </button>
            ${s.menu === '3d' ? html`<div class="menu" role="menu">
              <div class="titulo-menu">Vista 3D en Revit</div>
              ${FILTROS_3D.map((f) => html`<button type="button" role="menuitem" class="item" onClick=${() => { this.setState({ menu: null }); this.enviar({ tipo: 'ver3D', filtro: f }); }}>${f}</button>`)}
              <div class="nota">Se actualiza sola con cada cambio. No crea nada en el modelo.</div>
            </div>` : null}
          </div>
        </div>
        ${v.descartadas.length ? html`<div class="aviso" role="status">
          <span><b>${v.descartadas.join(', ')} no se ${v.descartadas.length === 1 ? 'cambia' : 'cambian'}:</b> ${v.descartadas.length === 1 ? 'tiene' : 'tienen'} otra forma (otros tramos o sección) que ${col.marca}. Elígela sola para configurarla.</span>
        </div>` : null}
        ${s.paso === 1 ? this.lienzoBarras(col) : null}
        ${s.paso === 2 ? this.lienzoEstribos(col) : null}
        ${s.paso === 3 ? this.lienzoGrapas(col) : null}
        ${s.paso === 4 ? this.lienzoTraslapos(col) : null}
        <div class="resumen">
          <span class="dato"><b>${R.barras}</b><span>barras</span></span>
          <span class="dato"><b>${R.estribos}</b><span>estribos</span></span>
          <span class="dato"><b>${R.grapas}</b><span>grapas</span></span>
          <span class="dato"><b>${R.cerrados}</b><span>estribos cerrados</span></span>
          <div class="relleno"></div>
          ${col.avisos.map((a) => html`<span class="ayuda" style="white-space: normal">${a}</span>`)}
        </div>
      </main>`;
    }

    lienzoBarras(col) {
      const { w, h } = this.state.tam;
      return html`<div class="lienzo"><div class="medida" ref=${(el) => this.medir(el)}>
        <${Seccion} sec=${col.seccion} w=${w} h=${h} rotulos="barras" />
        <span class="consejo">Sección vista desde arriba, a escala. Cada barra lleva el color de su número, igual que en Revit.</span>
      </div></div>`;
    }

    lienzoEstribos(col) {
      const { w, h } = this.state.tam, s = this.state;
      const usadas = [...new Set(col.zonasEstribos.map((z) => z.zona))];
      const ZN = { confinada: ['#1F5FAF', 'Puntas (confinado)'], central: ['#86A9D6', 'Centro del tramo'], uniforme: ['#4F7FBF', 'Uniforme'], nudo: ['#7D8A99', 'Nudo (cruce con la viga)'], zapata: ['#B9C2CC', 'Sin estribos'] };
      return html`<div class="lienzo">
        <div class="medida" ref=${(el) => this.medir(el)}>
          <${Elevacion} col=${col} w=${w} h=${h} conEstribos=${true} seleccionado=${s.tramo} onTramo=${(i) => this.setState({ tramo: s.tramo === i ? null : i })} />
        </div>
        <div class="lado-lienzo der">
          <h3 style="align-self: flex-start">Sección</h3>
          <${Seccion} sec=${col.seccion} w=${158} h=${158} chica=${true} />
          <div class="leyenda" style="flex-direction: column; align-self: stretch">
            ${usadas.map((z) => html`<span><i style=${`background: ${ZN[z][0]}; border-color: ${ZN[z][0]}`}></i>${ZN[z][1]}</span>`)}
          </div>
          <p class="ayuda" style="margin: 0; align-self: stretch">Haz clic en un tramo del dibujo para darle otros valores.</p>
        </div>
      </div>`;
    }

    lienzoGrapas(col) {
      const s = this.state;
      const { w, h } = s.tam;
      const mapa = (() => { const e = escalaSeccion(col.seccion, w, h, 80); return { X: e.X, Y: e.Y }; })();
      const fantasma = s.encima ? this.fantasmaPara(s.herramienta || s.arrastrando, s.encima, mapa) : null;
      const sel = s.pieza != null ? col.seccion.menores[s.pieza] : null;
      const consejo = s.primero ? 'Ahora haz clic en otra barra de la misma cara para cerrar el estribo. Esc para cancelar.'
        : s.herramienta ? `${NOMBRE_FORMA[s.herramienta]}: haz clic en una barra intermedia.${s.herramienta === 'Cerrado' ? ' Son dos clics, uno en cada barra de las puntas.' : ' Va de borde a borde, hasta la barra de enfrente.'}`
          : 'Elige una forma a la izquierda, o haz clic en una pieza para seleccionarla.';
      return html`<div class="lienzo">
        <div class="lado-lienzo">
          <h3>Formas</h3>
          ${FORMAS.map((f) => html`<button type="button" class="forma" draggable="true" aria-pressed=${s.herramienta === f.forma ? 'true' : 'false'}
              onDragStart=${(e) => { e.dataTransfer.setData('text/plain', f.forma); this.setState({ arrastrando: f.forma }); }}
              onDragEnd=${() => this.setState({ arrastrando: null, encima: null })}
              onClick=${() => this.setState({ herramienta: s.herramienta === f.forma ? null : f.forma, primero: null })}>
            <svg width="56" height="20" viewBox="0 0 56 20" aria-hidden="true"><path d=${f.icono} stroke="#16191D" stroke-width="2.2" fill="none" stroke-linejoin="round" stroke-linecap="round"></path></svg>
            ${f.nombre}
          </button>`)}
          <p class="ayuda" style="margin: 0">Arrastra una forma sobre una barra, o elígela y haz clic en la barra.</p>
        </div>
        <div class="medida" ref=${(el) => this.medir(el)}>
          <div class="opciones-lienzo">
            <div class="segmentado" role="group" aria-label="Qué sección se ve">
              <button type="button" class="chico" style="padding: 0 10px" aria-pressed=${!s.zonaTraslapo ? 'true' : 'false'} onClick=${() => this.setState({ zonaTraslapo: false })}>Sección normal</button>
              <button type="button" style="padding: 0 10px" aria-pressed=${s.zonaTraslapo ? 'true' : 'false'} onClick=${() => this.setState({ zonaTraslapo: true })}>Zona de traslapo</button>
            </div>
            <label><input type="checkbox" id="ocultar-esquinas" checked=${s.ocultarEsquinas} onChange=${(e) => this.setState({ ocultarEsquinas: e.target.checked })} /> Ocultar barras de esquina</label>
          </div>
          <${Seccion} sec=${col.seccion} w=${w} h=${h} modo="grapas" rotulos="filas" ocultarEsquinas=${s.ocultarEsquinas} zonaTraslapo=${s.zonaTraslapo}
            seleccionada=${s.pieza} fantasma=${fantasma}
            onMover=${(p, m) => this.setState({ encima: p ? this.barraBajo(p, m) : null })}
            onSoltar=${(forma) => { this.poner(forma, s.encima); this.setState({ arrastrando: null }); }}
            onBarra=${(b) => this.poner(s.herramienta, b)}
            onPieza=${(i) => this.setState({ pieza: s.pieza === i ? null : i, herramienta: null, primero: null })} />
          ${sel && sel.forma !== 'Cerrado' ? this.tiritas(col, sel, mapa) : null}
          <span class="consejo">${consejo}</span>
        </div>
      </div>`;
    }

    // Las tiritas 90° · 135° · 180° junto a cada punta de la grapa elegida, como en la ventana de estribos menores.
    tiritas(col, sel, mapa) {
      const m = this.state.vista.opciones.longitudinal.estribosMenores[sel.indice];
      if (!sel.puntas) return null;
      const [ini, fin] = sel.puntas, fila = sel.eje === 'Fila';
      const tirita = (pt, punta, actual) => {
        const x = mapa.X(pt[0]), y = mapa.Y(pt[1]);
        // Afuera de la sección, frente a la barra de esa punta.
        const dentro = (izq) => Math.max(4, Math.min(this.state.tam.w - 134, izq));
        const estilo = fila
          ? `left: ${dentro(punta === 'inicio' ? x - 196 : x + 70)}px; top: ${y - 14}px`
          : `left: ${dentro(x - 63)}px; top: ${punta === 'inicio' ? y + 46 : y - 74}px`;
        return html`<div class="angulos" style=${`position: absolute; ${estilo}; z-index: 3`}>
          <div class="seg" role="group" aria-label=${`Gancho de la punta ${punta === 'inicio' ? (fila ? 'izquierda' : 'de abajo') : (fila ? 'derecha' : 'de arriba')}`}>
            ${[90, 135, 180].map((a) => html`<button type="button" style="width: 42px" aria-pressed=${actual === a ? 'true' : 'false'} onClick=${() => this.enviar({ tipo: 'menor', accion: 'angulo', indice: sel.indice, punta, angulo: a })}>${a}°</button>`)}
          </div>
        </div>`;
      };
      return html`<div>${tirita(ini, 'inicio', m.anguloInicio)}${tirita(fin, 'fin', m.anguloFin)}</div>`;
    }

    lienzoTraslapos(col) {
      const { w, h } = this.state.tam;
      return html`<div class="lienzo"><div class="medida desplazable" ref=${(el) => this.medir(el)}>
        <${Traslapos} col=${col} w=${w} h=${h} />
      </div></div>`;
    }

    // ---- Panel derecho: los pasos

    ladoDerecho(v, col, o) {
      const s = this.state;
      return html`<aside class="lado der" aria-label="Configurar la columna">
        <div class="pasos" role="tablist">
          ${PASOS.map((p) => html`<button type="button" role="tab" class="paso" aria-selected=${s.paso === p.n ? 'true' : 'false'}
              onClick=${() => this.setState({ paso: p.n, herramienta: null, primero: null })}>
            <small>${p.n}</small><span>${p.nombre}</span>
          </button>`)}
        </div>
        <div class="contenido-paso" role="tabpanel">
          ${!col ? html`<p class="ayuda">Elige una columna en la planta para configurarla.</p>`
            : s.paso === 1 ? this.pasoBarras(v, col, o)
              : s.paso === 2 ? this.pasoEstribos(v, col, o)
                : s.paso === 3 ? this.pasoGrapas(v, col, o)
                  : this.pasoTraslapos(v, col, o)}
        </div>
      </aside>`;
    }

    pasoBarras(v, col, o) {
      const L = o.longitudinal, cat = v.catalogos.barrasLongitudinales, s = this.state;
      const cambiar = (c) => this.enviar({ tipo: 'cambiarLongitudinal', config: c });
      return html`
        <div class="grupo">
          <h3>Esquinas <small>siempre 4</small></h3>
          <${Dato} para="b-esq" etiqueta="Número de varilla"><${Varilla} id="b-esq" valor=${L.tipoBarraEsquinasNombre} opciones=${cat} onCambiar=${(n) => cambiar({ tipoBarraEsquinasNombre: n })} /></${Dato}>
        </div>
        <div class="grupo">
          <h3>Caras X <small>arriba y abajo, sin las esquinas</small></h3>
          <${Dato} para="n-x" etiqueta="Barras en cada cara"><${Contador} id="n-x" etiqueta="barras en cada cara X" valor=${L.barrasPorCaraX} min=${0} max=${8} onCambiar=${(n) => cambiar({ barrasPorCaraX: n })} /></${Dato}>
          <${Dato} para="b-x" etiqueta="Número de varilla"><${Varilla} id="b-x" valor=${L.tipoBarraCaraXNombre} opciones=${cat} onCambiar=${(n) => cambiar({ tipoBarraCaraXNombre: n })} /></${Dato}>
        </div>
        <div class="grupo">
          <h3>Caras Y <small>izquierda y derecha, sin las esquinas</small></h3>
          <${Dato} para="n-y" etiqueta="Barras en cada cara"><${Contador} id="n-y" etiqueta="barras en cada cara Y" valor=${L.barrasPorCaraY} min=${0} max=${8} onCambiar=${(n) => cambiar({ barrasPorCaraY: n })} /></${Dato}>
          <${Dato} para="b-y" etiqueta="Número de varilla"><${Varilla} id="b-y" valor=${L.tipoBarraCaraYNombre} opciones=${cat} onCambiar=${(n) => cambiar({ tipoBarraCaraYNombre: n })} /></${Dato}>
        </div>
        <p class="nota" style="margin: 0">En total, <b>${col.resumen.barras} barras</b>. Van de la zapata al remate, en toda la columna.</p>
        <button type="button" class="enlace mas" aria-expanded=${s.mas ? 'true' : 'false'} onClick=${() => this.setState({ mas: !s.mas })}>${s.mas ? '− Menos opciones' : '+ Más opciones'}</button>
        ${s.mas ? html`<div class="grupo">
          <${Dato} para="rec" etiqueta="Recubrimiento" sub="Del borde de la columna al estribo">
            <${Medida} id="rec" valor=${o.recubrimiento * 100} paso=${0.5} unidad="cm" decimales=${1} min=${1} max=${10} onCambiar=${(cm) => this.enviar({ tipo: 'cambiarRecubrimiento', valor: Math.round(cm * 10) / 1000 })} />
          </${Dato}>
          <p class="nota" style="margin: 0">Solo se usa para dibujar el acero. No cambia el recubrimiento de la columna en Revit.</p>
        </div>` : null}`;
    }

    pasoEstribos(v, col, o) {
      const s = this.state, E = o.estribos, T = E.tramosLibres;
      const hayNudos = col.tramos.some((t) => t.tipo === 'DentroDeViga'), hayZapata = col.tramos.some((t) => t.tipo === 'ContraZapata');
      const libres = (c) => this.enviar({ tipo: 'cambiarEstribos', tramosLibres: c });
      const cambiarConfinado = (x) => {
        // Como hoy: el centro sigue al doble del confinado mientras nadie lo haya cambiado a mano.
        const c = { espaciadoConfinado: x };
        if (Math.abs(T.espaciadoNoConfinado - 2 * T.espaciadoConfinado) < 1e-6) c.espaciadoNoConfinado = Math.round(2 * x * 1000) / 1000;
        libres(c);
      };
      const tramo = s.tramo != null ? col.tramos[s.tramo] : null;
      const distintos = col.tramos.filter((t) => t.excepcion);
      return html`
        <div class="grupo">
          <h3>Barra de los estribos</h3>
          <${Dato} para="b-est" etiqueta="Número de varilla" sub="La misma para toda la columna">
            <${Varilla} id="b-est" valor=${T.tipoBarraNombre} opciones=${v.catalogos.barrasEstribo} onCambiar=${(n) => libres({ tipoBarraNombre: n })} />
          </${Dato}>
        </div>
        <div class="grupo">
          <h3>Tramos libres <small>la columna a la vista, de viga a viga</small></h3>
          <div class="segmentado" role="group" aria-label="Cómo se reparten los estribos">
            <button type="button" aria-pressed=${T.modo === 'Uniforme' ? 'true' : 'false'} onClick=${() => libres({ modo: 'Uniforme' })}>Uniforme</button>
            <button type="button" aria-pressed=${T.modo === 'Confinado' ? 'true' : 'false'} onClick=${() => libres({ modo: 'Confinado' })}>Confinado</button>
          </div>
          ${T.modo === 'Uniforme' ? html`
            <${Dato} para="e-u" etiqueta="Un estribo cada"><${Medida} id="e-u" valor=${T.espaciadoUniforme} onCambiar=${(x) => libres({ espaciadoUniforme: x })} /></${Dato}>
            ${hayNudos ? html`<p class="nota" style="margin: 0">Los nudos llevan el mismo espaciado: cada ${f2(T.espaciadoUniforme)} m.</p>` : null}`
          : html`
            <p class="nota" style="margin: 0">Más juntos cerca de las vigas (las puntas) y más separados en el centro.</p>
            <${Dato} para="e-l1" etiqueta="Largo de cada punta" sub="L1, arriba y abajo"><${Medida} id="e-l1" valor=${T.longitudConfinamiento} onCambiar=${(x) => libres({ longitudConfinamiento: x })} /></${Dato}>
            <${Dato} para="e-c" etiqueta="En las puntas, cada"><${Medida} id="e-c" valor=${T.espaciadoConfinado} onCambiar=${cambiarConfinado} /></${Dato}>
            <${Dato} para="e-nc" etiqueta="En el centro, cada"><${Medida} id="e-nc" valor=${T.espaciadoNoConfinado} onCambiar=${(x) => libres({ espaciadoNoConfinado: x })} /></${Dato}>
            ${hayNudos ? html`<${Dato} para="e-n" etiqueta="En los nudos, cada" sub="Cruce con la viga: siempre uniforme">
              <${Medida} id="e-n" valor=${E.nudoEspaciado} onCambiar=${(x) => this.enviar({ tipo: 'cambiarEstribos', nudoEspaciado: x })} /></${Dato}>` : null}`}
        </div>
        ${hayZapata ? html`<div class="grupo">
          <h3>Zapata</h3>
          <p class="nota" style="margin: 0">Sin estribos. Ahí solo pasa el acero longitudinal, que se ancla dentro de la zapata.</p>
        </div>` : null}
        <div class="grupo">
          <h3>Un tramo distinto <small>${distintos.length ? `${distintos.length} ${distintos.length === 1 ? 'distinto' : 'distintos'}` : ''}</small></h3>
          ${tramo ? this.tarjetaTramo(col, o, tramo) : html`<p class="nota" style="margin: 0">Si un tramo tiene que ir distinto, haz clic en él en el dibujo.</p>`}
          ${distintos.length ? html`<div class="lista-piezas">${distintos.map((t) => html`<button type="button" aria-pressed=${s.tramo === t.indice ? 'true' : 'false'} onClick=${() => this.setState({ tramo: t.indice })}>
            <span>${nombreTramo(col, t)}</span><span style="color: #C2620A">distinto</span></button>`)}</div>` : null}
        </div>`;
    }

    tarjetaTramo(col, o, t) {
      const exc = o.estribos.excepciones[t.indice];
      const nombre = nombreTramo(col, t), rango = `${f2(t.zInf)} a ${f2(t.zSup)} m`;
      if (t.tipo === 'ContraZapata') return html`<div class="tarjeta"><b>${nombre}</b><span class="nota">${rango}. No lleva estribos.</span></div>`;
      const base = t.tipo === 'DentroDeViga'
        ? { ...o.estribos.tramosLibres, modo: 'Uniforme', espaciadoUniforme: o.estribos.tramosLibres.modo === 'Uniforme' ? o.estribos.tramosLibres.espaciadoUniforme : o.estribos.nudoEspaciado }
        : { ...o.estribos.tramosLibres };
      const poner = (config) => this.enviar({ tipo: 'cambiarEstribos', excepcion: { indice: t.indice, config } });
      const c = exc || base;
      return html`<div class=${'tarjeta' + (exc ? ' excepcion' : '')}>
        <div style="display: flex; justify-content: space-between; gap: 8px; align-items: baseline">
          <b>${nombre}</b><span class="nota mono">${rango}</span>
        </div>
        <label style="display: flex; gap: 8px; align-items: center">
          <input type="checkbox" id="t-distinto" checked=${!!exc} onChange=${(e) => poner(e.target.checked ? base : null)} />
          Este tramo distinto
        </label>
        ${exc ? (t.tipo === 'DentroDeViga'
          ? html`<${Dato} para="t-u" etiqueta="Un estribo cada" sub="En el nudo siempre es uniforme"><${Medida} id="t-u" valor=${c.espaciadoUniforme} onCambiar=${(x) => poner({ ...c, espaciadoUniforme: x })} /></${Dato}>`
          : html`
            <div class="segmentado" role="group" aria-label="Cómo se reparten los estribos en este tramo">
              <button type="button" aria-pressed=${c.modo === 'Uniforme' ? 'true' : 'false'} onClick=${() => poner({ ...c, modo: 'Uniforme' })}>Uniforme</button>
              <button type="button" aria-pressed=${c.modo === 'Confinado' ? 'true' : 'false'} onClick=${() => poner({ ...c, modo: 'Confinado' })}>Confinado</button>
            </div>
            ${c.modo === 'Uniforme'
              ? html`<${Dato} para="t-u" etiqueta="Un estribo cada"><${Medida} id="t-u" valor=${c.espaciadoUniforme} onCambiar=${(x) => poner({ ...c, espaciadoUniforme: x })} /></${Dato}>`
              : html`
                <${Dato} para="t-l1" etiqueta="Largo de cada punta"><${Medida} id="t-l1" valor=${c.longitudConfinamiento} onCambiar=${(x) => poner({ ...c, longitudConfinamiento: x })} /></${Dato}>
                <${Dato} para="t-c" etiqueta="En las puntas, cada"><${Medida} id="t-c" valor=${c.espaciadoConfinado} onCambiar=${(x) => poner({ ...c, espaciadoConfinado: x })} /></${Dato}>
                <${Dato} para="t-nc" etiqueta="En el centro, cada"><${Medida} id="t-nc" valor=${c.espaciadoNoConfinado} onCambiar=${(x) => poner({ ...c, espaciadoNoConfinado: x })} /></${Dato}>`}`)
        : html`<span class="nota">Hoy lleva lo de toda la columna.</span>`}
      </div>`;
    }

    pasoGrapas(v, col, o) {
      const s = this.state, piezas = o.longitudinal.estribosMenores, vistas = col.seccion.menores;
      const sel = s.pieza != null && piezas[s.pieza] ? { m: piezas[s.pieza], p: vistas[s.pieza] } : null;
      const donde = (m) => (m.forma === 'Cerrado'
        ? `${m.eje === 'Fila' ? 'filas' : 'columnas'} ${m.desde + 1} a ${m.hasta + 1}`
        : `${m.eje === 'Fila' ? 'fila' : 'columna'} ${m.desde + 1}`);
      return html`
        <div class="grupo">
          <h3>Pieza seleccionada</h3>
          ${sel ? html`<div class="pieza-panel">
            <div><b>${NOMBRE_FORMA[sel.m.forma]}</b> <span class="nota">· ${donde(sel.m)}</span></div>
            ${sel.p && !sel.p.valida ? html`<div class="aviso" style="border-color: #F1B0A8; background: #FDECEA">${sel.p.motivo}</div>` : null}
            <${Dato} para="p-var" etiqueta="# de varilla"><${Varilla} id="p-var" valor=${sel.m.tipoBarraNombre} conIgual=${true} opciones=${v.catalogos.barrasEstribo} onCambiar=${(n) => this.enviar({ tipo: 'menor', accion: 'varilla', indice: s.pieza, nombre: n })} /></${Dato}>
            ${sel.m.forma !== 'Cerrado' ? html`<p class="nota" style="margin: 0">El ángulo de cada gancho se elige en el dibujo, junto a cada punta.</p>` : null}
            <div class="acciones">
              ${sel.m.forma !== 'Cerrado' ? html`<button type="button" class="boton" onClick=${() => this.enviar({ tipo: 'menor', accion: 'voltear', indice: s.pieza })}>Voltear</button>` : null}
              <button type="button" class="boton" style="color: #B42318" onClick=${() => this.eliminarPieza()}>Eliminar (Supr)</button>
            </div>
          </div>` : html`<p class="nota" style="margin: 0">Ninguna. Haz clic en una pieza de la sección para seleccionarla.</p>`}
        </div>
        <div class="grupo">
          <h3>En esta columna <small>${piezas.filter((m) => m.forma !== 'Cerrado').length} grapa(s), ${piezas.filter((m) => m.forma === 'Cerrado').length} estribo(s) cerrado(s)</small></h3>
          ${piezas.length ? html`<div class="lista-piezas">${piezas.map((m, i) => html`<button type="button" aria-pressed=${s.pieza === i ? 'true' : 'false'} onClick=${() => this.setState({ pieza: s.pieza === i ? null : i, herramienta: null })}>
            <span>${NOMBRE_FORMA[m.forma]}</span><span class="nota">${donde(m)}${vistas[i] && !vistas[i].valida ? html` · <span style="color: #B42318">revisar</span>` : null}</span></button>`)}</div>`
          : html`<p class="nota" style="margin: 0">Sin grapas ni estribos cerrados. Se ponen en todos los tramos, junto con el estribo.</p>`}
        </div>`;
    }

    pasoTraslapos(v, col, o) {
      const L = o.longitudinal, cambiar = (c) => this.enviar({ tipo: 'cambiarLongitudinal', config: c });
      const T = col.traslapos;
      return html`
        <div class="grupo">
          <h3>Largo de barra comercial</h3>
          <div class="segmentado" role="group" aria-label="Largo de barra comercial">
            ${v.catalogos.largosComerciales.map((l) => html`<button type="button" aria-pressed=${L.longitudComercialBarra === l ? 'true' : 'false'} onClick=${() => cambiar({ longitudComercialBarra: l })}>${l} m</button>`)}
          </div>
          <p class="nota" style="margin: 0">El largo de la barra que se compra. Ninguna varilla cortada lo supera.</p>
          ${T.planes.some((p) => p.sinSolucion) ? html`<div class="aviso" role="status" style="border-color: #F1B0A8; background: #FDECEA">
            <span style="flex: 1">Con barras de ${L.longitudComercialBarra} m no se pueden cortar las varillas cumpliendo las reglas: ningún traslapo en un nudo, y A y B alternados.
              ${L.longitudComercialBarra < 12 ? html` <button type="button" class="enlace" onClick=${() => cambiar({ longitudComercialBarra: 12 })}>Usar 12 m</button>` : null}</span>
          </div>` : null}
        </div>
        <div class="grupo">
          <h3>Redondeo de varillas</h3>
          <${Dato} para="redondeo" etiqueta="Cada varilla mide un múltiplo de">
            <select id="redondeo" value=${String(L.redondeoVarillas)} onChange=${(e) => cambiar({ redondeoVarillas: parseFloat(e.target.value) })} style="height: 30px; padding: 0 6px; border: 1px solid #C9CFD6; border-radius: 6px">
              ${v.catalogos.redondeos.map((r) => html`<option value=${String(r)}>${f2(r)} m</option>`)}
            </select>
          </${Dato}>
          <p class="nota" style="margin: 0">Los traslapos se alargan lo necesario para que las medidas cierren.</p>
        </div>
        <div class="grupo">
          <h3>Solape mínimo</h3>
          <div class="segmentado" role="group" aria-label="Solape mínimo de los traslapos">
            <button type="button" aria-pressed=${!L.solapeMayorDeLosDos ? 'true' : 'false'} onClick=${() => cambiar({ solapeMayorDeLosDos: false })}>El de cada barra</button>
            <button type="button" aria-pressed=${L.solapeMayorDeLosDos ? 'true' : 'false'} onClick=${() => cambiar({ solapeMayorDeLosDos: true })}>El mayor de la columna</button>
          </div>
          <p class="nota" style="margin: 0">Sale del cuadro de varillas de la empresa. <button type="button" class="enlace" onClick=${() => this.setState({ cuadro: true })}>Ver el cuadro</button></p>
        </div>
        <div class="grupo">
          <h3>Cómo queda</h3>
          ${T.planes.map((p) => html`<div class="fila-dato">
            <span class="etq"><span class="varilla"><i style=${`background: ${p.color}`}></i><b>${p.nombre}</b> · ${p.barra} <span class="nota">×${p.cantidad}</span></span></span>
            <span class="mono">${p.sinSolucion ? html`<span style="color: #B42318">sin solución</span>` : p.varillas.map((x) => f2(x.largo)).join(' + ')}</span>
          </div>`)}
          <p class="nota" style="margin: 0">Las barras se alternan alrededor de la sección: una es A y la siguiente B. A traslapa en los tramos libres pares y B en los impares, así nunca traslapan todas a la misma altura. Cada número de varilla lleva su propio solape.</p>
        </div>`;
    }

    // ---- Diálogos dentro de la página

    dialogoChoques(v) {
      return html`<div class="velo" role="dialog" aria-modal="true" aria-labelledby="choques-titulo">
        <div class="dialogo">
          <h2 id="choques-titulo">Si lo generas así, ten en cuenta que:</h2>
          <ul>${v.generacion.choques.map((c) => html`<li>${c}</li>`)}</ul>
          <div class="pie">
            <button type="button" class="boton" onClick=${() => { this.enviar({ tipo: 'cancelarGenerar' }); this.setState({ paso: 3 }); }}>Volver a revisar</button>
            <button type="button" class="boton primario" onClick=${() => this.enviar({ tipo: 'generar', borrarAjeno: this.state.borrarAjeno, aunConChoques: true })}>Generar igual</button>
          </div>
        </div>
      </div>`;
    }

    dialogoCuadro(v, o) {
      const L = o.longitudinal;
      // En qué se usa cada varilla en esta columna, como VentanaCuadroVarillas.MarcarUsos.
      const usos = {};
      // Por número de varilla: en Revit los tipos se llaman «#3 COL» o «#3 CQEING», y el cuadro dice «#3».
      const usar = (nombre, en) => { const n = nombre && '#' + String(nombre).replace(/\D/g, ''); if (!n || n === '#') return; (usos[n] = usos[n] || []).includes(en) || usos[n].push(en); };
      usar(L.tipoBarraEsquinasNombre, 'esquinas');
      if (L.barrasPorCaraX) usar(L.tipoBarraCaraXNombre, 'caras X');
      if (L.barrasPorCaraY) usar(L.tipoBarraCaraYNombre, 'caras Y');
      usar(o.estribos.tramosLibres.tipoBarraNombre, 'estribos');
      L.estribosMenores.forEach((p) => usar(p.tipoBarraNombre || o.estribos.tramosLibres.tipoBarraNombre, 'grapas'));
      const m = (x, d = 3) => (x == null ? '—' : x.toFixed(d));
      const par = (a, b) => (a == null ? '—' : `${m(a)} / ${m(b)}`);
      return html`<div class="velo" role="dialog" aria-modal="true" aria-labelledby="cuadro-titulo" onClick=${(e) => { if (e.target === e.currentTarget) this.setState({ cuadro: false }); }}>
        <div class="dialogo ancho">
          <h2 id="cuadro-titulo">Cuadro de varillas</h2>
          <p class="nota" style="margin: 0">Cómo deberían quedar las barras según el cuadro de ganchos y traslapos de la empresa. Es el mismo cuadro con el que se genera el acero. En azul, las varillas que usa esta columna.</p>
          <div class="tabla-wrap"><table class="cuadro">
            <thead><tr><th>Varilla</th><th>Ø mm</th><th>Traslapo</th><th>Gancho 90°<br />B</th><th>Gancho 180°<br />E / D</th><th>Estribo 135°<br />E / D</th><th>kg/m</th></tr></thead>
            <tbody>${v.catalogos.cuadro.map((r) => html`<tr class=${usos[r.nombre] ? 'usada' : ''}>
              <td><span class="varilla"><i style=${`background: ${r.color}`}></i><b>${r.nombre}</b>${usos[r.nombre] ? html`<span style="color: #1F5FAF; font-family: var(--sans); font-size: 11px">${usos[r.nombre].join(', ')}</span>` : null}</span></td>
              <td>${r.mm.toFixed(1)}</td><td>${m(r.traslapo, 2)}</td><td>${m(r.b90)}</td><td>${par(r.e180, r.d180)}</td><td>${par(r.e135, r.d135)}</td><td>${m(r.kg)}</td>
            </tr>`)}</tbody>
          </table></div>
          <p class="nota" style="margin: 0">Valores en metros. B = pata del gancho de 90° de las longitudinales, medida por fuera del doblez (lo que se acota en el plano); E = largo recto del gancho; D = diámetro de doblado. «—»: el cuadro no lo da (la #2 no tiene traslapo en columnas; de la #6 a la #8 no hay estribo de 135°).</p>
          <div class="pie"><button type="button" class="boton" onClick=${() => this.setState({ cuadro: false })}>Cerrar</button></div>
        </div>
      </div>`;
    }
  }

  render(html`<${Ventana} />`, document.getElementById('tablero'));
})();
