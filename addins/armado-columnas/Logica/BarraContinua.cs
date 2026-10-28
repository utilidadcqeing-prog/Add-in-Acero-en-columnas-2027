namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using practicacq.Modelo;

/// <summary>
/// Una posición longitudinal de la columna como UNA sola varilla de punta a
/// punta, partida después con empalmes nativos de Revit -- issue #41.
///
/// Todo lo que hace esto se midió contra Revit real el 7-oct (ver
/// docs/planes/2026-10-07-factibilidad-barra-continua-41.md):
///  - un Rebar hosteado en una sola columna puede recorrer toda la pila, sin
///    recortarse ni dar aviso;
///  - <see cref="RebarSpliceUtils.SpliceRebar(Document, ElementId, RebarSpliceOptions, IList{RebarSpliceGeometry})"/>
///    acepta varios empalmes en una llamada, también fuera del host;
///  - después de partir, Revit deja piezas hosteadas en una columna que no las
///    contiene ("completely outside of its host"): hay que reasignarlas;
///  - el corrimiento lateral lo pone Revit, en zigzag: la segunda pieza de
///    cada empalme queda un diámetro hacia el lado POSITIVO del eje de la
///    normal de la barra (+X o +Y), sin importar el sentido de la normal;
///  - ese corrimiento relativo no se puede invertir, y las piezas de una
///    cadena de empalme están atadas: mover una mueve todas. Lo único que se
///    puede elegir es dónde queda la cadena entera (ver <see cref="Alinear"/>);
///  - hosteada en la columna de más abajo, Revit a veces no parte la varilla
///    aunque diga que puede; hay que pasarla antes a la columna del traslapo
///    (ver <see cref="Partir"/>);
///  - (17-oct) con traslapos de largos distintos hace falta un tipo de
///    empalme por largo y una llamada a SpliceRebar por traslapo, de abajo
///    hacia arriba, sobre la pieza de más arriba de la llamada anterior (ver
///    docs/planes/2026-10-17-diagnostico-traslapos-variables.md).
///
/// Las longitudes van todas en pies (unidades internas de Revit).
/// </summary>
public static class BarraContinua
{
    public readonly record struct ColumnaDePila(FamilyInstance Host, double ZInf, double ZSup);

    /// <summary>Un traslapo a aplicar: su altura central (pies) y el tipo de empalme con su largo.</summary>
    public readonly record struct TraslapoPedido(double ZCentro, ElementId Tipo);

    public sealed record Pedido(
        RebarBarType Barra, double X, double Y, XYZ Normal, double ZInicio, double ZFin,
        IReadOnlyList<TraslapoPedido> Traslapos, IReadOnlyList<ColumnaDePila> Pila,
        RebarHookType? GanchoAbajo, RebarHookType? GanchoArriba,
        RebarTerminationOrientation OrientacionAbajo, RebarTerminationOrientation OrientacionArriba,
        Limites Limites, bool DeArribaAAbajo = false, XYZ? Desfase = null);

    /// <summary>
    /// El rectángulo en planta donde puede quedar el eje de una pieza de esta
    /// varilla: de esquina a esquina de la armadura. Una pieza corrida que se
    /// salga de ahí quedaría pisando el estribo.
    /// </summary>
    public readonly record struct Limites(double XMin, double XMax, double YMin, double YMax);

    /// <summary>
    /// <c>Piezas</c> = cuántos Rebar quedaron en el modelo (0 si no se pudo crear la varilla).
    /// <c>Cadena</c> = las piezas creadas, para terminar de acomodarlas con <see cref="Acomodar"/> (null si no se creó nada).
    /// </summary>
    public readonly record struct Resultado(int Piezas, string? Aviso, IReadOnlyList<Rebar>? Cadena = null);

    /// <summary>
    /// Una pieza ya creada: su eje en planta y las alturas que cubre (sin
    /// ganchos), en pies. Revit corre las varillas unos milímetros respecto de
    /// lo pedido (medido el 21-oct en la columna 387983: hasta 5 mm), así que
    /// lo que tenga que tocarlas -- grapas y cerrados (#130) -- se ubica con esto.
    /// </summary>
    public readonly record struct PiezaReal(double X, double Y, double ZInf, double ZSup);

    /// <summary>
    /// TEMPORAL -- traza de diagnóstico (8-oct): la primera corrida real dejó
    /// varillas sin partir y piezas "completely outside of its host", cosa que
    /// la prueba aislada no había mostrado. Si no es null, recibe una línea
    /// por cada paso. Sacar cuando el #41 quede medido en Revit.
    /// </summary>
    public static Action<string>? Traza { get; set; }

    private static string Z(double pies) => $"{GeometriaSeccion.Metros(pies):0.###}";

    private static string Describir(Rebar r)
    {
        (XYZ a, XYZ b) = Extremos(r);
        string ganchos = "";
        for (int e = 0; e < 2; e++)
        {
            try { ganchos += r.GetHookTypeId(e) != ElementId.InvalidElementId ? "G" : "-"; }
            catch { ganchos += "?"; }
        }

        return $"Rebar {r.Id.Value} host {r.GetHostId().Value} " +
               $"({GeometriaSeccion.Metros(a.X):0.###}, {GeometriaSeccion.Metros(a.Y):0.###}) Z {Z(a.Z)} -> {Z(b.Z)} ganchos[{ganchos}]" +
               DescribirLargo(r);
    }

