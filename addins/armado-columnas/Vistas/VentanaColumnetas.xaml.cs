namespace practicacq.Vistas;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using practicacq.Logica;
using practicacq.Modelo;

// Revit y WPF definen ambos Rectangle y Color.
using Rectangle = System.Windows.Shapes.Rectangle;
using Color = System.Windows.Media.Color;

public partial class VentanaColumnetas : Window
{
    private readonly UIApplication _uiapp;
    private readonly ObservableCollection<MuroSeleccionado> _muros = new();

    private readonly ManejadorSeleccion _manejadorSeleccion = new();
    private readonly ManejadorGeneracion _manejadorGeneracion = new();
    private readonly ExternalEvent _eventoSeleccion;
    private readonly ExternalEvent _eventoGeneracion;

    public VentanaColumnetas(UIApplication uiapp)
    {
        InitializeComponent();

        _uiapp = uiapp;
        TablaMuros.ItemsSource = _muros;

        _manejadorSeleccion.AlTerminar = RecibirMuros;
        _manejadorGeneracion.AlTerminar = RecibirResultado;
        _manejadorGeneracion.AlFallar = mensaje =>
            Dispatcher.Invoke(() => TaskDialog.Show("Error", mensaje));

        _eventoSeleccion = ExternalEvent.Create(_manejadorSeleccion);
        _eventoGeneracion = ExternalEvent.Create(_manejadorGeneracion);

        CargarTipos();
        DibujarEsquema();
    }

    /// <summary>
    /// Llena los ComboBox con los tipos de pilar y de armazón del documento.
    /// Se llama desde el comando, que sí está en contexto de API.
    /// </summary>
    private void CargarTipos()
    {
        Document doc = _uiapp.ActiveUIDocument.Document;

        ComboColumnetas.ItemsSource = RecogerTipos(doc, BuiltInCategory.OST_StructuralColumns);
        ComboViguetas.ItemsSource = RecogerTipos(doc, BuiltInCategory.OST_StructuralFraming);

        if (ComboColumnetas.Items.Count > 0) ComboColumnetas.SelectedIndex = 0;
        if (ComboViguetas.Items.Count > 0) ComboViguetas.SelectedIndex = 0;

        if (ComboColumnetas.Items.Count == 0 || ComboViguetas.Items.Count == 0)
        {
            TaskDialog.Show(
                "Faltan familias",
                "El proyecto no tiene cargados tipos de pilar estructural o de armazón " +
                "estructural. Cárgalos antes de generar.");
        }
    }

    private static List<TipoFamilia> RecogerTipos(Document doc, BuiltInCategory categoria)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(categoria)
            .Cast<FamilySymbol>()
            .Select(s => new TipoFamilia(s))
            .OrderBy(t => t.Etiqueta, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // ---------- Selección de muros ----------

    private void BotonSeleccionar_Click(object sender, RoutedEventArgs e)
    {
        _eventoSeleccion.Raise();
    }

    private void RecibirMuros(List<Wall> muros)
    {
        Dispatcher.Invoke(() =>
        {
            Document doc = _uiapp.ActiveUIDocument.Document;
            _muros.Clear();
            foreach (Wall muro in muros)
                _muros.Add(new MuroSeleccionado(muro, doc));

            DibujarEsquema();
            Activate();
        });
    }

    // ---------- Generación ----------

    private void BotonGenerar_Click(object sender, RoutedEventArgs e)
    {
        if (_muros.Count == 0)
        {
            TaskDialog.Show("Sin muros", "Selecciona al menos un muro.");
            return;
        }

        bool generaColumnetas = ChkColumnetas.IsChecked == true;
        bool generaSup = ChkViguetasSup.IsChecked == true;
        bool generaInf = ChkViguetasInf.IsChecked == true;

        if (!generaColumnetas && !generaSup && !generaInf)
        {
            TaskDialog.Show("Nada que generar", "Marca al menos un elemento a generar.");
            return;
        }

        if (!LeerMetros(CampoSeparacion.Text, out double separacion) || separacion <= 0)
        {
            TaskDialog.Show("Dato inválido", "La separación entre columnetas debe ser un número mayor que cero.");
            return;
        }

        double desfase = 0;
        bool aplicaDesfase = ChkDesfase.IsChecked == true;
        if (aplicaDesfase && (!LeerMetros(CampoDesfase.Text, out desfase) || desfase < 0))
        {
            TaskDialog.Show("Dato inválido", "El desfase debe ser un número mayor o igual que cero.");
            return;
        }

        if (generaColumnetas && ComboColumnetas.SelectedItem is not TipoFamilia)
        {
            TaskDialog.Show("Falta el tipo", "Elige un tipo de columneta.");
            return;
        }

        if ((generaSup || generaInf) && ComboViguetas.SelectedItem is not TipoFamilia)
        {
            TaskDialog.Show("Falta el tipo", "Elige un tipo de vigueta.");
            return;
        }

        _manejadorGeneracion.Opciones = new OpcionesGeneracion
        {
            Muros = _muros.Select(m => m.Muro).ToList(),
            TipoColumneta = (ComboColumnetas.SelectedItem as TipoFamilia)?.Simbolo,
            TipoVigueta = (ComboViguetas.SelectedItem as TipoFamilia)?.Simbolo,
            GenerarColumnetas = generaColumnetas,
            GenerarViguetasSuperiores = generaSup,
            GenerarViguetasInferiores = generaInf,
            SeparacionMaxima = AMetrosInternos(separacion),
            AplicarDesfaseVertical = aplicaDesfase,
            DesfaseVertical = AMetrosInternos(desfase),
        };

        _eventoGeneracion.Raise();
    }

    private void RecibirResultado(ResultadoGeneracion res)
    {
        Dispatcher.Invoke(() =>
        {
            string resumen =
                $"Elementos anteriores eliminados: {res.Eliminados}\n\n" +
                $"Columnetas: {res.Columnetas}\n" +
                $"Viguetas superiores: {res.ViguetasSuperiores}\n" +
                $"Viguetas inferiores: {res.ViguetasInferiores}";

            if (res.ColumnetasOmitidas > 0)
                resumen += $"\n\nColumnetas omitidas por coincidir en esquina: {res.ColumnetasOmitidas}";

            if (res.Avisos.Count > 0)
                resumen += "\n\nAvisos:\n" + string.Join("\n", res.Avisos);

            TaskDialog.Show("Generación terminada", resumen);
            Activate();
        });
    }

    // ---------- Utilidades ----------

    private static bool LeerMetros(string texto, out double valor)
    {
        return double.TryParse(texto, NumberStyles.Float, CultureInfo.CurrentCulture, out valor)
            || double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out valor);
    }

