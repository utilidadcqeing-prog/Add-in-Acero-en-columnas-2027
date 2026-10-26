using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Tablero;
using CQEING.Vigas.Tablero;

namespace CQEING.Vigas.Revit;

/// <summary>
/// "Generar en Revit" (#85): crea el acero de varios pórticos en una sola transacción, que se deshace de una vez
/// (decisión 5 del plan docs/planes/2026-10-19-generar-acero-vigas-85.md). Antes de crear, borra lo que el add-in creó
/// en cada pórtico (decisión 6). Lo que no se puede crear se anota y se sigue.
/// </summary>
internal static class GeneradorAcero
{
    public const string NombreTransaccion = "Vigas: generar acero";

    /// <summary>Diferencia de largo con el tablero a partir de la cual se avisa, en mm (decisión 8).</summary>
    private const int ToleranciaLargo = 10;

    /// <summary>Un conjunto de Revit con las barras iguales de una fila (<see cref="AgrupadorBarras"/>).</summary>
    private sealed record Creada(Rebar Barra, ConjuntoBarras Conjunto, PorticoAGenerar Portico, bool EsBaston)
    {
        public PiezaLongitudinal Pieza => Conjunto.Primera;
    }

    /// <param name="saltados">Los pórticos que la sesión no deja generar, con su motivo: van primero en las fallas.</param>
    public static ResumenGeneracion Generar(Document doc, IReadOnlyList<PorticoAGenerar> porticos, IReadOnlyList<string> saltados, Reglas r)
    {
        var fallas = new List<string>(saltados);
        if (porticos.Count == 0)
            return new ResumenGeneracion(0, 0, 0, 0, fallas, []);

        TiposAcero tipos = TiposAcero.Leer(doc, r);
        RebarShape? formaEstribo = CreadorEstribos.FormaEstribo(doc);
        var formasMalas = new HashSet<ElementId>();
        var creadas = new List<Creada>();
        var estribos = new List<(Rebar Conjunto, PorticoAGenerar Portico, GrupoEstribos Grupo)>();
        var recubrimiento = new RecubrimientoTemporal(doc);
        using var t = new Transaction(doc, NombreTransaccion);
        FailureHandlingOptions opciones = t.GetFailureHandlingOptions();
        var manejo = new ManejoDeFallas();
        opciones.SetFailuresPreprocessor(manejo);
        t.SetFailureHandlingOptions(opciones);
        t.Start();
        foreach (PorticoAGenerar p in porticos)
        {
            fallas.AddRange((p.Plan.BastonesMontados ?? []).Select(b =>
                $"{b} ({p.Id}): no hay un puesto que no toque la barra que traslapa; el bastón quedó montado en ella."));
            MarcaAcero.BorrarAnteriores(doc, p.Id, p.Vigas);
            // Con qué se generó, en cada viga y en esta misma transacción: un deshacer quita las dos cosas.
            foreach (string id in p.Vigas)
                if (long.TryParse(id, out long n) && doc.GetElement(new ElementId(n)) is { } viga)
                    ConfiguracionPortico.Guardar(viga, p.Configuracion);
            foreach ((ConjuntoBarras conjunto, bool esBaston) in AgrupadorBarras.Agrupar(p.Plan.Barras).Select(c => (c, false))
                         .Concat(AgrupadorBarras.Agrupar(p.Plan.Bastones).Select(c => (c, true))))
            {
                if (Crear(doc, p, conjunto, tipos) is { } juntas)
                {
                    Anotar(juntas, conjunto);
                    continue;
                }
                // Si Revit no acepta el conjunto, cada barra va suelta, como antes: así se pierde a lo más una.
                foreach (PiezaLongitudinal pieza in conjunto.Piezas)
                {
                    var suelta = new ConjuntoBarras([pieza]);
                    if (conjunto.Piezas.Count > 1 && Crear(doc, p, suelta, tipos) is { } barra)
                        Anotar(barra, suelta);
                    else
                        fallas.Add($"{pieza.Pieza} ({p.Id}): Revit no pudo crear la barra #{pieza.Diametro}.");
                }

                void Anotar(Rebar barra, ConjuntoBarras c)
                {
                    MarcaAcero.Marcar(barra, p.Id, c.Primera.Pieza);
                    creadas.Add(new Creada(barra, c, p, esBaston));
                }
            }
            foreach (GrupoEstribos g in p.Plan.Estribos.Where(g => g.Tipo == TipoEstribo.Cerrado))
                CrearYAnotar(g);

            // Las grapas, con el recubrimiento de Revit de sus vigas casi nulo (ver RecubrimientoTemporal): se orientan y
            // se amarran a las caras antes de devolverlo.
            List<GrupoEstribos> abiertos = p.Plan.Estribos.Where(g => g.Tipo == TipoEstribo.Abierto).ToList();
            if (abiertos.Count == 0)
                continue;
            recubrimiento.Poner(p.Vigas.Select(id => long.TryParse(id, out long n) ? doc.GetElement(new ElementId(n)) : null).OfType<Element>());
            doc.Regenerate();
            var grapas = abiertos.Select(g => (Conjunto: CrearYAnotar(g), Grupo: g)).Where(x => x.Conjunto is not null).ToList();
            doc.Regenerate();
            bool volteadas = false;
            foreach ((Rebar? conjunto, GrupoEstribos g) in grapas)
                volteadas |= CreadorEstribos.OrientarAbierto(conjunto!, p.Ubicacion, g);
            if (volteadas)
                doc.Regenerate();
            foreach ((Rebar? conjunto, _) in grapas)
                CreadorEstribos.FijarPuntas(conjunto!);
            recubrimiento.Quitar();

            Rebar? CrearYAnotar(GrupoEstribos g)
            {
                if (CrearEstribos(doc, p, g, tipos, formaEstribo, formasMalas) is { } conjunto)
                {
                    MarcaAcero.Marcar(conjunto, p.Id, $"{g.Pieza} {g.Tramo}");
                    estribos.Add((conjunto, p, g));
                    return conjunto;
                }
                fallas.Add($"{g.Pieza} de {g.Tramo} ({p.Id}): Revit no pudo crear los estribos #{g.Diametro}.");
                return null;
            }
        }
        recubrimiento.Terminar();

        // Los ganchos y el punto de cada barra se miran con la geometría ya regenerada; se regenera una vez más solo si se
        // cambió algo.
        doc.Regenerate();
        bool volteados = false;
        foreach (Creada c in creadas)
        {
            volteados |= CreadorBarras.FijarEnSuPunto(c.Barra, c.Portico.Ubicacion, c.Pieza);
            volteados |= CreadorBarras.CorregirGanchos(c.Barra, c.Pieza, CreadorBarras.Pies(c.Portico.Ubicacion.ZAbajo + c.Pieza.YCorte));
        }
        // Siempre: FijarEnSuPunto cambia restricciones aunque no mueva nada, y las barras se miden ya resueltas.
        doc.Regenerate();
        // Con la primera barra de cada conjunto ya en su punto, las demás van al suyo.
        bool acomodadas = false;
        foreach (Creada c in creadas)
        {
            CreadorBarras.Acomodo a;
            try
            {
                a = CreadorBarras.AcomodarConjunto(c.Barra, c.Portico.Ubicacion, c.Conjunto);
            }
            catch (Exception e) when (e is Autodesk.Revit.Exceptions.ArgumentException or Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                a = CreadorBarras.Acomodo.NoSePudo;
            }
            acomodadas |= a == CreadorBarras.Acomodo.Movido;
            if (a == CreadorBarras.Acomodo.NoSePudo)
                fallas.Add($"{Nombre(c)}: Revit no dejó cada barra en su punto; quedaron repartidas a la misma separación.");
        }
        if (acomodadas)
            doc.Regenerate();

        // Colores por diámetro (decisión 9) en la vista activa y en la 3D del tablero, dentro de la misma transacción.
        IEnumerable<int> diametros = creadas.Select(c => c.Pieza.Diametro).Concat(estribos.Select(e => e.Grupo.Diametro));
        View[] vistas = new[] { doc.ActiveView }
            .Concat(new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View>().Where(v => !v.IsTemplate && v.Name == AccionesRevit.Nombre3d))
            .OfType<View>().ToArray();
        fallas.AddRange(FiltrosColor.Aplicar(doc, vistas, diametros, tipos));

        // Lo que Revit rechace al confirmar se quita y se anota (ver ManejoDeFallas), sin cuadros de error.
        manejo.Nuestras = creadas.ToDictionary(c => c.Barra.Id, Nombre)
            .Concat(estribos.ToDictionary(e => e.Conjunto.Id, e => $"{e.Grupo.Pieza} de {e.Grupo.Tramo} ({e.Portico.Id})"))
            .ToDictionary(par => par.Key, par => par.Value);
        t.Commit();
        fallas.AddRange(manejo.Quitadas);

        // Después de confirmar: solo cuenta lo que quedó (ya regenerado; NumberOfBarPositions sin regenerar da uno menos).
        List<Creada> quedaron = creadas.Where(c => c.Barra.IsValidObject).ToList();
        List<DiferenciaLargo> diferencias = quedaron
            .Select(c => new DiferenciaLargo(c.Portico.Id, c.Pieza.Pieza, c.Pieza.Diametro, CreadorBarras.LargoEnRevit(c.Barra), c.Pieza.Largo))
            .Where(d => Math.Abs(d.LargoRevit - d.LargoTablero) > ToleranciaLargo)
            .ToList();
        int cantidadEstribos = estribos.Where(e => e.Conjunto.IsValidObject).Sum(e => e.Conjunto.NumberOfBarPositions);
        return new ResumenGeneracion(porticos.Count, quedaron.Where(c => !c.EsBaston).Sum(c => c.Barra.NumberOfBarPositions),
            quedaron.Where(c => c.EsBaston).Sum(c => c.Barra.NumberOfBarPositions), cantidadEstribos,
            fallas.Distinct().ToList(), diferencias);
    }

