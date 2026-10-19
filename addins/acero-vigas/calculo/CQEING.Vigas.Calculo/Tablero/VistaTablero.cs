namespace CQEING.Vigas.Calculo.Tablero;

// Lo que la página del tablero (#83) pinta, en las unidades de la maqueta: el alzado y los largos en m, la sección en
// cm y los kilos en kg (decisión 4 del plan docs/planes/2026-10-16-tablero-vigas-83.md). Viaja como JSON en camelCase.

/// <summary>
/// Todo el tablero: la lista de pórticos, el abierto (null = ninguno) y las reglas. <paramref name="Elegidos"/> son los
/// ids de los pórticos elegidos con "Seleccionar en Revit": la lista muestra solo esos (null = todos).
/// </summary>
public sealed record VistaTablero(string Proyecto, IReadOnlyList<PorticoEnLista> Porticos, VistaPortico? Actual, VistaReglas Reglas, bool EnVivo,
    IReadOnlyList<string>? Elegidos = null);

/// <summary>Un pórtico en la columna izquierda. <paramref name="Sub"/> es como <c>"Eje 1 · A a D + voladizo"</c>.</summary>
public sealed record PorticoEnLista(string Id, string Nombre, string Sub, string Tipo, IReadOnlyList<TramoEnLista> Tramos);

/// <summary>
/// Un tramo del pórtico: <paramref name="Id"/> es el nombre del motor (<c>"A-B"</c>, <c>"Voladizo D"</c>);
/// <paramref name="Desde"/> y <paramref name="Hasta"/> van de eje a eje (el voladizo, hasta la punta), para "Ver: tramo".
/// </summary>
public sealed record TramoEnLista(string Id, string Nombre, double Desde, double Hasta);

/// <summary>
/// Un apoyo en el alzado: la x de su eje y de sus caras. <paramref name="EsViga"/>: una viga que cruza; se dibuja como
/// su sección y no como columna (decisión 4 del plan docs/planes/2026-10-19-ajustes-tablero-vigas-100.md).
/// </summary>
public sealed record ApoyoVista(string Eje, double X, double CaraIzquierda, double CaraDerecha, bool EsViga = false);

/// <summary>
/// Una barra corrida. <paramref name="Capa"/> es <c>"arriba"</c> o <c>"abajo"</c>; <paramref name="Recta"/> es como
/// <c>"5.80 + gancho"</c> o <c>"recta"</c>; <paramref name="Cantidad"/> como <c>"3 #5"</c>.
/// <paramref name="GanchoInicioTipo"/> y <paramref name="GanchoFinTipo"/> son <c>"90°"</c>, <c>"135°"</c>, <c>"180°"</c> o
/// <c>""</c>; <paramref name="PuntaInicioEnBorde"/> / <paramref name="PuntaFinEnBorde"/>: ahí van los botones de gancho (#84).
/// <paramref name="Color"/> es el de su diámetro (<see cref="Modelo.ColoresDiametro"/>); con dos diámetros, el del más grueso.
/// <paramref name="Rotulo"/> y <paramref name="RotuloPulgadas"/> son los de DC-CAD (<see cref="Tablero.Rotulo"/>): la
/// página elige cuál pinta (#100).
/// </summary>
public sealed record BarraVista(string Id, string Capa, double Desde, double Hasta, bool GanchoInicio, bool GanchoFin, double Largo,
    string Recta, bool Manual, bool LlegaAlVoladizo, string Cantidad, double Kg,
    string GanchoInicioTipo = "", string GanchoFinTipo = "", bool PuntaInicioEnBorde = false, bool PuntaFinEnBorde = false,
    string Color = "", string Rotulo = "", string RotuloPulgadas = "");

/// <summary>Un traslapo; <paramref name="Rotulo"/> es su largo con 2 decimales, como <c>"1.20"</c>.</summary>
public sealed record TraslapoVista(string Id, string BarraIzquierda, string BarraDerecha, string Capa, double Desde, double Hasta,
    double Minimo, string Diametro, bool Manual, string Rotulo = "");

/// <summary>
/// Un bastón. <paramref name="Donde"/> es como <c>"Centrado en la columna B"</c> o <c>"Centro de la luz A-B"</c>;
/// <paramref name="Cortado"/> = una punta se cortó en el borde del pórtico.
/// </summary>
public sealed record BastonVista(string Id, double Eje, double Desde, double Hasta, bool GanchoInicio, bool GanchoFin, double Largo,
    string Donde, string Cantidad, double Kg, bool Cortado,
    string GanchoInicioTipo = "", string GanchoFinTipo = "", bool PuntaInicioEnBorde = false, bool PuntaFinEnBorde = false,
    string Color = "", string Rotulo = "", string RotuloPulgadas = "");

