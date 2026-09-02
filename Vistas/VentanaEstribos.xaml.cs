namespace practicacq.Vistas;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.ComponentModel;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using practicacq.Logica;
using practicacq.Modelo;

public partial class VentanaEstribos : Window
{
    private readonly UIApplication _uiapp;
    private List<FamilyInstance> _columnas = new();

    private readonly ManejadorSeleccionColumnas _manejadorSeleccion = new();
    private readonly ManejadorEstribos _manejadorEstribos = new();
    private readonly ExternalEvent _eventoSeleccion;
    private readonly ExternalEvent _eventoGeneracion;

    public VentanaEstribos(UIApplication uiapp)
    {
        InitializeComponent();

        _uiapp = uiapp;

        _manejadorSeleccion.AlTerminar = RecibirColumnas;
        _manejadorEstribos.AlTerminar = RecibirResultado;
        _manejadorEstribos.AlFallar = mensaje =>
            Dispatcher.Invoke(() => TaskDialog.Show("Error", mensaje));

        _eventoSeleccion = ExternalEvent.Create(_manejadorSeleccion);
        _eventoGeneracion = ExternalEvent.Create(_manejadorEstribos);

        CargarTiposDeBarra();
        CargarTiposDeGancho();
    }

    private void CargarTiposDeGancho()
    {
        Document doc = _uiapp.ActiveUIDocument.Document;

        List<TipoGancho> ganchos = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarHookType))
            .Cast<RebarHookType>()
            .Select(g => new TipoGancho(g))
            .OrderByDescending(g => g.EsDeEstribo)
            .ThenByDescending(g => g.EsDe135)
            .ThenBy(g => g.Etiqueta, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        ComboGanchos.ItemsSource = Agrupar(ganchos, nameof(TipoGancho.Grupo));

        if (ganchos.Count > 0)
            ComboGanchos.SelectedIndex = 0;
        else
            TaskDialog.Show(
                "Faltan ganchos",
                "El proyecto no tiene cargado ningún tipo de gancho de armadura. " +
                "Cárgalos desde la plantilla estructural antes de generar estribos.");
    }

    private void CargarTiposDeBarra()
    {
        Document doc = _uiapp.ActiveUIDocument.Document;

        List<TipoBarra> tipos = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarBarType))
            .Cast<RebarBarType>()
            .Select(t => new TipoBarra(t))
            .OrderByDescending(t => t.EsDeColumna)
            .ThenBy(t => t.Diametro)
            .ToList();

        ComboBarras.ItemsSource = Agrupar(tipos, nameof(TipoBarra.Grupo));

        if (tipos.Count > 0)
            ComboBarras.SelectedIndex = 0;
        else
            TaskDialog.Show(
                "Faltan tipos de barra",
                "El proyecto no tiene cargado ningún tipo de barra de armadura. " +
                "Cárgalos antes de generar estribos.");
    }

    /// <summary>
    /// Envuelve la lista en una vista agrupada, para que el desplegable muestre
    /// encabezados en vez de 20 elementos seguidos sin jerarquía.
    /// </summary>
    private static ICollectionView Agrupar<T>(IEnumerable<T> elementos, string propiedad)
    {
        var vista = new CollectionViewSource { Source = elementos };
        vista.GroupDescriptions.Add(new PropertyGroupDescription(propiedad));
        return vista.View;
    }

    // ---------- Selección de columnas ----------

    private void BotonSeleccionar_Click(object sender, RoutedEventArgs e) => _eventoSeleccion.Raise();

    private void RecibirColumnas(List<FamilyInstance> columnas)
    {
        Dispatcher.Invoke(() =>
        {
            _columnas = columnas;
            TextoSeleccion.Text = columnas.Count switch
            {
                0 => "Ninguna columna seleccionada",
                1 => "1 columna seleccionada",
                _ => $"{columnas.Count} columnas seleccionadas"
            };
        });
    }

    // ---------- Generación ----------

    private void BotonGenerar_Click(object sender, RoutedEventArgs e)
    {
        if (_columnas.Count == 0)
        {
            TaskDialog.Show("Sin selección", "Selecciona al menos una columna.");
            return;
        }

        if (ComboBarras.SelectedItem is not TipoBarra barra)
        {
            TaskDialog.Show("Sin tipo de barra", "Elige el tipo de barra del estribo.");
            return;
        }

        if (ComboGanchos.SelectedItem is not TipoGancho gancho)
        {
            TaskDialog.Show("Sin tipo de gancho", "Elige el gancho de los extremos del estribo.");
            return;
        }

        if (!LeerMetros(CampoEspaciado.Text, "espaciado", out double espaciado) ||
            !LeerMetros(CampoExtremos.Text, "separación en los extremos", out double extremos) ||
            !LeerMetros(CampoRecubrimiento.Text, "recubrimiento", out double recubrimiento))
            return;

        _manejadorEstribos.Opciones = new OpcionesEstribos
        {
            Columnas = _columnas,
            TipoBarra = barra.Tipo,
            TipoGancho = gancho.Tipo,
            Espaciado = espaciado,
            SeparacionExtremos = extremos,
            RecubrimientoObjetivo = recubrimiento,
            CorregirRecubrimiento = ChkCorregir.IsChecked == true
        };

        _eventoGeneracion.Raise();
    }

    /// <summary>Lee un campo en metros y lo pasa a pies (unidades internas).</summary>
    private static bool LeerMetros(string texto, string campo, out double pies)
    {
        pies = 0;

        if (!double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out double metros) &&
            !double.TryParse(texto, NumberStyles.Float, CultureInfo.CurrentCulture, out metros))
        {
            TaskDialog.Show("Valor inválido", $"El {campo} no es un número válido.");
            return false;
        }

        if (metros <= 0)
        {
            TaskDialog.Show("Valor inválido", $"El {campo} tiene que ser mayor que cero.");
            return false;
        }

        pies = UnitUtils.ConvertToInternalUnits(metros, UnitTypeId.Meters);
        return true;
    }

    private void RecibirResultado(ResultadoEstribos res)
    {
        Dispatcher.Invoke(() =>
        {
            var lineas = new List<string>
            {
                $"Estribos creados: {res.Creados}",
                $"Columnas omitidas: {res.Omitidas}"
            };

            if (res.Eliminados > 0)
                lineas.Add($"Estribos anteriores reemplazados: {res.Eliminados}");

            if (res.RecubrimientosCorregidos > 0)
                lineas.Add($"Recubrimientos corregidos: {res.RecubrimientosCorregidos}");

            if (res.Detalles.Count > 0)
            {
                lineas.Add(string.Empty);
                lineas.AddRange(res.Detalles);
            }

            if (res.Avisos.Count > 0)
            {
                lineas.Add(string.Empty);
                lineas.AddRange(res.Avisos);
            }

            TaskDialog.Show("Estribos de columna", string.Join(Environment.NewLine, lineas));
        });
    }
}
