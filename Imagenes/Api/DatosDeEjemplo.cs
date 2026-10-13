namespace practicacq.Imagenes.Api;

/// <summary>
/// Presupuestos y estructura de ejemplo (los mismos de la maqueta de la issue #183) para probar el
/// add-in sin la API. Se usan solo cuando el <c>.env</c> no tiene <c>API_URL</c>/<c>API_KEY</c>, y la
/// ventana lo avisa en la barra de estado.
/// </summary>
internal static class DatosDeEjemplo
{
    public static List<PresupuestoDto> Presupuestos() => new()
    {
        new() { CodigoPresupuesto = "PROY_004_V2", Nombre = "Edificio estructural · versión 2" },
        new() { CodigoPresupuesto = "PROY_004", Nombre = "Edificio estructural · copia" },
        new() { CodigoPresupuesto = "PROY_007", Nombre = "Bodega industrial · copia" },
    };

    public static EstructuraDto Estructura(string codigoPresupuesto) => new()
    {
        CodigoPresupuesto = codigoPresupuesto,
        Capitulos =
        {
            new()
            {
                Codigo = "CAP_01", Nombre = "PRELIMINARES",
                Subcapitulos =
                {
                    new() { Codigo = "SUB_01_01", Nombre = "Replanteo", Items = { Item("ACT_001", "Localización y replanteo", "m2") } },
                },
            },
            new()
            {
                Codigo = "CAP_02", Nombre = "CIMENTACIÓN",
                Subcapitulos =
                {
                    new()
                    {
                        Codigo = "SUB_02_01", Nombre = "Zapatas",
                        Items = { Item("ACT_002", "Excavación de zapatas", "m3"), Item("ACT_003", "Concreto de zapatas", "m3") },
                    },
                },
            },
            new()
            {
                Codigo = "CAP_03", Nombre = "ESTRUCTURA",
                Subcapitulos =
                {
                    new()
                    {
                        Codigo = "SUB_03_01", Nombre = "Concreto",
                        Items =
                        {
                            Item("ACT_006", "Columnas en concreto 25x30 y 30x30", "m3"),
                            Item("ACT_007", "Vigas aéreas nivel +3.50", "m3"),
                        },
                    },
                    new() { Codigo = "SUB_03_02", Nombre = "Acero", Items = { Item("ACT_008", "Acero de refuerzo", "kg") } },
                },
            },
            new()
            {
                Codigo = "CAP_04", Nombre = "MAMPOSTERÍA",
                Subcapitulos =
                {
                    new() { Codigo = "SUB_04_01", Nombre = "Muros", Items = { Item("ACT_010", "Muro en bloque e = 0.12", "m2") } },
                },
            },
        },
    };

    private static ItemDto Item(string codigo, string descripcion, string unidad) =>
        new() { CodigoActividad = codigo, Descripcion = descripcion, Unidad = unidad };
}
