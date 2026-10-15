namespace CQEING.Gestor.Core.Tests.Apoyo;

internal static partial class Datos
{
    public static readonly DateTimeOffset Fecha = new(2026, 10, 20, 15, 0, 0, TimeSpan.Zero);

    public static string NombreDe(string id) => id == "imagenes" ? "Imágenes por actividad" : "Armado de columnas";

    public static PaqueteManifiesto Paquete(string anio, string sha256, string id = "armado-columnas", string version = "1.0.0") =>
        new(anio, anio == "2026" ? "net8.0-windows" : "net10.0-windows", $"{id}-{version}-revit{anio}.zip", sha256);

    public static Manifiesto Manifiesto(
        string id = "armado-columnas",
        string version = "1.0.0",
        IReadOnlyList<PaqueteManifiesto>? paquetes = null,
        IReadOnlyList<string>? heredados = null) =>
        new(1, id, NombreDe(id), "Descripción de prueba.", version, Fecha, "Notas de prueba",
            heredados ?? ["practicacq.addin"],
            paquetes ?? [Paquete("2026", new string('a', 64), id, version)]);
}