/// <summary>
/// Una zona de estribos en el alzado, del primer al último estribo; <paramref name="Junta"/> = la de confinamiento,
/// junto a una columna. Su rótulo, como DC-CAD (#100): <paramref name="Cantidad"/> como <c>"9e#3"</c> (y
/// <paramref name="CantidadPulgadas"/>, <c>"9eØ⅜\""</c>), <paramref name="Separacion"/> como <c>"@.10"</c> y
/// <paramref name="Largo"/> como <c>"L=0.95"</c>, de la cara de la columna al último estribo (vacío en el centro).
/// </summary>
public sealed record ZonaEstribos(double Desde, double Hasta, bool Junta, string Cantidad = "", string CantidadPulgadas = "",
    string Separacion = "", string Largo = "");

/// <summary>La sección de un tramo, como <c>"30×50"</c> (cm), en la mitad del tramo (<paramref name="X"/>, m).</summary>
public sealed record SeccionEnAlzado(double X, string Texto);

/// <summary>
/// El reparto de una cara en filas (#88). <paramref name="Error"/> es el nombre de <see cref="Modelo.ErrorCara"/>
/// (<c>"Ninguno"</c>, <c>"MenosDeDosBarras"</c>, <c>"NoCaben"</c>, <c>"NoCabenEnElAlto"</c>).
/// </summary>
public sealed record CaraVista(int CabenPorFila, IReadOnlyList<int> PorFila, string Error, int Maximo);

/// <summary>Una de las dos opciones de la capa de la cara (tarjeta "¿Dónde traslapan…?").</summary>
public sealed record OpcionCaraVista(double Kg, bool Existe);

/// <summary>
/// Una barra en el corte, en cm: x desde la cara izquierda e y desde la cara de arriba, como la maqueta.
/// <paramref name="Id"/> es <c>"a1"</c>… arriba y <c>"b1"</c>… abajo, por fila y de izquierda a derecha; los bastones
/// (<paramref name="Baston"/>) son <c>"bs1"</c>… arriba y <c>"bi1"</c>… abajo, y no se eligen para estribos (#91).
/// </summary>
public sealed record BarraSeccion(string Id, string Nombre, bool Arriba, int Fila, double X, double Y, double D, bool Esquina,
    bool Baston = false, string Color = "");

/// <summary>Un diámetro que hay en el pórtico, para la leyenda: <c>"#5"</c> y su color.</summary>
public sealed record DiametroVista(string Nombre, string Color);

/// <summary>Un estribo en el corte, por sus caras exteriores en cm; las y desde la cara de arriba.</summary>
public sealed record FiguraSeccion(string Id, string Tipo, double Izquierda, double Derecha, double Arriba, double Abajo);

public sealed record SeccionVista(double AnchoCm, double AltoCm, IReadOnlyList<BarraSeccion> Barras, IReadOnlyList<FiguraSeccion> Figuras);

/// <summary>Una fila de estribos en la lista: el cerrado <c>"E"</c> o un adicional <c>"E1"</c>…</summary>
public sealed record FilaEstribos(string Id, string Tipo, int Cantidad, string Diametro, double Largo, string Detalle, double Kg);

/// <summary>
/// Un aviso del motor con su texto para la pantalla. <paramref name="Tipo"/> es el nombre de <see cref="Modelo.TipoAviso"/>.
/// <paramref name="CambiarCara"/>: pasar a la otra cara evita el traslapo largo (la tarjeta muestra "Cambiar").
/// </summary>
public sealed record AvisoVista(string Tipo, string Elemento, string Texto, bool CambiarCara = false);

/// <summary>Un tramo del pórtico, en m.</summary>
public sealed record Rango(double Desde, double Hasta);

/// <summary>
/// El corte 1-1 (#91), en m: dónde quedó, si es la vista tipo, si se movió de donde lo pidió el usuario, la profundidad,
/// hacia dónde mira (<c>"derecha"</c> o <c>"izquierda"</c>), el tramo, los tramos donde se puede poner, los bastones en
/// proyección (en cm, como la sección) y un aviso (null = ninguno). <paramref name="Puntos"/> son los estribos donde
/// puede ir: la línea 1-1 salta de uno a otro (vacío = sin estribos, se mueve por <paramref name="Permitidos"/>).
/// </summary>
public sealed record CorteVista(double X, bool EsTipo, bool Ajustado, double Profundidad, string Mirando, string Tramo,
    IReadOnlyList<Rango> Permitidos, IReadOnlyList<BarraSeccion> Proyeccion, string? Aviso, IReadOnlyList<double> Puntos);