    /// <summary>Para el resumen: <c>"S1 (Pórtico 1)"</c>, o con las barras y la fila si es un conjunto de varias.</summary>
    private static string Nombre(Creada c) => c.Conjunto.Piezas.Count == 1
        ? $"{c.Pieza.Pieza} ({c.Portico.Id})"
        : $"{c.Pieza.Pieza} ({c.Portico.Id}, {c.Conjunto.Piezas.Count} barras #{c.Pieza.Diametro} de la fila {c.Pieza.Fila})";

    private static Rebar? CrearEstribos(Document doc, PorticoAGenerar p, GrupoEstribos g, TiposAcero tipos, RebarShape? forma, HashSet<ElementId> formasMalas)
    {
        if (tipos.Barra(g.Diametro) is not { } barra)
            return null;
        try
        {
            return CreadorEstribos.Crear(doc, p.Ubicacion, g, barra, tipos, forma, formasMalas);
        }
        // InternalException: ScaleToBox revienta si la caja es menor que el doblado más dos diámetros (#3 en una vigueta
        // de 10 cm con recubrimiento 2.5, 26-oct). Se pierde ese grupo, no toda la generación.
        catch (Exception e) when (e is Autodesk.Revit.Exceptions.ArgumentException or Autodesk.Revit.Exceptions.InvalidOperationException
                                      or Autodesk.Revit.Exceptions.InternalException)
        {
            return null;
        }
    }

