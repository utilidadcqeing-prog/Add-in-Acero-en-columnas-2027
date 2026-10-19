// Alzado del pórtico, con pan y zoom: el port de Beam.dc.html de la maqueta (versión 19) para el tablero (#83).
// Mismo DOM y mismos estilos que la maqueta (decisión 1 del plan docs/planes/2026-10-16-tablero-vigas-83.md); donde la
// maqueta tiene lugares fijos (C1…C4, T1…T4, a1…v2, colA…colD) se pinta un elemento igual por cada dato de `portico`.
(function () {
  'use strict';
  const { html, Component } = window.htmPreact;
  const f2 = (n) => (Math.round(n * 100) / 100).toFixed(2);
  // Gancho de una punta (#84), como la maqueta: 90° raya vertical, 135° diagonal hacia adentro, 180° en U. `alto` es
  // lo que baja (o sube) la raya; `adentro`, +1 en la punta izquierda y −1 en la derecha.
  const ganchoPts = (x, y, dir, tipo, adentro, alto) => (tipo === '135°' ? [[x + adentro * alto * 0.6, y + dir * alto * 0.75]]
    : tipo === '180°' ? [[x, y + dir * alto * 0.45], [x + adentro * alto * 0.65, y + dir * alto * 0.45]]
    : tipo ? [[x, y + dir * alto]] : []);
  const conGanchos = (xa, xz, y, dir, ti, tf, alto) => {
    let q = [[xa, y], [xz, y]];
    q = ganchoPts(xa, y, dir, ti, 1, alto).reverse().concat(q).concat(ganchoPts(xz, y, dir, tf, -1, alto));
    return q.map((c) => c[0] + ',' + c[1]).join(' ');
  };
  // Botones de gancho junto a la punta (#84): en las corridas 90° · 135° · 180°; en los bastones, también sin gancho.
  const TIPOS = [['Sin', 'Sin', 'M1 6h10'], ['De90', '90°', 'M1 3h8v7'], ['De135', '135°', 'M1 3h8l-4 6'], ['De180', '180°', 'M1 3h7a3 3 0 010 6H5']];
  const ANCHO_BOTON = 44;
  // El pórtico como quedaría con el salto `s` del arrastre (#84). Solo mueve el dibujo: los saltos y los largos los
  // manda C#. Un traslapo corrido alarga la barra de su izquierda y acorta la de su derecha; un bastón toma sus puntas.
  const conSalto = (p, s) => {
    const barras = p.barras.map((b) => ({ ...b })), traslapos = p.traslapos.map((t) => ({ ...t }));
    const ini = Math.min(...p.barras.map((b) => b.desde)), fin = Math.max(...p.barras.map((b) => b.hasta));
    let bastones = p.bastones;
    s.cambios.forEach((c) => {
      if (c.tipo === 'movido') {
        const t = traslapos.find((x) => x.id === c.id);
        if (!t) return;
        const d = c.desde / 1000 - t.desde;
        t.desde += d; t.hasta += d;
        barras.find((x) => x.id === t.barraIzquierda).hasta += d;
        barras.find((x) => x.id === t.barraDerecha).desde += d;
      } else {
        // fuera del borde una punta pierde el gancho; pegada a él, lo lleva si el total pasa del recto
        const conGancho = s.total > s.recto + 1e-9;
        bastones = bastones.map((b) => (b.id !== c.id ? b : { ...b, desde: c.desde / 1000, hasta: c.hasta / 1000,
          ganchoInicio: c.desde / 1000 <= ini + 1e-6 && (b.ganchoInicio || conGancho),
          ganchoFin: c.hasta / 1000 >= fin - 1e-6 && (b.ganchoFin || conGancho) }));
      }
    });
    return { ...p, barras, traslapos, bastones };
  };
  const TIP_LISTO = {
    barra: { Izquierda: 'Arrastra para correr el traslapo de esta punta', Centro: 'Arrastra para mover la barra entera', Derecha: 'Arrastra para correr el traslapo de esta punta' },
    baston: { Izquierda: 'Arrastra para cambiar el largo por esta punta', Centro: 'Arrastra para correr el bastón', Derecha: 'Arrastra para cambiar el largo por esta punta' },
  };

  class Alzado extends Component {
    constructor(props) {
      super(props);
      this.state = { z: null, tx: 0, ty: 0, drag: null, cutDrag: false, selLocal: 'S1', selLapLocal: '', rk: '', hd: null };
      // Esc mientras se arrastra un punto: el elemento queda como estaba
      this.esc = (e) => { if (e.key === 'Escape') this.soltarPunto(false); };
    }

    componentWillUnmount() {
      window.removeEventListener('keydown', this.esc);
    }

    // Termina el arrastre de un punto; con `aplicar`, avisa el salto donde quedó (si mueve algo).
    soltarPunto(aplicar) {
      const h = this.state.hd;
      window.removeEventListener('keydown', this.esc);
      if (!h) return;
      this.setState({ hd: null });
      const c = h.cands[h.idx];
      if (aplicar && c && c.cambios.length && this.props.onDrop) this.props.onDrop(c);
    }

    valores() {
      let p = this.props.portico;
      const W = this.props.w ?? 820, H = this.props.h ?? 300;
      const inv = !!this.props.invert, warn = !!this.props.warn;
      const showSt = this.props.showStirrups ?? true;
      const X = (m) => 30 + 52 * (m - p.inicio);
      const CH = 204;
      const r0 = this.props.rangeFrom ?? p.inicio, r1 = this.props.rangeTo ?? p.fin;
      const x0 = X(r0 - 0.3), x1 = X(r1 + 0.3), dx = x1 - x0;
      const fitZ = Math.min((W - 24) / dx, (H - 60) / CH);
      const fit = { z: fitZ, tx: (W - dx * fitZ) / 2 - x0 * fitZ, ty: 30 + (H - 56 - CH * fitZ) / 2 };
      const rk = r0 + ':' + r1;
      const v = (this.state.z == null || this.state.rk !== rk) ? fit : this.state;
      // Carlos (1:11:23): líneas y textos del mismo tamaño a cualquier zoom. Los trazos van con non-scaling-stroke; los
      // textos, radios y márgenes que dependen del tamaño en pantalla se multiplican por k (en el ajuste, k = 1).
      const k = fitZ / v.z;
      const sel = this.props.sel ?? this.state.selLocal;
      const selLap = this.props.selLap ?? this.state.selLapLocal;
      const manualLaps = String(this.props.lapsManual || '').split(',');
      // Antes de voltear (cimentación), la maqueta pinta arriba ("C") la capa que traslapa al centro de la luz.
      const arribaAntes = (capa) => (capa === 'arriba') !== inv;

      // arrastre (#84): mientras se arrastra un punto, se dibuja el elemento en el salto donde caería
      const hdS = this.state.hd && this.state.hd.sel === sel ? this.state.hd : null;
      const cur = hdS ? hdS.cands[hdS.idx] : null;
      const colVivo = cur && cur.enVoladizo ? '#B26B00' : '#1F5FAF';
      const p0 = p;
      if (cur) p = conSalto(p0, cur);
      const vivos = cur ? cur.cambios.filter((c) => c.tipo === 'movido').map((c) => c.id) : [];

      const pickBar = (id) => () => { if (this.props.onPick) this.props.onPick(id); else this.setState({ selLocal: id, selLapLocal: '' }); };
      const pickLapFn = (id) => () => { if (this.props.onPickLap) this.props.onPickLap(id); else this.setState({ selLapLocal: id }); };

      // barras: la y alterna 52/58 (C) y 118/112 (F) según el índice en su capa, como la maqueta
      const enCapa = { C: 0, F: 0 };
      const b = p.barras.map((bar) => {
        const isC = arribaAntes(bar.capa);
        const i = enCapa[isC ? 'C' : 'F']++;
        const y = isC ? (i % 2 ? 58 : 52) : (i % 2 ? 112 : 118);
        const dir = isC ? 1 : -1;
        const ti = bar.ganchoInicio ? bar.ganchoInicioTipo || '90°' : '', tf = bar.ganchoFin ? bar.ganchoFinTipo || '90°' : '';
        const pts = conGanchos(X(bar.desde), X(bar.hasta), y, dir, ti, tf, 22);
        const isSel = bar.id === sel && !selLap;
        return { id: bar.id, y, isC, pts, col: isSel ? colVivo : '#3F4A57', sw: isSel ? 4.2 : 2.6, pick: pickBar(bar.id),
          lx: (X(bar.desde) + X(bar.hasta)) / 2, ly: bar.capa === 'arriba' ? 33 : 150, lt: bar.id + ' · ' + f2(bar.largo) };
      });

      const warnLap = warn ? this.props.warnLap : '';
      const l = p.traslapos.map((t) => {
        const isSel = t.id === selLap, isMan = manualLaps.includes(t.id);
        const isWarn = t.id === warnLap && !isMan;
        const c = vivos.includes(t.id) ? colVivo : isSel || isMan ? '#1F5FAF' : (isWarn ? '#B26B00' : '#C77A12');
        return { isC: arribaAntes(t.capa), x: X(t.desde), w: X(t.hasta) - X(t.desde), c, o: isSel ? 0.28 : 0.16, sw: isSel ? 2.6 : (isMan || isWarn ? 1.8 : 1), pick: pickLapFn(t.id) };
      });

      // bastones: los de arriba en y = 70 y los de abajo en y = 100, con el gancho hacia adentro de la viga. Una punta
      // con gancho se dibuja 6 unidades más adentro: si no, su gancho cae en la misma vertical que el de la corrida y se
      // ven como una sola raya (Juan, 16-oct). Solo es el dibujo; el largo no cambia.
      const ADENTRO = 6;
      const t = p.bastones.map((bs) => {
        const top = (bs.id.indexOf('BS') === 0) !== inv;
        const y = top ? 70 : 100, dir = top ? 1 : -1;
        const xa = X(bs.desde) + (bs.ganchoInicio ? ADENTRO : 0), xz = X(bs.hasta) - (bs.ganchoFin ? ADENTRO : 0);
        const ti = bs.ganchoInicio ? bs.ganchoInicioTipo || '90°' : '', tf = bs.ganchoFin ? bs.ganchoFinTipo || '90°' : '';
        const pts = conGanchos(xa, xz, y, dir, ti, tf, 16);
        const isSel = bs.id === sel && !selLap;
        return { id: bs.id, y, top, pts, col: isSel ? colVivo : '#0F7B6C', sw: isSel ? 4 : 2.6, pick: pickBar(bs.id) };
      });

      // estribos: una raya por cada estribo real, donde va (Juan, 17-oct: al cambiar "cada" el dibujo no cambiaba y la
      // línea 1-1, que va de estribo en estribo, no caía en las rayas). Sin posiciones, el rayado de zonas de la maqueta:
      // es un relleno y crece con el zoom, a diferencia de las líneas (C# y la demostración siempre mandan posiciones).
      const posEst = p.posicionesEstribos || null;
      const st = posEst ? [] : p.zonas.map((z) => ({ x1: X(z.desde), x2: X(z.hasta), da: z.junta ? '1 4.2' : '1 9.4' }));
      const stx = posEst ? posEst.map(X) : [];

      const ap = p.apoyos;
      // un apoyo que es viga se dibuja como su sección, a la altura de la viga; las columnas y muros, de alto completo
      const cols = ap.map((a) => ({ x: X(a.caraIzquierda), w: X(a.caraDerecha) - X(a.caraIzquierda), y: a.esViga ? 40 : 14, h: a.esViga ? 90 : 152 }));
      const luces = ap.slice(1).map((a, i) => ({ x: X((ap[i].x + a.x) / 2), lt: 'luz ' + f2(a.caraIzquierda - ap[i].caraDerecha) }));
      const vols = [];
      if (ap.length && p.inicio < ap[0].caraIzquierda - 1e-9) vols.push({ a: p.inicio, z: ap[0].caraIzquierda });
      if (ap.length && p.fin > ap[ap.length - 1].caraDerecha + 1e-9) vols.push({ a: ap[ap.length - 1].caraDerecha, z: p.fin });
      const vol = vols.map((o) => ({ x: X(o.a), w: X(o.z) - X(o.a), lx: X((o.a + o.z) / 2), lt: 'voladizo ' + f2(o.z - o.a) }));
      const ejes = ap.map((a) => ({ x: X(a.x), eje: a.eje }));

      // corte 1-1 (#91): la línea se arrastra y se pega a los tramos permitidos que manda C#; la franja muestra la
      // profundidad hacia el lado que mira y las flechas, hacia dónde mira.
      const corte = this.props.corte;
      const cutM = this.props.cutLive ?? (corte ? corte.x : p.inicio);
      const prof = corte ? corte.profundidad : 0, dirC = corte && corte.mirando === 'izquierda' ? -1 : 1;
      const ka = k * dirC;
      const cut = { x: X(cutM), c: '#1F5FAF', bandX: Math.min(X(cutM), X(cutM + dirC * prof)), bandW: 52 * prof,
        arrow: (y) => 'M' + X(cutM) + ' ' + y + 'h' + (8 * ka) + 'm' + (-3 * ka) + ' ' + (-3 * k) + 'l' + (3 * ka) + ' ' + (3 * k) + 'l' + (-3 * ka) + ' ' + (3 * k) };
      const permitidos = corte ? corte.permitidos : [];
      const puntos = corte && corte.puntos ? corte.puntos : [];
      // como CalculadoraCorte en C#: de estribo en estribo (AlPunto); sin estribos, el punto permitido más cercano
      // (Ajustar). En un empate, el de la derecha.
      const cercano = (m, candidatos) => {
        let mejor = m, dist = Infinity;
        candidatos.forEach((b) => {
          const d = Math.abs(b - m);
          if (d < dist - 1e-9 || (Math.abs(d - dist) <= 1e-9 && b > mejor)) { mejor = b; dist = d; }
        });
        return mejor;
      };
      const pegar = (m) => {
        if (puntos.length) return cercano(m, puntos);
        if (!permitidos.length || permitidos.some((i) => i.desde - 1e-9 <= m && m <= i.hasta + 1e-9)) return m;
        return cercano(m, permitidos.flatMap((i) => [i.desde, i.hasta]));
      };
      const cutAt = (e) => {
        const { r, k } = scaleOf(this.base);
        return pegar((((e.clientX - r.left) * k - v.tx) / v.z - 30) / 52 + p.inicio);
      };
      const wl = p.traslapos.find((x) => x.id === warnLap);

      // botones de gancho: junto a cada punta del borde del elemento elegido, arriba de las barras de arriba y abajo de
      // las de abajo, sin salirse del alzado; siguen la punta con el zoom
      const ganchos = [];
      const datoSel = selLap ? null : (p.barras.find((x) => x.id === sel) || p.bastones.find((x) => x.id === sel));
      if (datoSel && this.props.onHook && !cur) {
        const esBarra = p.barras.includes(datoSel);
        const dib = (esBarra ? b : t).find((x) => x.id === sel);
        const arriba = esBarra ? dib.isC !== inv : dib.top;
        const tipos = esBarra ? TIPOS.slice(1) : TIPOS;
        const ancho = tipos.length * ANCHO_BOTON + 2;
        [['inicio', datoSel.desde, datoSel.puntaInicioEnBorde, datoSel.ganchoInicioTipo], ['fin', datoSel.hasta, datoSel.puntaFinEnBorde, datoSel.ganchoFinTipo]]
          .filter(([, , enBorde]) => enBorde)
          .forEach(([punta, m, , actual]) => {
            const xs = v.tx + X(m) * v.z, ys = v.ty + (inv ? 170 - dib.y : dib.y) * v.z;
            const clave = esBarra ? datoSel.id[0] + '.' + punta : datoSel.id + '.' + punta;
            const vigente = actual || (esBarra ? '90°' : 'Sin');
            ganchos.push({
              left: Math.max(4, Math.min(W - ancho - 4, punta === 'inicio' ? xs - 6 : xs - ancho + 6)) + 'px',
              // arriba, en la franja sobre la viga, justo encima de los rótulos de las barras (y = 33) y debajo de la leyenda:
              // pegados al elemento tapaban las corridas
              top: Math.max(arriba ? 26 : 4, Math.min(H - 32, arriba ? v.ty + 33 * v.z - 38 : ys + 14)) + 'px',
              label: 'Gancho de la punta ' + (punta === 'inicio' ? 'izquierda' : 'derecha') + ' de ' + datoSel.id,
              botones: tipos.map(([valor, texto, icono], i) => ({ texto, icono, on: texto === vigente, borde: i ? '1px solid #C9CFD6' : '0',
                titulo: valor === 'Sin' ? 'Sin gancho' : 'Gancho de ' + texto, elegir: () => this.props.onHook(clave, valor, texto) })),
            });
          });
      }

      const scaleOf = (el) => { const r = el.getBoundingClientRect(); return { r, k: r.width ? W / r.width : 1 }; };

      // los 3 puntos del elemento elegido (decisiones 6 y 8): izquierda y derecha mueven esa punta; el del centro, todo.
      // Los saltos de cada punto llegan de C# (`arrastre`); un punto sin otro salto sale gris y dice por qué.
      let puntos3 = null, fantasma = null;
      let foot = 'Arrastra para mover · rueda para zoom · doble clic para ajustar · clic en una barra, bastón o traslapo para editarlo; arrastra sus puntos para moverlo · arrastra la línea 1-1 para mover el corte';
      let footCol = '#5E6670';
      if (datoSel && this.props.onDrop) {
        const esBarra = p.barras.some((x) => x.id === sel);
        const dib = (esBarra ? b : t).find((x) => x.id === sel);
        const arr = this.props.arrastre && this.props.arrastre.elemento === sel ? this.props.arrastre : null;
        const bloqueo = (punto) => {
          if (esBarra && (punto === 'Izquierda' ? datoSel.puntaInicioEnBorde : punto === 'Derecha' ? datoSel.puntaFinEnBorde
            : datoSel.puntaInicioEnBorde || datoSel.puntaFinEnBorde)) {
            return punto === 'Centro' ? 'Una barra que llega al borde no se mueve entera' : 'Esta punta llega al borde con su gancho: no se arrastra';
          }
          if (!arr || !arr[punto]) return 'Buscando dónde puede ir…';
          return arr[punto].length > 1 ? ''
            : esBarra ? 'No se puede correr: en ningún otro lugar cumple el traslapo, el nudo y la varilla' : 'No se puede mover';
        };
        const punto = (nombre, m) => {
          const why = bloqueo(nombre), ok = !why;
          return { x: X(m), tip: why || TIP_LISTO[esBarra ? 'barra' : 'baston'][nombre],
            fill: nombre === 'Centro' ? (ok ? '#1F5FAF' : '#C9CFD6') : '#FFFFFF', stroke: ok ? '#1F5FAF' : '#9AA3AD',
            cur: ok ? (nombre === 'Centro' ? 'move' : 'ew-resize') : 'not-allowed',
            down: (e) => {
              e.stopPropagation();
              if (!ok || e.button !== 0) return;
              const cands = arr[nombre];
              let idx = cands.findIndex((c) => !c.cambios.length);
              if (idx < 0) idx = 0;
              e.currentTarget.setPointerCapture(e.pointerId);
              const { k } = scaleOf(e.currentTarget.ownerSVGElement);
              window.addEventListener('keydown', this.esc);
              this.setState({ hd: { sel, cands, idx, cx: e.clientX, x0m: cands[idx].x, k, z: v.z } });
            } };
        };
        puntos3 = { y: dib.y, pts: [punto('Izquierda', datoSel.desde), punto('Centro', (datoSel.desde + datoSel.hasta) / 2), punto('Derecha', datoSel.hasta)] };

        if (cur) {
          // el elemento donde estaba, punteado, y el rótulo con los dos largos donde quedaría
          const viejo = (esBarra ? p0.barras : p0.bastones).find((x) => x.id === sel);
          const top = esBarra ? dib.isC !== inv : dib.top, yS = inv ? 170 - dib.y : dib.y;
          const ti = viejo.ganchoInicio ? viejo.ganchoInicioTipo || '90°' : '', tf = viejo.ganchoFin ? viejo.ganchoFinTipo || '90°' : '';
          fantasma = {
            pts: conGanchos(X(viejo.desde), X(viejo.hasta), dib.y, esBarra ? (dib.isC ? 1 : -1) : (dib.top ? 1 : -1), ti, tf, esBarra ? 22 : 16),
            laps: p0.traslapos.filter((x) => vivos.includes(x.id)).map((x) => ({ x: X(x.desde), w: X(x.hasta) - X(x.desde), y: arribaAntes(x.capa) ? 45 : 106 })),
            lx: (X(datoSel.desde) + X(datoSel.hasta)) / 2, ly: top ? yS - 14 * k : yS + 24 * k, lt: cur.rotulo, col: colVivo,
          };
          if (esBarra) {
            foot = p.traslapos.filter((x) => vivos.includes(x.id)).map((x) => 'Traslapo ' + x.id + ' de ' + f2(x.desde) + ' a ' + f2(x.hasta) + ' m').join(' · ') + ' · ' + cur.rotulo;
            if (cur.enVoladizo) { foot += ' · en el voladizo: al soltar te pide confirmar'; footCol = '#9A5B00'; }
          } else {
            const ini = Math.min(...p.barras.map((x) => x.desde)), fin = Math.max(...p.barras.map((x) => x.hasta));
            const pegado = datoSel.desde <= ini + 1e-6 || datoSel.hasta >= fin - 1e-6;
            foot = sel + ' de ' + f2(datoSel.desde) + ' a ' + f2(datoSel.hasta) + ' m · ' + cur.rotulo + (pegado ? ' · pegado al borde' : '');
          }
        }
      }

      const zoomAt = (mx, my, f) => {
        const z = Math.min(Math.max(v.z * f, fitZ * 0.4), fitZ * 12);
        const s = z / v.z;
        this.setState({ z, rk, tx: mx - (mx - v.tx) * s, ty: my - (my - v.ty) * s });
      };
      return {
        w: W, h: H, wpx: W + 'px', hpx: H + 'px',
        cursor: this.state.drag ? 'grabbing' : 'grab',
        view: 'translate(' + v.tx + ' ' + v.ty + ') scale(' + v.z + ')',
        flip: inv ? 'translate(0 170) scale(1 -1)' : 'translate(0 0)',
        caption: (inv ? 'Cimentación' : 'Aérea') + ' · alzado · vertical ×3',
        b, l, t, st, stx, cols, luces, vol, ejes, cut, ganchos, puntos3, fantasma, foot, footCol, k,
        // la x del mouse pasa a m y se pega al salto más cercano de los que mandó C#
        hdMove: (e) => {
          const h = this.state.hd;
          if (!h) return;
          const raw = h.x0m + (e.clientX - h.cx) * h.k / (52 * h.z);
          let idx = 0, best = Infinity;
          h.cands.forEach((c, i) => { const d = Math.abs(c.x - raw); if (d < best) { best = d; idx = i; } });
          if (idx !== h.idx) this.setState({ hd: { ...h, idx } });
        },
        hdUp: () => this.soltarPunto(true),
        hdCancel: () => this.soltarPunto(false),
        beamX: X(p.inicio), beamW: X(p.fin) - X(p.inicio),
        stVis: showSt ? 'visible' : 'hidden',
        warnText: wl ? 'traslapo ' + f2(wl.hasta - wl.desde) + ' · +' + f2(wl.hasta - wl.desde - wl.minimo) : '',
        warnX: wl ? X((wl.desde + wl.hasta) / 2) : 0, warnY: inv ? 100 : 82,
        zoomPct: Math.round((v.z / fitZ) * 100) + '%',
        stop: (e) => e.stopPropagation(),
        cutDown: (e) => {
          if (e.button !== 0 || !this.props.onCut) return;
          e.stopPropagation();
          e.currentTarget.setPointerCapture(e.pointerId);
          this.setState({ cutDrag: true });
        },
        cutMove: (e) => { if (this.state.cutDrag) this.props.onCut(cutAt(e), false); },
        cutUp: (e) => {
          if (!this.state.cutDrag) return;
          this.setState({ cutDrag: false });
          this.props.onCut(cutAt(e), true);
        },
        down: (e) => {
          if (e.button !== 0 && e.button !== 1) return;
          if (e.button === 1) e.preventDefault();
          const { k } = scaleOf(e.currentTarget);
          this.setState({ drag: { x: e.clientX, y: e.clientY, tx: v.tx, ty: v.ty, z: v.z, k } });
        },
        move: (e) => {
          const d = this.state.drag;
          if (!d) return;
          this.setState({ z: d.z, rk, tx: d.tx + (e.clientX - d.x) * d.k, ty: d.ty + (e.clientY - d.y) * d.k });
        },
        up: () => { if (this.state.drag) this.setState({ drag: null }); },
        wheel: (e) => {
          const { r, k } = scaleOf(e.currentTarget);
          zoomAt((e.clientX - r.left) * k, (e.clientY - r.top) * k, e.deltaY < 0 ? 1.15 : 1 / 1.15);
        },
        zoomIn: () => zoomAt(W / 2, H / 2, 1.25),
        zoomOut: () => zoomAt(W / 2, H / 2, 1 / 1.25),
        fitView: () => this.setState({ z: null, drag: null }),
      };
    }

    render() {
      const v = this.valores();
      const k = v.k, NS = 'non-scaling-stroke';
      const lap = (x) => html`<rect x="${x.x}" y="${x.isC ? 45 : 106}" width="${x.w}" height="18" fill="${x.c}" fill-opacity="${x.o}" stroke="${x.c}" stroke-width="${x.sw}" vector-effect="${NS}" style="cursor: pointer" onClick=${x.pick}></rect>`;
      const barra = (x) => html`<polyline points="${x.pts}" fill="none" stroke="${x.col}" stroke-width="${x.sw}" vector-effect="${NS}" stroke-linecap="round" stroke-linejoin="round" style="cursor: pointer" onClick=${x.pick}></polyline>`;
      const rotulo = (x) => html`<text x="${x.lx}" y="${x.ly}" text-anchor="middle" font-size="${10 * k}" font-family="IBM Plex Mono, monospace" fill="${x.col}">${x.lt}</text>`;
      return html`
<div style="position: relative; width: ${v.wpx}; height: ${v.hpx}; overflow: hidden; background: #FAFBFC; border: 1px solid #D9DDE2; border-radius: 6px; font-family: 'IBM Plex Sans', 'Segoe UI', sans-serif; color: #16191D; cursor: ${v.cursor}; user-select: none; touch-action: none" onPointerDown=${v.down} onPointerMove=${v.move} onPointerUp=${v.up} onPointerLeave=${v.up} onWheel=${v.wheel} onDblClick=${v.fitView}>
  <svg width="${v.w}" height="${v.h}" style="display: block" role="img" aria-label="Alzado del pórtico con barras, traslapos, bastones y estribos">
    <g transform="${v.view}">
      <rect x="${v.beamX}" y="40" width="${v.beamW}" height="90" fill="#F1F3F5" stroke="#9AA3AD" stroke-width="1" vector-effect="${NS}"></rect>
      ${v.vol.map((o) => html`<rect x="${o.x}" y="40" width="${o.w}" height="90" fill="#E9ECEF" stroke="none"></rect>`)}
      <g visibility="${v.stVis}">
        ${v.st.map((z) => html`<line x1="${z.x1}" y1="85" x2="${z.x2}" y2="85" stroke="#C3CAD2" stroke-width="80" stroke-dasharray="${z.da}"></line>`)}
        ${v.stx.map((x) => html`<line x1="${x}" y1="45" x2="${x}" y2="125" stroke="#C3CAD2" stroke-width="1" vector-effect="${NS}"></line>`)}
      </g>
      ${v.cols.map((c) => html`<rect x="${c.x}" y="${c.y}" width="${c.w}" height="${c.h}" fill="#D5DAE0" stroke="#9AA3AD" stroke-width="1" vector-effect="${NS}"></rect>`)}
      ${v.luces.map((m) => html`<line x1="${m.x}" y1="22" x2="${m.x}" y2="158" stroke="#8A939D" stroke-width="1" stroke-dasharray="4 3" vector-effect="${NS}"></line>`)}
      <g transform="${v.flip}">
        ${v.l.filter((x) => x.isC).map(lap)}
        ${v.l.filter((x) => !x.isC).map(lap)}
        ${v.b.filter((x) => x.isC).map(barra)}
        ${v.b.filter((x) => !x.isC).map(barra)}
        ${v.t.filter((x) => x.top).map(barra)}
        ${v.t.filter((x) => !x.top).map(barra)}
        ${v.fantasma ? html`
        ${v.fantasma.laps.map((x) => html`<rect x="${x.x}" y="${x.y}" width="${x.w}" height="18" fill="none" stroke="#9AA3AD" stroke-width="1.2" stroke-dasharray="4 3" vector-effect="${NS}" pointer-events="none"></rect>`)}
        <polyline points="${v.fantasma.pts}" fill="none" stroke="#9AA3AD" stroke-width="1.6" stroke-dasharray="5 4" vector-effect="${NS}" stroke-linecap="round" stroke-linejoin="round" pointer-events="none"></polyline>` : null}
        ${v.puntos3 ? html`
        <g>
          ${v.puntos3.pts.map((o) => html`<circle cx="${o.x}" cy="${v.puntos3.y}" r="${5 * k}" fill="${o.fill}" stroke="${o.stroke}" stroke-width="1.6" vector-effect="${NS}" pointer-events="none"></circle>`)}
          ${v.puntos3.pts.map((o) => html`<circle cx="${o.x}" cy="${v.puntos3.y}" r="${11 * k}" fill="transparent" aria-label=${o.tip} style="cursor: ${o.cur}" onPointerDown=${o.down} onPointerMove=${v.hdMove} onPointerUp=${v.hdUp} onPointerCancel=${v.hdCancel}><title>${o.tip}</title></circle>`)}
        </g>` : null}
      </g>
      ${v.b.filter((x) => x.isC).map(rotulo)}
      ${v.b.filter((x) => !x.isC).map(rotulo)}
      ${v.fantasma ? html`<text x="${v.fantasma.lx}" y="${v.fantasma.ly}" text-anchor="middle" font-size="${12 * k}" font-weight="600" font-family="IBM Plex Mono, monospace" fill="${v.fantasma.col}" stroke="#FAFBFC" stroke-width="${4 * k}" paint-order="stroke" pointer-events="none">${v.fantasma.lt}</text>` : null}
      <text x="${v.warnX}" y="${v.warnY}" text-anchor="middle" font-size="${10 * k}" font-weight="600" font-family="IBM Plex Mono, monospace" fill="#B26B00">${v.warnText}</text>
      <rect x="${v.cut.bandX}" y="40" width="${v.cut.bandW}" height="90" fill="${v.cut.c}" fill-opacity="0.08" pointer-events="none"></rect>
      <line x1="${v.cut.x}" y1="18" x2="${v.cut.x}" y2="152" stroke="${v.cut.c}" stroke-width="1.6" stroke-dasharray="6 3" vector-effect="${NS}" pointer-events="none"></line>
      <path d="${v.cut.arrow(18)}" stroke="${v.cut.c}" stroke-width="1.6" vector-effect="${NS}" fill="none" stroke-linecap="round" stroke-linejoin="round" pointer-events="none"></path>
      <path d="${v.cut.arrow(152)}" stroke="${v.cut.c}" stroke-width="1.6" vector-effect="${NS}" fill="none" stroke-linecap="round" stroke-linejoin="round" pointer-events="none"></path>
      <text x="${v.cut.x}" y="${18 - 6 * k}" text-anchor="middle" font-size="${10 * k}" font-weight="600" font-family="IBM Plex Mono, monospace" fill="${v.cut.c}" pointer-events="none">1-1</text>
      <rect x="${v.cut.x - 7 * k}" y="2" width="${14 * k}" height="158" fill="transparent" style="cursor: ew-resize" aria-label="Línea del corte 1-1: arrástrala para moverla" onPointerDown=${v.cutDown} onPointerMove=${v.cutMove} onPointerUp=${v.cutUp} onPointerCancel=${v.cutUp}></rect>
      ${v.luces.map((m) => html`<text x="${m.x}" y="178" text-anchor="middle" font-size="${9 * k}" font-family="IBM Plex Mono, monospace" fill="#5E6670">${m.lt}</text>`)}
      ${v.vol.map((o) => html`<text x="${o.lx}" y="178" text-anchor="middle" font-size="${9 * k}" font-family="IBM Plex Mono, monospace" fill="#5E6670">${o.lt}</text>`)}
      ${v.ejes.map((e) => html`<circle cx="${e.x}" cy="192" r="${8 * k}" fill="#FFFFFF" stroke="#9AA3AD" vector-effect="${NS}"></circle><text x="${e.x}" y="${192 + 3.5 * k}" text-anchor="middle" font-size="${9 * k}" font-family="IBM Plex Mono, monospace" fill="#3F4A57">${e.eje}</text>`)}
    </g>
  </svg>
  ${v.ganchos.map((g) => html`
  <div role="group" aria-label=${g.label} style="position: absolute; left: ${g.left}; top: ${g.top}; display: flex; border: 1px solid #C9CFD6; border-radius: 6px; overflow: hidden; background: #FFFFFF; box-shadow: 0 2px 6px rgba(22, 25, 29, 0.14)" onPointerDown=${v.stop} onDblClick=${v.stop}>
    ${g.botones.map((o) => html`
    <button type="button" aria-pressed=${o.on ? 'true' : 'false'} title=${o.titulo} onClick=${o.elegir} style="display: flex; align-items: center; justify-content: center; gap: 3px; width: ${ANCHO_BOTON}px; height: 26px; padding: 0; border: 0; border-left: ${o.borde}; font: inherit; font-size: 11px; cursor: pointer; background: ${o.on ? '#1F5FAF' : '#FFFFFF'}; color: ${o.on ? '#FFFFFF' : '#16191D'}">
      <svg width="12" height="12" viewBox="0 0 12 12" aria-hidden="true"><path d=${o.icono} stroke="currentColor" stroke-width="1.4" fill="none" stroke-linecap="round" stroke-linejoin="round"></path></svg>${o.texto}
    </button>`)}
  </div>`)}
  <div style="position: absolute; top: 8px; left: 10px; max-width: ${v.w - 160}px; display: flex; flex-wrap: wrap; gap: 2px 12px; align-items: center; font-family: 'IBM Plex Mono', monospace; font-size: 11px; color: #5E6670; pointer-events: none; white-space: nowrap">
    <span>${v.caption}</span>
    <span style="display: flex; align-items: center; gap: 4px"><span style="width: 14px; height: 3px; background: #3F4A57"></span>corrida</span>
    <span style="display: flex; align-items: center; gap: 4px"><span style="width: 14px; height: 3px; background: #0F7B6C"></span>bastón</span>
    <span style="display: flex; align-items: center; gap: 4px"><span style="width: 12px; height: 8px; background: rgba(199,122,18,0.25); border: 1px solid #C77A12"></span>traslapo</span>
  </div>
  <div style="position: absolute; top: 5px; right: 5px; display: flex; gap: 2px; align-items: center; background: #FFFFFF; border: 1px solid #D9DDE2; border-radius: 6px; padding: 2px" onPointerDown=${v.stop} onDblClick=${v.stop}>
    <button type="button" aria-label="Alejar" onClick=${v.zoomOut} style="width: 28px; height: 28px; border: 0; background: transparent; border-radius: 4px; cursor: pointer; display: grid; place-items: center; color: #16191D">
      <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden="true"><path d="M3 7h8" stroke="currentColor" stroke-width="1.6" fill="none" stroke-linecap="round"></path></svg>
    </button>
    <span style="font-family: 'IBM Plex Mono', monospace; font-size: 11px; min-width: 40px; text-align: center; color: #3F4A57">${v.zoomPct}</span>
    <button type="button" aria-label="Acercar" onClick=${v.zoomIn} style="width: 28px; height: 28px; border: 0; background: transparent; border-radius: 4px; cursor: pointer; display: grid; place-items: center; color: #16191D">
      <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden="true"><path d="M3 7h8M7 3v8" stroke="currentColor" stroke-width="1.6" fill="none" stroke-linecap="round"></path></svg>
    </button>
    <button type="button" aria-label="Ajustar a la vista" onClick=${v.fitView} style="width: 28px; height: 28px; border: 0; background: transparent; border-radius: 4px; cursor: pointer; display: grid; place-items: center; color: #16191D">
      <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden="true"><path d="M2 5V2h3M9 2h3v3M12 9v3H9M5 12H2V9" stroke="currentColor" stroke-width="1.5" fill="none" stroke-linecap="round" stroke-linejoin="round"></path></svg>
    </button>
  </div>
  <div style="position: absolute; bottom: 6px; left: 10px; right: 10px; font-size: 11px; line-height: 1.35; color: ${v.footCol}; pointer-events: none">${v.foot}</div>
</div>`;
    }
  }

  window.Alzado = Alzado;
})();
