namespace practicacq.Vistas;

using System;
using System.Globalization;
using System.Windows.Data;
using practicacq.Modelo;

/// <summary>
/// Muestra la cantidad de «N. barras X / Y» como "4 (2 + 2)" (Luciano,
/// 20-oct). Los ítems de las listas siguen siendo <c>int</c>: solo cambia
/// cómo se leen.
/// </summary>
public sealed class ConvertidorBarrasPorCara : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int total ? TextoBarras.PorCara(total) : value?.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
