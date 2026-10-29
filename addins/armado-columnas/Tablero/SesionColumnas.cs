namespace practicacq.Tablero;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using practicacq.Modelo;

/// <summary>
/// El estado de la ventana web de columnas (#205): qué columnas están
/// elegidas, cuáles están listas para generar y qué eligió el usuario en
/// cada una. Atiende los mensajes de la página (<see cref="Atender"/>) y
/// devuelve la <see cref="VistaColumnas"/> entera para que la redibuje.
///
/// No conoce Revit: lo que necesita de Revit lo deja pedido
/// (<see cref="SeleccionPedida"/>, <see cref="Pedido3D"/>,
/// <see cref="GeneracionPedida"/>) y la ventana se encarga. Así se prueba
/// entera sin abrir Revit (tools/PruebasTableroColumnas).
///
/// Reglas (decisiones del plan docs/planes/2026-10-29-interfaz-web-columnas.md):
/// todo cambio cae en las columnas elegidas compatibles con la primera (#63)
/// y las deja «listas» (7 y 8); «Generar» arma las listas.
/// </summary>
public sealed class SesionColumnas
{
    private readonly IProveedorContexto _contexto;
    private readonly IProveedorTipos _tipos;
    private readonly IAlmacenConfiguraciones _almacen;
    private readonly IReadOnlyList<TipoBarra> _barras;

    /// <summary>Solo columnas: las columnetas no van en esta ventana (decisión 3).</summary>
    private readonly List<ColumnaResumen> _columnas;
    private readonly Dictionary<string, ColumnaContexto> _leidas = new();

    private readonly Dictionary<string, OpcionesColumna> _opciones = new();
    private readonly HashSet<string> _listas = new();
    private readonly Dictionary<string, ColumnaGenerada> _generadas = new();
    private List<string> _elegidas = new();

    /// <summary>Lo que se muestra en una columna sin opciones propias: lo último que se cambió, o los valores por defecto.</summary>
    private OpcionesColumna _actuales;

    private string? _configuracion;
    private string? _aviso;
    private List<string> _choques = new();
    private bool _pendiente;
    private List<string>? _resultado;
    private List<string> _avisosResultado = new();
    private string? _error;
    private PlanArmado? _planEnviado;

    public SesionColumnas(
        IProveedorContexto contexto, IProveedorTipos tipos,
        IAlmacenConfiguraciones? almacen = null, IRegistroGenerados? registro = null)
    {
        _contexto = contexto;
        _tipos = tipos;
        _almacen = almacen ?? new AlmacenConfiguracionesEnMemoria();
        _barras = tipos.ListarBarras();
        _columnas = contexto.ListarColumnas().Where(c => !c.EsColumneta).ToList();
        _actuales = PorDefecto();

        // #180: las ya generadas arrancan con lo que se usó al generar.
        IRegistroGenerados registroGenerados = registro ?? new RegistroGeneradosEnMemoria();
        foreach (ColumnaResumen resumen in _columnas)
        {
            if (registroGenerados.Leer(resumen.Id) is not { AlgunTramo: true } generada) continue;
            _generadas[resumen.Id] = generada;
            _opciones[resumen.Id] = DesdeGenerada(generada, Leer(resumen.Id));
        }
    }

    // ------------------------------------------------------------- pedidos a Revit

    /// <summary>La página pidió «Seleccionar en Revit». La ventana lo atiende y lo vuelve a false.</summary>
    public bool SeleccionPedida { get; set; }

    /// <summary>
    /// La vista 3D que hay que dibujar, con <see cref="OpcionesPara3D"/>. La
    /// pide la página (mensaje <c>ver3D</c>) y, desde ahí, cada cambio la
    /// vuelve a pedir: «se actualiza sola». La ventana la dibuja y lo vuelve a null.
    /// </summary>
    public FiltroPrevia3D? Pedido3D { get; set; }

    /// <summary>El pedido 3D vino del menú «Ver en 3D»: además de dibujar, hay que abrir la vista. La ventana lo vuelve a false.</summary>
    public bool Abrir3D { get; set; }

    /// <summary>El último filtro del menú «Ver en 3D»; null si la vista 3D no se abrió.</summary>
    private FiltroPrevia3D? _filtro3D;

    /// <summary>Lo que hay que generar, cuando la página pidió «Generar» y no hay choques sin confirmar. La ventana lo vuelve a null.</summary>
    public OpcionesArmado? GeneracionPedida { get; set; }

