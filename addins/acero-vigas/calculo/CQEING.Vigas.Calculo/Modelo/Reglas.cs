namespace CQEING.Vigas.Calculo.Modelo;

/// <summary>
/// Un gancho del cuadro "Dimensiones mínimas para ganchos estándar", en mm. <paramref name="B"/> es la longitud
/// del gancho, la que se suma al largo de la barra. <paramref name="E"/> es la extensión recta,
/// <paramref name="D"/> el diámetro mínimo de doblado y <paramref name="A"/> la longitud recta de anclaje; son null
/// cuando el cuadro no los trae.
/// </summary>
public sealed record Gancho(int B, int? E = null, int? D = null, int? A = null);

/// <summary>El gancho de una punta de barra o de bastón (#84). Las corridas no pueden quedar <see cref="Sin"/> en el borde.</summary>
public enum TipoGancho { Sin, De90, De135, De180 }

/// <summary>
/// Valores de un diámetro, en mm y kg/m. <paramref name="Traslapo"/> es null si la tabla no lo trae (#2 en vigas).
/// Las barras longitudinales usan <paramref name="Gancho90"/>; <paramref name="Gancho180"/> y
/// <paramref name="Estribo135"/> quedan para estribos y casos especiales.
/// <paramref name="Diametro"/> es el diámetro real de la barra (15.9 para el #5).
/// </summary>
public sealed record ReglaDiametro(int? Traslapo, Gancho Gancho90, Gancho Gancho180, Gancho Estribo135,
    double KgPorMetro, double Diametro);