    private static double AMetrosInternos(double metros) =>
        UnitUtils.ConvertToInternalUnits(metros, UnitTypeId.Meters);

    // ---------- Esquema ----------

    private void ChkDesfase_Click(object sender, RoutedEventArgs e)
    {
        bool activo = ChkDesfase.IsChecked == true;
        CampoDesfase.IsEnabled = activo;
        EtiquetaDesfase.Foreground = activo ? Brushes.Black : Brushes.Gray;
        DibujarEsquema();
    }

    private void Opciones_Cambiadas(object sender, RoutedEventArgs e) => DibujarEsquema();

    private void Esquema_SizeChanged(object sender, SizeChangedEventArgs e) => DibujarEsquema();

    /// <summary>
    /// Dibuja el alzado de un muro tipo con lo que se va a generar. Es
    /// orientativo: no refleja las medidas reales de los muros seleccionados.
    /// </summary>
    private void DibujarEsquema()
    {
        if (Esquema is null || Esquema.ActualWidth < 10 || Esquema.ActualHeight < 10) return;

        Esquema.Children.Clear();

        const double largoMuro = 6.0;   // metros, muro de referencia
        const double altoMuro = 2.6;

        double margen = 40;
        double ancho = Esquema.ActualWidth - margen * 2;
        double alto = Esquema.ActualHeight - margen * 2 - 20;
        if (ancho < 20 || alto < 20) return;

        double escala = Math.Min(ancho / largoMuro, alto / altoMuro);
        double w = largoMuro * escala;
        double h = altoMuro * escala;
        double x0 = (Esquema.ActualWidth - w) / 2;
        double y0 = (Esquema.ActualHeight - h) / 2 + 10;

        // Muro
        Agregar(new Rectangle
        {
            Width = w,
            Height = h,
            Fill = new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xEC)),
            Stroke = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
            StrokeThickness = 1,
        }, x0, y0);

        var relleno = new SolidColorBrush(Color.FromRgb(0x5B, 0x8F, 0xF9));
        var rellenoVigueta = new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x6A));

        double cantoVigueta = 0.35 * escala;
        double anchoColumneta = 0.20 * escala;

        bool sup = ChkViguetasSup.IsChecked == true;
        bool inf = ChkViguetasInf.IsChecked == true;

        double desfase = 0;
        if (ChkDesfase.IsChecked == true && LeerMetros(CampoDesfase.Text, out double d) && d >= 0)
            desfase = Math.Min(d, altoMuro - 0.35);

        // Columnetas
        if (ChkColumnetas.IsChecked == true)
        {
            double separacion = 3.0;
            if (LeerMetros(CampoSeparacion.Text, out double s) && s > 0) separacion = s;

            int tramos = Math.Max(1, (int)Math.Ceiling(largoMuro / separacion - 1e-6));
            tramos = Math.Min(tramos, 40); // evita dibujar un bosque si ponen 0.01

            for (int i = 0; i <= tramos; i++)
            {
                double x = x0 + w * i / tramos - anchoColumneta / 2;
                x = Math.Max(x0, Math.Min(x, x0 + w - anchoColumneta));

                Agregar(new Rectangle
                {
                    Width = anchoColumneta,
                    Height = h,
                    Fill = relleno,
                }, x, y0);
            }
        }

        // Viguetas
        if (inf)
        {
            Agregar(new Rectangle { Width = w, Height = cantoVigueta, Fill = rellenoVigueta },
                x0, y0 + h - cantoVigueta);
        }

        if (sup)
        {
            double y = y0 + desfase * escala;
            Agregar(new Rectangle { Width = w, Height = cantoVigueta, Fill = rellenoVigueta }, x0, y);
        }

        TextoEsquema.Text = $"Esquema orientativo · muro tipo de {largoMuro:0.#} × {altoMuro:0.#} m";
    }

    private void Agregar(UIElement elemento, double x, double y)
    {
        Canvas.SetLeft(elemento, x);
        Canvas.SetTop(elemento, y);
        Esquema.Children.Add(elemento);
    }
}