    // ------------------------------------------------------------- vista

    public VistaColumnas Vista()
    {
        string? activa = Activa;
        ColumnaContexto? columna = activa is null ? null : Leer(activa);
        OpcionesColumna opciones = activa is null ? _actuales : OpcionesDe(activa);
        (List<string> compatibles, List<string> descartadas) = Compatibles();

        ColumnaVista? vistaColumna = null;
        if (activa is not null && columna is not null)
        {
            var avisos = new List<string>();
            if (EstadoDeLoGenerado(activa, columna) is string generada) avisos.Add(generada);
            vistaColumna = ArmadorVistaColumnas.Columna(
                activa, columna, Resumen(activa)?.MarcaUbicacion ?? "", opciones, BarraDe, avisos);
        }

        return new VistaColumnas(
            new PlantaVista(
                _contexto.ListarRejillas(),
                _columnas.Select(c => new ColumnaEnPlanta(c.Id, c.Marca, c.MarcaUbicacion, c.X, c.Y, c.LadoX, c.LadoY, Estado(c.Id))).ToList()),
            _elegidas.ToList(),
            compatibles,
            descartadas.Select(id => Leer(id).Marca).ToList(),
            vistaColumna,
            opciones.AVista(columna),
            Catalogos(),
            _configuracion,
            new GeneracionVista(
                _choques.ToList(), _pendiente,
                _resultado is null ? null : new ResultadoVista(_resultado.ToList(), _avisosResultado.ToList()),
                _error,
                _columnas.Where(c => _listas.Contains(c.Id)).Select(c => c.Marca).ToList()),
            _aviso);
    }

    // ------------------------------------------------------------- mensajes

    public VistaColumnas Atender(string json)
    {
        using JsonDocument documento = JsonDocument.Parse(json);
        JsonElement m = documento.RootElement;
        _aviso = null;

        switch (Texto(m, "tipo"))
        {
            case "elegirColumnas":
                _elegidas = m.GetProperty("ids").EnumerateArray().Select(e => e.GetString() ?? "")
                    .Where(id => _columnas.Any(c => c.Id == id)).Distinct().ToList();
                _configuracion = null;
                break;

            case "quitarDeLista":
                QuitarDeLista(Texto(m, "id") ?? "");
                break;

            case "seleccionarEnRevit":
                SeleccionPedida = true;
                break;

            case "cambiarLongitudinal":
                Cambiar(o => CambiarLongitudinal(o.Longitudinal, m.GetProperty("config")));
                break;

            case "cambiarRecubrimiento":
                Cambiar(o => o.Recubrimiento = m.GetProperty("valor").GetDouble());
                break;

            case "cambiarEstribos":
                Cambiar(o => CambiarEstribos(o.Estribos, m));
                break;

            case "menor":
                Cambiar(o => CambiarMenor(o.Longitudinal.EstribosMenores, m));
                break;

            case "ver3D":
                _filtro3D = Filtro3D(Texto(m, "filtro"));
                Abrir3D = true;
                break;

            case "aplicarConfiguracion":
                AplicarConfiguracion(Texto(m, "nombre") ?? "");
                break;

            case "guardarConfiguracion":
                GuardarConfiguracion(Texto(m, "nombre") ?? "", m.TryGetProperty("comoNueva", out JsonElement nueva) && nueva.GetBoolean());
                break;

            case "borrarConfiguracion":
            {
                string nombre = Texto(m, "nombre") ?? "";
                _almacen.Borrar(nombre);
                if (_configuracion == nombre) _configuracion = null;
                _aviso = $"Se eliminó «{nombre}» del modelo.";
                break;
            }

            case "generar":
                Generar(Booleano(m, "borrarAjeno"), Booleano(m, "aunConChoques"));
                break;

            case "cancelarGenerar":
                _choques = new();
                _pendiente = false;
                _resultado = null;
                _error = null;
                break;
        }

        if (_filtro3D is not null && Activa is not null) Pedido3D = _filtro3D.Copiar();
        return Vista();
    }

    /// <summary>Un aviso que viene de Revit (algo falló al atender un pedido), con la vista entera.</summary>
    public VistaColumnas Avisar(string texto)
    {
        _aviso = texto;
        return Vista();
    }

