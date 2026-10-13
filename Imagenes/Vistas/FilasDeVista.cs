using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using practicacq.Imagenes.Modelo;

namespace practicacq.Imagenes.Vistas;

/// <summary>
/// Una fila de la lista "Estructura": capítulo, subcapítulo o actividad. Es una lista plana (no un
/// TreeView) para que el buscador pueda filtrar sin perder los encabezados de cada actividad.
/// </summary>
public sealed class FilaEstructura
{
    public int Nivel { get; init; }
    public string Codigo { get; init; } = string.Empty;
    public string Texto { get; init; } = string.Empty;
    public ActividadPresupuesto? Actividad { get; init; }
    public int CantidadImagenes { get; init; }

    public bool EsActividad => Actividad is not null;
    public Thickness Sangria => new(Nivel * 14, 0, 0, 0);
    public FontWeight Peso => EsActividad ? FontWeights.Normal : FontWeights.Bold;
    public string TextoCantidad => EsActividad ? CantidadImagenes.ToString() : string.Empty;
}

/// <summary>Un tipo seleccionado en el editor, con sus botones de paleta.</summary>
public sealed class FilaTipo
{
    public FilaTipo(TipoSeleccionado tipo)
    {
        Tipo = tipo;
        Opciones = PaletaColores.Colores.Select(c => new OpcionColor(c, this)).ToList();
        Ampliada = PaletaColores.Ampliada.Select(fila => fila.Select(c => new OpcionColor(c, this)).ToList()).ToList();
    }

    public TipoSeleccionado Tipo { get; }

    /// <summary>Los 8 colores rápidos.</summary>
    public List<OpcionColor> Opciones { get; }

    /// <summary>La paleta del botón "+", por filas (de claro a oscuro, y grises al final).</summary>
    public List<List<OpcionColor>> Ampliada { get; }
}

public sealed class OpcionColor
{
    public OpcionColor(string hex, FilaTipo fila)
    {
        Hex = hex;
        Fila = fila;
    }

    public string Hex { get; }
    public FilaTipo Fila { get; }
}

/// <summary>Una tarjeta del tablero de la actividad.</summary>
public sealed class TarjetaImagen
{
    public TarjetaImagen(ImagenActividad imagen, string idModeloActivo)
    {
        Imagen = imagen;
        Miniatura = CargarSinBloquear(imagen.RutaLocal);
        EsDeEsteModelo = imagen.EsDelModelo(idModeloActivo);
    }

    public ImagenActividad Imagen { get; }
    public BitmapImage? Miniatura { get; }

    /// <summary>Si la imagen salió del modelo abierto ahora en Revit.</summary>
    public bool EsDeEsteModelo { get; }

    public string TextoVista => $"Vista {Imagen.TipoVista} · {Imagen.NombreVistaRevit}";

    public string TextoModelo =>
        string.IsNullOrEmpty(Imagen.IdModelo) ? "Modelo: sin registrar (imagen anterior)"
        : EsDeEsteModelo ? $"Modelo: {Imagen.NombreModelo} (este modelo)"
        : $"Modelo: {Imagen.NombreModelo} (otro modelo)";

    public string ColorModelo => EsDeEsteModelo ? "#0F8660" : "#B86A0C";

    /// <summary>
    /// Carga la imagen en memoria y suelta el archivo enseguida (<c>OnLoad</c>): si no, WPF lo deja
    /// bloqueado y no se puede renombrar ni reemplazar mientras el tablero esté abierto.
    /// </summary>
    public static BitmapImage? CargarSinBloquear(string ruta)
    {
        if (string.IsNullOrEmpty(ruta) || !File.Exists(ruta)) return null;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bmp.UriSource = new Uri(ruta, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
