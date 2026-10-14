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
        Miniatura = CargarImagen(imagen);
        EsDeEsteModelo = imagen.EsDelModelo(idModeloActivo);
    }

    public ImagenActividad Imagen { get; }
    public BitmapImage? Miniatura { get; }

    /// <summary>Si la imagen salió del modelo abierto ahora en Revit.</summary>
    public bool EsDeEsteModelo { get; }

    public string TextoVista =>
        $"Vista {Imagen.TipoVista} · {Imagen.NombreVistaRevit}" + (Imagen.Version > 1 ? $" · versión {Imagen.Version}" : string.Empty);

    /// <summary>
    /// Si se puede rehacer sola (issue #186): tiene que ser del modelo abierto y guardar los
    /// <c>UniqueId</c> (decisión 7 del plan <c>docs/planes/2026-10-14-actualizar-imagenes.md</c>).
    /// </summary>
    public bool PuedeActualizar => EsDeEsteModelo && Imagen.TieneDatosParaActualizar;

    public string TextoActualizar =>
        Imagen.SoloEnApi ? "Imagen hecha en otra PC: se actualiza desde la PC donde se creó."
        : !EsDeEsteModelo ? "Solo se puede actualizar con el modelo del que salió abierto en Revit."
        : !Imagen.TieneDatosParaActualizar ? "Imagen anterior a la actualización automática: rehazla con Nueva imagen."
        : "Vuelve a generar la imagen con el modelo como está ahora (mismo código, nombre, descripción y colores).";

    public string TextoModelo =>
        string.IsNullOrEmpty(Imagen.IdModelo) ? "Modelo: sin registrar (imagen anterior)"
        : EsDeEsteModelo ? $"Modelo: {Imagen.NombreModelo} (este modelo)"
        : $"Modelo: {Imagen.NombreModelo} (otro modelo)";

    public string ColorModelo => EsDeEsteModelo ? "#0F8660" : "#B86A0C";

    /// <summary>
    /// Carga la imagen en memoria y suelta el archivo enseguida (<c>OnLoad</c>): si no, WPF lo deja
    /// bloqueado y no se puede renombrar ni reemplazar mientras el tablero esté abierto.
    /// </summary>
    /// <summary>
    /// La imagen desde el archivo de esta PC o, si no está (imagen de otra PC, fase B), desde su URL de
    /// Cloudinary. La de la URL se descarga sola y no se congela (no se puede mientras descarga).
    /// </summary>
    public static BitmapImage? CargarImagen(ImagenActividad imagen)
    {
        BitmapImage? local = CargarSinBloquear(imagen.RutaLocal);
        if (local is not null || string.IsNullOrEmpty(imagen.Url)) return local;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(imagen.Url, UriKind.Absolute);
            bmp.EndInit();
            return bmp;
        }
        catch (Exception ex) when (ex is UriFormatException or NotSupportedException or IOException)
        {
            return null;
        }
    }

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