    /// <summary>
    /// La columna activa con sus opciones de ahora, listas o no: es solo para
    /// mirar en 3D, no se genera nada. null sin columna elegida.
    /// </summary>
    public OpcionesArmado? OpcionesPara3D()
    {
        if (Activa is not string activa) return null;

        ColumnaContexto columna = Leer(activa);
        OpcionesColumna o = OpcionesDe(activa);
        return new OpcionesArmado
        {
            Plan = new PlanArmado
            {
                Columnas =
                {
                    new ColumnaPlan
                    {
                        Id = activa, Contexto = columna, Tramos = o.Estribos.ATramos(columna),
                        Longitudinal = o.Longitudinal.Copiar(), Recubrimiento = o.RecubrimientoElegido,
                    },
                },
            },
        };
    }

    /// <summary>
    /// Las columnas que el usuario eligió en Revit con «Seleccionar en Revit». <paramref name="ids"/> son elementos de
    /// Revit, de cualquier piso (hallazgo 12).
    /// </summary>
    public VistaColumnas SincronizarDesdeRevit(IEnumerable<string> ids)
    {
        _elegidas = ColumnasDe(ids);
        _configuracion = null;
        if (_filtro3D is not null && Activa is not null) Pedido3D = _filtro3D.Copiar();
        return Vista();
    }

    /// <summary>Las columnas a las que pertenecen esos elementos de Revit, sin repetir (hallazgo 12).</summary>
    public List<string> ColumnasDe(IEnumerable<string> elementos)
    {
        HashSet<string> buscados = elementos.ToHashSet();
        return _columnas.Where(c => c.IdsEnRevit.Any(buscados.Contains)).Select(c => c.Id).ToList();
    }

    /// <summary>Los elementos de Revit de todas las columnas de la planta, de todos los pisos (para «Todas» en la barra de selección).</summary>
    public IReadOnlyList<string> ElementosEnRevit => _columnas.SelectMany(c => c.IdsEnRevit).ToList();

    // ------------------------------------------------------------- cambios

    private string? Activa => _elegidas.FirstOrDefault();

    private OpcionesColumna OpcionesDe(string id) => _opciones.TryGetValue(id, out OpcionesColumna? o) ? o : _actuales;

    /// <summary>
    /// Lo que cambia el usuario cae en todas las elegidas compatibles con la
    /// activa y las deja «listas» (decisiones 7 y 8). Cada una con su copia:
    /// cambiar una sola después no tiene que cambiar las demás.
    /// </summary>
    private void Cambiar(Action<OpcionesColumna> cambio)
    {
        if (Activa is not string activa) return;

        OpcionesColumna nueva = OpcionesDe(activa).Copiar();
        cambio(nueva);
        _actuales = nueva.Copiar();
        foreach (string id in Compatibles().Compatibles)
        {
            _opciones[id] = nueva.Copiar();
            _listas.Add(id);
        }
        _resultado = null;
        _error = null;
    }

    private (List<string> Compatibles, List<string> Descartadas) Compatibles()
    {
        if (Activa is not string activa) return (new(), new());

        ColumnaContexto referencia = Leer(activa);
        var compatibles = new List<string>();
        var descartadas = new List<string>();
        foreach (string id in _elegidas)
            (id == activa || CompatibilidadColumnas.MismaEstructura(referencia, Leer(id)) ? compatibles : descartadas).Add(id);
        return (compatibles, descartadas);
    }

    private void QuitarDeLista(string id)
    {
        if (!_listas.Remove(id)) return;

        // Vuelve a lo que tenía antes: lo generado, o nada.
        if (_generadas.TryGetValue(id, out ColumnaGenerada? generada)) _opciones[id] = DesdeGenerada(generada, Leer(id));
        else _opciones.Remove(id);
    }

