namespace practicacq.Vistas;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using practicacq.Modelo;

/// <summary>
/// L4: "Ventana Configurador de Estribos Menores". Diálogo modal, se abre
/// desde L2 con la cantidad de barras por cara ya cargada.
///
/// La rejilla se genera sola desde <c>barrasPorCaraX</c>/<c>barrasPorCaraY</c>:
/// cada cara reparte su cantidad mitad y mitad (mismo criterio que documenta
/// <see cref="ConfigLongitudinal"/>), así que cada mitad es una posición
/// interna donde puede ir una grapa o una caja cruzando entre bordes
/// opuestos. Sin una sola referencia a Revit, como el resto del carril.
/// </summary>
public partial class VentanaEstribosMenores : Window
{
    private readonly int _filas;
    private readonly int _columnas;
    private readonly TipoEstriboMenor?[,] _celdas;

    /// <summary>Lo que queda configurado si el usuario acepta (DialogResult == true).</summary>
    public List<EstriboMenor> Resultado { get; private set; } = new();

    public VentanaEstribosMenores(int barrasPorCaraX, int barrasPorCaraY, IEnumerable<EstriboMenor> existentes)
    {
        InitializeComponent();

        _columnas = Math.Max(1, barrasPorCaraX / 2);
        _filas = Math.Max(1, barrasPorCaraY / 2);
        _celdas = new TipoEstriboMenor?[_filas, _columnas];

        foreach (EstriboMenor e in existentes)
        {
            if (e.PosicionY >= 0 && e.PosicionY < _filas && e.PosicionX >= 0 && e.PosicionX < _columnas)
                _celdas[e.PosicionY, e.PosicionX] = e.Tipo;
        }

        ArmarRejilla();
        ActualizarResumen();
    }

    private void ArmarRejilla()
    {
        GridRejilla.RowDefinitions.Clear();
        GridRejilla.ColumnDefinitions.Clear();
        GridRejilla.Children.Clear();

        for (int f = 0; f < _filas; f++)
            GridRejilla.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        for (int c = 0; c < _columnas; c++)
            GridRejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });

        for (int f = 0; f < _filas; f++)
        {
            for (int c = 0; c < _columnas; c++)
            {
                var boton = new Button
                {
                    Margin = new Thickness(2),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Tag = (f, c),
                };
                boton.Click += Celda_Click;
                Grid.SetRow(boton, f);
                Grid.SetColumn(boton, c);
                GridRejilla.Children.Add(boton);
                PintarCelda(boton, f, c);
            }
        }
    }

    private void Celda_Click(object sender, RoutedEventArgs e)
    {
        var boton = (Button)sender;
        (int f, int c) = ((int, int))boton.Tag;

        // Ciclo: vacío -> grapa -> caja -> vacío.
        _celdas[f, c] = _celdas[f, c] switch
        {
            null => TipoEstriboMenor.Grapa,
            TipoEstriboMenor.Grapa => TipoEstriboMenor.Caja,
            _ => null,
        };

        PintarCelda(boton, f, c);
        ActualizarResumen();
    }

    private void PintarCelda(Button boton, int fila, int columna)
    {
        TipoEstriboMenor? tipo = _celdas[fila, columna];

        boton.Background = tipo switch
        {
            TipoEstriboMenor.Grapa => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
            TipoEstriboMenor.Caja => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            _ => new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
        };
        boton.Content = tipo switch
        {
            TipoEstriboMenor.Grapa => "G",
            TipoEstriboMenor.Caja => "C",
            _ => "",
        };
        boton.Foreground = tipo is null ? Brushes.Gray : Brushes.White;
    }

    private void ActualizarResumen()
    {
        int grapas = 0, cajas = 0;
        foreach (TipoEstriboMenor? t in _celdas)
        {
            if (t == TipoEstriboMenor.Grapa) grapas++;
            else if (t == TipoEstriboMenor.Caja) cajas++;
        }

        TextoResumen.Text = $"{grapas} grapa(s), {cajas} caja(s) de {_filas * _columnas} posición(es).";
    }

    private void BotonAceptar_Click(object sender, RoutedEventArgs e)
    {
        Resultado = new List<EstriboMenor>();

        for (int f = 0; f < _filas; f++)
            for (int c = 0; c < _columnas; c++)
                if (_celdas[f, c] is { } tipo)
                    Resultado.Add(new EstriboMenor { PosicionX = c, PosicionY = f, Tipo = tipo });

        DialogResult = true;
    }

    private void BotonCancelar_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
