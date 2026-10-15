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
                    new() { Codigo = "SUB_01_01", Nombre = "Replanteo", Items = { Item(901, "ACT_001", "Localización y replanteo", "m2") } },
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
                        Items = { Item(902, "ACT_002", "Excavación de zapatas", "m3"), Item(903, "ACT_003", "Concreto de zapatas", "m3") },
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
                            Item(904, "ACT_006", "Columnas en concreto 25x30 y 30x30", "m3"),
                            Item(905, "ACT_007", "Vigas aéreas nivel +3.50", "m3"),
                        },
                    },
                    new() { Codigo = "SUB_03_02", Nombre = "Acero", Items = { Item(906, "ACT_008", "Acero de refuerzo", "kg") } },
                },
            },
            new()
            {
                Codigo = "CAP_04", Nombre = "MAMPOSTERÍA",
                Subcapitulos =
                {
                    new() { Codigo = "SUB_04_01", Nombre = "Muros", Items = { Item(907, "ACT_010", "Muro en bloque e = 0.12", "m2") } },
                },
            },
        },
    };

    /// <summary>Los <c>id_item</c> son inventados (901, 902…): en modo ejemplo no se registra nada en la API.</summary>
    private static ItemDto Item(int idItem, string codigo, string descripcion, string unidad) =>
        new() { IdItem = idItem, CodigoActividad = codigo, Descripcion = descripcion, Unidad = unidad };
}
