using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using CQEING.Despiece.Calculo;
using Color = Autodesk.Revit.DB.Color;

namespace CQEING.Despiece.Revit;

/// <summary>Exporta una vista a PNG con fondo blanco. El fondo de Revit vuelve a su color pase lo que pase.</summary>
internal static class ExportadorImagen
{
    /// <summary>La sección es pequeña: con esto se ve nítida aun al acercarla.</summary>
    public const int AnchoNormal = 1800;

    /// <summary>La vista larga se acerca para leer sus etiquetas (Juan, 30-oct): una viga larga queda muy angosta a 1800 px.</summary>
    public const int AnchoLarga = 3000;

    /// <summary>Las plantas se acercan con la rueda (#221). A 2000 px exportan en la mitad de tiempo que a 3000 (Juan, 30-oct).</summary>
    public const int AnchoPlanta = 2000;

    /// <summary>Devuelve la ruta del PNG, en <paramref name="carpeta"/>, con nombre único para que la página no muestre uno viejo.</summary>
    public static string Exportar(Document doc, View vista, string carpeta, int anchoPixeles = AnchoNormal)
    {
        string prefijo = "e" + Guid.NewGuid().ToString("N");
        var opciones = new ImageExportOptions
        {
            ExportRange = ExportRange.SetOfViews,
            FilePath = Path.Combine(carpeta, prefijo),
            ZoomType = ZoomFitType.FitToPage,
            PixelSize = anchoPixeles,
            FitDirection = FitDirectionType.Horizontal,
            HLRandWFViewsFileType = ImageFileType.PNG,
            ShadowViewsFileType = ImageFileType.PNG,
        };
        opciones.SetViewsAndSheets([vista.Id]);
        Color fondo = doc.Application.BackgroundColor;
        try
        {
            doc.Application.BackgroundColor = new Color(255, 255, 255);
            doc.ExportImage(opciones);
        }
        finally
        {
            doc.Application.BackgroundColor = fondo;
        }
        // Revit le agrega el tipo y el nombre de la vista al archivo.
        string exportado = Directory.GetFiles(carpeta, prefijo + "*.png").Single();
        string final = Path.Combine(carpeta, prefijo + ".png");
        File.Move(exportado, final);
        return final;
    }

    /// <summary>El tamaño del PNG en píxeles. Solo lee su encabezado.</summary>
    public static (int Ancho, int Alto) Tamano(string ruta)
    {
        using FileStream entrada = File.OpenRead(ruta);
        BitmapFrame cuadro = BitmapDecoder.Create(entrada, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        return (cuadro.PixelWidth, cuadro.PixelHeight);
    }

    /// <summary>Ubica y borra las cruces de calibración del PNG, y lo vuelve a guardar. Null si no están las dos.</summary>
    public static (Calibracion? Calibracion, int Ancho, int Alto) Calibrar(string ruta, PuntoModelo marcaA, PuntoModelo marcaB)
    {
        BitmapSource imagen;
        using (FileStream entrada = File.OpenRead(ruta))
        {
            BitmapFrame cuadro = BitmapDecoder.Create(entrada, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            imagen = new FormatConvertedBitmap(cuadro, PixelFormats.Bgra32, null, 0);
        }
        int ancho = imagen.PixelWidth, alto = imagen.PixelHeight;
        var bgra = new byte[ancho * alto * 4];
        imagen.CopyPixels(bgra, ancho * 4, 0);
        Calibracion? calibracion = CalibracionImagen.Calibrar(bgra, ancho, alto, marcaA, marcaB);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(ancho, alto, 96, 96, PixelFormats.Bgra32, null, bgra, ancho * 4)));
        using (FileStream salida = File.Create(ruta))
            encoder.Save(salida);
        return (calibracion, ancho, alto);
    }
}