    /// <summary>Lo que manda la página en <c>cambiarLongitudinal</c>: solo los campos que cambiaron, con las barras de CADA cara.</summary>
    private static void CambiarLongitudinal(ConfigLongitudinal cfg, JsonElement c)
    {
        if (c.TryGetProperty("barrasPorCaraX", out JsonElement x)) cfg.BarrasPorCaraX = x.GetInt32() * 2;
        if (c.TryGetProperty("barrasPorCaraY", out JsonElement y)) cfg.BarrasPorCaraY = y.GetInt32() * 2;
        if (c.TryGetProperty("tipoBarraEsquinasNombre", out JsonElement esq)) cfg.TipoBarraEsquinasNombre = esq.GetString();
        if (c.TryGetProperty("tipoBarraCaraXNombre", out JsonElement bx)) cfg.TipoBarraCaraXNombre = bx.GetString();
        if (c.TryGetProperty("tipoBarraCaraYNombre", out JsonElement by)) cfg.TipoBarraCaraYNombre = by.GetString();
        if (c.TryGetProperty("longitudComercialBarra", out JsonElement largo)) cfg.LongitudComercialBarra = largo.GetDouble();
        if (c.TryGetProperty("redondeoVarillas", out JsonElement redondeo)) cfg.RedondeoVarillas = redondeo.GetDouble();
        if (c.TryGetProperty("solapeMayorDeLosDos", out JsonElement solape)) cfg.SolapeMayorDeLosDos = solape.GetBoolean();
    }

    private static void CambiarEstribos(ConfigEstribosColumna estribos, JsonElement m)
    {
        if (m.TryGetProperty("tramosLibres", out JsonElement t))
        {
            ConfigTramo c = estribos.TramosLibres;
            if (t.TryGetProperty("modo", out JsonElement modo)) c.Modo = Enum.Parse<ModoEstribo>(modo.GetString() ?? nameof(ModoEstribo.Uniforme));
            if (t.TryGetProperty("tipoBarraNombre", out JsonElement barra)) c.TipoBarraNombre = barra.GetString();
            if (t.TryGetProperty("espaciadoUniforme", out JsonElement u)) c.EspaciadoUniforme = u.GetDouble();
            if (t.TryGetProperty("longitudConfinamiento", out JsonElement l1)) c.LongitudConfinamiento = l1.GetDouble();
            if (t.TryGetProperty("espaciadoConfinado", out JsonElement conf)) c.EspaciadoConfinado = conf.GetDouble();
            if (t.TryGetProperty("espaciadoNoConfinado", out JsonElement centro)) c.EspaciadoNoConfinado = centro.GetDouble();
        }

        if (m.TryGetProperty("nudoEspaciado", out JsonElement nudo)) estribos.NudoEspaciado = nudo.GetDouble();

        if (m.TryGetProperty("excepcion", out JsonElement e))
        {
            int indice = e.GetProperty("indice").GetInt32();
            if (e.TryGetProperty("config", out JsonElement cfg) && cfg.ValueKind == JsonValueKind.Object)
                estribos.Excepciones[indice] = cfg.Deserialize<ConfigTramo>(VistaColumnas.Json)!;
            else
                estribos.Excepciones.Remove(indice);
        }
    }

    private static void CambiarMenor(List<EstriboMenor> piezas, JsonElement m)
    {
        string? accion = Texto(m, "accion");
        if (accion == "agregar")
        {
            int desde = m.GetProperty("desde").GetInt32(), hasta = m.GetProperty("hasta").GetInt32();
            piezas.Add(new EstriboMenor
            {
                Forma = Enum.Parse<FormaMenor>(Texto(m, "forma") ?? nameof(FormaMenor.GrapaC)),
                Eje = Enum.Parse<EjeCaja>(Texto(m, "eje") ?? nameof(EjeCaja.Fila)),
                Desde = Math.Min(desde, hasta),
                Hasta = Math.Max(desde, hasta),
            });
            return;
        }

        int indice = m.TryGetProperty("indice", out JsonElement i) ? i.GetInt32() : -1;
        if (indice < 0 || indice >= piezas.Count) return;
        EstriboMenor pieza = piezas[indice];
        switch (accion)
        {
            case "eliminar": piezas.RemoveAt(indice); break;
            case "voltear": pieza.Volteado = !pieza.Volteado; break;
            case "varilla": pieza.TipoBarraNombre = Texto(m, "nombre") is { Length: > 0 } nombre ? nombre : null; break;
            case "angulo":
                var angulo = (AnguloGancho)m.GetProperty("angulo").GetInt32();
                if (Texto(m, "punta") == "inicio") pieza.AnguloInicio = angulo;
                else pieza.AnguloFin = angulo;
                break;
        }
    }

    // ------------------------------------------------------------- configuraciones guardadas

