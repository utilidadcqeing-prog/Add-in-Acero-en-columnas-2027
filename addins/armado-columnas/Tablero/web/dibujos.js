// Los dibujos de la ventana de columnas (#205): planta, sección, elevación de estribos y traslapos A/B. Solo dibujan lo
// que trae la vista (VistaColumnas); no calculan acero. Medidas de la vista: elevación en m, sección en cm.
(function () {
  'use strict';
  const { html } = window.htmPreact;

  const C = {
    tinta: '#16191D', suave: '#5E6670', linea: '#C9CFD6', borde: '#D9DDE2', fondo: '#FAFBFC', azul: '#1F5FAF',
    hormigon: '#EEF1F4', hormigonBorde: '#8A94A0', estribo: '#6B7480', viga: '#D3DBE4', vigaBorde: '#9AA6B3',
    confinada: '#1F5FAF', central: '#86A9D6', uniforme: '#4F7FBF', nudo: '#7D8A99', naranja: '#C2620A', rojo: '#B42318', verde: '#1E7A46',
  };
  const f2 = (x) => (Math.round(x * 100) / 100).toFixed(2);
  const ESTADO = {
    sinConfigurar: { relleno: '#FFFFFF', borde: '#8A94A0', texto: 'Sin configurar' },
    lista: { relleno: '#CFE0F5', borde: C.azul, texto: 'Lista para generar' },
    generada: { relleno: '#CDEBD8', borde: C.verde, texto: 'Ya generada' },
  };

  // ---- Planta: rejillas y columnas, para elegir

  function Planta({ planta, elegidas, compatibles, coincide, w, h, onElegir }) {
    const xs = planta.rejillas.flatMap((r) => [r.x1, r.x2]), ys = planta.rejillas.flatMap((r) => [r.y1, r.y2]);
    const x0 = Math.min(...xs), x1 = Math.max(...xs), y0 = Math.min(...ys), y1 = Math.max(...ys);
    const m = 22, k = Math.min((w - 2 * m) / (x1 - x0), (h - 2 * m) / (y1 - y0));
    const ox = (w - (x1 - x0) * k) / 2, oy = (h - (y1 - y0) * k) / 2;
    const X = (x) => ox + (x - x0) * k, Y = (y) => h - oy - (y - y0) * k;
    return html`
      <svg width=${w} height=${h} viewBox="0 0 ${w} ${h}" role="group" aria-label="Planta: haz clic en una columna para elegirla">
        ${planta.rejillas.map((r) => {
          const vertical = Math.abs(r.x1 - r.x2) < 1e-6;
          const bx = vertical ? X(r.x1) : X(r.x1) - 10, by = vertical ? Y(Math.max(r.y1, r.y2)) - 10 : Y(r.y1);
          return html`<g>
            <line x1=${X(r.x1)} y1=${Y(r.y1)} x2=${X(r.x2)} y2=${Y(r.y2)} stroke=${C.linea} stroke-width="1" stroke-dasharray="6 4"></line>
            <circle cx=${bx} cy=${by} r="9" fill="#FFFFFF" stroke=${C.suave} stroke-width="1"></circle>
            <text x=${bx} y=${by + 3.5} text-anchor="middle" font-size="10" fill=${C.suave} font-family="IBM Plex Sans, sans-serif">${r.nombre}</text>
          </g>`;
        })}
        ${planta.columnas.map((c) => {
          const e = ESTADO[c.estado], el = elegidas.includes(c.id), fuera = el && !compatibles.includes(c.id);
          const lx = Math.max(16, c.ladoX * k * 2.2), ly = Math.max(16, c.ladoY * k * 2.2);
          return html`<g class="planta-col" tabindex="0" role="button" aria-pressed=${el ? 'true' : 'false'}
              aria-label=${`${c.marca}${c.ubicacion && c.ubicacion !== c.marca ? ` (${c.ubicacion})` : ''}: ${e.texto}`}
              onClick=${(ev) => onElegir(c.id, ev.ctrlKey || ev.shiftKey || ev.metaKey)}
              onKeyDown=${(ev) => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); onElegir(c.id, ev.ctrlKey || ev.shiftKey); } }}>
            ${coincide && coincide.includes(c.id) ? html`<rect x=${X(c.x) - lx / 2 - 7} y=${Y(c.y) - ly / 2 - 7} width=${lx + 14} height=${ly + 14} rx="5" fill="#FFE9A8"></rect>` : null}
            ${el ? html`<rect x=${X(c.x) - lx / 2 - 4} y=${Y(c.y) - ly / 2 - 4} width=${lx + 8} height=${ly + 8} rx="3" fill="none" stroke=${fuera ? C.naranja : C.tinta} stroke-width="2" stroke-dasharray=${fuera ? '4 3' : null}></rect>` : null}
            <rect x=${X(c.x) - lx / 2} y=${Y(c.y) - ly / 2} width=${lx} height=${ly} fill=${e.relleno} stroke=${e.borde} stroke-width="1.6"></rect>
            ${c.estado === 'generada' ? html`<path d="M${X(c.x) - 4} ${Y(c.y)}l3 3 5-6" stroke=${C.verde} stroke-width="1.8" fill="none" stroke-linecap="round"></path>` : null}
            <text x=${X(c.x)} y=${Y(c.y) + ly / 2 + 15} text-anchor="middle" font-size="11" font-weight=${el ? 600 : 400} fill=${C.tinta} font-family="IBM Plex Sans, sans-serif">${c.ubicacion || c.marca}</text>
          </g>`;
        })}
      </svg>`;
  }

  // ---- Sección vista desde arriba (cm), en grande para los pasos Barras y Grapas

  function escalaSeccion(sec, w, h, margen) {
    const k = Math.min((w - 2 * margen) / sec.ancho, (h - 2 * margen) / sec.alto);
    const ox = (w - sec.ancho * k) / 2, oy = (h - sec.alto * k) / 2;
    return { k, X: (x) => ox + x * k, Y: (y) => oy + (sec.alto - y) * k, ox, oy };
  }

  function Seccion({ sec, w, h, modo, ocultarEsquinas, zonaTraslapo, seleccionada, fantasma, rotulos, onBarra, onPieza, onMover, onSoltar, chica }) {
    const margen = chica ? 18 : modo === 'grapas' ? 80 : 64;
    const { k, X, Y, ox, oy } = escalaSeccion(sec, w, h, margen);
    const E = sec.estribo, ge = Math.max(2, E.grosor * k);
    const radio = (b) => Math.max(chica ? 2.5 : 5, (b.diametroMm / 20) * k);
    const barrasVisibles = sec.barras.filter((b) => !(ocultarEsquinas && b.posicion === 'esquina'));
    const pts = (p) => p.map(([x, y]) => `${X(x)},${Y(y)}`).join(' ');
    const gancho = `M${X(E.x1) + 1} ${Y(E.y1 + 5)} l${3.6 * k} ${-3.6 * k} M${X(E.x1 + 5)} ${Y(E.y1) - 1} l${3.6 * k} ${-3.6 * k}`;
    const posicionEnSvg = (ev) => { const r = ev.currentTarget.getBoundingClientRect(); return { x: ev.clientX - r.left, y: ev.clientY - r.top }; };
    return html`
      <svg width=${w} height=${h} viewBox="0 0 ${w} ${h}" class=${modo === 'grapas' ? 'seccion editable' : 'seccion'}
          onPointerMove=${onMover ? (ev) => onMover(posicionEnSvg(ev), { X, Y, k }) : null}
          onPointerLeave=${onMover ? () => onMover(null) : null}
          onDragOver=${onSoltar ? (ev) => { ev.preventDefault(); onMover(posicionEnSvg(ev), { X, Y, k }); } : null}
          onDrop=${onSoltar ? (ev) => { ev.preventDefault(); onSoltar(ev.dataTransfer.getData('text/plain')); } : null}>
        <rect x=${ox} y=${oy} width=${sec.ancho * k} height=${sec.alto * k} fill=${C.hormigon} stroke=${C.hormigonBorde} stroke-width="1"></rect>
        ${E.puntos ? html`<polyline points=${pts(E.puntos)} fill="none" stroke=${C.estribo} stroke-width=${ge} stroke-linecap="round" stroke-linejoin="round"></polyline>` : html`
          <rect x=${X(E.x1)} y=${Y(E.y2)} width=${(E.x2 - E.x1) * k} height=${(E.y2 - E.y1) * k} rx=${Math.max(4, 2.2 * k)} fill="none" stroke=${C.estribo} stroke-width=${ge}></rect>
          <path d=${gancho} stroke=${C.estribo} stroke-width=${ge} stroke-linecap="round" fill="none"></path>`}
        ${sec.menores.filter((p) => p.puntos.length).map((p) => {
          const sel = seleccionada === p.indice, grosor = Math.max(2, (p.grosor || E.grosor) * k);
          return html`<g class="pieza" onClick=${onPieza ? (ev) => { ev.stopPropagation(); onPieza(p.indice); } : null}>
            ${sel ? html`<polyline points=${pts(p.puntos)} fill="none" stroke="#9EC1EE" stroke-width=${grosor + 7} stroke-linecap="round" stroke-linejoin="round"></polyline>` : null}
            <polyline points=${pts(p.puntos)} fill="none" stroke=${p.valida ? p.color : C.rojo} stroke-width=${grosor} stroke-linecap="round" stroke-linejoin="round" stroke-dasharray=${p.valida ? null : '5 3'}></polyline>
            <polyline points=${pts(p.puntos)} fill="none" stroke="transparent" stroke-width="14"></polyline>
          </g>`;
        })}
        ${fantasma ? html`<polyline points=${fantasma.map(([x, y]) => `${x},${y}`).join(' ')} fill="none" stroke=${C.azul} stroke-width=${Math.max(2, ge * 0.85)} stroke-opacity="0.45" stroke-linecap="round" stroke-linejoin="round"></polyline>` : null}
        ${barrasVisibles.map((b) => html`<g>
          ${zonaTraslapo ? html`<circle cx=${X(b.x) + radio(b) * 1.5} cy=${Y(b.y) - radio(b) * 0.6} r=${radio(b)} fill=${b.color} fill-opacity="0.45" stroke=${b.color}></circle>` : null}
          <circle cx=${X(b.x)} cy=${Y(b.y)} r=${radio(b)} fill=${b.color} stroke="#16191D" stroke-opacity="0.35" stroke-width="1"
            class=${onBarra && b.eje ? 'barra-clic' : null}
            onClick=${onBarra && b.eje ? (ev) => { ev.stopPropagation(); onBarra(b); } : null}></circle>
        </g>`)}
        ${chica ? null : html`
          <g font-family="IBM Plex Mono, monospace" font-size="11" fill=${C.suave}>
            <line x1=${ox} y1=${oy + sec.alto * k + 22} x2=${ox + sec.ancho * k} y2=${oy + sec.alto * k + 22} stroke=${C.suave}></line>
            <line x1=${ox} y1=${oy + sec.alto * k + 17} x2=${ox} y2=${oy + sec.alto * k + 27} stroke=${C.suave}></line>
            <line x1=${ox + sec.ancho * k} y1=${oy + sec.alto * k + 17} x2=${ox + sec.ancho * k} y2=${oy + sec.alto * k + 27} stroke=${C.suave}></line>
            <text x=${ox + (sec.ancho * k) / 2} y=${oy + sec.alto * k + 38} text-anchor="middle">${sec.ancho} cm</text>
            <line x1=${ox + sec.ancho * k + 22} y1=${oy} x2=${ox + sec.ancho * k + 22} y2=${oy + sec.alto * k} stroke=${C.suave}></line>
            <line x1=${ox + sec.ancho * k + 17} y1=${oy} x2=${ox + sec.ancho * k + 27} y2=${oy} stroke=${C.suave}></line>
            <line x1=${ox + sec.ancho * k + 17} y1=${oy + sec.alto * k} x2=${ox + sec.ancho * k + 27} y2=${oy + sec.alto * k} stroke=${C.suave}></line>
            <text x=${ox + sec.ancho * k + 34} y=${oy + (sec.alto * k) / 2 + 4}>${sec.alto} cm</text>
          </g>`}
        ${rotulos === 'filas' ? html`<g font-family="IBM Plex Sans, sans-serif" font-size="11" fill=${C.suave}>
          ${sec.barras.filter((b) => b.posicion === 'izquierda').map((b) => html`<text x=${ox - 12} y=${Y(b.y) + 4} text-anchor="end">F${b.indice + 1}</text>`)}
          ${sec.barras.filter((b) => b.posicion === 'abajo').map((b) => html`<text x=${X(b.x)} y=${oy + sec.alto * k + 54} text-anchor="middle">C${b.indice + 1}</text>`)}
        </g>` : null}
        ${rotulos === 'barras' ? RotulosBarras(sec, X, Y, ox, oy, k) : null}
      </svg>`;
  }

  // Rótulos del paso Barras: qué número de varilla va en cada lugar, afuera de la sección.
  function RotulosBarras(sec, X, Y, ox, oy, k) {
    // La esquina de arriba a la izquierda, la más cerca del rótulo: así la línea no cruza la sección.
    const esq = sec.barras.filter((b) => b.posicion === 'esquina').sort((a, b) => a.x - b.x || b.y - a.y)[0];
    const abajo = sec.barras.filter((b) => b.posicion === 'abajo'), izq = sec.barras.filter((b) => b.posicion === 'izquierda');
    const chip = (x, y, texto, color, ancla) => html`<g>
      <circle cx=${ancla === 'end' ? x + 6 : x - 6} cy=${y - 4} r="4" fill=${color}></circle>
      <text x=${x + (ancla === 'end' ? -2 : 2)} y=${y} text-anchor=${ancla} font-family="IBM Plex Mono, monospace" font-size="12" fill=${C.tinta}>${texto}</text>
    </g>`;
    return html`<g>
      ${esq ? html`<line x1=${X(esq.x)} y1=${Y(esq.y)} x2=${ox - 18} y2=${oy - 18} stroke=${C.suave} stroke-width="0.8"></line>
        ${chip(ox - 22, oy - 22, `4 esquinas ${esq.nombre}`, esq.color, 'end')}` : null}
      ${abajo.length ? html`<line x1=${X(abajo[0].x)} y1=${Y(abajo[0].y)} x2=${X(abajo[0].x)} y2=${oy - 18} stroke=${C.suave} stroke-width="0.8" stroke-dasharray="2 2"></line>
        ${chip(X(abajo[0].x) + 12, oy - 22, `caras X: ${abajo.length} + ${abajo.length} ${abajo[0].nombre}`, abajo[0].color, 'start')}` : null}
      ${izq.length ? html`<line x1=${X(izq[0].x)} y1=${Y(izq[0].y)} x2=${ox - 18} y2=${Y(izq[0].y)} stroke=${C.suave} stroke-width="0.8" stroke-dasharray="2 2"></line>
        ${chip(ox - 22, Y(izq[0].y) + 4, `caras Y: ${izq.length} + ${izq.length} ${izq[0].nombre}`, izq[0].color, 'end')}` : null}
    </g>`;
  }

  // ---- Elevación de la columna entera con sus estribos (paso Estribos)

  const ZONA = {
    confinada: { color: C.confinada, fondo: '#D3E2F6' },
    central: { color: C.central, fondo: '#FFFFFF' },
    uniforme: { color: C.uniforme, fondo: '#EDF2F9' },
    nudo: { color: C.nudo, fondo: '#E6EAEF' },
  };

  function rango(col) {
    // Desde abajo de la columna, no desde la cota 0: en Revit una columna puede arrancar en cualquier nivel.
    const base = col.tramos.length ? col.tramos[0].zInf : 0;
    const zMin = Math.min(col.zapata ? col.zapata.zInf : base, col.zInferiorLongitudinal, base);
    const zMax = Math.max(col.zSuperior, ...col.niveles.map((n) => n.z));
    return { zMin: zMin - 0.15, zMax: zMax + 0.15 };
  }

  function Elevacion({ col, w, h, seleccionado, onTramo, conEstribos }) {
    const { zMin, zMax } = rango(col);
    const k = (h - 24) / (zMax - zMin);
    const Z = (z) => 12 + (zMax - z) * k;
    const cw = 54, cx = Math.round(w * 0.36), x0 = cx - cw / 2, x1 = cx + cw / 2;
    const largoViga = Math.min(80, cx - 90);
    const libres = col.tramos.filter((t) => t.tipo === 'Libre');
    const zonasDe = (t) => col.zonasEstribos.filter((z) => z.indice === t.indice);
    const etiqueta = (t) => {
      const zs = zonasDe(t);
      if (t.tipo === 'ContraZapata') return { texto: 'sin estribos', sub: 'contra la zapata', color: C.suave };
      if (t.tipo === 'DentroDeViga') return { texto: `nudo · cada ${f2(zs[0].cada)}`, sub: null, color: C.nudo };
      const conf = zs.find((z) => z.zona === 'confinada'), cen = zs.find((z) => z.zona === 'central');
      if (!conf) return { texto: `cada ${f2(zs[0].cada)}`, sub: `${zs[0].cantidad} estribos`, color: C.uniforme };
      return { texto: `puntas ${f2(conf.cada)} · centro ${cen ? f2(cen.cada) : '—'}`, sub: `L1 ${f2(conf.zSup - conf.zInf)} m en cada punta`, color: C.confinada };
    };
    const xs = x1 + (col.tramos.some((t) => t.tipo === 'DentroDeViga') ? largoViga : 0) + 18;
    return html`
      <svg width=${w} height=${h} viewBox="0 0 ${w} ${h}" role="group" aria-label="Elevación de la columna: haz clic en un tramo para darle otros valores">
        <defs>
          <pattern id="rayado" width="6" height="6" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><line x1="0" y1="0" x2="0" y2="6" stroke="#B9C2CC" stroke-width="1.2"></line></pattern>
        </defs>
        ${col.niveles.map((n) => html`<g font-family="IBM Plex Mono, monospace" font-size="11" fill=${C.suave}>
          <line x1="8" y1=${Z(n.z)} x2=${x0 - 6} y2=${Z(n.z)} stroke=${C.linea} stroke-dasharray="3 3"></line>
          <text x="8" y=${Z(n.z) - 4}>${n.nombre} · N+${f2(n.z)}</text>
        </g>`)}
        ${col.zapata ? html`<rect x=${cx - Math.max(cw * 1.4, (col.zapata.ancho * k) / 2)} y=${Z(col.zapata.zSup)} width=${Math.max(cw * 2.8, col.zapata.ancho * k)} height=${(col.zapata.zSup - col.zapata.zInf) * k} fill="url(#rayado)" stroke=${C.hormigonBorde}></rect>
          <text x=${cx - Math.max(cw * 1.4, (col.zapata.ancho * k) / 2) - 8} y=${Z((col.zapata.zSup + col.zapata.zInf) / 2) + 4} text-anchor="end" font-size="12" fill=${C.suave} font-family="IBM Plex Sans, sans-serif">Zapata</text>` : null}
        ${col.tramos.map((t) => {
          const y = Z(t.zSup), alto = (t.zSup - t.zInf) * k;
          if (t.tipo === 'DentroDeViga') return html`<g>
            <rect x=${x0 - largoViga} y=${y} width=${largoViga * 2 + cw} height=${alto} fill=${C.viga} stroke=${C.vigaBorde}></rect>
            <text x=${x0 - largoViga + 4} y=${y + alto / 2 + 4} font-size="10" fill=${C.suave} font-family="IBM Plex Sans, sans-serif">viga</text>
          </g>`;
          return html`<rect x=${x0} y=${y} width=${cw} height=${alto} fill=${t.tipo === 'ContraZapata' ? 'url(#rayado)' : '#FFFFFF'} stroke=${C.hormigonBorde}></rect>`;
        })}
        ${conEstribos ? (() => {
          // Con poca altura, los estribos reales quedarían a menos de 3 px y se verían como una banda llena: se dibujan
          // todos más separados, en la misma proporción, para que las puntas confinadas se sigan viendo más juntas.
          const conCada = col.zonasEstribos.filter((z) => z.cada);
          const minimo = Math.min(...conCada.map((z) => z.cada * k));
          const f = Math.max(1, 3.2 / minimo);
          return conCada.map((z) => {
            const zz = ZONA[z.zona], paso = z.cada * k * f, lineas = [];
            for (let yy = Z(z.zInf) - paso / 2; yy > Z(z.zSup) + 0.5; yy -= paso) lineas.push(yy);
            return html`<g>
              <rect x=${x0 + 1} y=${Z(z.zSup)} width=${cw - 2} height=${(z.zSup - z.zInf) * k} fill=${zz.fondo}></rect>
              ${lineas.map((yy) => html`<line x1=${x0 + 5} y1=${yy} x2=${x1 - 5} y2=${yy} stroke=${zz.color} stroke-width=${z.zona === 'confinada' ? 1.4 : 1.2}></line>`)}
              ${z.zona === 'central' ? html`
                <line x1=${x0 - 7} y1=${Z(z.zInf)} x2=${x1 + 7} y2=${Z(z.zInf)} stroke=${C.confinada} stroke-width="1" stroke-dasharray="3 2"></line>
                <line x1=${x0 - 7} y1=${Z(z.zSup)} x2=${x1 + 7} y2=${Z(z.zSup)} stroke=${C.confinada} stroke-width="1" stroke-dasharray="3 2"></line>` : null}
            </g>`;
          });
        })() : null}
        <line x1=${x0 + 8} y1=${Z(col.zInferiorLongitudinal)} x2=${x0 + 8} y2=${Z(col.zSuperior - col.recubrimiento)} stroke=${col.seccion.barras[0].color} stroke-width="2.4"></line>
        <line x1=${x1 - 8} y1=${Z(col.zInferiorLongitudinal)} x2=${x1 - 8} y2=${Z(col.zSuperior - col.recubrimiento)} stroke=${col.seccion.barras[0].color} stroke-width="2.4"></line>
        ${col.tramos.map((t) => {
          const y = Z(t.zSup), alto = (t.zSup - t.zInf) * k, sel = seleccionado === t.indice, e = etiqueta(t);
          const mid = y + alto / 2;
          return html`<g class="tramo-clic" tabindex=${onTramo ? 0 : -1} role=${onTramo ? 'button' : null}
              aria-label=${`Tramo ${t.indice + 1}: ${e.texto}`} aria-pressed=${sel ? 'true' : 'false'}
              onClick=${onTramo ? () => onTramo(t.indice) : null}
              onKeyDown=${onTramo ? (ev) => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); onTramo(t.indice); } } : null}>
            <rect x=${x0 - 3} y=${y} width=${cw + 6} height=${Math.max(alto, 6)} fill="transparent" stroke=${sel ? C.azul : t.excepcion ? C.naranja : 'transparent'} stroke-width=${sel ? 2.5 : 2} stroke-dasharray=${t.excepcion && !sel ? '4 3' : null}></rect>
            ${conEstribos && (t.tipo !== 'DentroDeViga' || alto > 13) ? html`<g font-family="IBM Plex Mono, monospace">
              <line x1=${x1 + (t.tipo === 'DentroDeViga' ? largoViga : 4)} y1=${mid} x2=${xs - 4} y2=${mid} stroke=${C.linea}></line>
              <text x=${xs} y=${mid + (e.sub && alto > 34 ? -2 : 4)} font-size="12" fill=${e.color} font-weight=${sel ? 600 : 500}>${e.texto}${t.excepcion ? html`<tspan fill=${C.naranja} font-family="IBM Plex Sans, sans-serif" font-weight="600">  · distinto</tspan>` : null}</text>
              ${e.sub && alto > 34 ? html`<text x=${xs} y=${mid + 13} font-size="10.5" fill=${C.suave} font-family="IBM Plex Sans, sans-serif">${e.sub}</text>` : null}
            </g>` : null}
          </g>`;
        })}
        ${libres.length === 0 ? null : html`<text x=${cx} y=${Z(col.zSuperior) - 6} text-anchor="middle" font-size="11" fill=${C.suave} font-family="IBM Plex Sans, sans-serif">${col.marca}</text>`}
      </svg>`;
  }

  // ---- Traslapos: la columna con las varillas A y B, sus largos y dónde traslapan (paso Traslapos)

  function Traslapos({ col, w, h }) {
    const { zMin, zMax } = rango(col);
    const k = (h - 56) / (zMax - zMin);
    const Z = (z) => 44 + (zMax - z) * k;
    const cw = 34, cx = 150, x0 = cx - cw / 2;
    const T = col.traslapos;
    // Un carril por plan (grupo y número de varilla), repartidos en el ancho que queda a la derecha de la columna.
    const ancho = Math.max(128, (w - 250) / T.planes.length);
    const W = Math.max(w, 250 + T.planes.length * ancho);
    const carril = (p, i) => {
      const xc = 250 + i * ancho + 50;
      return html`<g>
        <text x=${xc - 6} y="16" font-size="13" font-weight="600" fill=${C.tinta} font-family="IBM Plex Sans, sans-serif">${p.nombre}</text>
        <circle cx=${xc + 16} cy="12" r="4.5" fill=${p.color}></circle>
        <text x=${xc + 25} y="16" font-size="12" fill=${C.suave} font-family="IBM Plex Mono, monospace">${p.barra} ×${p.cantidad}</text>
        <text x=${xc - 6} y="30" font-size="10.5" fill=${C.suave} font-family="IBM Plex Sans, sans-serif">solape ${f2(p.solape)}${p.enTramosDelOtro ? ' · en tramos del otro' : ''}</text>
        ${p.varillas.map((v, j) => {
          const x = xc + (j % 2) * 9;
          return html`<g>
            <line x1=${x} y1=${Z(v.zInf)} x2=${x} y2=${Z(v.zSup)} stroke=${p.color} stroke-width="4" stroke-linecap="round"></line>
            <text x=${x + 14} y=${Z((v.zInf + v.zSup) / 2) + 4} font-size="12" fill=${C.tinta} font-family="IBM Plex Mono, monospace">${f2(v.largo)} m</text>
          </g>`;
        })}
        ${p.traslapos.map((t) => html`<g>
          <rect x=${xc - 8} y=${Z(t.zSup)} width="25" height=${Math.max(3, (t.zSup - t.zInf) * k)} fill=${p.color} fill-opacity="0.16" stroke=${p.color} stroke-dasharray="3 2"></rect>
          <text x=${xc - 12} y=${Z((t.zInf + t.zSup) / 2) + 4} text-anchor="end" font-size="11" fill=${C.suave} font-family="IBM Plex Mono, monospace">↕ ${f2(t.largo)}</text>
        </g>`)}
        ${p.sinSolucion ? html`<text x=${xc - 6} y=${h - 6} font-size="12" fill=${C.rojo} font-family="IBM Plex Sans, sans-serif">Sin solución con este largo comercial</text>` : null}
      </g>`;
    };
    return html`
      <svg width=${W} height=${h} viewBox="0 0 ${W} ${h}" role="img" aria-label="Varillas A y B de la columna con sus largos y traslapos">
        ${col.niveles.map((n) => html`<g font-family="IBM Plex Mono, monospace" font-size="11" fill=${C.suave}>
          <line x1="8" y1=${Z(n.z)} x2=${W - 8} y2=${Z(n.z)} stroke="#E4E8EC"></line>
          <text x="8" y=${Z(n.z) - 4}>${n.nombre} · N+${f2(n.z)}</text>
        </g>`)}
        ${col.zapata ? html`<rect x=${cx - 40} y=${Z(col.zapata.zSup)} width="80" height=${(col.zapata.zSup - col.zapata.zInf) * k} fill="#E9EDF1" stroke=${C.hormigonBorde}></rect>` : null}
        ${col.tramos.map((t) => html`<rect x=${t.tipo === 'DentroDeViga' ? x0 - 30 : x0} y=${Z(t.zSup)} width=${t.tipo === 'DentroDeViga' ? cw + 60 : cw} height=${(t.zSup - t.zInf) * k}
            fill=${t.tipo === 'DentroDeViga' ? C.viga : '#FFFFFF'} stroke=${t.tipo === 'DentroDeViga' ? C.vigaBorde : C.hormigonBorde}></rect>`)}
        ${T.planes.map(carril)}
      </svg>`;
  }

  window.DibujosColumnas = { Planta, Seccion, Elevacion, Traslapos, escalaSeccion, ESTADO, C };
})();