/// <summary>
/// Reglas generales (#82), en mm. Las claves de <paramref name="PorDiametro"/> son el número de la barra (5 = #5).
/// </summary>
/// <param name="Modulo">Múltiplo de corte.</param>
/// <param name="Varilla">Varilla comercial normal (6 m o 12 m, según el botón de Reglas).</param>
/// <param name="VarillaLarga">La que se usa sola cuando un tramo no se resuelve con la normal.</param>
/// <param name="DistanciaLibre">Separación libre mínima entre barras de una misma fila.</param>
public sealed record Reglas(IReadOnlyDictionary<int, ReglaDiametro> PorDiametro, int Modulo = 500, int Varilla = 6000,
    int VarillaLarga = 12000, double DistanciaLibre = 25)
{
    /// <summary>
    /// Los cuadros de Carlos (reunión del 16-oct-2026), los pesos y los diámetros estándar.
    /// El estribo de 135° del #6 al #8 sale del cuadro de traslapos: el de dimensiones no los trae, ni su E, D y A.
    /// </summary>
    public static Reglas Generales { get; } = new(new Dictionary<int, ReglaDiametro>
    {
        [2] = new(null, new(112, 77, 38, 86), new(96, 65, 38, 110), new(76, 38, 26, 57), 0.250, 6.4),
        [3] = new(550, new(166, 114, 57, 128), new(142, 65, 57, 131), new(113, 57, 38, 84), 0.560, 9.5),
        [4] = new(750, new(222, 152, 76, 181), new(190, 65, 76, 160), new(151, 76, 51, 113), 0.994, 12.7),
        [5] = new(900, new(278, 191, 95, 227), new(238, 65, 95, 200), new(189, 95, 64, 141), 1.552, 15.9),
        [6] = new(1100, new(334, 229, 115, 272), new(286, 76, 115, 240), new(272), 2.235, 19.1),
        [7] = new(1350, new(388, 266, 133, 317), new(333, 89, 133, 279), new(316), 3.042, 22.2),
        [8] = new(1700, new(444, 305, 152, 363), new(381, 102, 152, 319), new(362), 3.973, 25.4),
    });

    /// <summary>
    /// Lo que el gancho <paramref name="t"/> suma al largo de la barra, en mm. El tramo recto llega a la cara de afuera
    /// del doblez (como lo dibuja Revit), así que el gancho suma su arco por el eje más la cola E del cuadro, menos el
    /// radio y medio diámetro que el tramo ya cubre: la pata queda con la cola de Carlos (Juan, 26-oct; antes sumaba la B
    /// entera y Revit alargaba la cola, 256 en vez de 191 en el #5 a 90°). Sin E en el cuadro (135° del #6 al #8), la B.
    /// El de 135° sale del cuadro de estribos, el único 135° que mandó Carlos (decisión 5 del plan de #84; pregunta 1 en
    /// #79), con el doblado de gancho (<see cref="DobladoGancho"/>).
    /// </summary>
    public static int LargoGancho(ReglaDiametro r, TipoGancho t)
    {
        (int grados, Gancho g) = t switch
        {
            TipoGancho.De90 => (90, r.Gancho90),
            TipoGancho.De135 => (135, r.Estribo135),
            TipoGancho.De180 => (180, r.Gancho180),
            _ => (0, r.Gancho90),
        };
        if (grados == 0)
            return 0;
        if (g.E is not int cola)
            return g.B;
        double radio = DobladoGancho(r, t) / 2 + r.Diametro / 2;
        return (int)Math.Round(radio * grados * Math.PI / 180 + cola - radio - r.Diametro / 2);
    }

    /// <summary>
    /// Diámetro de doblado de un gancho de barra o bastón, en mm: el del cuadro de 90° (o de 180°), que es el de gancho
    /// del tipo de barra en Revit (6 d); el de 135° también, porque la fila de 135° del cuadro es la de estribos y Revit
    /// dobla las barras con el de gancho. 6 d si el cuadro no lo trae.
    /// </summary>
    public static double DobladoGancho(ReglaDiametro r, TipoGancho t) =>
        (t == TipoGancho.De180 ? r.Gancho180.D : r.Gancho90.D) ?? 6 * r.Diametro;

    /// <summary>Varilla de la que sale una barra de ese largo: la normal o, si no alcanza, la larga.</summary>
    public int VarillaPara(int largo) => largo > Varilla ? VarillaLarga : Varilla;

    /// <summary>Los que muestra la pantalla de Reglas (la maqueta va del #3 al #8). El #2 se guarda sin mostrarse.</summary>
    public static IReadOnlyList<int> DiametrosEnPantalla { get; } = [3, 4, 5, 6, 7, 8];

    /// <summary>
    /// Lo que impide guardarlas, un mensaje por problema: primero los valores generales y luego cada diámetro, de
    /// menor a mayor. Vacía si son válidas. Un diámetro sin traslapo se permite: el motor avisa al calcular.
    /// </summary>
    public IReadOnlyList<string> Validar()
    {
        var errores = new List<string>();
        if (Modulo <= 0) errores.Add("El múltiplo de corte debe ser mayor que 0.");
        if (Varilla is not (6000 or 12000)) errores.Add("La varilla debe ser de 6 m o de 12 m.");
        if (VarillaLarga < Varilla) errores.Add("La varilla larga no puede ser más corta que la normal.");
        if (DistanciaLibre <= 0) errores.Add("La distancia libre entre barras debe ser mayor que 0.");
        foreach ((int d, ReglaDiametro r) in PorDiametro.OrderBy(par => par.Key))
        {
            if (r.Traslapo <= 0) errores.Add($"El traslapo del #{d} debe ser mayor que 0.");
            if (r.Gancho90.B <= 0) errores.Add($"El gancho de 90° del #{d} debe ser mayor que 0.");
            if (r.Gancho180.B <= 0) errores.Add($"El gancho de 180° del #{d} debe ser mayor que 0.");
            if (r.Estribo135.B <= 0) errores.Add($"El estribo de 135° del #{d} debe ser mayor que 0.");
            if (r.KgPorMetro <= 0) errores.Add($"El peso por metro del #{d} debe ser mayor que 0.");
            if (r.Diametro <= 0) errores.Add($"El diámetro del #{d} debe ser mayor que 0.");
        }
        return errores;
    }

    /// <summary>Si tienen los mismos valores (el diccionario se compara por contenido, no por referencia).</summary>
    public bool Equivale(Reglas otra) =>
        Modulo == otra.Modulo && Varilla == otra.Varilla && VarillaLarga == otra.VarillaLarga
        && DistanciaLibre.Equals(otra.DistanciaLibre) && PorDiametro.Count == otra.PorDiametro.Count
        && PorDiametro.All(par => otra.PorDiametro.TryGetValue(par.Key, out ReglaDiametro? r) && r == par.Value);
}