/// <summary>
/// El pórtico abierto, calculado. <paramref name="Inicio"/> y <paramref name="Fin"/> son las puntas del concreto.
/// <paramref name="CaraUsada"/> y <paramref name="CaraMasEconomica"/> valen <c>"izq"</c> (al inicio de la luz) o
/// <c>"der"</c> (al final). <paramref name="Kg"/> suma barras, bastones y estribos. <paramref name="Seccion"/> es la
/// sección en el corte 1-1: barras corridas, bastones que pasan por ahí y estribos de ese tramo (<paramref name="Corte"/>).
/// <paramref name="PosicionesEstribos"/> es el centro de cada estribo cerrado, en m, para dibujarlos en el alzado donde van.
/// <paramref name="Diametros"/> son los de las barras y bastones del pórtico, del más grueso al más delgado (la leyenda).
/// <paramref name="Secciones"/>: la de cada tramo, para escribirla arriba de él.
/// </summary>
public sealed record VistaPortico(
    string Id,
    string Tipo,
    double Inicio,
    double Fin,
    IReadOnlyList<ApoyoVista> Apoyos,
    IReadOnlyList<BarraVista> Barras,
    IReadOnlyList<TraslapoVista> Traslapos,
    IReadOnlyList<BastonVista> Bastones,
    IReadOnlyList<ZonaEstribos> Zonas,
    CaraVista Superior,
    CaraVista Inferior,
    OpcionCaraVista Izquierda,
    OpcionCaraVista Derecha,
    string CaraUsada,
    string CaraMasEconomica,
    SeccionVista Seccion,
    IReadOnlyList<FilaEstribos> ListaEstribos,
    IReadOnlyList<AvisoVista> Avisos,
    IReadOnlyList<Rango> ZonasTraslapo,
    double Kg,
    int Estribos,
    bool SePuedeGenerar,
    CorteVista? Corte = null,
    IReadOnlyList<double>? PosicionesEstribos = null,
    IReadOnlyList<DiametroVista>? Diametros = null,
    IReadOnlyList<SeccionEnAlzado>? Secciones = null);

/// <summary>
/// La pantalla Reglas, con los textos de sus campos (m y kg/m, como <c>RULES0</c> de la maqueta).
/// <paramref name="Problema"/> dice por qué se usan las de base, si pasó.
/// </summary>
public sealed record VistaReglas(IReadOnlyList<ReglaVista> PorDiametro, string Multiplo, string DistanciaLibreCm, bool Varilla12, string? Problema);

/// <summary>Una fila de la tabla de reglas: traslapo, gancho de 90°, estribo de 135° y peso por metro.</summary>
public sealed record ReglaVista(string D, string T, string G, string E, string K);

// La edición (#84): la página pide ver un cambio antes de aplicarlo y C# contesta con lo que pasaría.

public enum AccionEdicion { Barra, Traslapo, Baston, Arrastre }

public enum PuntoArrastre { Izquierda, Centro, Derecha }

/// <summary>
/// Lo que la página quiere ver antes de aplicar. <paramref name="Valor"/> va en m (el largo escrito del traslapo o del
/// bastón); <paramref name="N"/> vuelve en la respuesta, para que la página descarte las viejas.
/// </summary>
public sealed record PedidoEdicion(int N, string Elemento, AccionEdicion Accion, double? Valor = null, PuntoArrastre? Punto = null);

/// <summary>Una opción del panel: <paramref name="Accion"/> es <c>"subir"</c>, <c>"bajar"</c> o <c>"aplicar"</c>; <paramref name="Valor"/> en m.</summary>
public sealed record OpcionEdicion(string Accion, double Valor, bool SePuede, string Texto);

/// <summary>
/// Un cambio que deja el arrastre al soltar. <paramref name="Tipo"/> es <c>"movido"</c> (<paramref name="Id"/> del
/// traslapo y su <paramref name="Desde"/>) o <c>"puntas"</c> (el bastón, <paramref name="Desde"/> y <paramref name="Hasta"/>), en mm.
/// </summary>
public sealed record CambioArrastre(string Tipo, string Id, int Desde, int Hasta = 0, bool EnVoladizo = false);

/// <summary>Un lugar donde puede caer el punto arrastrado: <paramref name="X"/> en m, los largos del elemento en m y su rótulo.</summary>
public sealed record Salto(double X, double Recto, double Total, string Rotulo, bool EnVoladizo, IReadOnlyList<CambioArrastre> Cambios);

/// <summary>La respuesta a <see cref="PedidoEdicion"/>: las opciones del panel o, al arrastrar, los saltos posibles.</summary>
public sealed record PreviaVista(int N, string Elemento, IReadOnlyList<OpcionEdicion> Opciones, IReadOnlyList<Salto> Saltos);