    /// <summary>
    /// TEMPORAL -- #177: las longitudinales salen unos cm más largas. Agrega
    /// el largo que da Revit (redondeado y sin redondear), la altura que
    /// ocupa la barra contando ganchos y radios, y el redondeo que se le
    /// aplica, para compararlo con el largo de corte de la pestaña.
    /// </summary>
    private static string DescribirLargo(Rebar r)
    {
        string texto = "";
        try { texto += $"; largo total {GeometriaSeccion.Metros(r.TotalLength):0.####}"; }
        catch (Exception ex) { texto += $"; largo total: {ex.GetType().Name}"; }

        try
        {
            Parameter? largo = r.get_Parameter(BuiltInParameter.REBAR_ELEM_LENGTH);
            if (largo is not null) texto += $", REBAR_ELEM_LENGTH {GeometriaSeccion.Metros(largo.AsDouble()):0.####}";
        }
        catch (Exception ex) { texto += $", REBAR_ELEM_LENGTH: {ex.GetType().Name}"; }

        try
        {
            List<XYZ> puntos = r
                .GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0)
                .SelectMany(c => c.Tessellate())
                .ToList();
            texto += $", con ganchos Z {Z(puntos.Min(q => q.Z))} -> {Z(puntos.Max(q => q.Z))}";

            // Lo que se ve acotado en un plano: la pata del gancho hasta el
            // borde exterior del doblez (eje + medio diámetro), para
            // compararla con el gancho de la tabla.
            (XYZ a, XYZ _) = Extremos(r);
            double pata = puntos.Max(q => Math.Sqrt((q.X - a.X) * (q.X - a.X) + (q.Y - a.Y) * (q.Y - a.Y)));
            if (pata > 1e-6 && r.Document.GetElement(r.GetTypeId()) is RebarBarType tipo)
                texto += $", pata exterior del gancho {GeometriaSeccion.Metros(pata + tipo.BarModelDiameter / 2):0.####}";
        }
        catch (Exception ex) { texto += $", con ganchos: {ex.GetType().Name}"; }