    private static Rebar? Crear(Document doc, PorticoAGenerar p, ConjuntoBarras conjunto, TiposAcero tipos)
    {
        if (tipos.Barra(conjunto.Primera.Diametro) is not { } barra)
            return null;
        try
        {
            return CreadorBarras.Crear(doc, p.Ubicacion, conjunto, barra, tipos);
        }
        catch (Exception e) when (e is Autodesk.Revit.Exceptions.ArgumentException or Autodesk.Revit.Exceptions.InvalidOperationException
                                      or Autodesk.Revit.Exceptions.InternalException)
        {
            return null;
        }
    }

    /// <summary>
    /// Lo que Revit dice al confirmar la transacción:
    /// <list type="bullet">
    /// <item>las advertencias se descartan (decisión 3): se sabe que una barra corrida recorre varias vigas y Revit avisa que
    /// queda fuera de su host;</item>
    /// <item>un error sobre una barra que creó el add-in quita esa barra y lo anota en <see cref="Quitadas"/>, en vez de abrir
    /// el cuadro de error de Revit que bloquea todo (prueba del 2026-10-19: "El diámetro de curvatura de armadura es
    /// demasiado grande"). El resto se confirma. Un error que no toca barras del add-in sigue a Revit.</item>
    /// </list>
    /// </summary>
    private sealed class ManejoDeFallas : IFailuresPreprocessor
    {
        /// <summary>Las barras que creó esta generación, con su nombre para el resumen.</summary>
        public Dictionary<ElementId, string> Nuestras { get; set; } = [];

        /// <summary>Una línea por barra quitada: qué era y qué dijo Revit.</summary>
        public List<string> Quitadas { get; } = [];

        public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
        {
            var quitar = new HashSet<ElementId>();
            foreach (FailureMessageAccessor m in a.GetFailureMessages())
            {
                if (m.GetSeverity() == FailureSeverity.Warning)
                {
                    a.DeleteWarning(m);
                    continue;
                }
                List<ElementId> nuestras = m.GetFailingElementIds().Where(Nuestras.ContainsKey).ToList();
                foreach (ElementId id in nuestras.Where(quitar.Add))
                    Quitadas.Add($"{Nuestras[id]}: Revit no lo pudo crear ({m.GetDescriptionText().TrimEnd('.')}).");
            }
            if (quitar.Count == 0)
                return FailureProcessingResult.Continue;
            a.DeleteElements(quitar.ToList());
            return FailureProcessingResult.ProceedWithCommit;
        }
    }
}