    private void AplicarConfiguracion(string nombre)
    {
        if (_almacen.Listar().FirstOrDefault(g => g.Nombre == nombre) is not { } guardada) return;

        var avisos = new List<string> { $"Se aplicó «{nombre}»." };
        Cambiar(o =>
        {
            ConfigLongitudinal longitudinal = guardada.Longitudinal.Copiar();
            if (longitudinal.MigrarBarrasPorCara())
                avisos.Add("Era una configuración con barras por tipo A/B: las esquinas tomaron la barra A y las caras la B; revísalas.");
            longitudinal.MigrarEstribosMenores();
            if (longitudinal.DescartarMenoresFueraDeRango() is int descartadas and > 0)
                avisos.Add($"Se quitaron {descartadas} grapa(s) o estribo(s) cerrado(s) que apuntaban a barras que esta configuración ya no tiene.");

            // Una barra que no es apta para estribo (#64) pasa a la de por defecto, avisando.
            ConfigEstribosColumna estribos = guardada.EstribosDeLaColumna();
            if (estribos.TramosLibres.TipoBarraNombre is string barra && ValoresPorDefecto.BarrasAptas(_barras).All(b => b.Nombre != barra))
            {
                estribos.TramosLibres.TipoBarraNombre = ValoresPorDefecto.BarraPorDefecto(_tipos, ValoresPorDefecto.NumeroVarillaEstribo);
                avisos.Add($"«{barra}» no es apta para estribo según el cuadro; se cambió por «{estribos.TramosLibres.TipoBarraNombre}».");
            }

            o.Longitudinal = longitudinal;
            o.Estribos = estribos;
        });
        _configuracion = nombre;
        _aviso = string.Join(" ", avisos);
    }

    /// <summary>
    /// Se escriben los dos campos (decisión 9): <c>Estribos</c> para esta
    /// ventana y <c>Tramo</c> (= los tramos libres) para que una 1.4.1 que
    /// lea el modelo siga funcionando.
    /// </summary>
    private void GuardarConfiguracion(string nombre, bool comoNueva)
    {
        if (Activa is not string activa || string.IsNullOrWhiteSpace(nombre)) return;

        OpcionesColumna o = OpcionesDe(activa).Copiar();
        _almacen.Guardar(new ConfiguracionGuardada
        {
            Nombre = nombre.Trim(),
            Tramo = o.Estribos.Copiar().TramosLibres,
            Longitudinal = o.Longitudinal,
            Estribos = o.Estribos,
        });
        _configuracion = nombre.Trim();
        _aviso = comoNueva ? $"Se guardó «{_configuracion}» en el modelo." : $"Se guardaron los cambios de «{_configuracion}».";
    }

    // ------------------------------------------------------------- generar

    /// <summary>
    /// El plan de las columnas listas, con los estribos traducidos a tramos
    /// (<see cref="ConfigEstribosColumna.ATramos"/>). null si no hay ninguna.
    /// </summary>
    public OpcionesArmado? OpcionesParaGenerar(bool borrarAjeno)
    {
        var plan = new PlanArmado();
        foreach (ColumnaResumen resumen in _columnas.Where(c => _listas.Contains(c.Id)))
        {
            ColumnaContexto columna = Leer(resumen.Id);
            OpcionesColumna o = OpcionesDe(resumen.Id);
            plan.Columnas.Add(new ColumnaPlan
            {
                Id = resumen.Id,
                Contexto = columna,
                Tramos = o.Estribos.ATramos(columna),
                Longitudinal = o.Longitudinal.Copiar(),
                Recubrimiento = o.RecubrimientoElegido,
            });
        }

        return plan.Columnas.Count == 0 ? null : new OpcionesArmado { Plan = plan, BorrarArmadoAjeno = borrarAjeno };
    }

