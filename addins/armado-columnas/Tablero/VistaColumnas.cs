namespace practicacq.Tablero;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using practicacq.Modelo;

/// <summary>
/// Todo lo que la página de columnas dibuja (#205, decisión 2 del plan
/// docs/planes/2026-10-29-interfaz-web-columnas.md): la página no calcula
/// acero, solo formatea lo que llega acá. La forma es la de la maqueta
/// aprobada (<c>web/demo.js</c>), en camelCase.
///
/// Unidades: la sección va en CENTÍMETROS (como la dibuja la página) y todo
/// lo demás en metros.
/// </summary>
public sealed record VistaColumnas(
    PlantaVista Planta,
    IReadOnlyList<string> Elegidas,
    IReadOnlyList<string> Compatibles,
    IReadOnlyList<string> Descartadas,
    ColumnaVista? Columna,
    OpcionesVista Opciones,
    CatalogosVista Catalogos,
    string? Configuracion,
    GeneracionVista Generacion,
    string? Aviso)
{
    /// <summary>camelCase y los enums por nombre («GrapaC», «Fila»), como los espera la página.</summary>
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public string AJson() => JsonSerializer.Serialize(this, Json);
}

public sealed record PlantaVista(IReadOnlyList<Rejilla> Rejillas, IReadOnlyList<ColumnaEnPlanta> Columnas);

/// <param name="Estado">«sinConfigurar», «lista» o «generada».</param>
public sealed record ColumnaEnPlanta(string Id, string Marca, string Ubicacion, double X, double Y, double LadoX, double LadoY, string Estado);

public sealed record ColumnaVista(
    string Id, string Marca, string Ubicacion, double BaseX, double BaseY, double Recubrimiento,
    double ZSuperior, double ZInferiorLongitudinal, ZapataVista? Zapata, IReadOnlyList<NivelVista> Niveles,
    IReadOnlyList<TramoVista> Tramos, SeccionVista Seccion, IReadOnlyList<ZonaVista> ZonasEstribos,
    TraslaposVista Traslapos, IReadOnlyList<string> Avisos, ResumenVista Resumen);

/// <param name="Ancho">0 = no se conoce: la página dibuja una de ancho fijo.</param>
public sealed record ZapataVista(double ZInf, double ZSup, double Ancho);

public sealed record NivelVista(string Nombre, double Z);

/// <param name="Excepcion">El usuario marcó «Este tramo distinto».</param>
public sealed record TramoVista(int Indice, TipoTramo Tipo, double ZInf, double ZSup, bool Excepcion);

/// <summary>La sección desde arriba, en cm, con el origen abajo a la izquierda.</summary>
public sealed record SeccionVista(
    double Ancho, double Alto, double Recubrimiento, EstriboVista Estribo,
    IReadOnlyList<BarraVista> Barras, IReadOnlyList<MenorVista> Menores);

public sealed record EstriboVista(double X1, double Y1, double X2, double Y2, IReadOnlyList<double[]> Puntos, string Color, double Grosor);

/// <param name="Posicion">«esquina», «abajo», «arriba», «izquierda» o «derecha».</param>
/// <param name="Eje">«Columna» para las caras X, «Fila» para las caras Y; null en las esquinas.</param>
/// <param name="Indice">Qué columna o fila, desde 0 (de izquierda a derecha, de abajo hacia arriba).</param>
public sealed record BarraVista(double X, double Y, double DiametroMm, string Color, string Nombre, string Posicion, EjeCaja? Eje, int? Indice);

/// <summary>Una grapa o un estribo cerrado, con el mismo índice que en <see cref="LongitudinalVista.EstribosMenores"/>.</summary>
/// <param name="Puntas">Las dos barras que engancha una grapa; null en los cerrados.</param>
public sealed record MenorVista(
    int Indice, FormaMenor Forma, EjeCaja Eje, int Desde, int Hasta, string Color,
    IReadOnlyList<double[]> Puntos, IReadOnlyList<double[]>? Puntas, double Grosor, bool Valida, string? Motivo);

/// <param name="Zona">«zapata», «nudo», «uniforme», «confinada» o «central».</param>
/// <param name="Cada">El espaciado; null en la zapata, que no lleva estribos.</param>
public sealed record ZonaVista(int Indice, double ZInf, double ZSup, string Zona, double? Cada, int Cantidad);

public sealed record TraslaposVista(IReadOnlyList<PlanVista> Planes, double ZInicio, double ZFin);

/// <param name="Nombre">«A» o «B».</param>
/// <param name="Cantidad">Cuántas barras del anillo son de este grupo y este número.</param>
public sealed record PlanVista(
    string Nombre, string Barra, string Color, int Cantidad, double Solape, bool EnTramosDelOtro,
    IReadOnlyList<TrozoVista> Varillas, IReadOnlyList<TrozoVista> Traslapos, bool SinSolucion, string? Motivo);

public sealed record TrozoVista(double ZInf, double ZSup, double Largo);

/// <param name="Pisos">Cuántos tramos libres tiene.</param>
public sealed record ResumenVista(int Barras, int Estribos, int Grapas, int Cerrados, int Pisos);

/// <summary>
/// Las opciones como las edita la página: las barras por cara son las de
/// CADA cara (en <see cref="ConfigLongitudinal"/> cuentan las dos juntas,
/// hallazgo 8 del plan) y los ángulos de las grapas van en grados.
/// </summary>
public sealed record OpcionesVista(double Recubrimiento, LongitudinalVista Longitudinal, ConfigEstribosColumna Estribos);

public sealed record LongitudinalVista(
    int BarrasPorCaraX, int BarrasPorCaraY, string? TipoBarraEsquinasNombre, string? TipoBarraCaraXNombre, string? TipoBarraCaraYNombre,
    double LongitudComercialBarra, double RedondeoVarillas, bool SolapeMayorDeLosDos, IReadOnlyList<PiezaVista> EstribosMenores);

public sealed record PiezaVista(
    FormaMenor Forma, EjeCaja Eje, int Desde, int Hasta, bool Volteado, int AnguloInicio, int AnguloFin, string? TipoBarraNombre);

public sealed record CatalogosVista(
    IReadOnlyList<VarillaVista> BarrasLongitudinales, IReadOnlyList<VarillaVista> BarrasEstribo,
    IReadOnlyList<double> LargosComerciales, IReadOnlyList<double> Redondeos,
    IReadOnlyList<FilaCuadroVista> Cuadro, IReadOnlyList<string> Configuraciones);

/// <param name="Nombre">El nombre del tipo de barra en Revit: es lo que se guarda.</param>
public sealed record VarillaVista(string Nombre, double DiametroMm, string Color);

/// <summary>Una fila del cuadro de varillas, en metros. B del gancho de 90° (hallazgo 11 del plan).</summary>
public sealed record FilaCuadroVista(
    int N, string Nombre, double Mm, string Color, double? Traslapo, double B90, double D90,
    double E180, double D180, double? E135, double? D135, double Kg);

/// <param name="Pendiente">Hay choques y se espera que el usuario diga si genera igual.</param>
/// <param name="Listas">Las marcas de las columnas que se van a generar.</param>
public sealed record GeneracionVista(
    IReadOnlyList<string> Choques, bool Pendiente, ResultadoVista? Resultado, string? Error, IReadOnlyList<string> Listas);

public sealed record ResultadoVista(IReadOnlyList<string> Detalles);
