namespace practicacq.Modelo;

/// <summary>
/// La costura entre la ventana y Revit. La ventana pide el contexto de una
/// columna por su id y recibe objetos planos; quién los saca del modelo no le
/// importa.
///
/// Vive en <c>Modelo/</c> a propósito, y el id de columna viaja como
/// <see cref="string"/> y no como <c>ElementId</c> de Revit: era el TODO que
/// dejaba pendiente la versión anterior de esta interfaz, y L1 lo cierra así
/// para que <c>Modelo/</c> (y con él, toda la ventana) compile y corra sin la
/// API de Revit referenciada — condición del criterio de aceptación de L5
/// ("sin abrir Revit, en el portátil de 8 GB").
///
/// El lado de Revit (<c>Logica/LectorContexto</c>, implementación real de esta
/// interfaz) es quien convierte ese string a <c>ElementId</c> — hoy con
/// <c>new ElementId(long.Parse(id))</c>, simétrico de
/// <c>elemento.Id.Value.ToString()</c> del lado falso. Ver
/// <see cref="ProveedorContextoFalso"/> en <c>Logica/</c> para el lado de
/// desarrollo sin Revit.
/// </summary>
public interface IProveedorContexto
{
    /// <summary>
    /// Lee una columna del modelo por su id. No abre transacción: solo
    /// consulta. Lo que no se pudo determinar sale en
    /// <see cref="ColumnaContexto.Avisos"/>, no como excepción, para que un
    /// lote no se caiga por una columna mala.
    /// </summary>
    ColumnaContexto Leer(string idColumna);

    /// <summary>
    /// Las columnas candidatas para elegir en la lista de selección de L2 —
    /// no su contexto completo (eso es <see cref="Leer"/> por columna, una
    /// vez que se sabe cuáles interesan), solo lo mínimo para poblar la lista:
    /// id y marca.
    /// </summary>
    IReadOnlyList<ColumnaResumen> ListarColumnas();

    /// <summary>
    /// Las rejillas (grids) del modelo, para el selector visual de planta
    /// (reunión 9-sep: William pidió ver las columnas ubicadas como en un
    /// plano de planta real, con las burbujas de rejilla si el modelo las
    /// tiene). Lista vacía es un resultado válido y esperado -- un modelo sin
    /// rejillas, o el proveedor falso -- el selector visual debe dibujar
    /// igual, solo sin ellas.
    /// </summary>
    IReadOnlyList<Rejilla> ListarRejillas();

    /// <summary>
    /// Descarta cualquier geometría de Revit (sólidos, contornos) leída y
    /// guardada en corridas anteriores. Hay que llamarlo después de generar
    /// armado: los objetos de geometría que devuelve la API de Revit dejan de
    /// ser válidos en cuanto el documento se modifica (creación/borrado de
    /// Rebar, <c>Document.Regenerate()</c>), y seguir usándolos después
    /// -- por ejemplo al aceptar un tramo o navegar el panel de
    /// elevación -- puede terminar en un crash nativo de Revit en vez de una
    /// excepción de .NET (probado sobre Revit real).
    /// </summary>
    void InvalidarCache();
}

/// <summary>
/// Fila de la lista de selección de L2, antes de leer el contexto completo.
///
/// <see cref="EsColumneta"/> se calcula del lado del proveedor (real o
/// falso) y viaja ya resuelta acá para que la ventana no necesite conocer
/// las dimensiones -- solo agrupa por este booleano.
///
/// <see cref="MarcaUbicacion"/> es la "marca de ubicación del pilar" que Revit
/// arma sola del cruce de rejillas ("A-1", o "A-1 (150, 0)" si la columna no
/// cae justo en el cruce) -- es lo que se dibuja como etiqueta en la planta
/// (reunión 11-sep: "eso existe, no hay que ponerse a calcular, sino dejarlo,
/// solamente obtenerlo"). Es OTRA COSA que <see cref="Marca"/>, que es la marca
/// de instancia que el usuario escribe ("C-12") y la que se lee en la elevación
/// y en los avisos del generador. Puede venir vacía: un modelo sin rejillas no
/// tiene coordenada, y entonces no se dibuja etiqueta.
/// </summary>
/// <param name="LadoX">Lado de la sección en X, en metros, para dibujarla a escala en la planta (#133). 0 si no se conoce.</param>
/// <param name="LadoY">Lado de la sección en Y, en metros. 0 si no se conoce.</param>
public sealed record ColumnaResumen(
    string Id, string Marca, bool EsColumneta, double X, double Y, string MarcaUbicacion = "", double LadoX = 0, double LadoY = 0)
{
    /// <summary>
    /// Umbral de "columneta", en metros -- issue #33: confirmado por William
    /// en la reunión del 4-sep. Si la base O la altura de la sección es menor
    /// a esto, es columneta. No cambia su comportamiento (sigue siendo
    /// perfectamente armable): solo se agrupa aparte en la lista de L2 para
    /// que no quede sobrecargada con columnas de muro delgadas.
    /// </summary>
    public const double UmbralMetros = 0.20;

    public static bool CalcularEsColumneta(double baseX, double baseY) =>
        baseX < UmbralMetros || baseY < UmbralMetros;
}

/// <summary>
/// Una rejilla (grid) del modelo, para el selector visual de planta -- solo
/// su nombre y los extremos de su recta, en metros (coordenadas de
/// proyecto). Se asume recta: una rejilla curva no se puede representar así
/// y se descarta del lado del proveedor real (ver <c>LectorContexto</c>),
/// mismo criterio que ya usa <c>GeometriaSeccion.ValidarRectangular</c> para
/// secciones no rectangulares -- no soportado por ahora, no un error fatal.
/// </summary>
public sealed record Rejilla(string Nombre, double X1, double Y1, double X2, double Y2);