    private void Generar(bool borrarAjeno, bool aunConChoques)
    {
        _resultado = null;
        _error = null;
        if (OpcionesParaGenerar(borrarAjeno) is not OpcionesArmado opciones)
        {
            _aviso = "No hay columnas listas: cambia algo en una columna para que quede lista.";
            return;
        }

        // #131: si algo choca, se avisa y se deja seguir. Un error al revisar
        // nunca impide generar: se genera como si no hubiera choques.
        List<string> choques;
        try { choques = Choques(opciones.Plan); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[#131] No se pudieron revisar los choques antes de generar: {ex}");
            choques = new();
        }

        if (choques.Count > 0 && !aunConChoques)
        {
            _choques = choques;
            _pendiente = true;
            return;
        }

        _choques = new();
        _pendiente = false;
        _planEnviado = opciones.Plan;
        GeneracionPedida = opciones;
    }

    /// <summary>
    /// Los choques de todas las columnas que se van a generar (#131), como
    /// <c>VentanaConfiguradorEstribos.ChoquesDelPlan</c>: un aviso por par en
    /// toda la columna, mandando el tramo con el estribo más grueso.
    /// </summary>
    private List<string> Choques(PlanArmado plan)
    {
        var choques = new List<string>();
        foreach (ColumnaPlan columna in plan.Columnas)
        {
            if (columna.Contexto is not ColumnaContexto contexto || columna.Longitudinal is not ConfigLongitudinal longitudinal) continue;

            double recubrimiento = columna.Recubrimiento ?? contexto.Recubrimiento;
            double estriboMasGrueso = columna.Tramos.Select(t => BarraDe(t?.TipoBarraNombre)?.DiametroMm ?? 0).DefaultIfEmpty(0).Max();

            List<ChoquesSeccion.Resultado> porTramo = Enumerable
                .Range(0, Math.Min(columna.Tramos.Count, contexto.Tramos.Count))
                .Where(i => columna.Tramos[i] is not null)
                .OrderBy(i => contexto.Tramos[i].Tipo == TipoTramo.ContraZapata ? 1 : 0)
                .ThenByDescending(i => BarraDe(columna.Tramos[i]!.TipoBarraNombre)?.DiametroMm ?? 0)
                .Select(i => ChoquesSeccion.DeTramo(
                    contexto, contexto.Tramos[i], columna.Tramos[i]!, longitudinal, recubrimiento, BarraDe, estriboMasGrueso))
                .ToList();

            List<string> deEsta = ChoquesSeccion.DeLaColumna(porTramo).Select(c => $"{contexto.Marca}: {c.Motivo}").ToList();
            int sinRevisar = porTramo.Select(r => r.VarillasSinRevisarEnTraslapo).DefaultIfEmpty(0).Max();
            if (deEsta.Count > 0 && sinRevisar > 0) deEsta.Add($"{contexto.Marca}: {ChoquesSeccion.NotaSinRevisar(sinRevisar)}");
            choques.AddRange(deEsta);
        }
        return choques;
    }

    /// <summary>Cuando Revit terminó de generar lo que pidió <see cref="GeneracionPedida"/>.</summary>
    public VistaColumnas ResultadoGeneracion(ResultadoArmado resultado)
    {
        // La generación cambió el documento: lo leído antes ya no sirve (ver IProveedorContexto.InvalidarCache).
        OlvidarLoLeido();

        if (_planEnviado is not null)
        {
            List<string> marcas = new();
            foreach (ColumnaPlan columna in _planEnviado.Columnas)
            {
                _generadas.TryGetValue(columna.Id, out ColumnaGenerada? previa);
                _generadas[columna.Id] = ColumnaGenerada.Combinar(previa, columna, _planEnviado.GeneradoEl);
                _listas.Remove(columna.Id);
                marcas.Add(columna.Contexto?.Marca ?? columna.Id);
            }

            _resultado = new List<string>
            {
                (marcas.Count == 1 ? $"Se armó la columna {marcas[0]}" : $"Se armaron {marcas.Count} columnas: {string.Join(", ", marcas)}")
                    + (resultado.Avisos.Count > 0 ? ", pero con avisos: revísalos abajo." : "."),
                resultado.Eliminados > 0
                    ? $"Se reemplazaron {resultado.Eliminados} elemento(s) de acero que había antes."
                    : "No había acero anterior en esas columnas.",
            };
            _avisosResultado = resultado.Avisos.ToList();
        }
        _planEnviado = null;
        return Vista();
    }

    /// <summary>Cuando la generación tiró una excepción no anticipada.</summary>
    public VistaColumnas ErrorGeneracion(string mensaje)
    {
        // Aunque haya fallado, la corrida pudo cambiar el documento antes de tirar la excepción.
        OlvidarLoLeido();
        _planEnviado = null;
        _error = mensaje;
        return Vista();
    }

    // ------------------------------------------------------------- ayudas

    /// <summary>
    /// Los de la ventana vieja, pero con los tramos libres en confinado
    /// (Luciano, 29-oct: decisión 14 del plan). Los nudos arrancan con el
    /// espaciado del centro (decisión 5).
    /// </summary>
    private OpcionesColumna PorDefecto()
    {
        ConfigTramo tramo = ValoresPorDefecto.Tramo(_tipos);
        tramo.Modo = ModoEstribo.Confinado;
        return new OpcionesColumna
        {
            Longitudinal = ValoresPorDefecto.Longitudinal(_tipos),
            Estribos = ConfigEstribosColumna.Desde(tramo),
        };
    }

    private OpcionesColumna DesdeGenerada(ColumnaGenerada generada, ColumnaContexto columna)
    {
        ConfigLongitudinal longitudinal = generada.Longitudinal.Copiar();
        longitudinal.MigrarEstribosMenores();
        return new OpcionesColumna
        {
            Longitudinal = longitudinal,
            Estribos = ConfigEstribosColumna.DesdeTramos(columna, generada.Tramos, ValoresPorDefecto.Tramo(_tipos)),
            Recubrimiento = generada.Recubrimiento,
        };
    }

    private string Estado(string id) =>
        _listas.Contains(id) ? "lista" : _generadas.ContainsKey(id) ? "generada" : "sinConfigurar";

    /// <summary>#180: el aviso de una columna ya generada, o null si no se generó.</summary>
    private string? EstadoDeLoGenerado(string id, ColumnaContexto columna)
    {
        if (!_generadas.TryGetValue(id, out ColumnaGenerada? generada) || _listas.Contains(id)) return null;

        string texto = $"{columna.Marca} ya se generó ({generada.GeneradoEl:dd/MM/yyyy HH:mm}): se muestra la configuración con la que se generó.";
        IReadOnlyList<string> cambios = generada.Cambios(columna);
        if (cambios.Count > 0)
            texto += $" Ojo: la columna cambió en Revit desde entonces ({string.Join("; ", cambios)}). Revísala antes de volver a generar.";
        return texto;
    }

    private CatalogosVista Catalogos()
    {
        VarillaVista Varilla(TipoBarra b) => new(b.Nombre, b.DiametroMm, ArmadorVistaColumnas.Color(b.DiametroMm));
        return new CatalogosVista(
            _barras.Select(Varilla).ToList(),
            ValoresPorDefecto.BarrasAptas(_barras).Select(Varilla).ToList(),
            new[] { 6.0, 12.0 },
            PlanTraslapos.Redondeos,
            CuadroVarillas.Cuadro.Select(v => new FilaCuadroVista(
                v.Numero, $"#{v.Numero}", v.DiametroMm, ArmadorVistaColumnas.Color(v.DiametroMm), v.Traslapo,
                v.Gancho90, v.Gancho90Cuadro.DiametroDoblado, v.Gancho180Cuadro.Extension, v.Gancho180Cuadro.DiametroDoblado,
                v.GanchoEstribo135?.Extension, v.GanchoEstribo135?.DiametroDoblado, v.KgPorMetro)).ToList(),
            _almacen.Listar().Select(g => g.Nombre).ToList());
    }

    /// <summary>Lo que corresponde a cada filtro del menú «Ver en 3D» de la página.</summary>
    private static FiltroPrevia3D Filtro3D(string? nombre) => nombre switch
    {
        "Solo longitudinales" => FiltroPrevia3D.SoloLongitudinales(),
        "Solo estribos (con grapas)" => FiltroPrevia3D.SoloEstribos(),
        "Solo ganchos" => FiltroPrevia3D.SoloGanchos(),
        "Solo traslapos" => FiltroPrevia3D.SoloTraslapos(),
        _ => FiltroPrevia3D.Todo(),
    };

    private TipoBarra? BarraDe(string? nombre) => string.IsNullOrEmpty(nombre) ? null : _barras.FirstOrDefault(b => b.Nombre == nombre);

    private ColumnaResumen? Resumen(string id) => _columnas.FirstOrDefault(c => c.Id == id);

    private ColumnaContexto Leer(string id)
    {
        if (!_leidas.TryGetValue(id, out ColumnaContexto? columna)) _leidas[id] = columna = _contexto.Leer(id);
        return columna;
    }

    private void OlvidarLoLeido()
    {
        _contexto.InvalidarCache();
        _leidas.Clear();
    }

    private static string? Texto(JsonElement m, string campo) =>
        m.TryGetProperty(campo, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    private static bool Booleano(JsonElement m, string campo) =>
        m.TryGetProperty(campo, out JsonElement e) && e.ValueKind == JsonValueKind.True;
}
