// La vista simulada de Revit: el port de EnRevit.dc.html de la maqueta aprobada (#220). No cambiar sin el visto bueno de Juan.
(function () {
  const { html } = window.htmPreact;

  const calcular = (props) => {
    const tipo = props.tipo ?? 'viga';
    const modo = props.modo ?? 'elegir';
    const w = props.w ?? 1000, h = props.h ?? 640;
    const el = props.el ?? (tipo === 'viga' ? 'AB' : 'A2');
    const aplica = props.aplica ?? (tipo === 'viga' ? 'V Cim A-B-C - (0.50x0.90)' : 'Col A2-B2 - (0.40x0.40)');
    const onEl = props.onEl;
    const svgH = h - 32;
    const s = Math.min(w / 1000, svgH / 610);
    const cx = w / 2, cy = svgH * 0.42;
    const p = (x, y, z) => [cx + (x - y) * 170 * s, cy + (x + y) * 62 * s - (z - 1) * 125 * s];
    const seg = (a, b) => { const A = p(a[0], a[1], a[2]), B = p(b[0], b[1], b[2]); return 'M' + A[0].toFixed(1) + ' ' + A[1].toFixed(1) + 'L' + B[0].toFixed(1) + ' ' + B[1].toFixed(1); };
    const AZUL = '#1F5FAF', CLARO = '#7FA6D6', GRIS = '#9AA2AB';
    const lines = [], hits = [];
    const add = (d, c, wd, dash) => lines.push({ d, c, w: wd, dash: dash ?? 'none' });
    const hit = (d, id) => { if (onEl && modo === 'elegir') hits.push({ d, pick: () => onEl(id) }); };
    const grupoCol = (x, y) => {
      const k = 'ABC'[y] + (x + 1);
      if (k === 'A2' || k === 'B2') return 'A2';
      if (k === 'A3' || k === 'B3') return 'A3';
      return k === 'C1' ? 'C1' : '';
    };
    for (let x = 0; x < 3; x++) for (let y = 0; y < 3; y++) {
      const d = seg([x, y, 0], [x, y, 2]);
      const g = tipo === 'columna' ? grupoCol(x, y) : '';
      const on = g && g === el;
      add(d, on ? AZUL : GRIS, on ? 11 * s + 2 : 6 * s + 1);
      if (g) hit(d, g);
    }
    for (let z = 1; z <= 2; z++) {
      for (let y = 0; y < 3; y++) {
        const g = 'AB';
        const d = seg([0, y, z], [2, y, z]);
        const on = tipo === 'viga' && g === el;
        const prim = on && y === 0 && z === 1;
        add(d, prim ? AZUL : on ? CLARO : GRIS, on ? 10 * s + 2 : 6 * s + 1);
        if (tipo === 'viga') hit(d, g);
      }
      for (let x = 0; x < 3; x++) {
        const g = '1';
        const d = seg([x, 0, z], [x, 2, z]);
        const on = tipo === 'viga' && g === el;
        const prim = on && x === 0 && z === 1;
        add(d, prim ? AZUL : on ? CLARO : GRIS, on ? 10 * s + 2 : 6 * s + 1);
        if (tipo === 'viga') hit(d, g);
      }
    }
    const tags = [];
    const tag = (P, dx, dy, t, c) => {
      if (w < 500) return;
      const x = P[0] + dx * s, y = P[1] + dy * s;
      add('M' + P[0].toFixed(1) + ' ' + P[1].toFixed(1) + 'L' + x.toFixed(1) + ' ' + y.toFixed(1), c, 1);
      tags.push({ x, y, t, c });
    };
    if (modo === 'previa') {
      const off = (d, dy) => d.replace(/M([\d.]+) ([\d.]+)L([\d.]+) ([\d.]+)/, (m, a, b, c, e) => 'M' + a + ' ' + (+b + dy) + 'L' + c + ' ' + (+e + dy));
      if (tipo === 'viga') {
        const eje = el === '1' ? [[0, 0, 1], [0, 2, 1]] : [[0, 0, 1], [2, 0, 1]];
        const d = seg(eje[0], eje[1]);
        add(off(d, -3), '#9334E6', 2.5);
        add(off(d, 3), '#8B4513', 2.5);
        const mid = [(eje[0][0] + eje[1][0]) / 2, (eje[0][1] + eje[1][1]) / 2];
        const horiz = eje[0][1] === eje[1][1];
        add(seg([mid[0] - (horiz ? 0 : 0.35), mid[1] - (horiz ? 0.35 : 0), 1], [mid[0] + (horiz ? 0 : 0.35), mid[1] + (horiz ? 0.35 : 0), 1]), '#16191D', 1.5, '6 4');
        const P = p(mid[0], mid[1], 1);
        tag(P, -170, -90, '7 #7 L= 11.52', '#9334E6');
        tag(P, 170, -90, '5 #5 L= 10.83', '#EA4335');
        tag(P, 0, 110, 'Sección 1-1', '#16191D');
      } else {
        const pos = el === 'A3' ? [2, 0] : el === 'C1' ? [0, 2] : [1, 0];
        const d = seg([pos[0], pos[1], 0], [pos[0], pos[1], 2]);
        const A = d.match(/M([\d.]+) ([\d.]+)L([\d.]+) ([\d.]+)/);
        add('M' + (+A[1] - 3) + ' ' + A[2] + 'L' + (+A[3] - 3) + ' ' + A[4], '#34A853', 2.5);
        add('M' + (+A[1] + 3) + ' ' + A[2] + 'L' + (+A[3] + 3) + ' ' + A[4], '#EA4335', 2.5);
        add(seg([pos[0] - 0.3, pos[1], 1.5], [pos[0] + 0.3, pos[1], 1.5]), '#16191D', 1.5, '6 4');
        const P = p(pos[0], pos[1], 1);
        tag(P, -170, -60, '1 #6 · 3.40', '#34A853');
        tag(P, 170, -60, '1 #5 · 3.20', '#EA4335');
        tag(p(pos[0], pos[1], 1.5), 170, -50, 'Sección 1-1', '#16191D');
      }
    }
    const ejes = [];
    ['1', '2', '3'].forEach((t, i) => { const q = p(i, -0.45, 0); ejes.push({ t, x: q[0], y: q[1] }); });
    ['A', 'B', 'C'].forEach((t, i) => { const q = p(-0.45, i, 0); ejes.push({ t, x: q[0], y: q[1] }); });
    return {
      w, h, svgH, aplica, lines, hits, ejes, tags,
      esElegir: modo === 'elegir', esPrevia: modo === 'previa',
      vistaNombre: modo === 'previa' ? 'Vista 3D · previa de ' + aplica : 'Vista 3D · estructura',
      queElegir: tipo === 'viga' ? 'una viga del modelo' : 'una columna del modelo'
    };
  };

  window.EnRevit = (props) => {
    const v = calcular(props);
    return html`
      <div style="position: relative; width: ${v.w}px; height: ${v.h}px; display: flex; flex-direction: column; background: #E9ECEF; border: 1px solid #AEB5BD; box-sizing: border-box; overflow: hidden; font-family: 'IBM Plex Sans', 'Segoe UI', sans-serif; font-size: 13px; color: #16191D">
        <div style="height: 30px; flex-shrink: 0; display: flex; align-items: center; gap: 8px; padding: 0 10px; background: #3B4450; color: #FFFFFF; font-size: 12px">
          <span style="font-weight: 600">Revit</span><span>${v.vistaNombre}</span>
        </div>
        <div style="position: relative; flex: 1; min-height: 0; background: #FFFFFF">
          <svg width=${v.w} height=${v.svgH} style="display: block" role="img" aria-label="Modelo en Revit con el elemento resaltado">
            ${v.lines.map((l) => html`<path d=${l.d} fill="none" stroke=${l.c} stroke-width=${l.w} stroke-linecap="round" stroke-dasharray=${l.dash}></path>`)}
            ${v.ejes.map((e) => html`<circle cx=${e.x} cy=${e.y} r="13" fill="#FFFFFF" stroke="#5E6670"></circle>`)}
            ${v.hits.map((l) => html`<path d=${l.d} fill="none" stroke="#000000" stroke-opacity="0" stroke-width="16" style="cursor: pointer" onClick=${l.pick}></path>`)}
          </svg>
          ${v.ejes.map((e) => html`<span style="position: absolute; left: ${e.x}px; top: ${e.y}px; transform: translate(-50%, -50%); font-size: 12px; line-height: 1; pointer-events: none">${e.t}</span>`)}
          ${v.esElegir ? html`
            <div style="position: absolute; top: 10px; left: 50%; transform: translateX(-50%); display: flex; align-items: center; gap: 8px; padding: 6px 8px 6px 14px; background: #FFFFFF; border: 1px solid #C9CFD6; border-radius: 6px; box-shadow: 0 2px 8px rgba(22,25,29,.2); white-space: nowrap">
              <span>Haz clic en ${v.queElegir}</span>
              <button type="button" onClick=${props.onTodas} style="height: 28px; padding: 0 10px; border: 1px solid #C9CFD6; border-radius: 6px; background: #FFFFFF; font: inherit; color: #16191D; cursor: pointer">Todas</button>
              <button type="button" onClick=${props.onTerminar} style="height: 28px; padding: 0 10px; border: 1px solid #1F5FAF; border-radius: 6px; background: #1F5FAF; font: inherit; color: #FFFFFF; cursor: pointer">Terminar</button>
              <button type="button" onClick=${props.onCancelar} style="height: 28px; padding: 0 10px; border: 1px solid #C9CFD6; border-radius: 6px; background: #FFFFFF; font: inherit; color: #16191D; cursor: pointer">Cancelar</button>
            </div>` : null}
          ${v.esPrevia ? html`
            <div style="position: absolute; left: 10px; bottom: 10px; max-width: 70%; padding: 8px 12px; background: #FFFFFF; border: 1px solid #C9CFD6; border-radius: 6px; font-size: 12px; line-height: 1.45">
              <div style="font-weight: 600">Vista previa temporal</div>
              <div style="color: #5E6670">Se despieza una vez para ${v.aplica}. Aún no se crea nada en el proyecto.</div>
            </div>
            ${v.tags.map((t) => html`<div style="position: absolute; left: ${t.x}px; top: ${t.y}px; transform: translate(-50%, -50%); padding: 3px 8px; background: #FFFFFF; border: 1px solid ${t.c}; border-radius: 4px; font-family: 'IBM Plex Mono', monospace; font-size: 12px; white-space: nowrap">${t.t}</div>`)}` : null}
        </div>
      </div>`;
  };
})();
