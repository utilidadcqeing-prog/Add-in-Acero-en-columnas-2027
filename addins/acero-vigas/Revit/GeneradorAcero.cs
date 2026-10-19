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

    private sealed record Creada(Rebar Barra, PiezaLongitudinal Pieza, PorticoAGenerar Portico, bool EsBaston);

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
        using var t = new Transaction(doc, NombreTransaccion);
        FailureHandlingOptions opciones = t.GetFailureHandlingOptions();
        var manejo = new ManejoDeFallas();
        opciones.SetFailuresPreprocessor(manejo);
        t.SetFailureHandlingOptions(opciones);
        t.Start();
        foreach (PorticoAGenerar p in porticos)
        {
            MarcaAcero.BorrarAnteriores(doc, p.Id, p.Vigas);
            foreach ((PiezaLongitudinal pieza, bool esBaston) in p.Plan.Barras.Select(b => (b, false)).Concat(p.Plan.Bastones.Select(b => (b, true))))
            {
                if (Crear(doc, p, pieza, tipos) is { } barra)
                {
                    MarcaAcero.Marcar(barra, p.Id, pieza.Pieza);
                    creadas.Add(new Creada(barra, pieza, p, esBaston));
                }
                else
                    fallas.Add($"{pieza.Pieza} ({p.Id}): Revit no pudo crear la barra #{pieza.Diametro}.");
            }
            foreach (GrupoEstribos g in p.Plan.Estribos)
            {
                if (CrearEstribos(doc, p, g, tipos, formaEstribo, formasMalas) is { } conjunto)
                {
                    MarcaAcero.Marcar(conjunto, p.Id, $"{g.Pieza} {g.Tramo}");
                    estribos.Add((conjunto, p, g));
                }
                else
                    fallas.Add($"{g.Pieza} de {g.Tramo} ({p.Id}): Revit no pudo crear los estribos #{g.Diametro}.");
            }
        }

        // Los ganchos y el punto de cada barra se miran con la geometría ya regenerada; se regenera una vez más solo si se
        // cambió algo.
        doc.Regenerate();
        bool volteados = false;
        foreach (Creada c in creadas)
        {
            volteados |= CreadorBarras.FijarEnSuPunto(c.Barra, c.Portico.Ubicacion, c.Pieza);
            volteados |= CreadorBarras.CorregirGanchos(c.Barra, c.Pieza, CreadorBarras.Pies(c.Portico.Ubicacion.ZAbajo + c.Pieza.YCorte));
        }
        foreach ((Rebar conjunto, PorticoAGenerar p, GrupoEstribos g) in estribos)
            volteados |= CreadorEstribos.CorregirAbierto(conjunto, p.Ubicacion, g);
        if (volteados)
            doc.Regenerate();

        // Colores por diámetro (decisión 9) en la vista activa y en la 3D del tablero, dentro de la misma transacción.
        IEnumerable<int> diametros = creadas.Select(c => c.Pieza.Diametro).Concat(estribos.Select(e => e.Grupo.Diametro));
        View[] vistas = new[] { doc.ActiveView }
            .Concat(new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View>().Where(v => !v.IsTemplate && v.Name == AccionesRevit.Nombre3d))
            .OfType<View>().ToArray();
        fallas.AddRange(FiltrosColor.Aplicar(doc, vistas, diametros, tipos));

        // Lo que Revit rechace al confirmar se quita y se anota (ver ManejoDeFallas), sin cuadros de error.
        manejo.Nuestras = creadas.ToDictionary(c => c.Barra.Id, c => $"{c.Pieza.Pieza} ({c.Portico.Id})")
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
        return new ResumenGeneracion(porticos.Count, quedaron.Count(c => !c.EsBaston), quedaron.Count(c => c.EsBaston), cantidadEstribos,
            fallas.Distinct().ToList(), diferencias);
    }

    private static Rebar? CrearEstribos(Document doc, PorticoAGenerar p, GrupoEstribos g, TiposAcero tipos, RebarShape? forma, HashSet<ElementId> formasMalas)
    {
        if (tipos.Barra(g.Diametro) is not { } barra)
            return null;
        try
        {
            return CreadorEstribos.Crear(doc, p.Ubicacion, g, barra, tipos, forma, formasMalas);
        }
        catch (Exception e) when (e is Autodesk.Revit.Exceptions.ArgumentException or Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            return null;
        }
    }

    private static Rebar? Crear(Document doc, PorticoAGenerar p, PiezaLongitudinal pieza, TiposAcero tipos)
    {
        if (tipos.Barra(pieza.Diametro) is not { } barra)
            return null;
        try
        {
            return CreadorBarras.Crear(doc, p.Ubicacion, pieza, barra, tipos);
        }
        catch (Exception e) when (e is Autodesk.Revit.Exceptions.ArgumentException or Autodesk.Revit.Exceptions.InvalidOperationException)
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
