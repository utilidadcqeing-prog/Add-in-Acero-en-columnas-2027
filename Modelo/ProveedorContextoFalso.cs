namespace practicacq.Modelo;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Implementación falsa de <see cref="IProveedorContexto"/>, para que L2-L5 se
/// desarrollen y se prueben sin abrir Revit — el criterio de aceptación de L5
/// ("sin abrir Revit, en el portátil de 8 GB") depende de que esto exista.
///
/// Devuelve tres columnas fijas:
///
///  - <c>C-12</c>: la columna de 10 tramos del edificio de referencia de
///    William (5 niveles, arranca sobre zapata) — el caso principal que
///    también usan J1/J2 para contrastar contra la lectura real.
///  - <c>C-1</c>: un solo tramo libre, sin ninguna viga que la toque (para
///    probar el aviso "ninguna viga toca esta columna" sin correr Revit).
///  - <c>C-7</c>: tres tramos (dos niveles), para tener algo intermedio con
///    lo que probar la selección múltiple entre columnas distintas.
///
/// <see cref="Logica.LectorContexto"/> (J2) reemplaza a esto sin que L2-L5
/// cambien una línea — es el punto de integración de los dos carriles.
/// </summary>
public sealed class ProveedorContextoFalso : IProveedorContexto
{
    private readonly Dictionary<string, ColumnaContexto> _columnas;

    public ProveedorContextoFalso()
    {
        _columnas = new List<ColumnaContexto>
        {
            ColumnaEdificioWilliam(),
            ColumnaSinVigas(),
            ColumnaDeTresTramos(),
        }.ToDictionary(c => IdDe(c), c => c);
    }

    public IReadOnlyList<ColumnaResumen> ListarColumnas() =>
        _columnas.Values.Select(c => new ColumnaResumen(IdDe(c), c.Marca)).ToList();

    public ColumnaContexto Leer(string idColumna) =>
        _columnas.TryGetValue(idColumna, out ColumnaContexto? c)
            ? c
            : throw new KeyNotFoundException($"El proveedor falso no tiene ninguna columna con id «{idColumna}».");

    /// <summary>El id es la propia marca acá: no hay ElementId real detrás de esto.</summary>
    private static string IdDe(ColumnaContexto c) => c.Marca;

    // ------------------------------------------------------------------ C-12

    /// <summary>
    /// Edificio de 5 niveles, columna de la planta baja: arranca 20 cm sobre la
    /// zapata, cruza 4 vigas de 45 cm de canto (una por nivel) con 2,75 m
    /// libres entre cada una, y remata libre hasta la losa de techo sin que una
    /// quinta viga la corte — por eso son 10 tramos y no 11: el remate no
    /// cierra contra un nudo.
    /// </summary>
    private static ColumnaContexto ColumnaEdificioWilliam()
    {
        const double alturaEntrepiso = 3.20;
        const double cantoViga = 0.45;
        const double arranqueSobreZapata = 0.20;
        const int nivelesConViga = 4;

        var tramos = new List<Tramo>();
        double z = 0.0;
        int indice = 0;

        void Agregar(double alto, TipoTramo tipo)
        {
            tramos.Add(new Tramo { Indice = indice++, ZInferior = z, ZSuperior = z + alto, Tipo = tipo });
            z += alto;
        }

        Agregar(arranqueSobreZapata, TipoTramo.ContraZapata);

        for (int nivel = 1; nivel <= nivelesConViga; nivel++)
        {
            double libre = alturaEntrepiso - cantoViga - (nivel == 1 ? arranqueSobreZapata : 0.0);
            Agregar(libre, TipoTramo.Libre);
            Agregar(cantoViga, TipoTramo.DentroDeViga);
        }

        // Remate: del último nudo hasta la losa de techo, sin viga que lo corte.
        Agregar(alturaEntrepiso - cantoViga, TipoTramo.Libre);

        var niveles = new List<NivelRotulo>
        {
            new() { Nombre = "PB", Cota = 0.00 },
            new() { Nombre = "P1", Cota = alturaEntrepiso },
            new() { Nombre = "P2", Cota = alturaEntrepiso * 2 },
            new() { Nombre = "P3", Cota = alturaEntrepiso * 3 },
            new() { Nombre = "P4", Cota = alturaEntrepiso * 4 },
            new() { Nombre = "Techo", Cota = alturaEntrepiso * 5 },
        };

        return new ColumnaContexto
        {
            Marca = "C-12",
            BaseX = 0.40,
            BaseY = 0.40,
            Recubrimiento = 0.04,
            ZInferior = 0.0,
            // El longitudinal entra a la zapata, a diferencia del estribo:
            // valor ilustrativo (issue #26), no viene de una zapata real acá.
            ZInferiorLongitudinal = -0.30,
            ZSuperior = z,
            Tramos = tramos,
            Niveles = niveles,
            Avisos = new List<string> { "Arranca sobre cimentación: el acero empieza en 0.20 m." },
        };
    }

    // ------------------------------------------------------------------- C-1

    /// <summary>Columna de un solo piso, sin ninguna viga que la toque — el caso "sin nudos".</summary>
    private static ColumnaContexto ColumnaSinVigas() => new()
    {
        Marca = "C-1",
        BaseX = 0.30,
        BaseY = 0.30,
        Recubrimiento = 0.04,
        ZInferior = 0.0,
        ZSuperior = 3.00,
        Tramos = new List<Tramo>
        {
            new() { Indice = 0, ZInferior = 0.0, ZSuperior = 3.00, Tipo = TipoTramo.Libre },
        },
        Niveles = new List<NivelRotulo>
        {
            new() { Nombre = "PB", Cota = 0.00 },
            new() { Nombre = "P1", Cota = 3.00 },
        },
        Avisos = new List<string>
        {
            "Ninguna viga toca esta columna: queda un solo tramo libre de punta a punta.",
        },
    };

    // ------------------------------------------------------------------- C-7

    /// <summary>Columna de dos niveles (tres tramos) — caso intermedio para probar selección mixta.</summary>
    private static ColumnaContexto ColumnaDeTresTramos()
    {
        const double alturaEntrepiso = 3.00;
        const double cantoViga = 0.40;

        var tramos = new List<Tramo>
        {
            new() { Indice = 0, ZInferior = 0.00, ZSuperior = alturaEntrepiso - cantoViga, Tipo = TipoTramo.Libre },
            new() { Indice = 1, ZInferior = alturaEntrepiso - cantoViga, ZSuperior = alturaEntrepiso, Tipo = TipoTramo.DentroDeViga },
            new() { Indice = 2, ZInferior = alturaEntrepiso, ZSuperior = alturaEntrepiso * 2 - cantoViga, Tipo = TipoTramo.Libre },
        };

        return new ColumnaContexto
        {
            Marca = "C-7",
            BaseX = 0.35,
            BaseY = 0.35,
            Recubrimiento = 0.04,
            ZInferior = 0.0,
            ZSuperior = tramos[^1].ZSuperior,
            Tramos = tramos,
            Niveles = new List<NivelRotulo>
            {
                new() { Nombre = "PB", Cota = 0.00 },
                new() { Nombre = "P1", Cota = alturaEntrepiso },
                new() { Nombre = "P2", Cota = alturaEntrepiso * 2 },
            },
            Avisos = new List<string>(),
        };
    }
}
