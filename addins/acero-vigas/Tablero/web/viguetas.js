// «Crear viguetas» (#947). La página dibuja la planta del piso y pide; los paneles y el reparto (fórmula del Excel de
// Carlos) se calculan en C# (Calculo/Viguetas). Mensajes: ProtocoloViguetas.cs. Medidas en mm; en pantalla, en m.
(() => {
  const $ = (id) => document.getElementById(id);
  const CLAVE = 'cqeing-viguetas';
  const CHK = '<svg viewBox="0 0 12 12" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-label="Listo"><path d="M2.6 6.3l2.2 2.2 4.6-4.9"/></svg>';
  const ICO_BORRAR = '<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"><path d="M3 4.5h10M6.5 4.5V3h3v1.5M4.5 4.5l.6 8.5h5.8l.6-8.5M7 7v4M9 7v4"/></svg>';

  const S = {
    doc: '', pisos: [], tipos: [], piso: null, comoBorde: false, panel: null,
    dir: 'X', sep: '0,80', anc: '0,10', tipo: '', borradas: [], editar: false, propuesta: null, vista: null,
  };
  try {
    const m = JSON.parse(localStorage.getItem(CLAVE) || 'null');
    if (m) Object.assign(S, { sep: m.sep || S.sep, anc: m.anc || S.anc, tipo: m.tipo || '', dir: m.dir || S.dir });
  } catch (e) { /* sin almacenamiento se usan los valores por defecto */ }
  const guardar = () => { try { localStorage.setItem(CLAVE, JSON.stringify({ sep: S.sep, anc: S.anc, tipo: S.tipo, dir: S.dir })); } catch (e) { /* nada */ } };

  const fmt = (mm) => (mm / 1000).toFixed(2).replace('.', ',');
  const num = (s) => { const t = String(s).trim(); const v = Number(t.replace(',', '.')); return t !== '' && isFinite(v) ? v : NaN; };
  const esc = (t) => String(t ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  let enviar = () => {};

  function valores() {
    const s = num(S.sep), b = num(S.anc), e = {};
    if (!(s > 0)) e.sep = 'Escribe un número mayor que 0';
    if (!(b > 0)) e.anc = 'Escribe un número mayor que 0';
    if (!e.sep && !e.anc && b >= s) e.anc = 'Debe ser menor que la separación';
    return { s: Math.round(s * 1000), b: Math.round(b * 1000), e, ok: !e.sep && !e.anc };
  }
  const panelSel = () => (S.piso && S.panel ? S.piso.paneles.find((p) => p.id === S.panel) || null : null);
  const caja = (pts) => {
    const xs = pts.map((p) => p[0]), ys = pts.map((p) => p[1]);
    return { x0: Math.min(...xs), x1: Math.max(...xs), y0: Math.min(...ys), y1: Math.max(...ys) };
  };
  const vivas = () => (S.propuesta ? S.propuesta.viguetas.filter((v) => !v.borrada) : []);

  /* ---------- mensajes ---------- */
  let tPedir;
  function pedir() {
    clearTimeout(tPedir);
    const V = valores();
    if (!panelSel() || !V.ok) { S.propuesta = null; pintar(); return; }
    tPedir = setTimeout(() => enviar({ tipo: 'propuesta', ...pedido() }), 120);
  }
  function pedido() {
    const V = valores();
    return { panel: S.panel, direccion: S.dir, ejeAEje: V.s, ancho: V.b, borradas: S.borradas };
  }
  function abrirPiso(id) { enviar({ tipo: 'piso', id, viguetasComoBorde: S.comoBorde }); }

  function recibir(m) {
    if (m.tipo === 'pisos') {
      S.doc = m.documento; S.pisos = m.pisos; S.tipos = m.tipos;
      if (!S.tipos.some((t) => t.id === S.tipo)) S.tipo = (S.tipos.find((t) => t.sugerido) || S.tipos[0] || {}).id || '';
      modal();
      todo();
    } else if (m.tipo === 'piso') {
      const mismo = S.piso && S.piso.id === m.id;
      S.piso = m; S.comoBorde = m.viguetasComoBorde;
      if (!mismo) S.vista = null;
      if (!mismo || !m.paneles.some((p) => p.id === S.panel)) { S.panel = null; S.borradas = []; S.propuesta = null; S.editar = false; }
      $('modal').hidden = true;
      todo();
      if (S.panel) pedir();
    } else if (m.tipo === 'propuesta') {
      if (m.panel === S.panel) { S.propuesta = m.propuesta; pintar(); }
    } else if (m.tipo === 'aviso') {
      aviso(m.texto, m.bien);
    }
  }

  /* ---------- primera entrada ---------- */
  function modal() {
    $('m-pisos').innerHTML = S.pisos.length
      ? S.pisos.map((p) => `<button class="piso" data-piso="${esc(p.id)}"><span class="lv">${esc(p.nombre)}</span><span>${fmt(p.cota)} m</span><span class="pc">${p.armazon} vigas y viguetas</span></button>`).join('')
      : '<p>Este modelo no tiene vigas con nivel de referencia.</p>';
    $('modal').hidden = !!S.piso;
  }
  $('m-pisos').addEventListener('click', (e) => { const b = e.target.closest('[data-piso]'); if (b) abrirPiso(b.dataset.piso); });

  /* ---------- barra lateral ---------- */
  function lateral() {
    const pan = panelSel(), V = valores();
    const n = (k, ok, extra = '') => `<span class="n ${ok ? 'done' : ''}" ${extra}>${ok ? CHK : k}</span>`;
    const campo = (id, k, lab) => `<label class="fld ${V.e[k] ? 'bad' : ''}" id="w-${k}" for="${id}"><span class="fl">${lab}</span><span class="inp"><input id="${id}" inputmode="decimal" autocomplete="off" value="${esc(S[k])}"><em>m</em></span><span class="err" id="e-${k}">${V.e[k] || ''}</span></label>`;
    let infoPanel = '<span class="info">Haz clic en un panel de la planta.</span>';
    if (pan) {
      const c = caja(pan.libre);
      infoPanel = `<span class="info"><b>${esc(pan.id)}</b> · libre <b>${fmt(c.x1 - c.x0)} × ${fmt(c.y1 - c.y0)}</b> m</span>`
        + (pan.creadas ? `<span class="info">Ya tiene <b>${pan.creadas}</b> viguetas creadas por CQEING.</span>` : '');
    }
    $('side').innerHTML = `
      <div class="sec"><div class="sh">${n(1, !!S.piso)}Piso</div>
        <select class="sel" id="f-piso" aria-label="Piso">${S.piso ? '' : '<option value="">Elige un piso</option>'}${S.pisos.map((p) => `<option value="${esc(p.id)}" ${S.piso && S.piso.id === p.id ? 'selected' : ''}>${esc(p.nombre)}</option>`).join('')}</select>
        <label class="chk" title="Las viguetas que ya están en el modelo también cierran paneles (viguetas de borde)"><input type="checkbox" id="f-borde" ${S.comoBorde ? 'checked' : ''} ${S.piso ? '' : 'disabled'}>Usar las viguetas del modelo como borde</label></div>
      <div class="sec ${S.piso ? '' : 'off'}"><div class="sh">${n(2, !!pan)}Panel</div>${infoPanel}
        <button class="ib ${S.editar ? 'on' : ''}" id="b-editar" data-act="editar" ${S.propuesta ? '' : 'disabled'} aria-pressed="${S.editar}" title="Clic sobre una vigueta para quitarla de la propuesta">${ICO_BORRAR}Eliminar viguetas${S.borradas.length ? `<span class="k">${S.borradas.length}</span>` : ''}</button></div>
      <div class="sec ${pan ? '' : 'off'}"><div class="sh">${n(3, !!pan)}Dirección</div>
        <div class="dirs">${[['X', '↔', 'Viguetas paralelas a X'], ['Y', '↕', 'Viguetas paralelas a Y']].map(([v, a, t]) => `<button class="dirbtn" data-act="dir" data-v="${v}" aria-pressed="${S.dir === v}" title="${t}"><span class="arr">${a}</span><b>${v}</b></button>`).join('')}</div></div>
      <div class="sec ${pan ? '' : 'off'}"><div class="sh">${n(4, !!pan && V.ok, 'id="n4"')}Separación</div>
        <div class="campos">${campo('f-sep', 'sep', 'Eje a eje')}${campo('f-anc', 'anc', 'Ancho de vigueta')}</div>
        <label class="fld" for="f-tipo"><span class="fl">Tipo de vigueta en Revit</span>
          <select class="sel" id="f-tipo">${S.tipos.map((t) => `<option value="${esc(t.id)}" ${t.id === S.tipo ? 'selected' : ''}>${esc(t.nombre)}</option>`).join('')}</select></label></div>
      <div class="sum" id="sum"></div>`;
  }

  function resumen() {
    const pan = panelSel(), p = S.propuesta;
    const f = (k, v) => `<dt>${k}</dt><dd>${v}</dd>`;
    $('sum').innerHTML = `<div class="sum-h">Resumen</div><dl>${f('Piso', S.piso ? esc(S.piso.nombre) : '—')}${f('Paneles', S.piso ? S.piso.paneles.length : '—')}${f('Panel', pan ? esc(pan.id) : '—')}${f('Viguetas', p ? vivas().length : '—')}${f('Tipos', p ? p.tipos.length : '—')}${f('Ya creadas', pan ? pan.creadas : '—')}</dl>`;
  }

  $('side').addEventListener('click', (e) => {
    const b = e.target.closest('[data-act]'); if (!b || b.disabled) return;
    if (b.dataset.act === 'editar') { S.editar = !S.editar; }
    if (b.dataset.act === 'dir' && S.dir !== b.dataset.v) { S.dir = b.dataset.v; S.borradas = []; S.editar = false; guardar(); pedir(); }
    lateral(); pintar();
  });
  $('side').addEventListener('change', (e) => {
    if (e.target.id === 'f-piso' && e.target.value) abrirPiso(e.target.value);
    if (e.target.id === 'f-borde') { S.comoBorde = e.target.checked; abrirPiso(S.piso.id); }
    if (e.target.id === 'f-tipo') { S.tipo = e.target.value; guardar(); pie(); }
  });
  $('side').addEventListener('input', (e) => {
    const k = { 'f-sep': 'sep', 'f-anc': 'anc' }[e.target.id]; if (!k) return;
    S[k] = e.target.value; S.borradas = [];
    const V = valores();
    ['sep', 'anc'].forEach((x) => { $('w-' + x).classList.toggle('bad', !!V.e[x]); $('e-' + x).textContent = V.e[x] || ''; });
    $('n4').classList.toggle('done', V.ok); $('n4').innerHTML = V.ok ? CHK : '4';
    if (V.ok) guardar();
    pedir();
  });

  /* ---------- dibujo de la planta ---------- */
  const pv = $('pv');
  function encuadre(c, margen) {
    const w0 = (c.x1 - c.x0) * (1 + margen * 2) || 1000, h0 = (c.y1 - c.y0) * (1 + margen * 2) || 1000;
    const r = (pv.clientWidth || 1) / (pv.clientHeight || 1);
    const w = Math.max(w0, h0 * r), h = w / r;
    return { x: (c.x0 + c.x1) / 2 - w / 2, y: -(c.y0 + c.y1) / 2 - h / 2, w, h };
  }
  function cajaPiso() {
    const pts = [];
    S.piso.armazon.forEach((a) => pts.push(a.desde, a.hasta));
    S.piso.columnas.forEach((c) => pts.push(c.min, c.max));
    return pts.length ? caja(pts) : { x0: 0, x1: 1000, y0: 0, y1: 1000 };
  }
  const pt = (p) => `${p[0].toFixed(1)},${(-p[1]).toFixed(1)}`;
  function barra(a, b, ancho, attrs) {
    const dx = b[0] - a[0], dy = b[1] - a[1], l = Math.hypot(dx, dy) || 1, nx = -dy / l * ancho / 2, ny = dx / l * ancho / 2;
    return `<polygon points="${pt([a[0] + nx, a[1] + ny])} ${pt([b[0] + nx, b[1] + ny])} ${pt([b[0] - nx, b[1] - ny])} ${pt([a[0] - nx, a[1] - ny])}" ${attrs}/>`;
  }

  function dibujo() {
    $('vacio').hidden = !!S.piso;
    if (!S.piso) { pv.querySelector('svg')?.remove(); return; }
    if (!S.vista) S.vista = encuadre(cajaPiso(), 0.1);
    const v = S.vista, fs = v.w / 80, pan = panelSel(), p = S.propuesta;
    const ns = 'vector-effect="non-scaling-stroke"';
    let h = `<svg viewBox="${v.x} ${v.y} ${v.w} ${v.h}" class="${S.editar ? 'editando' : ''}" role="img" aria-label="Planta del piso">`;

    S.piso.paneles.forEach((x) => {
      h += `<polygon class="pn ${x.id === S.panel ? 'sel' : ''} ${x.creadas ? 'con' : ''}" data-panel="${esc(x.id)}" points="${x.libre.map(pt).join(' ')}" ${ns}><title>${esc(x.id)}${x.creadas ? ` · ${x.creadas} viguetas creadas` : ''}</title></polygon>`;
    });
    S.piso.armazon.forEach((a) => {
      const cls = a.creada ? 'creada' : a.esVigueta ? 'existente' : 'viga';
      h += barra(a.desde, a.hasta, a.ancho, `class="${cls}"`).replace('/>', `><title>${esc(a.tipo || '')}</title></polygon>`);
    });
    S.piso.columnas.forEach((c) => { h += `<rect class="colm" x="${c.min[0]}" y="${-c.max[1]}" width="${c.max[0] - c.min[0]}" height="${c.max[1] - c.min[1]}"/>`; });

    if (pan && p) {
      p.viguetas.forEach((g) => {
        h += g.borrada
          ? barra(g.desde, g.hasta, p.ancho, `class="ghost" data-v="${esc(g.id)}" ${ns}`).replace('/>', '><title>Eliminada · clic para restaurar</title></polygon>')
          : barra(g.desde, g.hasta, p.ancho, `class="vg" data-v="${esc(g.id)}"`).replace('/>', `><title>Vigueta ${esc(g.id)} · Tipo ${g.tipo} · ${fmt(g.largo)} m</title></polygon>`);
        if (!g.borrada && fs < 260) {
          const enX = p.direccion === 'X';
          h += `<text class="vt halo" x="${g.desde[0] + (enX ? fs * 0.4 : p.ancho)}" y="${-g.desde[1] - (enX ? p.ancho : fs * 0.4)}" font-size="${fs * 0.62}" stroke-width="${fs * 0.25}">T${g.tipo}</text>`;
        }
      });
      h += cotas(pan, p, fs);
    }
    S.piso.paneles.forEach((x) => {
      const c = caja(x.libre), cx = (c.x0 + c.x1) / 2, cy = -(c.y0 + c.y1) / 2;
      const chico = Math.min(c.x1 - c.x0, c.y1 - c.y0) < fs * 3;
      if (x.id === S.panel && p) return;
      h += `<text class="lbl halo" x="${cx}" y="${cy}" text-anchor="middle" font-size="${fs * (chico ? 0.6 : 0.95)}" stroke-width="${fs * 0.3}">${esc(x.id)}</text>`;
      if (x.creadas && !chico) h += `<text class="lbl2 halo" x="${cx}" y="${cy + fs * 1.1}" text-anchor="middle" font-size="${fs * 0.6}" stroke-width="${fs * 0.25}">${x.creadas} creadas</text>`;
    });
    h += '</svg>';
    pv.querySelector('svg')?.remove();
    pv.insertAdjacentHTML('beforeend', h);
  }

  /** Cotas del panel elegido: libre en los extremos, eje a eje y el total, como en la maqueta. */
  function cotas(pan, p, fs) {
    const c = caja(pan.libre), b = p.ancho, enY = p.direccion === 'X';
    const ejes = [...new Set(p.viguetas.map((g) => g.cota))].sort((x, y) => x - y);
    const desde = enY ? c.y0 : c.x0, hasta = enY ? c.y1 : c.x1;
    const tramos = [[desde, ejes[0] - b / 2, p.libreExtremos, 'lib']];
    for (let i = 0; i + 1 < ejes.length; i++) tramos.push([ejes[i], ejes[i + 1], p.ejeAEje, 'eje']);
    tramos.push([ejes[ejes.length - 1] + b / 2, hasta, p.libreExtremos, 'lib']);
    const t = fs * 0.3, d1 = fs * 2, d2 = fs * 4;
    const ln = (x1, y1, x2, y2) => `<line x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}" vector-effect="non-scaling-stroke"/>`;
    const txt = (x, y, s, rot) => `<text x="${x}" y="${y}" text-anchor="middle" font-size="${fs * 0.72}" class="halo" stroke-width="${fs * 0.25}" ${rot ? `transform="rotate(-90 ${x} ${y})"` : ''}>${s}</text>`;
    let h = '';
    const cota = (a, z, lab, cls, off) => {
      if (enY) {
        const x = c.x0 - off, y1 = -z, y2 = -a;
        h += `<g class="dim ${cls}">${ln(x, y1, x, y2)}${ln(x - t, y1 + t, x + t, y1 - t)}${ln(x - t, y2 + t, x + t, y2 - t)}${txt(x - fs * 0.45, (y1 + y2) / 2, lab, true)}</g>`;
      } else {
        const y = -c.y1 - off;
        h += `<g class="dim ${cls}">${ln(a, y, z, y)}${ln(a - t, y + t, a + t, y - t)}${ln(z - t, y + t, z + t, y - t)}${txt((a + z) / 2, y - fs * 0.45, lab, false)}</g>`;
      }
    };
    tramos.forEach(([a, z, val, cls]) => cota(a, z, fmt(val), cls, d1));
    cota(desde, hasta, `${fmt(hasta - desde)} m`, 'tot', d2);
    return h;
  }

  /* ---------- acercar, mover y elegir ---------- */
  pv.addEventListener('wheel', (e) => {
    if (!S.vista) return;
    e.preventDefault();
    const r = pv.getBoundingClientRect(), f = e.deltaY > 0 ? 1.15 : 1 / 1.15, v = S.vista;
    const px = v.x + (e.clientX - r.left) / r.width * v.w, py = v.y + (e.clientY - r.top) / r.height * v.h;
    S.vista = { x: px - (px - v.x) * f, y: py - (py - v.y) * f, w: v.w * f, h: v.h * f };
    dibujo();
  }, { passive: false });

  let arrastre = null;
  pv.addEventListener('mousedown', (e) => { if (S.vista && e.button === 0) arrastre = { x: e.clientX, y: e.clientY, v: S.vista, movio: false }; });
  window.addEventListener('mousemove', (e) => {
    if (!arrastre) return;
    const dx = e.clientX - arrastre.x, dy = e.clientY - arrastre.y;
    if (!arrastre.movio && Math.hypot(dx, dy) < 4) return;
    arrastre.movio = true; pv.classList.add('arrastre');
    const r = pv.getBoundingClientRect(), v = arrastre.v;
    S.vista = { ...v, x: v.x - dx / r.width * v.w, y: v.y - dy / r.height * v.h };
    dibujo();
  });
  window.addEventListener('mouseup', (e) => {
    const a = arrastre; arrastre = null; pv.classList.remove('arrastre');
    if (!a || a.movio || !pv.contains(e.target)) return;
    const vg = e.target.closest('[data-v]'), pn = e.target.closest('[data-panel]');
    if (S.editar && vg) {
      const id = vg.dataset.v;
      S.borradas = S.borradas.includes(id) ? S.borradas.filter((x) => x !== id) : [...S.borradas, id];
      lateral(); pedir();
    } else if (!S.editar && pn && pn.dataset.panel !== S.panel) {
      S.panel = pn.dataset.panel; S.borradas = []; S.propuesta = null;
      lateral(); pintar(); pedir();
    }
  });
  window.addEventListener('keydown', (e) => { if (e.key === 'Escape' && S.editar) { S.editar = false; lateral(); pintar(); } });
  window.addEventListener('resize', () => { if (S.piso) { S.vista = null; dibujo(); } });
  $('b-todo').addEventListener('click', () => { if (S.piso) { S.vista = encuadre(cajaPiso(), 0.1); dibujo(); } });
  $('b-panel').addEventListener('click', () => { const pan = panelSel(); if (pan) { S.vista = encuadre(caja(pan.libre), 0.35); dibujo(); } });

  /* ---------- franjas, pie y avisos ---------- */
  function franjas() {
    const pan = panelSel(), p = S.propuesta, V = valores();
    $('chips').innerHTML = (S.piso ? `<span class="cchip mono">${esc(S.piso.nombre)}</span><span class="cchip">${S.piso.paneles.length} paneles</span>` : '')
      + (pan ? `<span class="cchip ok"><i></i>${esc(pan.id)}</span><span class="cchip mono">${S.dir === 'X' ? '↔' : '↕'} ${S.dir}</span>` : (S.piso ? '<span class="cchip warn"><i></i>Sin panel elegido</span>' : ''));
    $('b-panel').disabled = !pan;
    $('msg').innerHTML = S.editar
      ? `<div class="banner"><span><b>Eliminar viguetas:</b> clic sobre una vigueta para quitarla o restaurarla</span><button class="bb" data-act="deshacer" ${S.borradas.length ? '' : 'disabled'}>Deshacer</button><button class="bb solid" data-act="listo">Listo</button></div>`
      : (S.piso && !pan ? '<div class="banner hint"><span>Haz clic en un panel de la planta para ver sus viguetas.</span></div>' : '');
    const m = (l, v) => `<div class="m"><span class="m-l">${l}</span><span class="m-v">${v}</span></div>`;
    let pie = '';
    if (!S.piso) pie = '<span class="msg-warn">Elige un piso.</span>';
    else if (!pan) pie = '<span class="msg-warn">Elige un panel.</span>';
    else if (!V.ok) pie = '<span class="msg-warn">Revisa la separación y el ancho.</span>';
    else if (p) pie = `<span class="tipos">${p.tipos.map((t) => `<span class="tc" title="Viguetas iguales van en el mismo tipo">T${t.numero} · ${fmt(t.largo)} m × ${t.cantidad}</span>`).join('')}${S.borradas.length ? `<span class="tc del">Eliminadas × ${S.borradas.length}</span>` : ''}</span>`;
    $('mt').innerHTML = `<div class="mt-row">${m('Cantidad', p ? p.cantidad : '—')}${m('Eje a eje', p ? fmt(p.ejeAEje) + ' m' : '—')}${m('Libre en extremos', p ? fmt(p.libreExtremos) + ' m' : '—')}</div>
      <div class="mt-foot">${pie}<span class="tag" title="Cantidad = REDONDEAR.MENOS(libre / eje a eje); extremos = (libre − ((cantidad − 1) · eje a eje + ancho)) / 2; si pasan del libre máximo se agrega una vigueta.">Fórmula del Excel de Carlos${p ? ' · libre máx. ' + fmt(p.libreMaximo) + ' m' : ''}</span></div>`;
  }
  $('msg').addEventListener('click', (e) => {
    const b = e.target.closest('[data-act]'); if (!b || b.disabled) return;
    if (b.dataset.act === 'deshacer') { S.borradas = S.borradas.slice(0, -1); pedir(); }
    if (b.dataset.act === 'listo') S.editar = false;
    lateral(); pintar();
  });

  function pie() {
    const pan = panelSel();
    $('doc').textContent = S.doc;
    // La propuesta llega después de elegir el panel: sin esto el botón quedaba desactivado.
    if ($('b-editar')) $('b-editar').disabled = !S.propuesta;
    $('b-quitar').disabled = !(pan && pan.creadas);
    $('b-quitar').textContent = pan && pan.creadas ? `Quitar creadas del panel (${pan.creadas})` : 'Quitar creadas del panel';
    $('b-crear').disabled = !(S.propuesta && vivas().length && S.tipo);
    $('b-crear').textContent = pan && pan.creadas ? 'Reemplazar viguetas' : 'Crear viguetas';
  }
  $('b-crear').addEventListener('click', () => { enviar({ tipo: 'crear', ...pedido(), tipoVigueta: S.tipo }); aviso('Creando las viguetas en Revit…'); });
  $('b-quitar').addEventListener('click', () => { enviar({ tipo: 'quitar', panel: S.panel }); aviso('Quitando las viguetas creadas…'); });
  $('b-cerrar').addEventListener('click', () => enviar({ tipo: 'cerrar' }));

  let tAviso;
  function aviso(texto, bien) {
    const t = $('toast'); t.textContent = texto; t.classList.toggle('bien', !!bien); t.classList.add('show');
    clearTimeout(tAviso); tAviso = setTimeout(() => t.classList.remove('show'), 3200);
  }

  function pintar() { dibujo(); franjas(); pie(); if ($('sum')) resumen(); }
  function todo() { lateral(); pintar(); }

  /* ---------- arranque ---------- */
  const webview = window.chrome && window.chrome.webview;
  todo();
  if (webview) {
    enviar = (m) => webview.postMessage(m);
    webview.addEventListener('message', (e) => recibir(e.data));
    enviar({ tipo: 'listo' });
  } else {
    // Fuera de Revit: un piso de ejemplo para ver la página (viguetas-demo.js).
    const s = document.createElement('script');
    s.src = 'viguetas-demo.js';
    s.onload = () => { const demo = window.DEMO_VIGUETAS(); enviar = (m) => demo(m).forEach((r) => setTimeout(() => recibir(r), 0)); enviar({ tipo: 'listo' }); };
    document.head.appendChild(s);
  }
})();
