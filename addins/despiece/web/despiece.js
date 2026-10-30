// El dibujo del despiece: el port de Despiece.dc.html de la maqueta aprobada (#220). No cambiar sin el visto bueno de Juan.
// Dibuja los datos de ejemplo de la maqueta; los reales llegan con #222 a #228.
(function () {
  const { html } = window.htmPreact;

  const trazar = (props) => {
    const tipo = props.tipo ?? 'viga';
    const w = props.w ?? 1000, h = props.h ?? Math.round(w * 0.54);
    const sel = props.sel ?? null;
    const pick = props.onPick;
    const el = props.el ?? 'AB';
    const esc = props.esc ?? '1:50';
    const conSeccion = props.conSeccion ?? true;
    const conEtiq = props.conEtiquetas ?? true;
    const conCotas = props.conCotas ?? true;
    const conColor = props.conColor ?? true;
    const aplica = props.aplica ?? (tipo === 'viga' ? 'V Cim A-B-C - (0.50x0.90)' : 'Col A2-B2 - (0.40x0.40)');
    const INK = '#16191D', GRIS = '#5E6670', COTA = '#16191D', MONO = 'Arial, Helvetica, sans-serif', SANS = 'Arial, Helvetica, sans-serif';
    const pinta = (c) => (conColor ? c : INK);
    const C = { 3: pinta('#FBBC04'), 4: pinta('#4285F4'), 5: pinta('#EA4335'), 6: pinta('#34A853'), 7: pinta('#9334E6'), 8: pinta('#8B4513') };
    const TA = '#0E9AA7', TB = '#7CB342';
    const rects = [], lines = [], dots = [], texts = [], hits = [];
    const op = (id) => (sel && id && sel !== id ? 0.2 : 1);
    const rect = (x, y, w2, h2, f, s, sw, rx, id) => rects.push({ x, y, w: w2, h: h2, f, s, sw: sw ?? 1, rx: rx ?? 0, op: op(id) });
    const line = (d, c, wd, id, dash) => lines.push({ d, c, w: sel === id && id ? wd + 1.5 : wd, dash: dash ?? 'none', op: op(id) });
    const bar = (id, d, c, wd) => { line(d, c, wd ?? 3, id); if (pick) hits.push({ d, pick: () => pick(id) }); };
    const dot = (x, y, r, c, id) => dots.push({ x, y, r, c, op: op(id) });
    const text = (x, y, t, o) => {
      o = o ?? {};
      const ax = o.a === 'middle' ? '-50%' : o.a === 'end' ? '-100%' : '0';
      texts.push({ x, y, t, c: o.c ?? INK, s: o.s ?? 13, fw: o.fw ?? 400, ff: o.mono ? MONO : SANS, op: op(o.id), tf: (o.r ? 'rotate(-90deg) ' : '') + 'translate(' + ax + ', -82%)' });
    };
    const cadena = (xs, y, ls, id) => {
      if (!conCotas) return;
      let d = '';
      xs.forEach((x, i) => { d += 'M' + (x - 3) + ' ' + (y + 3) + 'L' + (x + 3) + ' ' + (y - 3); if (i) d += 'M' + xs[i - 1] + ' ' + y + 'H' + x; });
      line(d, COTA, 1, id);
      ls.forEach((l, i) => text((xs[i] + xs[i + 1]) / 2, y + 13, l, { mono: true, s: 11, a: 'middle', c: COTA, id }));
    };
    const cadenaV = (ys, x, ls, id) => {
      if (!conCotas) return;
      let d = '';
      ys.forEach((y, i) => { d += 'M' + (x - 3) + ' ' + (y + 3) + 'L' + (x + 3) + ' ' + (y - 3); if (i) d += 'M' + x + ' ' + ys[i - 1] + 'V' + y; });
      line(d, COTA, 1, id);
      ls.forEach((l, i) => { const ym = (ys[i] + ys[i + 1]) / 2; text(x + 13, ym, l, { mono: true, s: 11, a: 'middle', c: COTA, id, r: true }); });
    };
    const titulo = (x, y, t) => text(x, y, t, { fw: 600, c: GRIS });
    const etiqueta = (x, y, t, o) => { if (conEtiq) text(x, y, t, { mono: true, fw: 500, ...(o ?? {}) }); };
    const guia = (d, id) => { if (conEtiq) line(d, COTA, 1, id); };
    const hcota = (x1, x2, y, t, id) => {
      if (!conCotas) return;
      line('M' + x1 + ' ' + y + 'H' + x2 + 'M' + (x1 - 3) + ' ' + (y + 3) + 'L' + (x1 + 3) + ' ' + (y - 3) + 'M' + (x2 - 3) + ' ' + (y + 3) + 'L' + (x2 + 3) + ' ' + (y - 3), COTA, 1, id);
      text((x1 + x2) / 2, y + 14, t, { mono: true, s: 12, a: 'middle', c: COTA, id });
    };
    const vcota = (y1, y2, x, t, id) => {
      if (!conCotas) return;
      line('M' + x + ' ' + y1 + 'V' + y2 + 'M' + (x - 3) + ' ' + (y1 + 3) + 'L' + (x + 3) + ' ' + (y1 - 3) + 'M' + (x - 3) + ' ' + (y2 + 3) + 'L' + (x + 3) + ' ' + (y2 - 3), COTA, 1, id);
      const ym = (y1 + y2) / 2;
      text(x + 14, ym, t, { mono: true, s: 12, a: 'middle', c: COTA, id, r: true });
    };
    const eje = (x, y, t) => { rect(x - 11, y - 11, 22, 22, '#FFFFFF', GRIS, 1, 11); text(x, y + 4, t, { s: 12, a: 'middle' }); };
    const marcaCorte = (x, y) => { rect(x - 9, y - 9, 18, 18, INK, INK, 1, 9); text(x, y + 4, '1', { s: 11, a: 'middle', c: '#FFFFFF', fw: 600 }); };

    if (tipo === 'viga' && el !== '1') {
      // Copia el formato del plano E-01 de Santiago: V Cim A-B-C - (0.50x0.90).
      eje(80, 20, '1'); eje(260, 20, '2'); eje(720, 20, '3');
      line('M80 34V470M260 34V470M720 34V470', '#AEB5BD', 1, null, '6 4');
      hcota(80, 720, 44, '10.25');
      hcota(80, 260, 68, '2.89'); hcota(260, 720, 68, '7.36');
      const larga = (id, y, arriba, c, et, recto) => {
        const dy = arriba ? 22 : -22;
        bar(id, 'M60 ' + (y + dy) + 'V' + y + 'H740V' + (y + dy), c);
        etiqueta(400, arriba ? y + 16 : y - 7, et, { a: 'middle', id, c, fw: 600 });
        if (conCotas) {
          text(400, arriba ? y - 6 : y + 16, recto, { mono: true, s: 12, a: 'middle', c: COTA, id });
          text(50, y + dy / 2, '0.40', { mono: true, s: 11, a: 'middle', c: COTA, id, r: true });
          text(758, y + dy / 2, '0.40', { mono: true, s: 11, a: 'middle', c: COTA, id, r: true });
        }
      };
      larga('r7s', 112, true, C[7], '7 #7 L= 11.52', '10.83');
      rect(60, 170, 680, 76, '#FFFFFF', INK, 1.5);
      let tick = '';
      for (let x = 70; x <= 730; x += 6) tick += 'M' + x + ' 174V242';
      line(tick, INK, 1.1, 'e');
      if (pick) hits.push({ d: 'M70 208H730', pick: () => pick('e') });
      etiqueta(170, 162, '25 #4 @0.10', { s: 12, id: 'e', a: 'middle' });
      etiqueta(490, 162, '70 #4 @0.10', { s: 12, id: 'e', a: 'middle' });
      if (conCotas) { line('M40 208H58', COTA, 1); text(4, 212, 'N+0.00', { mono: true, s: 11, c: COTA }); }
      const piel = (y, et, recto) => {
        bar('piel', 'M60 ' + y + 'H740', C[5]);
        etiqueta(400, y - 7, et, { a: 'middle', id: 'piel', c: C[5], fw: 600 });
        if (conCotas) text(400, y + 16, recto, { mono: true, s: 12, a: 'middle', c: COTA, id: 'piel' });
      };
      piel(288, '5 #5 L= 10.83', '10.83');
      piel(346, '5 #5 L= 10.82', '10.82');
      if (conEtiq) { line('M160 317L210 288M160 317L210 346', COTA, 1, 'piel'); text(150, 321, '2x piel', { s: 12, a: 'end', id: 'piel' }); }
      larga('r7i', 430, false, C[7], '7 #7 L= 11.52', '10.83');

      if (conSeccion) {
        titulo(800, 14, 'Sección');
        hcota(800, 900, 24, '0.50');
        cadena([800, 814, 886, 900], 50, ['0.07', '0.36', '0.07']);
        cadenaV([70, 84, 236, 250], 778, ['0.07', '0.76', '0.07']);
        if (conCotas) text(800, 272, 'Recubrimiento 0.07', { mono: true, s: 12, c: COTA });
        rect(800, 70, 100, 180, '#C9CFD6', '#3F4A57');
        rect(810, 80, 80, 160, 'none', C[4], 2, 3, 'e');
        [818, 836, 854, 872, 882].forEach((x) => dot(x, 90, 4.5, C[7], 'r7s'));
        [827, 863].forEach((x) => dot(x, 104, 4.5, C[7], 'r7s'));
        [818, 836, 854, 872, 882].forEach((x) => dot(x, 230, 4.5, C[7], 'r7i'));
        [827, 863].forEach((x) => dot(x, 216, 4.5, C[7], 'r7i'));
        [132, 160, 188].forEach((y) => { dot(816, y, 3.5, C[5], 'piel'); dot(884, y, 3.5, C[5], 'piel'); });
        vcota(70, 250, 750, '0.90');
        guia('M882 90H918', 'r7s'); etiqueta(922, 94, '7 #7', { id: 'r7s', s: 12 });
        guia('M884 160H918', 'piel'); etiqueta(922, 156, 'Piel', { id: 'piel', s: 12 }); etiqueta(922, 170, '5 #5', { id: 'piel', s: 12 });
        guia('M882 230H918', 'r7i'); etiqueta(922, 234, '7 #7', { id: 'r7i', s: 12 });
        titulo(800, 296, 'Estribo');
        line('M804 324L822 342M804 324H880V470H804V324L822 324', C[4], 3, 'e');
        hcota(804, 880, 480, '0.36', 'e');
        vcota(324, 470, 890, '0.75', 'e');
        if (conCotas) text(826, 352, '0.15', { mono: true, s: 11, c: COTA, id: 'e' });
      }
    } else if (tipo === 'viga') {
      titulo(60, 24, 'Vista longitudinal');
      rect(60, 100, 640, 70, '#F4F6F8', '#8A929C');
      [60, 360, 660].forEach((x) => rect(x, 76, 40, 118, '#DDE1E6', '#8A929C'));
      eje(80, 54, '1'); eje(380, 54, '2'); eje(680, 54, '3');
      let tick = '';
      for (let x = 112; x <= 350; x += 12) tick += 'M' + x + ' 106V164';
      for (let x = 412; x <= 650; x += 12) tick += 'M' + x + ' 106V164';
      line(tick, C[3], 1.3, 'e');
      bar('r7', 'M72 132V114H257M143 118H607M462 114H689V132', C[7]);
      bar('bast', 'M72 124H169M296 124H464M608 124H688', C[7]);
      bar('r8', 'M72 140V158H296M144 154H608M465 158H688V140', C[8]);
      line('M230 84V186', INK, 1.2, null, '6 4');
      marcaCorte(230, 76); marcaCorte(230, 194);
      etiqueta(160, 212, '8 #3 c/.10', { s: 12, id: 'e', a: 'middle' });
      etiqueta(300, 212, '12 #3 c/.20', { s: 12, id: 'e', a: 'middle' });
      etiqueta(460, 212, '12 #3 c/.20', { s: 12, id: 'e', a: 'middle' });
      etiqueta(600, 212, '8 #3 c/.10', { s: 12, id: 'e', a: 'middle' });
      if (pick) hits.push({ d: 'M112 106H350M412 106H650', pick: () => pick('e') });
      hcota(100, 360, 224, '2.60'); hcota(400, 660, 224, '2.60');

      // Como la captura de Santiago: tramos a la misma altura de la viga, "1 #N" arriba, largo abajo y traslapo entre rayas.
      const raya = (x, y, id) => line('M' + (x - 5) + ' ' + (y + 9) + 'L' + (x + 5) + ' ' + (y - 9), COTA, 1.2, id);
      const fila = (id, y, n, c, t) => {
        const [a, b, e] = t.tramos;
        bar(id, 'M' + a.x1 + ' ' + (y - 16) + 'V' + y + 'H' + a.x2 + 'M' + b.x1 + ' ' + (y + 4) + 'H' + b.x2 + 'M' + e.x1 + ' ' + y + 'H' + e.x2 + 'V' + (y - 16), c);
        t.tramos.forEach((p) => {
          etiqueta(p.xe, y - 10, '1 #' + n, { a: 'middle', id });
          if (conCotas) text(p.xe, y + 24, p.l, { mono: true, a: 'middle', c: COTA, id });
        });
        t.traslapos.forEach((q) => {
          if (!conCotas) return;
          raya(q.x1, y + 2, id); raya(q.x2, y + 2, id);
          text((q.x1 + q.x2) / 2, y - 10, q.l, { mono: true, s: 12, a: 'middle', c: COTA, id });
        });
      };
      fila('r8', 270, 8, C[8], {
        tramos: [{ x1: 72, x2: 296, xe: 108, l: '2.65' }, { x1: 144, x2: 608, xe: 380, l: '5.50' }, { x1: 465, x2: 688, xe: 652, l: '2.65' }],
        traslapos: [{ x1: 144, x2: 296, l: '1.80' }, { x1: 465, x2: 608, l: '1.70' }]
      });
      [{ x1: 72, x2: 169, l: '1.15' }, { x1: 296, x2: 464, l: '2.00' }, { x1: 608, x2: 688, l: '0.95' }].forEach((p) => {
        bar('bast', 'M' + p.x1 + ' 350H' + p.x2, C[7]);
        etiqueta((p.x1 + p.x2) / 2, 340, '1 #7', { a: 'middle', id: 'bast' });
        if (conCotas) text((p.x1 + p.x2) / 2, 370, p.l, { mono: true, a: 'middle', c: COTA, id: 'bast' });
      });
      fila('r7', 440, 7, C[7], {
        tramos: [{ x1: 72, x2: 257, xe: 106, l: '2.19' }, { x1: 143, x2: 607, xe: 380, l: '5.50' }, { x1: 462, x2: 689, xe: 650, l: '2.69' }],
        traslapos: [{ x1: 143, x2: 257, l: '1.35' }, { x1: 462, x2: 607, l: '1.72' }]
      });

      if (conSeccion) {
        titulo(770, 24, 'Sección 1-1');
        rect(790, 40, 120, 190, '#F4F6F8', '#8A929C');
        rect(802, 52, 96, 166, 'none', C[3], 2, 3, 'e');
        [814, 850, 886].forEach((x) => dot(x, 64, 6, C[7], 'r7'));
        dot(850, 78, 5, C[7], 'bast');
        [814, 850, 886].forEach((x) => dot(x, 206, 6.5, C[8], 'r8'));
        guia('M886 64H920', 'r7'); etiqueta(924, 68, '#7', { id: 'r7' });
        guia('M886 206H920', 'r8'); etiqueta(924, 210, '#8', { id: 'r8' });
        hcota(790, 910, 240, '0.50');
        cadena([790, 802, 898, 910], 262, ['0.04', '0.42', '0.04']);
        vcota(40, 230, 772, '0.60');
        if (conCotas) text(790, 296, 'Recubrimiento 0.04', { mono: true, s: 12, c: COTA });
        titulo(770, 322, 'Estribo #3');
        line('M786 426V340H872V426H786V408', C[3], 3, 'e');
        hcota(786, 872, 436, '0.24', 'e');
        vcota(340, 426, 884, '0.44', 'e');
        if (conCotas) text(786, 472, 'Gancho 0.10', { mono: true, s: 12, c: COTA, id: 'e' });
      }
    } else {
      titulo(60, 24, 'Vista completa');
      rect(60, 50, 50, 430, '#F4F6F8', '#8A929C');
      rect(110, 240, 28, 40, '#DDE1E6', '#8A929C');
      let tick = '';
      for (let y = 60; y <= 230; y += 10) tick += 'M64 ' + y + 'H106';
      for (let y = 290; y <= 470; y += 20) tick += 'M64 ' + y + 'H106';
      line(tick, C[3], 1.3, 'e');
      if (pick) hits.push({ d: 'M85 60V470', pick: () => pick('e') });
      etiqueta(128, 145, '14 #3 c/.10', { s: 12, id: 'e', a: 'middle', r: true });
      etiqueta(128, 380, '10 #3 c/.20', { s: 12, id: 'e', a: 'middle', r: true });
      line('M50 180H120', INK, 1.2, null, '6 4');
      marcaCorte(42, 180);
      vcota(50, 480, 28, '4.30');
      if (conCotas) { text(116, 236, 'N+4.00', { mono: true, s: 11, c: COTA }); text(116, 492, 'N+0.00', { mono: true, s: 11, c: COTA }); }

      // Mismo formato que la viga, de pie: etiqueta a un lado, largo al otro y traslapo entre rayas.
      const fila = (id, x, n, c, abajo, arriba, yBaja, ySube, lT) => {
        bar(id, 'M' + (x + 16) + ' 440H' + x + 'V' + yBaja + 'M' + (x + 4) + ' ' + ySube + 'V' + 52, c);
        const yA = 400, yB = 90;
        [[yA, abajo], [yB, arriba]].forEach(([ym, l]) => {
          etiqueta(x - 10, ym, '4 #' + n + ' L= ' + l, { a: 'middle', id, c, fw: 600, r: true });
          if (conCotas) text(x + 22, ym, l, { mono: true, a: 'middle', c: COTA, id, r: true });
        });
        if (conCotas) {
          line('M' + (x - 7) + ' ' + (yBaja + 5) + 'L' + (x + 11) + ' ' + (yBaja - 5) + 'M' + (x - 7) + ' ' + (ySube + 5) + 'L' + (x + 11) + ' ' + (ySube - 5), COTA, 1.2, id);
          const ym = (yBaja + ySube) / 2;
          text(x + 22, ym, lT, { mono: true, s: 12, a: 'middle', c: COTA, id, r: true });
        }
      };
      fila('c6', 220, 6, C[6], '3.20', '3.40', 210, 296, '1.20');
      fila('c5', 320, 5, C[5], '2.80', '3.20', 238, 310, '1.00');

      if (conSeccion) {
        const sec = (ox, titu, cFn, letras) => {
          titulo(ox, 24, titu);
          rect(ox, 42, 150, 150, '#F4F6F8', '#8A929C');
          rect(ox + 12, 54, 126, 126, 'none', C[3], 2, 3, 'e');
          [[24, 24], [126, 24], [24, 126], [126, 126]].forEach((p, i) => {
            const k = cFn('e', i); dot(ox + p[0], 42 + p[1], 8, k.c, 'c6');
            if (letras) text(ox + p[0], 42 + p[1] + 4, k.l, { s: 10, a: 'middle', c: '#16191D', fw: 600 });
          });
          [[75, 24], [24, 75], [126, 75], [75, 126]].forEach((p, i) => {
            const k = cFn('i', i); dot(ox + p[0], 42 + p[1], 7, k.c, 'c5');
            if (letras) text(ox + p[0], 42 + p[1] + 4, k.l, { s: 10, a: 'middle', c: '#16191D', fw: 600 });
          });
          hcota(ox, ox + 150, 202, '0.40');
          cadena([ox, ox + 12, ox + 138, ox + 150], 228, ['0.04', '0.32', '0.04']);
          vcota(42, 192, ox - 22, '0.40');
        };
        sec(490, 'Sección 1-1 · traslapos', (k, i) => ((k === 'e' ? i === 0 || i === 3 : i === 1 || i === 2) ? { c: TA, l: 'A' } : { c: TB, l: 'B' }), true);
        sec(720, 'Sección 1-1 · varillas', (k) => (k === 'e' ? { c: C[6] } : { c: C[5] }), false);
        guia('M846 66H884', 'c6'); etiqueta(888, 70, '#6 ext.', { id: 'c6' });
        guia('M846 117H884', 'c5'); etiqueta(888, 121, '#5 int.', { id: 'c5' });
        if (conEtiq) { text(490, 266, 'A y B: traslapan intercalados', { s: 12, c: GRIS }); }
        if (conCotas) { text(720, 266, 'Recubrimiento 0.04', { mono: true, s: 12, c: COTA }); text(720, 284, 'Longitud total 5.40', { mono: true, s: 12, c: COTA }); }
        titulo(490, 300, 'Estribo #3');
        line('M498 420V328H590V420H498V402', C[3], 3, 'e');
        hcota(498, 590, 430, '0.32', 'e');
        vcota(328, 420, 602, '0.32', 'e');
        if (conCotas) text(498, 466, 'Gancho 0.10', { mono: true, s: 12, c: COTA, id: 'e' });
      }
    }
    // Título de vista como el de Revit en la captura del E-01.
    line('M52 508H620', '#1F5FAF', 1.5);
    eje(40, 508, '1');
    text(62, 502, aplica, { s: 16 });
    text(62, 526, esc.replace(':', ' : '), { s: 12 });
    text(940, 526, 'Datos de ejemplo', { a: 'end', c: GRIS, s: 12 });
    return { w, h, k: w / 1000, rects, lines, dots, texts, hits };
  };

  window.Despiece = (props) => {
    const v = trazar(props);
    return html`
      <div style="position: relative; overflow: hidden; width: ${v.w}px; height: ${v.h}px; background: #FFFFFF; font-family: 'IBM Plex Sans', 'Segoe UI', sans-serif">
        <svg width=${v.w} height=${v.h} viewBox="0 0 1000 540" style="display: block" role="img" aria-label="Despiece con vistas, barras sacadas, etiquetas, cotas y secciones">
          ${v.rects.map((r) => html`<rect x=${r.x} y=${r.y} width=${r.w} height=${r.h} rx=${r.rx} fill=${r.f} stroke=${r.s} stroke-width=${r.sw} opacity=${r.op}></rect>`)}
          ${v.lines.map((l) => html`<path d=${l.d} fill="none" stroke=${l.c} stroke-width=${l.w} stroke-linejoin="round" stroke-linecap="round" stroke-dasharray=${l.dash} opacity=${l.op}></path>`)}
          ${v.dots.map((o) => html`<circle cx=${o.x} cy=${o.y} r=${o.r} fill=${o.c} opacity=${o.op}></circle>`)}
          ${v.hits.map((l) => html`<path d=${l.d} fill="none" stroke="#000000" stroke-opacity="0" stroke-width="16" style="cursor: pointer" onClick=${l.pick}></path>`)}
        </svg>
        <div style="position: absolute; left: 0; top: 0; width: 1000px; height: 540px; transform: scale(${v.k}); transform-origin: 0 0; pointer-events: none">
          ${v.texts.map((t) => html`<span style="position: absolute; left: ${t.x}px; top: ${t.y}px; transform: ${t.tf}; transform-origin: 0 0; white-space: nowrap; line-height: 1; color: ${t.c}; font-size: ${t.s}px; font-weight: ${t.fw}; font-family: ${t.ff}; opacity: ${t.op}">${t.t}</span>`)}
        </div>
      </div>`;
  };
})();