        texto += $", redondeo {DescribirRedondeo(r.GetReinforcementRoundingManager())}";
        return texto;
    }

    /// <summary>TEMPORAL -- #177: de dónde sale el redondeo del largo y cuánto vale.</summary>
    public static string DescribirRedondeo(ReinforcementRoundingManager gestor)
    {
        try
        {
            if (!gestor.IsActiveOnElement) return "inactivo";
            if (gestor is not RebarRoundingManager barras) return "activo";
            return $"activo ({barras.ApplicableReinforcementRoundingSource}): largo total " +
                   $"{barras.ApplicableTotalLengthRounding:0.####} {gestor.GetLengthUnitTypeId().TypeId} " +
                   $"{barras.ApplicableTotalLengthRoundingMethod}, tramos {barras.ApplicableSegmentLengthRounding:0.####} " +
                   $"{barras.ApplicableSegmentLengthRoundingMethod}";
        }
        catch (Exception ex)
        {
            return $"? ({ex.GetType().Name}: {ex.Message})";
        }
    }

    /// <summary>
    /// TEMPORAL -- vuelca a la traza a qué está atada cada "asa" de la varilla
    /// (recubrimiento, cara del host, otra armadura) y a qué distancia. Es para
    /// entender por qué Revit deja las piezas 5 a 8 mm más adentro de donde se
    /// piden y las devuelve ahí aunque se las mueva (8-oct).
    /// </summary>
    private static void VolcarRestricciones(Rebar r)
    {
        if (Traza is null) return;

        try
        {
            RebarConstraintsManager gestor = r.GetRebarConstraintsManager();
            foreach (RebarConstrainedHandle asa in gestor.GetAllHandles())
            {
                string nombre;
                try { nombre = $"{asa.GetHandleType()} «{asa.GetHandleName()}»"; }
                catch { nombre = asa.GetHandleType().ToString(); }

                RebarConstraint? c = gestor.GetCurrentConstraintOnHandle(asa);
                if (c is null)
                {
                    Traza($"      asa {nombre}: sin restricción");
                    continue;
                }

                string detalle = c.GetConstraintType().ToString();
                try
                {
                    if (c.IsToCover())
                        detalle += $", al RECUBRIMIENTO, distancia {GeometriaSeccion.Metros(c.GetDistanceToTargetCover()) * 100:0.##} cm";
                    else if (c.IsToOtherRebar())
                        detalle += $", a OTRA ARMADURA, distancia {GeometriaSeccion.Metros(c.GetDistanceToTargetRebar()) * 100:0.##} cm";
                    else if (c.IsToHostFaceOrCover())
                        detalle += $", a la CARA DEL HOST, distancia {GeometriaSeccion.Metros(c.GetDistanceToTargetHostFace()) * 100:0.##} cm";
                }
                catch (Exception ex) { detalle += $" (distancia: {ex.GetType().Name})"; }

                try { detalle += $"; destino: elemento {c.GetTargetElement()?.Id.Value}"; }
                catch { /* sin destino único */ }

                try
                {
                    if (c.IsToCover() || c.IsToHostFaceOrCover())
                        detalle += $"; recubrimiento del destino: {GeometriaSeccion.Metros(c.GetTargetCoverType(0)?.CoverDistance ?? 0) * 100:0.##} cm";
                }
                catch { /* no aplica */ }

                Traza($"      asa {nombre}: {detalle}");
            }
        }
        catch (Exception ex)
        {
            Traza($"      restricciones: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static Resultado Crear(Document doc, Pedido p)
    {
        Traza?.Invoke(
            $"VARILLA «{p.Barra.Name}» en ({GeometriaSeccion.Metros(p.X):0.###}, {GeometriaSeccion.Metros(p.Y):0.###}) " +
            $"normal ({p.Normal.X:0}, {p.Normal.Y:0}) Z {Z(p.ZInicio)} -> {Z(p.ZFin)}; gancho abajo: {p.GanchoAbajo?.Name ?? "no"}, " +
            $"arriba: {p.GanchoArriba?.Name ?? "no"}; traslapos pedidos: " +
            $"[{string.Join("; ", p.Traslapos.Select(t => $"Z={Z(t.ZCentro)} «{doc.GetElement(t.Tipo)?.Name}»"))}]");

        Rebar? entera;
        try
        {
            entera = CrearEntera(doc, p, HostInicial(p));
        }
        catch (Exception ex)
        {
            // Una creación fallida dentro de la transacción puede arrastrar a
            // las siguientes -- mismo criterio que GeneradorArmado.GenerarColumna.
            try { doc.Regenerate(); } catch { /* best-effort */ }
            return new Resultado(0, $"no se pudo crear una barra longitudinal ({GeometriaSeccion.Explicar(ex)}).");
        }

        if (entera is null)
            return new Resultado(0,
                $"Revit no pudo crear una barra longitudinal «{p.Barra.Name}» (Rebar.CreateFromCurves devolvió null) en " +
                $"X={GeometriaSeccion.Metros(p.X):0.###} Y={GeometriaSeccion.Metros(p.Y):0.###}, " +
                $"Z {GeometriaSeccion.Metros(p.ZInicio):0.###} a {GeometriaSeccion.Metros(p.ZFin):0.###} m.");

        MarcaEstribos.Marcar(entera, entera.GetHostId());
        Traza?.Invoke($"  creada: {Describir(entera)}");
        if (Traza is not null)
        {
            try { doc.Regenerate(); } catch { /* best-effort */ }
            VolcarRestricciones(entera);
        }

        if (p.Traslapos.Count == 0)
        {
            Alinear(doc, new List<Rebar> { entera }, p);
            return new Resultado(1, null, new[] { entera });
        }

        List<Rebar> piezas;
        var rechazados = new List<string>();
        try
        {
            (piezas, entera) = Partir(doc, entera, p, rechazados);
        }
        catch (Exception ex)
        {
            Traza?.Invoke($"  SpliceRebar tiró {ex.GetType().Name}: {ex.Message}");
            try { doc.Regenerate(); } catch { /* best-effort */ }
            return new Resultado(1,
                $"Revit no pudo aplicar los traslapos de una varilla «{p.Barra.Name}» ({GeometriaSeccion.Explicar(ex)}); " +
                "quedó entera, sin traslapos.",
                entera.IsValidObject ? new[] { entera } : null);
        }

        // Un traslapo que Revit rechaza de antemano (típico: cae en un tramo
        // tan corto que el solape no cabe) no se pide; la varilla queda con un
        // tramo más largo que la barra comercial, y eso se avisa.
        string? avisoRechazados = rechazados.Count == 0
            ? null
            : $"en las varillas «{p.Barra.Name}» no cabe el traslapo previsto en {string.Join(" ni en ", rechazados)}; " +
              "ahí no se traslapa, y ese tramo de varilla puede superar la barra comercial.";

        if (piezas.Count == 0)
        {
            if (entera.IsValidObject) Alinear(doc, new List<Rebar> { entera }, p);
            return new Resultado(1, avisoRechazados
                ?? $"Revit no partió una varilla «{p.Barra.Name}» en sus traslapos; quedó entera, sin traslapos.",
                entera.IsValidObject ? new[] { entera } : null);
        }

        // SpliceRebar puede reemplazar la barra por elementos nuevos o
        // conservarla como una de las piezas: ni la marca ni (sin confirmar)
        // las terminaciones están garantizadas en todas, así que se reponen.
        foreach (Rebar pieza in piezas)
        {
            Traza?.Invoke($"  pieza: {Describir(pieza)}");
            Reasignar(doc, pieza, p.Pila);
            MarcaEstribos.Marcar(pieza, pieza.GetHostId());
        }

        Alinear(doc, piezas, p);

        // Con la varilla dibujada al revés, en cada pieza el extremo 0 es el de
        // arriba.
        ReponerGancho(piezas[0], p.DeArribaAAbajo ? 1 : 0, p.GanchoAbajo, Orientacion(p, p.OrientacionAbajo));
        ReponerGancho(piezas[^1], p.DeArribaAAbajo ? 0 : 1, p.GanchoArriba, Orientacion(p, p.OrientacionArriba));

        if (Traza is not null)
        {
            try { doc.Regenerate(); } catch { /* best-effort */ }
            foreach (Rebar pieza in piezas)
            {
                Traza($"  final: {Describir(pieza)}");
                VolcarRestricciones(pieza);
            }
        }

        int esperadas = p.Traslapos.Count - rechazados.Count + 1;
        return new Resultado(piezas.Count, piezas.Count == esperadas
            ? avisoRechazados
            : $"una varilla «{p.Barra.Name}» quedó en {piezas.Count} pieza(s) y se esperaban {esperadas}: " +
              "Revit no aplicó todos los traslapos pedidos.",
            piezas);
    }

    /// <summary>
    /// Termina de acomodar las varillas de una columna, todas juntas, una vez
    /// creadas: elige hacia qué lado queda la pieza corrida de cada traslapo
    /// para que no choque con las vecinas (<see cref="AcomodoTraslapos"/>),
    /// corre las que hagan falta, les fija las ataduras y devuelve dónde quedó
    /// cada pieza de cada varilla, en el mismo orden.
    ///
    /// Revit decide para dónde se corre la pieza; lo que se puede elegir es
    /// qué grupo de piezas queda en la posición pedida (como en
    /// <see cref="Alinear"/>), y eso se hace moviendo la cadena entera. La
    /// otra manera solo vale si las piezas quedan entre las esquinas
    /// (<see cref="Limites"/>). Se parte de dónde quedaron de verdad.
    /// Las piezas tampoco pueden tocar <paramref name="obstaculos"/> (los
    /// ganchos del estribo).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<PiezaReal>> Acomodar(
        Document doc, IReadOnlyList<(Pedido Pedido, IReadOnlyList<Rebar> Cadena)> varillas,
        IReadOnlyList<AcomodoTraslapos.Pieza>? obstaculos = null)
    {
        const double holgura = 1e-3;
        try { doc.Regenerate(); } catch { /* best-effort */ }

        var opciones = new List<AcomodoTraslapos.Varilla>();
        var traslados = new List<XYZ?>();
        foreach ((Pedido p, IReadOnlyList<Rebar> cadena) in varillas)
        {
            double radio = p.Barra.BarNominalDiameter / 2;
            var comun = new List<AcomodoTraslapos.Pieza>();
            var desvios = new List<XYZ>();
            foreach (Rebar r in cadena.Where(r => r.IsValidObject))
            {
                try
                {
                    (XYZ abajo, XYZ arriba) = Extremos(r);
                    comun.Add(new AcomodoTraslapos.Pieza(abajo.X, abajo.Y, radio, abajo.Z, arriba.Z));
                    desvios.Add(new XYZ(abajo.X - p.X, abajo.Y - p.Y, 0));
                }
                catch { /* best-effort: esa pieza no se mira */ }
            }

            // Los dos grupos: el que está en la posición pedida (el más cerca)
            // y el corrido. Voltear es llevar el corrido a la posición pedida.
            XYZ? traslado = null;
            XYZ? enSuLugar = desvios.OrderBy(d => d.GetLength()).FirstOrDefault();
            XYZ? corrido = enSuLugar is null ? null : desvios.FirstOrDefault(d => d.DistanceTo(enSuLugar) > holgura);
            if (enSuLugar is not null && corrido is not null)
            {
                XYZ t = enSuLugar - corrido;
                bool dentro = desvios.All(d =>
                    p.X + d.X + t.X >= p.Limites.XMin - holgura && p.X + d.X + t.X <= p.Limites.XMax + holgura &&
                    p.Y + d.Y + t.Y >= p.Limites.YMin - holgura && p.Y + d.Y + t.Y <= p.Limites.YMax + holgura);
                if (dentro) traslado = t;
            }

            traslados.Add(traslado);
            opciones.Add(new AcomodoTraslapos.Varilla(comun, traslado is null
                ? null
                : comun.Select(q => q with { X = q.X + traslado.X, Y = q.Y + traslado.Y }).ToList()));
        }

        int[] elegidas = AcomodoTraslapos.Elegir(opciones, obstaculos);

        if (obstaculos is { Count: > 0 })
            for (int i = 0; i < opciones.Count; i++)
            {
                double metida = AcomodoTraslapos.Choque(opciones[i].Opciones[elegidas[i]], obstaculos);
                if (metida > 0)
                    Traza?.Invoke(
                        $"TOCA EL GANCHO del estribo la varilla «{varillas[i].Pedido.Barra.Name}» en ({GeometriaSeccion.Metros(varillas[i].Pedido.X):0.###}, " +
                        $"{GeometriaSeccion.Metros(varillas[i].Pedido.Y):0.###}): no hay manera de dejarla sin tocarlo.");
            }

        var resultado = new List<IReadOnlyList<PiezaReal>>();
        for (int i = 0; i < varillas.Count; i++)
        {
            IReadOnlyList<Rebar> cadena = varillas[i].Cadena;
            if (elegidas[i] == 1 && traslados[i] is { } t)
            {
                Traza?.Invoke(
                    $"VOLTEAR la varilla «{varillas[i].Pedido.Barra.Name}» en ({GeometriaSeccion.Metros(varillas[i].Pedido.X):0.###}, " +
                    $"{GeometriaSeccion.Metros(varillas[i].Pedido.Y):0.###}): correrla ({GeometriaSeccion.Metros(t.X) * 1000:0.0}, " +
                    $"{GeometriaSeccion.Metros(t.Y) * 1000:0.0}) mm para que la pieza corrida no choque.");
                if (!Desplazar(doc, cadena, t))
                    Traza?.Invoke("  NO QUEDÓ donde se pidió; se deja donde la dejó Revit.");
            }

            FijarAtaduras(doc, cadena);
            resultado.Add(LeerPosiciones(doc, cadena));
        }

        return resultado;
    }

    /// <summary>
    /// Corre una varilla ya partida (la cadena entera) <paramref name="traslado"/>
    /// en planta, y comprueba que haya quedado ahí. Medido el 21-oct en la
    /// columna 387983: <c>MoveElement</c> no alcanza -- Revit vuelve a resolver
    /// las ataduras, y de dos varillas que se pidió correr 22,2 y 19,1 mm, una
    /// quedó corrida −11,1 y la otra no se movió. Por eso primero se corre la
    /// atadura que la ubica: el asa del plano de la barra (su normal es el eje
    /// en que se corre) a una distancia fija de una cara de la columna
    /// (<see cref="RebarConstraint.SetDistanceToTargetHostFace"/>, como atadura
    /// preferida). El signo de esa distancia depende de la cara, así que se
    /// prueba y se mide; si ninguna cara la deja en su lugar, se intenta con
    /// <c>MoveElement</c>. Devuelve si quedó a menos de medio milímetro.
    /// </summary>
    private static bool Desplazar(Document doc, IReadOnlyList<Rebar> cadena, XYZ traslado)
    {
        const double tolerancia = 0.0005 / 0.3048; // 0,5 mm
        var deLaCadena = new HashSet<ElementId>(cadena.Where(r => r.IsValidObject).Select(r => r.Id));
        Rebar? referencia = cadena.FirstOrDefault(r => r.IsValidObject);
        if (referencia is null) return false;

        XYZ Donde()
        {
            doc.Regenerate();
            XYZ a = Extremos(referencia).Abajo;
            return new XYZ(a.X, a.Y, 0);
        }

        static string Mm(XYZ v) => $"({GeometriaSeccion.Metros(v.X) * 1000:0.0}, {GeometriaSeccion.Metros(v.Y) * 1000:0.0})";

        XYZ inicio = Donde();
        XYZ objetivo = inicio + new XYZ(traslado.X, traslado.Y, 0);
        bool Llego(string como)
        {
            XYZ ahora = Donde();
            bool llego = ahora.DistanceTo(objetivo) < tolerancia;
            Traza?.Invoke($"  {como}: de {Mm(inicio)} a {Mm(ahora)}, se pedía {Mm(objetivo)} -> {(llego ? "OK" : "no")}");
            return llego;
        }

        // La pieza que sostiene a la cadena: la que no tiene el plano atado a
        // otra pieza de la misma varilla (las demás la siguen por el empalme).
        foreach (Rebar pieza in cadena.Where(r => r.IsValidObject))
        {
            RebarConstraintsManager gestor;
            RebarConstrainedHandle? plano;
            try
            {
                gestor = pieza.GetRebarConstraintsManager();
                plano = gestor.GetAllHandles().FirstOrDefault(h => h.GetHandleType() == RebarHandleType.RebarPlane);
                if (plano is null) continue;
                RebarConstraint? actual = gestor.GetCurrentConstraintOnHandle(plano);
                if (actual is not null && actual.IsToOtherRebar() && actual.GetTargetElement() is Element destino &&
                    deLaCadena.Contains(destino.Id))
                    continue;
                Traza?.Invoke($"  pieza {pieza.Id.Value}: plano atado a {Describir(actual)}");
            }
            catch (Exception ex)
            {
                Traza?.Invoke($"  pieza {pieza.Id.Value}: no se pudo leer su atadura ({ex.GetType().Name}: {ex.Message})");
                continue;
            }

            IList<RebarConstraint> candidatas;
            try { candidatas = gestor.GetConstraintCandidatesForHandle(plano, pieza.GetHostId()); }
            catch (Exception ex)
            {
                Traza?.Invoke($"  pieza {pieza.Id.Value}: sin ataduras posibles ({ex.GetType().Name}: {ex.Message})");
                continue;
            }

            foreach (RebarConstraint candidata in candidatas.Where(c => c.IsFixedDistanceToHostFace()))
            {
                double distancia;
                try { distancia = candidata.GetDistanceToTargetHostFace(); }
                catch { continue; }

                foreach (int signo in new[] { 1, -1 })
                {
                    try
                    {
                        candidata.SetDistanceToTargetHostFace(distancia + signo * traslado.GetLength());
                        gestor.SetPreferredConstraint(candidata);
                        if (Llego($"atadura a la cara de la columna, {GeometriaSeccion.Metros(distancia) * 1000:0.0} " +
                                  $"{(signo > 0 ? "+" : "-")} {GeometriaSeccion.Metros(traslado.GetLength()) * 1000:0.0} mm"))
                            return true;
                    }
                    catch (Exception ex)
                    {
                        Traza?.Invoke($"  la atadura no se pudo usar ({ex.GetType().Name}: {ex.Message})");
                    }
                }

                // Se deja esa cara como estaba antes de probar la siguiente.
                try
                {
                    candidata.SetDistanceToTargetHostFace(distancia);
                    gestor.SetPreferredConstraint(candidata);
                    doc.Regenerate();
                }
                catch { /* best-effort */ }
            }
        }

        // Último recurso: mover la cadena (se corre una pieza y las demás la siguen).
        try
        {
            ElementTransformUtils.MoveElement(doc, referencia.Id, objetivo - Donde());
            return Llego("MoveElement");
        }
        catch (Exception ex)
        {
            Traza?.Invoke($"  MoveElement tiró {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static string Describir(RebarConstraint? c)
    {
        if (c is null) return "nada";
        try
        {
            string destino = c.GetTargetElement()?.Id.Value.ToString() ?? "?";
            if (c.IsFixedDistanceToHostFace())
                return $"la cara del host {destino}, a {GeometriaSeccion.Metros(c.GetDistanceToTargetHostFace()) * 1000:0.0} mm";
            if (c.IsToOtherRebar()) return $"la armadura {destino}";
            return $"{c.GetConstraintType()} ({destino})";
        }
        catch (Exception ex) { return $"{c.GetConstraintType()} ({ex.GetType().Name})"; }
    }

    /// <summary>
    /// Deja cada pieza atada a lo que la ubica AHORA (el estribo, la cara de la
    /// columna o la otra pieza del traslapo), como atadura preferida. Sin esto,
    /// al crear después una grapa, Revit cambiaba la atadura de la varilla a la
    /// grapa nueva y la corría hasta tocarla: medido el 21-oct en la columna
    /// 387983, 12,4 mm, y la grapa ya no la abrazaba (#130). Con una atadura
    /// preferida, la pieza sigue a ese destino aunque aparezcan otros más cerca
    /// (documentación de <see cref="RebarConstraintsManager.SetPreferredConstraint"/>).
    ///
    /// Las ataduras de una pieza a OTRA PIEZA de la misma varilla no se tocan:
    /// son el empalme, y fijarlas lo rompe ("The rebar splice connection cannot
    /// be maintained", 12 avisos en la columna 387983, 21-oct). Esa pieza sigue
    /// a su compañera, que sí queda fija.
    /// Best-effort: si Revit no lo acepta, la pieza queda como estaba.
    /// </summary>
    private static void FijarAtaduras(Document doc, IReadOnlyCollection<Rebar> piezas)
    {
        try { doc.Regenerate(); } catch { /* best-effort */ }

        var deLaVarilla = new HashSet<ElementId>(piezas.Where(r => r.IsValidObject).Select(r => r.Id));
        bool EsDelEmpalme(RebarConstraint c)
        {
            try { return c.IsToOtherRebar() && c.GetTargetElement() is Element e && deLaVarilla.Contains(e.Id); }
            catch { return true; } // ante la duda, no se toca
        }

        foreach (Rebar pieza in piezas)
        {
            try
            {
                if (!pieza.IsValidObject) continue;
                RebarConstraintsManager gestor = pieza.GetRebarConstraintsManager();
                List<RebarConstrainedHandle> atadas = gestor.GetAllHandles()
                    .Where(asa => gestor.GetCurrentConstraintOnHandle(asa) is RebarConstraint c && !EsDelEmpalme(c))
                    .ToList();
                if (atadas.Count > 0) gestor.SetPreferredConstraintsToSurfaceForHandles(atadas);
            }
            catch (Exception ex)
            {
                Traza?.Invoke($"  no se pudieron fijar las ataduras de la pieza {pieza.Id.Value}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Dónde quedó cada pieza, después de alinearla y de reponerle los ganchos
    /// (que también pueden moverla). Best-effort: una pieza que no se pueda
    /// leer no se informa, y quien use esto cae a la posición teórica.
    /// </summary>
    private static IReadOnlyList<PiezaReal> LeerPosiciones(Document doc, IEnumerable<Rebar> piezas)
    {
        try { doc.Regenerate(); } catch { /* best-effort */ }

        var posiciones = new List<PiezaReal>();
        foreach (Rebar pieza in piezas)
        {
            try
            {
                if (!pieza.IsValidObject) continue;
                (XYZ abajo, XYZ arriba) = Extremos(pieza);
                posiciones.Add(new PiezaReal(abajo.X, abajo.Y, abajo.Z, arriba.Z));
            }
            catch
            {
                // best-effort
            }
        }

        return posiciones;
    }

    /// <summary>
    /// La columna de la pila que más largo de la varilla contiene. No se usa
    /// la de más abajo sin más: suele ser un dado corto de otra sección, y
    /// cada vez que una varilla cambia a un host de sección o recubrimiento
    /// distintos Revit la corre unos milímetros (medido el 8-oct: 5 a 8 mm al
    /// pasar del dado de 40x40 a la columna de 30x30).
    /// </summary>
    /// <summary>
    /// Left/Right de Revit son relativos al sentido de la barra (ver
    /// GeneradorLongitudinal.OrientacionesHaciaAdentro): dibujada al revés, para
    /// que el gancho mire al mismo lado hay que pedir el opuesto.
    /// </summary>
    private static RebarTerminationOrientation Orientacion(Pedido p, RebarTerminationOrientation haciaArriba) =>
        !p.DeArribaAAbajo ? haciaArriba
        : haciaArriba == RebarTerminationOrientation.Left ? RebarTerminationOrientation.Right
        : RebarTerminationOrientation.Left;

    private static FamilyInstance HostInicial(Pedido p) =>
        p.Pila
            .OrderByDescending(c => Math.Min(c.ZSup, p.ZFin) - Math.Max(c.ZInf, p.ZInicio))
            .First().Host;

    private static Rebar? CrearEntera(Document doc, Pedido p, FamilyInstance host)
    {
        // ZInicio < ZFin siempre. Dibujada de abajo hacia arriba (lo de
        // siempre), el extremo 0 es el de abajo; al revés, el de arriba. El
        // sentido decide qué pieza corre Revit en cada traslapo (medido el
        // 21-oct, ver AcomodoTraslapos.Cuatro).
        // Con Desfase se dibuja corrida en planta (ver GeneradorLongitudinal,
        // la normal diagonal): la posición pedida sigue siendo X, Y.
        double x = p.X + (p.Desfase?.X ?? 0), y = p.Y + (p.Desfase?.Y ?? 0);
        XYZ abajo = new(x, y, p.ZInicio), arriba = new(x, y, p.ZFin);
        Line linea = p.DeArribaAAbajo ? Line.CreateBound(arriba, abajo) : Line.CreateBound(abajo, arriba);

        using var terminaciones = new BarTerminationsData(doc);
        if (p.GanchoAbajo is not null)
        {
            if (p.DeArribaAAbajo)
            {
                terminaciones.HookTypeIdAtEnd = p.GanchoAbajo.Id;
                terminaciones.TerminationOrientationAtEnd = Orientacion(p, p.OrientacionAbajo);
            }
            else
            {
                terminaciones.HookTypeIdAtStart = p.GanchoAbajo.Id;
                terminaciones.TerminationOrientationAtStart = p.OrientacionAbajo;
            }
        }

        if (p.GanchoArriba is not null)
        {
            if (p.DeArribaAAbajo)
            {
                terminaciones.HookTypeIdAtStart = p.GanchoArriba.Id;
                terminaciones.TerminationOrientationAtStart = Orientacion(p, p.OrientacionArriba);
            }
            else
            {
                terminaciones.HookTypeIdAtEnd = p.GanchoArriba.Id;
                terminaciones.TerminationOrientationAtEnd = p.OrientacionArriba;
            }
        }

        // La varilla excede a su host por las dos puntas (medido: Revit no la
        // recorta). La normal
        // tiene que ser perpendicular a la barra: con BasisZ el plano queda
        // degenerado y CreateFromCurves devuelve null siempre.
        return Rebar.CreateFromCurves(
            doc, RebarStyle.Standard, p.Barra, host, p.Normal,
            new List<Curve> { linea }, terminaciones,
            useExistingShapeIfPossible: true, createNewShape: true);
    }

    /// <summary>
    /// Las piezas resultantes, de abajo hacia arriba; vacío si la varilla
    /// quedó entera. <paramref name="rechazados"/> recibe, en texto, los
    /// traslapos que Revit descartó antes de partir. Devuelve también la
    /// varilla entera tal como quedó: puede no ser la que entró, porque acá
    /// se vuelve a crear cuando hace falta cambiarla de columna.
    /// </summary>
    private static (List<Rebar> Piezas, Rebar Entera) Partir(Document doc, Rebar entera, Pedido p, List<string> rechazados)
    {
        doc.Regenerate();

        // La X/Y se lee de la barra ya creada, no del pedido: el punto de
        // empalme tiene que tocar la barra exacto, si no Revit acepta los
        // datos pero no corta (aviso del Dynamo de referencia de William).
        (XYZ abajo, XYZ _) = Extremos(entera);

        // Cada traslapo con su propio tipo de empalme: el largo puede ser
        // distinto en cada uno (el del cuadro es solo el mínimo).
        var aceptados = new List<TraslapoPedido>();
        foreach (TraslapoPedido t in p.Traslapos.OrderBy(t => t.ZCentro))
        {
            var opciones = new RebarSpliceOptions(doc, t.Tipo, RebarSplicePosition.Middle);
            RebarSpliceError puede = RebarSpliceUtils.CanRebarBeSpliced(
                entera, opciones, new RebarSpliceGeometry(new XYZ(abajo.X, abajo.Y, t.ZCentro), XYZ.BasisZ));
            Traza?.Invoke($"  CanRebarBeSpliced en Z={Z(t.ZCentro)}: {puede}");

            if (puede == RebarSpliceError.Success)
                aceptados.Add(t);
            else
                rechazados.Add($"Z={Z(t.ZCentro)} m ({puede})");
        }

        if (aceptados.Count == 0) return (new List<Rebar>(), entera);
        double primeraAltura = aceptados[0].ZCentro;

        // Un intento de partir, con la barra donde esté en ese momento. Una
        // llamada a SpliceRebar por traslapo, de abajo hacia arriba, cada una
        // sobre la pieza de más arriba que dejó la anterior -- el método medido
        // contra Revit real en docs/planes/2026-10-17-diagnostico-traslapos-variables.md.
        // Las piezas se leen de la cadena de empalme al final: lo que cuenta
        // es lo que quedó en el modelo.
        List<Rebar> Intentar(string etiqueta)
        {
            Rebar? aPartir = entera;
            foreach (TraslapoPedido t in aceptados)
            {
                if (aPartir is null || !aPartir.IsValidObject) break;

                (XYZ donde, XYZ _) = Extremos(aPartir);
                var opciones = new RebarSpliceOptions(doc, t.Tipo, RebarSplicePosition.Middle);
                IList<ElementId> resultantes;
                try
                {
                    resultantes = RebarSpliceUtils.SpliceRebar(
                        doc, aPartir.Id, opciones,
                        new List<RebarSpliceGeometry> { new(new XYZ(donde.X, donde.Y, t.ZCentro), XYZ.BasisZ) });
                }
                catch (Exception ex)
                {
                    Traza?.Invoke($"  [{etiqueta}] SpliceRebar en Z={Z(t.ZCentro)} tiró {ex.GetType().Name}: {ex.Message}");
                    try { doc.Regenerate(); } catch { /* best-effort */ }
                    break;
                }

                doc.Regenerate();
                Traza?.Invoke($"  [{etiqueta}] SpliceRebar en Z={Z(t.ZCentro)} devolvió {resultantes.Count} id(s)");
                aPartir = resultantes
                    .Select(id => doc.GetElement(id) as Rebar)
                    .Where(r => r is not null)
                    .Select(r => r!)
                    .OrderByDescending(r => Extremos(r).Abajo.Z)
                    .FirstOrDefault();
            }

            Rebar? semilla = aPartir is not null && aPartir.IsValidObject ? aPartir
                : entera.IsValidObject ? entera : null;
            List<Rebar> cadena = semilla is null
                ? new List<Rebar>()
                : RebarSpliceUtils.GetSpliceChain(semilla)
                    .Select(id => doc.GetElement(id) as Rebar)
                    .Where(r => r is not null)
                    .Select(r => r!)
                    .OrderBy(r => Extremos(r).Abajo.Z)
                    .ToList();

            Traza?.Invoke($"  [{etiqueta}] cadena de {cadena.Count} pieza(s)");
            return cadena.Count > 1 ? cadena : new List<Rebar>();
        }

        // Antes de partir, la varilla tiene que estar hosteada en la columna
        // donde cae el primer traslapo. Medido el 8-oct sobre el generador
        // real: hosteada en otra columna, Revit dijo que podía partir
        // (CanRebarBeSpliced = Success) y no partió, sin error, en 8 de 12
        // varillas; hosteadas en la columna del traslapo se partieron todas.
        //
        // Si hay que cambiarla, se CREA DE NUEVO en esa columna en vez de
        // usar SetHostId: el cambio de host corre la varilla unos milímetros
        // y después no hay forma de devolverla (Revit la vuelve a llevar a esa
        // distancia de la cara). SetHostId queda como respaldo.
        foreach (ColumnaDePila columna in p.Pila)
        {
            if (primeraAltura < columna.ZInf || primeraAltura > columna.ZSup) continue;
            if (columna.Host.Id == entera.GetHostId()) break;

            try
            {
                Rebar? nueva = CrearEntera(doc, p, columna.Host);
                if (nueva is not null)
                {
                    doc.Delete(entera.Id);
                    entera = nueva;
                    MarcaEstribos.Marcar(entera, entera.GetHostId());
                    doc.Regenerate();
                    Traza?.Invoke($"  creada de nuevo en la columna del traslapo: {Describir(entera)}");
                }
                else
                {
                    entera.SetHostId(doc, columna.Host.Id);
                    doc.Regenerate();
                    Traza?.Invoke($"  pasada con SetHostId a la columna del traslapo: {Describir(entera)}");
                }
            }
            catch (Exception ex)
            {
                Traza?.Invoke($"  cambio a la columna {columna.Host.Id.Value} tiró {ex.GetType().Name}: {ex.Message}");
                try { doc.Regenerate(); } catch { /* best-effort */ }
            }

            break;
        }

        List<Rebar> piezas = Intentar("en la columna del traslapo");
        if (piezas.Count > 0 || !entera.IsValidObject) return (piezas, entera);

        // TEMPORAL -- reintentos de diagnóstico, cambiando UNA cosa por vez. Si
        // alguno llega a hacer falta, la traza dice cuál y hay que revisar la
        // explicación de arriba.
        Traza?.Invoke($"  tras el fallo: {Describir(entera)}");

        // 1) Separada 2,5 cm del estribo, hacia el centro de la sección.
        var alCentro = new XYZ(
            (p.Limites.XMin + p.Limites.XMax) / 2 - abajo.X, (p.Limites.YMin + p.Limites.YMax) / 2 - abajo.Y, 0);
        if (alCentro.GetLength() > 1e-6)
        {
            XYZ paso = alCentro.Normalize() * UnitUtils.ConvertToInternalUnits(0.025, UnitTypeId.Meters);
            try
            {
                ElementTransformUtils.MoveElement(doc, entera.Id, paso);
                doc.Regenerate();
                Traza?.Invoke($"  experimento separada: {Describir(entera)}");
                piezas = Intentar("separada 2,5 cm del estribo");
                if (piezas.Count > 0)
                {
                    // Las piezas están atadas: mover una devuelve toda la cadena.
                    ElementTransformUtils.MoveElement(doc, piezas[0].Id, paso.Negate());
                    doc.Regenerate();
                    return (piezas, entera);
                }

                if (!entera.IsValidObject) return (piezas, entera);
                ElementTransformUtils.MoveElement(doc, entera.Id, paso.Negate());
                doc.Regenerate();
            }
            catch (Exception ex)
            {
                Traza?.Invoke($"  experimento separada tiró {ex.GetType().Name}: {ex.Message}");
            }
        }

        // 2) Sin ganchos (Crear los repone después en las piezas de las puntas).
        try
        {
            entera.SetHookTypeId(0, ElementId.InvalidElementId);
            entera.SetHookTypeId(1, ElementId.InvalidElementId);
            doc.Regenerate();
            Traza?.Invoke($"  experimento sin ganchos: {Describir(entera)}");
            piezas = Intentar("sin ganchos");
            if (piezas.Count > 0 || !entera.IsValidObject) return (piezas, entera);

            ReponerGancho(entera, 0, p.GanchoAbajo, p.OrientacionAbajo);
            ReponerGancho(entera, 1, p.GanchoArriba, p.OrientacionArriba);
            doc.Regenerate();
        }
        catch (Exception ex)
        {
            Traza?.Invoke($"  experimento sin ganchos tiró {ex.GetType().Name}: {ex.Message}");
        }

        return (piezas, entera);
    }

    /// <summary>Pasa la pieza a la columna de la pila que contiene su punto medio, si no es ya la suya.</summary>
    private static void Reasignar(Document doc, Rebar pieza, IReadOnlyList<ColumnaDePila> pila)
    {
        (XYZ abajo, XYZ arriba) = Extremos(pieza);
        double zMedio = (abajo.Z + arriba.Z) / 2;

        foreach (ColumnaDePila columna in pila)
        {
            if (zMedio < columna.ZInf || zMedio > columna.ZSup) continue;
            if (columna.Host.Id == pieza.GetHostId())
            {
                Traza?.Invoke($"    host: ya está en {columna.Host.Id.Value}");
                return;
            }

            // Best-effort: si Revit no acepta el cambio, la pieza queda con el
            // host que le puso SpliceRebar -- sigue siendo una columna de la
            // pila, así que el borrado al regenerar la encuentra igual.
            try
            {
                pieza.SetHostId(doc, columna.Host.Id);
                Traza?.Invoke($"    host: pasada a {columna.Host.Id.Value}");
            }
            catch (Exception ex)
            {
                Traza?.Invoke($"    host: SetHostId({columna.Host.Id.Value}) tiró {ex.GetType().Name}: {ex.Message}");
            }

            return;
        }

        Traza?.Invoke($"    host: NINGUNA columna de la pila contiene su punto medio Z={Z(zMedio)}");
    }

    /// <summary>
    /// Deja la varilla (entera o ya partida) en la posición pedida.
    ///
    /// Hace falta por dos cosas medidas contra Revit real (7 y 8-oct):
    ///  - Revit mueve la varilla unos milímetros al cambiarle el host (y a
    ///    veces al crearla), así que no queda exactamente donde se pidió;
    ///  - en cada empalme corre una de las dos piezas un diámetro hacia el
    ///    lado positivo del eje de la normal, y cuál de las dos corre no es
    ///    fijo. En la esquina donde ese lado es hacia afuera, la pieza corrida
    ///    quedaría pisando el estribo.
    ///
    /// Las piezas quedan entonces en dos grupos, separados un diámetro. Se
    /// lleva uno a la posición pedida: el primero si con eso el otro queda
    /// dentro de <see cref="Limites"/>, y si no el otro. El zigzag es el
    /// mismo, a lo sumo con los turnos cambiados.
    ///
    /// Alcanza con mover una pieza: las de una cadena de empalme están atadas
    /// entre sí y se mueven todas juntas (medido el 7-oct).
    /// </summary>
    private static void Alinear(Document doc, List<Rebar> piezas, Pedido p)
    {
        const double holgura = 1e-3;

        try
        {
            doc.Regenerate();

            List<XYZ> desvios = piezas
                .Select(r => Extremos(r).Abajo)
                .Select(a => new XYZ(a.X - p.X, a.Y - p.Y, 0))
                .ToList();

            XYZ primero = desvios[0];
            XYZ? otro = desvios.FirstOrDefault(d => d.DistanceTo(primero) > holgura);

            bool Dentro(XYZ desvio) =>
                p.X + desvio.X >= p.Limites.XMin - holgura && p.X + desvio.X <= p.Limites.XMax + holgura &&
                p.Y + desvio.Y >= p.Limites.YMin - holgura && p.Y + desvio.Y <= p.Limites.YMax + holgura;

            XYZ traslado = otro is not null && !Dentro(otro - primero) ? otro.Negate() : primero.Negate();
            if (traslado.GetLength() < 1e-5) return;

            ElementTransformUtils.MoveElement(doc, piezas[0].Id, traslado);
            doc.Regenerate();
            Traza?.Invoke(
                $"  alineada ({GeometriaSeccion.Metros(traslado.X) * 100:0.##}, {GeometriaSeccion.Metros(traslado.Y) * 100:0.##}) cm");
        }
        catch (Exception ex)
        {
            // best-effort: queda como la dejó Revit
            Traza?.Invoke($"  alinear tiró {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ReponerGancho(
        Rebar pieza, int extremo, RebarHookType? gancho, RebarTerminationOrientation orientacion)
    {
        if (gancho is null) return;

        try { pieza.SetHookTypeId(extremo, gancho.Id); } catch { /* best-effort */ }
        try { pieza.SetTerminationOrientation(extremo, orientacion); } catch { /* best-effort */ }
    }

    /// <summary>Extremos inferior y superior de la línea central, sin ganchos ni radios.</summary>
    private static (XYZ Abajo, XYZ Arriba) Extremos(Rebar rebar)
    {
        List<XYZ> puntos = rebar
            .GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0)
            .SelectMany(c => new[] { c.GetEndPoint(0), c.GetEndPoint(1) })
            .OrderBy(q => q.Z)
            .ToList();

        return (puntos[0], puntos[^1]);
    }
}
