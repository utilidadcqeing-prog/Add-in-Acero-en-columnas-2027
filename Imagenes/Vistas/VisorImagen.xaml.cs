using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace practicacq.Imagenes.Vistas;

/// <summary>
/// Vista previa en grande de las imágenes guardadas de una actividad, sin salir de Revit. Se abre
/// con clic en una tarjeta del tablero; las flechas (o ← →) pasan a las otras imágenes de la misma
/// actividad, en el orden del tablero.
/// </summary>
public partial class VisorImagen : Window
{
    private readonly IReadOnlyList<TarjetaImagen> _tarjetas;
    private int _indice;

    public VisorImagen(IReadOnlyList<TarjetaImagen> tarjetas, int indiceInicial)
    {
        InitializeComponent();
        _tarjetas = tarjetas;
        _indice = Math.Clamp(indiceInicial, 0, tarjetas.Count - 1);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Left) Mover(-1);
            else if (e.Key == Key.Right) Mover(1);
            else if (e.Key == Key.Escape) Close();
        };

        Mostrar();
    }

    private TarjetaImagen Actual => _tarjetas[_indice];

    private void Mostrar()
    {
        TarjetaImagen t = Actual;
        var imagen = t.Imagen;

        Title = $"CQEING · {imagen.Codigo} {imagen.Nombre}";
        ImgGrande.Source = t.Miniatura;
        TxtSinArchivo.Visibility = t.Miniatura is null ? Visibility.Visible : Visibility.Collapsed;

        TxtPosicion.Text = $"Imagen {_indice + 1} de {_tarjetas.Count} · actividad {imagen.CodigoActividad}";
        TxtCodigo.Text = imagen.Codigo;
        TxtNombre.Text = imagen.Nombre;
        TxtDescripcion.Text = string.IsNullOrWhiteSpace(imagen.Descripcion) ? "Sin descripción" : imagen.Descripcion;
        TxtModelo.Text = t.TextoModelo.Replace("Modelo: ", string.Empty);
        TxtModelo.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(t.ColorModelo)!;
        TxtVista.Text = $"{imagen.TipoVista} · {imagen.NombreVistaRevit}";
        LstColores.ItemsSource = imagen.Colores;
        TxtFecha.Text = imagen.Fecha.ToString("dd/MM/yyyy HH:mm");
        TxtSubida.Text = imagen.TextoEstadoSubida;

        BtnAbrirCloudinary.IsEnabled = !imagen.SubidaPendiente;
        BtnAnterior.Visibility = _tarjetas.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        BtnSiguiente.Visibility = BtnAnterior.Visibility;
    }

    private void Mover(int paso)
    {
        if (_tarjetas.Count < 2) return;
        _indice = (_indice + paso + _tarjetas.Count) % _tarjetas.Count;
        Mostrar();
    }

    private void BtnAnterior_Click(object sender, RoutedEventArgs e) => Mover(-1);

    private void BtnSiguiente_Click(object sender, RoutedEventArgs e) => Mover(1);

    private void BtnAbrirCloudinary_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Actual.Imagen.Url))
            Process.Start(new ProcessStartInfo(Actual.Imagen.Url) { UseShellExecute = true });
    }

    private void BtnAbrirCarpeta_Click(object sender, RoutedEventArgs e)
    {
        string ruta = Actual.Imagen.RutaLocal;
        if (File.Exists(ruta))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{ruta}\"") { UseShellExecute = true });
        else if (Directory.Exists(Path.GetDirectoryName(ruta)))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(ruta)}\"") { UseShellExecute = true });
    }
}
