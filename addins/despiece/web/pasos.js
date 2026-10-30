// Los tres pasos del despiece: el port de Asistente.dc.html de la maqueta aprobada (#220). No cambiar sin el visto bueno de Juan.
(function () {
  const { html, render, Component } = window.htmPreact;

  class Pasos extends Component {
    constructor(props) {
      super(props);
      this.state = { paso: 1, tipo: 'viga', el: 'AB', sel: null, conRevit: false, hecho: false, ...(props.inicial ?? {}) };
      this.alCambiarTamano = () => this.forceUpdate();
    }

    componentDidMount() {
      window.addEventListener('resize', this.alCambiarTamano);
    }

    componentWillUnmount() {
      window.removeEventListener('resize', this.alCambiarTamano);
    }

    render() {
      const { paso, tipo, el, sel, conRevit, hecho } = this.state;
      // La página llena la ventana (Juan, 30-oct: "no se está usando el 100% de la página"). Abajo de 1440 × 900 la
      // achica el zoom de VentanaDespiece, así que aquí nunca hay menos que eso.
      const ancho = window.innerWidth, alto = window.innerHeight;
      const altoCuerpo = alto - 64 - 68 - 40 - 66; // encabezado, pie, márgenes y título del paso
      const anchoModelo = ancho - 48 - 360 - 16;
      const anchoDibujo = Math.min(ancho - 48 - 320 - 16 - 2, Math.round((altoCuerpo - 60) / 0.54));
      const { elementos: DATA, filas: FILAS } = window.DATOS_EJEMPLO;
      const Despiece = window.Despiece, EnRevit = window.EnRevit;
      const lista = DATA[tipo];
      const act = lista.find((e) => e.id === el) ?? lista[0];
      const set = (o) => this.setState(o);
      const pasos = ['Elegir', 'Revisar', 'Crear vistas'].map((n, i) => {
        const k = i + 1, on = k === paso, ya = k < paso;
        return { n, num: ya ? '✓' : String(k), color: on ? '#16191D' : '#5E6670', fw: on ? 600 : 400, fondo: on || ya ? '#1F5FAF' : '#DDE1E6', numC: on || ya ? '#FFFFFF' : '#3F4A57', pick: () => set({ paso: k }) };
      });
      const tipos = [
        { n: 'Vigas', k: 'viga', icon: 'M3 14h30v8H3zM8 14v8M28 14v8' },
        { n: 'Columnas', k: 'columna', icon: 'M13 3h10v30H13zM13 12h10M13 24h10' },
        { n: 'Viguetas · pronto', off: true, icon: 'M3 12h30M3 18h30M3 24h30' },
        { n: 'Zapatas · pronto', off: true, icon: 'M6 22h24v9H6zM14 22V6h8v16' }
      ].map((t) => {
        const on = t.k === tipo;
        return { n: t.n, icon: t.icon, off: !!t.off, borde: on ? '#1F5FAF' : '#C9CFD6', fondo: on ? '#E4ECF6' : '#FFFFFF', color: on ? '#174A8A' : t.off ? '#6B737C' : '#16191D', cursor: t.off ? 'not-allowed' : 'pointer', pick: () => { if (t.k) set({ tipo: t.k, el: DATA[t.k][0].id, sel: null }); } };
      });
      const lst = lista.map((e) => ({ ...e, borde: e.id === act.id ? '#1F5FAF' : '#D9DDE2', fondo: e.id === act.id ? '#E4ECF6' : '#FFFFFF', pick: () => set({ el: e.id }) }));
      const filas = FILAS[tipo === 'viga' && act.id === '1' ? 'viga1' : tipo].map((f) => ({ ...f, borde: f.id === sel ? '#1F5FAF' : '#D9DDE2', fondo: f.id === sel ? '#E4ECF6' : '#FFFFFF', pick: () => set({ sel: f.id === sel ? null : f.id }) }));
      const T = {
        1: ['¿Qué vas a despiezar?', 'Elige en la lista o haz clic en el modelo. Los que llevan el mismo acero salen juntos.'],
        2: ['Revisa el despiece', 'Haz clic en una barra del dibujo o de la lista para resaltarla.'],
        3: ['Estas vistas se crearán', 'Quedan en el navegador de Revit, con etiquetas y colores. Tú las arrastras a tu plano.']
      };
      const titulo = T[paso][0], ayuda = T[paso][1];
      const textoAtras = paso === 1 ? 'Cerrar' : 'Atrás';
      const textoSig = paso === 3 ? (hecho ? 'Despiezar otro' : 'Crear vistas') : 'Siguiente';
      const textoRevit = conRevit ? 'Ocultar la vista en Revit' : 'Ver en Revit';
      const verRevit = () => set({ conRevit: !conRevit });
      const noop = () => {};
      const pickEl = (id) => set({ el: id });
      const pickBar = (id) => set({ sel: id === sel ? null : id });
      const back = () => {
        if (paso > 1) set({ paso: paso - 1, hecho: false });
        else window.chrome?.webview?.postMessage({ tipo: 'cerrar' });
      };
      const next = () => {
        if (paso < 3) return set({ paso: paso + 1 });
        if (hecho) return set({ paso: 1, hecho: false, sel: null });
        set({ hecho: true });
      };
      const aplica = act.aplica;

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
                <div style="display: flex; flex-direction: column; gap: 12px; min-height: 0">
                  <div style="display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px">
                    ${tipos.map((t) => html`
                      <button type="button" disabled=${t.off} onClick=${t.pick} style="display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 6px; height: 92px; border: 1px solid ${t.borde}; border-radius: 6px; background: ${t.fondo}; color: ${t.color}; font: inherit; font-weight: 500; cursor: ${t.cursor}">
                        <svg width="36" height="36" viewBox="0 0 36 36" aria-hidden="true"><path d=${t.icon} stroke="currentColor" stroke-width="2" fill="none" stroke-linejoin="round"></path></svg>${t.n}
                      </button>`)}
                  </div>
                  <div style="flex: 1; min-height: 0; overflow: auto; display: flex; flex-direction: column; gap: 6px">
                    ${lst.map((e) => html`
                      <button type="button" onClick=${e.pick} style="display: flex; flex-direction: column; gap: 3px; padding: 10px 12px; text-align: left; border: 1px solid ${e.borde}; border-radius: 6px; background: ${e.fondo}; font: inherit; color: #16191D; cursor: pointer">
                        <span style="font-weight: 600">${e.n}</span><span style="font-size: 12px; color: #5E6670">${e.sub}</span>
                      </button>`)}
                  </div>
                </div>
                <${EnRevit} tipo=${tipo} modo="elegir" el=${act.id} aplica=${aplica} onEl=${pickEl} onTodas=${next} onTerminar=${next} onCancelar=${noop} w=${anchoModelo} h=${altoCuerpo} />
              </div>` : null}

            ${paso === 2 ? html`
              <div style="flex: 1; min-height: 0; display: grid; grid-template-columns: minmax(0, 1fr) 320px; gap: 16px">
                <div style="display: flex; flex-direction: column; min-width: 0; background: #FFFFFF; border: 1px solid #AEB5BD">
                  <div style="height: 30px; flex-shrink: 0; display: flex; align-items: center; padding: 0 10px; background: #3B4450; color: #FFFFFF; font-size: 12px">Revit · Sección: ${aplica}</div>
                  <div style="flex: 1; min-height: 0; display: grid; place-items: center">
                    <${Despiece} tipo=${tipo} el=${act.id} sel=${sel} onPick=${pickBar} aplica=${aplica} w=${anchoDibujo} h=${Math.round(anchoDibujo * 0.54)} />
                  </div>
                  <div style="height: 28px; flex-shrink: 0; display: flex; align-items: center; gap: 14px; padding: 0 10px; background: #E9ECEF; border-top: 1px solid #AEB5BD; font-size: 12px; color: #3F4A57">
                    <span>1 : 50</span><span>Detalle medio</span><span>Estructura alámbrica</span>
                  </div>
                </div>
                <div style="display: flex; flex-direction: column; gap: 8px; min-height: 0; overflow: auto">
                  <span style="font-weight: 600">Barras de ${aplica}</span>
                  ${filas.map((f) => html`
                    <button type="button" onClick=${f.pick} style="display: grid; grid-template-columns: 14px minmax(0, 1fr); gap: 10px; align-items: center; padding: 10px; text-align: left; border: 1px solid ${f.borde}; border-radius: 6px; background: ${f.fondo}; font: inherit; color: #16191D; cursor: pointer">
                      <span style="width: 14px; height: 14px; border-radius: 3px; background: ${f.c}"></span>
                      <span style="display: flex; flex-direction: column; gap: 2px; min-width: 0"><span style="font-family: 'IBM Plex Mono', monospace; font-size: 13px">${f.et}</span><span style="font-size: 12px; color: #5E6670">${f.d}</span></span>
                    </button>`)}
                  <button type="button" onClick=${verRevit} style="height: 40px; flex-shrink: 0; border: 1px solid #C9CFD6; border-radius: 6px; background: #FFFFFF; font: inherit; color: #16191D; cursor: pointer">${textoRevit}</button>
                  ${conRevit ? html`<${EnRevit} tipo=${tipo} modo="previa" el=${act.id} aplica=${aplica} w=${320} h=${240} />` : null}
                </div>
              </div>` : null}

            ${paso === 3 ? html`
              <${EnRevit} tipo=${tipo} modo="hoja" el=${act.id} aplica=${aplica} w=${ancho - 48} h=${altoCuerpo} />` : null}
          </div>

          <div style="height: 68px; flex-shrink: 0; display: flex; align-items: center; justify-content: space-between; padding: 0 24px; background: #FFFFFF; border-top: 1px solid #D9DDE2">
            <button type="button" onClick=${back} style="height: 44px; padding: 0 20px; border: 1px solid #C9CFD6; border-radius: 6px; background: #FFFFFF; font: inherit; color: #16191D; cursor: pointer">${textoAtras}</button>
            ${hecho ? html`<span style="color: #1E7A3C; font-weight: 500">Vistas creadas. Arrástralas a tu plano.</span>` : null}
            <button type="button" onClick=${next} style="height: 44px; padding: 0 24px; border: 0; border-radius: 6px; background: #1F5FAF; color: #FFFFFF; font: inherit; font-weight: 600; cursor: pointer">${textoSig}</button>
          </div>
        </div>`;
    }
  }

  window.Pasos = Pasos;
  const destino = document.getElementById('app');
  if (destino) render(html`<${Pasos} />`, destino);
})();
