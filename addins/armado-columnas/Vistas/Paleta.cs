namespace practicacq.Vistas;

using System.Windows.Media;

/// <summary>
/// Paleta única de las ventanas del add-in (issue #116): cada color significa
/// siempre lo mismo. Es la única fuente: <c>Estilos.xaml</c> la expone a los XAML
/// (<c>{StaticResource Exito}</c>) y el código que dibuja en canvas la usa
/// directo (<c>Paleta.Exito</c>), así los dos pintan igual.
///
/// No incluye los colores de varilla por diámetro: esos son la tabla de la
/// empresa y viven en <see cref="practicacq.Modelo.ColoresVarilla"/>.
/// </summary>
public static class Paleta
{
    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static SolidColorBrush Pincel(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>El mismo color con transparencia (0 a 255), para rellenos suaves.</summary>
    public static Color ConAlfa(Color c, byte alfa) => Color.FromArgb(alfa, c.R, c.G, c.B);

    // ---- Superficies y bordes ----
    public static Color ColorFondo { get; } = Rgb(0xF0F0F0);
    public static Color ColorSuperficie { get; } = Colors.White;
    public static Color ColorBorde { get; } = Rgb(0xD0D0D0);
    public static Color ColorSeparador { get; } = Rgb(0xE5E5E5);
    /// <summary>Contorno de lo dibujado (columna, secciones).</summary>
    public static Color ColorContorno { get; } = Rgb(0x9CA3AF);
    public static Color ColorContornoFuerte { get; } = Rgb(0x4B5563);

    // ---- Texto ----
    public static Color ColorTexto { get; } = Colors.Black;
    /// <summary>Ayudas, leyendas y notas.</summary>
    public static Color ColorTextoSecundario { get; } = Rgb(0x555555);
    public static Color ColorTextoApagado { get; } = Rgb(0x909090);

    // ---- Significados ----
    /// <summary>Selección y acento (azul).</summary>
    public static Color ColorPrimario { get; } = Rgb(0x0B63CE);
    /// <summary>Lo aceptado, lo que se genera (verde).</summary>
    public static Color ColorExito { get; } = Rgb(0x16A34A);
    public static Color ColorExitoClaro { get; } = Rgb(0x22C55E);
    public static Color ColorExitoFondo { get; } = Rgb(0xDCFCE7);
    public static Color ColorExitoTexto { get; } = Rgb(0x166534);
    /// <summary>Avisos (amarillo): fondo, borde y texto.</summary>
    public static Color ColorAvisoFondo { get; } = Rgb(0xFEF3C7);
    public static Color ColorAvisoBorde { get; } = Rgb(0xF59E0B);
    public static Color ColorAvisoTexto { get; } = Rgb(0x92400E);
    /// <summary>Errores y validación (rojo).</summary>
    public static Color ColorError { get; } = Rgb(0xB91C1C);
    public static Color ColorErrorClaro { get; } = Rgb(0xEF4444);
    /// <summary>Acción que escribe en el modelo (generar el armado).</summary>
    public static Color ColorAccionFinal { get; } = Rgb(0xB45309);

    // ---- Lo que se dibuja de la columna ----
    public static Color ColorNudo { get; } = Rgb(0x3B82F6);
    public static Color ColorZapata { get; } = Rgb(0xB45309);
    public static Color ColorGrupoA { get; } = Rgb(0x1D4ED8);
    public static Color ColorGrupoB { get; } = Rgb(0x047857);

    // ---- Dibujos de las ventanas secundarias ----
    public static Color ColorHormigon { get; } = Rgb(0xF3F4F6);
    public static Color ColorTapadaClara { get; } = Rgb(0xC0C4CA);
    public static Color ColorGrapaS { get; } = Rgb(0x7C3AED);
    public static Color ColorEstribo { get; } = Rgb(0x6B7280);
    public static Color ColorContornoOscuro { get; } = Rgb(0x374151);
    public static Color ColorBordeSuave { get; } = Rgb(0xC9CED6);
    public static Color ColorSeleccionFondo { get; } = Rgb(0xEFF6FF);
    /// <summary>Pieza elegida en el configurador de estribos menores.</summary>
    public static Color ColorSeleccion { get; } = Rgb(0x1D4ED8);
    public static Color ColorEsquemaMuro { get; } = Rgb(0xECECEC);
    public static Color ColorEsquemaMuroBorde { get; } = Rgb(0xA0A0A0);
    public static Color ColorEsquemaColumneta { get; } = Rgb(0x5B8FF9);
    public static Color ColorEsquemaVigueta { get; } = Rgb(0x2EA06A);

    // ---- Estados de los botones (issue #117): pasar el mouse, apretar, deshabilitado ----
    public static Color ColorSuperficieHover { get; } = Rgb(0xF3F4F6);
    public static Color ColorSuperficiePresionada { get; } = Rgb(0xE5E7EB);
    public static Color ColorExitoHover { get; } = Rgb(0x15803D);
    public static Color ColorExitoPresionado { get; } = Rgb(0x166534);
    public static Color ColorAccionFinalHover { get; } = Rgb(0x92400E);
    public static Color ColorAccionFinalPresionada { get; } = Rgb(0x78350F);
    public static Color ColorErrorFondo { get; } = Rgb(0xFEF2F2);
    public static Color ColorDeshabilitadoFondo { get; } = Rgb(0xE5E7EB);
    public static Color ColorDeshabilitadoTexto { get; } = Rgb(0x9CA3AF);

    // ---- Los mismos colores como pincel ----
    public static SolidColorBrush Fondo { get; } = Pincel(ColorFondo);
    public static SolidColorBrush Superficie { get; } = Pincel(ColorSuperficie);
    public static SolidColorBrush Borde { get; } = Pincel(ColorBorde);
    public static SolidColorBrush Separador { get; } = Pincel(ColorSeparador);
    public static SolidColorBrush Contorno { get; } = Pincel(ColorContorno);
    public static SolidColorBrush ContornoFuerte { get; } = Pincel(ColorContornoFuerte);
    public static SolidColorBrush Texto { get; } = Pincel(ColorTexto);
    public static SolidColorBrush TextoSecundario { get; } = Pincel(ColorTextoSecundario);
    public static SolidColorBrush TextoApagado { get; } = Pincel(ColorTextoApagado);
    public static SolidColorBrush Primario { get; } = Pincel(ColorPrimario);
    public static SolidColorBrush Exito { get; } = Pincel(ColorExito);
    public static SolidColorBrush ExitoClaro { get; } = Pincel(ColorExitoClaro);
    public static SolidColorBrush ExitoFondo { get; } = Pincel(ColorExitoFondo);
    public static SolidColorBrush ExitoTexto { get; } = Pincel(ColorExitoTexto);
    public static SolidColorBrush AvisoFondo { get; } = Pincel(ColorAvisoFondo);
    public static SolidColorBrush AvisoBorde { get; } = Pincel(ColorAvisoBorde);
    public static SolidColorBrush AvisoTexto { get; } = Pincel(ColorAvisoTexto);
    public static SolidColorBrush Error { get; } = Pincel(ColorError);
    public static SolidColorBrush ErrorClaro { get; } = Pincel(ColorErrorClaro);
    public static SolidColorBrush AccionFinal { get; } = Pincel(ColorAccionFinal);
    public static SolidColorBrush Nudo { get; } = Pincel(ColorNudo);
    public static SolidColorBrush Zapata { get; } = Pincel(ColorZapata);
    public static SolidColorBrush GrupoA { get; } = Pincel(ColorGrupoA);
    public static SolidColorBrush GrupoB { get; } = Pincel(ColorGrupoB);

    public static SolidColorBrush Hormigon { get; } = Pincel(ColorHormigon);
    public static SolidColorBrush TapadaClara { get; } = Pincel(ColorTapadaClara);
    public static SolidColorBrush GrapaS { get; } = Pincel(ColorGrapaS);
    public static SolidColorBrush Estribo { get; } = Pincel(ColorEstribo);
    public static SolidColorBrush ContornoOscuro { get; } = Pincel(ColorContornoOscuro);
    public static SolidColorBrush BordeSuave { get; } = Pincel(ColorBordeSuave);
    public static SolidColorBrush SeleccionFondo { get; } = Pincel(ColorSeleccionFondo);
    public static SolidColorBrush Seleccion { get; } = Pincel(ColorSeleccion);
    public static SolidColorBrush EsquemaMuro { get; } = Pincel(ColorEsquemaMuro);
    public static SolidColorBrush EsquemaMuroBorde { get; } = Pincel(ColorEsquemaMuroBorde);
    public static SolidColorBrush EsquemaColumneta { get; } = Pincel(ColorEsquemaColumneta);
    public static SolidColorBrush EsquemaVigueta { get; } = Pincel(ColorEsquemaVigueta);
    public static SolidColorBrush SuperficieHover { get; } = Pincel(ColorSuperficieHover);
    public static SolidColorBrush SuperficiePresionada { get; } = Pincel(ColorSuperficiePresionada);
    public static SolidColorBrush ExitoHover { get; } = Pincel(ColorExitoHover);
    public static SolidColorBrush ExitoPresionado { get; } = Pincel(ColorExitoPresionado);
    public static SolidColorBrush AccionFinalHover { get; } = Pincel(ColorAccionFinalHover);
    public static SolidColorBrush AccionFinalPresionada { get; } = Pincel(ColorAccionFinalPresionada);
    public static SolidColorBrush ErrorFondo { get; } = Pincel(ColorErrorFondo);
    public static SolidColorBrush DeshabilitadoFondo { get; } = Pincel(ColorDeshabilitadoFondo);
    public static SolidColorBrush DeshabilitadoTexto { get; } = Pincel(ColorDeshabilitadoTexto);
}
