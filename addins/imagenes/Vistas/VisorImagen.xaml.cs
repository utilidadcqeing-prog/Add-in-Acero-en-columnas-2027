using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using practicacq.Imagenes.Modelo;

namespace practicacq.Imagenes.Vistas;

/// <summary>
/// Vista previa en grande de las imágenes guardadas de una actividad, sin salir de Revit. Se abre
/// con clic en una tarjeta del tablero; las flechas (o ← →) pasan a las otras imágenes de la misma
/// actividad, en el orden del tablero.
/// </summary>
public partial class VisorImagen : Window
{
    private readonly IReadOnlyList<TarjetaImagen> _tarjetas;
    private readonly Func<ImagenActividad, Task> _actualizar;
    private int _indice;

    /// <param name="actualizar">
    /// Rehace la imagen en Revit (issue #186). Lo hace la ventana principal, que es dueña del registro y
    /// de la subida; al terminar, el visor vuelve a cargar el PNG.
    /// </param>
    public VisorImagen(IReadOnlyList<TarjetaImagen> tarjetas, int indiceInicial, Func<ImagenActividad, Task> actualizar)
    {
        InitializeComponent();
        _tarjetas = tarjetas;
        _actualizar = actualizar;
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
        // Se lee del disco y no de la tarjeta: después de "Actualizar" el PNG es otro.
        var png = TarjetaImagen.CargarImagen(imagen);
        ImgGrande.Source = png;
        TxtSinArchivo.Visibility = png is null ? Visibility.Visible : Visibility.Collapsed;

        TxtPosicion.Text = $"Imagen {_indice + 1} de {_tarjetas.Count} · actividad {imagen.CodigoActividad}";
        TxtCodigo.Text = imagen.Codigo;
        TxtNombre.Text = imagen.Nombre;
        TxtDescripcion.Text = string.IsNullOrWhiteSpace(imagen.Descripcion) ? "Sin descripción" : imagen.Descripcion;
        TxtModelo.Text = t.TextoModelo.Replace("Modelo: ", string.Empty);
        TxtModelo.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(t.ColorModelo)!;
        TxtVista.Text = $"{imagen.TipoVista} · {imagen.NombreVistaRevit}";
        LstColores.ItemsSource = imagen.Colores;
        TxtFecha.Text = imagen.Fecha.ToString("dd/MM/yyyy HH:mm") + (imagen.Version > 1 ? $" · versión {imagen.Version}" : string.Empty);
        TxtSubida.Text = imagen.TextoEstadoSubida;

        BtnAbrirCloudinary.IsEnabled = !imagen.SubidaPendiente;
        BtnActualizar.IsEnabled = t.PuedeActualizar;
        BtnActualizar.ToolTip = t.TextoActualizar;
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

    private async void BtnActualizar_Click(object sender, RoutedEventArgs e)
    {
        BtnActualizar.IsEnabled = false;
        BtnActualizar.Content = "Actualizando en Revit…";
        try
        {
            await _actualizar(Actual.Imagen);
        }
        finally
        {
            BtnActualizar.Content = "Actualizar esta imagen";
            if (IsLoaded) Mostrar();
        }
    }

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
