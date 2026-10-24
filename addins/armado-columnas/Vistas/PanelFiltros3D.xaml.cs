namespace practicacq.Vistas;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using practicacq.Modelo;

/// <summary>
/// El botón «Ver en 3D» y los filtros de la vista previa 3D (#123,
/// docs/planes/2026-10-21-vista-previa-3d-columnas-123.md, pasos 4 y 6). Solo
/// arma un <see cref="FiltroPrevia3D"/>: quien lo pinta en Revit es la ventana,
/// por su evento.
/// </summary>
public partial class PanelFiltros3D : UserControl
{
    private static readonly Brush ColorTexto = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly Brush ColorAviso = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E));

    /// <summary>Mientras se ponen las casillas desde el código, no se avisa cada cambio.</summary>
    private bool _cargando;

    /// <summary>Las casillas de diámetro, por número de varilla (-1 = fuera de la tabla).</summary>
    private readonly Dictionary<int, CheckBox> _diametros = new();

    public PanelFiltros3D()
    {
        InitializeComponent();

        foreach (CheckBox casilla in Casillas())
        {
            casilla.Checked += (_, _) => Avisar();
            casilla.Unchecked += (_, _) => Avisar();
        }

        foreach (RadioButton radio in new[] { RadioTodo, RadioGanchos, RadioTraslapos })
            radio.Checked += (_, _) => Avisar();
    }

    /// <summary>«Ver en 3D»: abrir la vista y empezar a actualizarla.</summary>
    public event Action? VerSolicitado;

    /// <summary>Cambió algún filtro.</summary>
    public event Action? FiltroCambiado;

    public bool SoloTramoActivo => CasillaTramoActivo.IsChecked == true;

    /// <summary>
    /// Lo marcado. <see cref="FiltroPrevia3D.SoloAltura"/> queda sin poner: el
    /// tramo activo lo conoce la ventana.
    /// </summary>
    public FiltroPrevia3D LeerFiltro()
    {
        static HashSet<T> Marcados<T>(params (CheckBox Casilla, T Valor)[] opciones) =>
            opciones.Where(o => o.Casilla.IsChecked == true).Select(o => o.Valor).ToHashSet();

        return new FiltroPrevia3D
        {
            Clases = Marcados(
                (CasillaLongitudinales, ClasePieza.Longitudinal), (CasillaEstribos, ClasePieza.Estribo), (CasillaMenores, ClasePieza.EstriboMenor)),
            Grupos = Marcados((CasillaA, GrupoTraslapo.A), (CasillaB, GrupoTraslapo.B)),
            Posiciones = Marcados(
                (CasillaEsquinas, PosicionBarra.Esquina), (CasillaCaraX, PosicionBarra.CaraX), (CasillaCaraY, PosicionBarra.CaraY)),
            Zonas = Marcados((CasillaConfinada, ZonaEstribo.Confinada), (CasillaNoConfinada, ZonaEstribo.NoConfinada)),
            NumerosOcultos = _diametros.Where(p => p.Value.IsChecked != true).Select(p => p.Key).ToHashSet(),
            Partes = RadioGanchos.IsChecked == true ? ModoPartes.SoloGanchos
                : RadioTraslapos.IsChecked == true ? ModoPartes.SoloTraslapos
                : ModoPartes.Todo,
        };
    }

    /// <summary>Lo que dejó la vista previa: estado y casillas de diámetro con lo que hay de verdad.</summary>
    public void MostrarResumen(ResumenPrevia3D resumen)
    {
        MostrarEstado(resumen.Texto, resumen.Fallo);
        if (resumen.Numeros.Count > 0) MostrarDiametros(resumen.Numeros);
    }

    public void MostrarEstado(string texto, bool aviso = false)
    {
        TextoEstado.Text = texto;
        TextoEstado.Foreground = aviso ? ColorAviso : ColorTexto;
    }

    /// <summary>Una casilla por número de varilla, con su color. Las que ya estaban conservan lo marcado.</summary>
    private void MostrarDiametros(IReadOnlyList<int> numeros)
    {
        if (numeros.OrderBy(n => n).SequenceEqual(_diametros.Keys.OrderBy(n => n))) return;

        var antes = _diametros.ToDictionary(p => p.Key, p => p.Value.IsChecked == true);
        _diametros.Clear();
        CasillasDiametro.Children.Clear();
        foreach (int numero in numeros.OrderBy(n => n))
        {
            ColoresVarilla.ColorRgb color = ColoresVarilla.Tabla
                .Where(t => t.Numero == numero).Select(t => t.Color).DefaultIfEmpty(ColoresVarilla.Gris).First();
            var contenido = new StackPanel { Orientation = Orientation.Horizontal };
            contenido.Children.Add(new Rectangle
            {
                Width = 12, Height = 6, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
                Fill = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)),
            });
            contenido.Children.Add(new TextBlock { Text = numero > 0 ? $"#{numero}" : "otro" });

            var casilla = new CheckBox { Content = contenido, IsChecked = !antes.TryGetValue(numero, out bool marcada) || marcada };
            casilla.Checked += (_, _) => Avisar();
            casilla.Unchecked += (_, _) => Avisar();
            _diametros[numero] = casilla;
            CasillasDiametro.Children.Add(casilla);
        }
    }

    private IEnumerable<CheckBox> Casillas() => new[]
    {
        CasillaLongitudinales, CasillaEstribos, CasillaMenores, CasillaA, CasillaB, CasillaEsquinas, CasillaCaraX, CasillaCaraY,
        CasillaConfinada, CasillaNoConfinada, CasillaTramoActivo,
    };

    private void Avisar()
    {
        if (!_cargando) FiltroCambiado?.Invoke();
    }

    private void BotonVer3D_Click(object sender, RoutedEventArgs e) => VerSolicitado?.Invoke();

    // ------------------------------------------------------------ atajos

    /// <summary>Pone las casillas como dice <paramref name="filtro"/> (diámetros y tramo, todos marcados) y avisa una sola vez.</summary>
    private void Aplicar(FiltroPrevia3D filtro)
    {
        _cargando = true;
        try
        {
            CasillaLongitudinales.IsChecked = filtro.Clases.Contains(ClasePieza.Longitudinal);
            CasillaEstribos.IsChecked = filtro.Clases.Contains(ClasePieza.Estribo);
            CasillaMenores.IsChecked = filtro.Clases.Contains(ClasePieza.EstriboMenor);
            CasillaA.IsChecked = filtro.Grupos.Contains(GrupoTraslapo.A);
            CasillaB.IsChecked = filtro.Grupos.Contains(GrupoTraslapo.B);
            CasillaEsquinas.IsChecked = filtro.Posiciones.Contains(PosicionBarra.Esquina);
            CasillaCaraX.IsChecked = filtro.Posiciones.Contains(PosicionBarra.CaraX);
            CasillaCaraY.IsChecked = filtro.Posiciones.Contains(PosicionBarra.CaraY);
            CasillaConfinada.IsChecked = filtro.Zonas.Contains(ZonaEstribo.Confinada);
            CasillaNoConfinada.IsChecked = filtro.Zonas.Contains(ZonaEstribo.NoConfinada);
            CasillaTramoActivo.IsChecked = false;
            foreach (CheckBox casilla in _diametros.Values) casilla.IsChecked = true;
            RadioTodo.IsChecked = filtro.Partes == ModoPartes.Todo;
            RadioGanchos.IsChecked = filtro.Partes == ModoPartes.SoloGanchos;
            RadioTraslapos.IsChecked = filtro.Partes == ModoPartes.SoloTraslapos;
        }
        finally
        {
            _cargando = false;
        }

        FiltroCambiado?.Invoke();
    }

    private void AtajoTodo_Click(object sender, RoutedEventArgs e) => Aplicar(FiltroPrevia3D.Todo());
    private void AtajoLongitudinales_Click(object sender, RoutedEventArgs e) => Aplicar(FiltroPrevia3D.SoloLongitudinales());
    private void AtajoEstribos_Click(object sender, RoutedEventArgs e) => Aplicar(FiltroPrevia3D.SoloEstribos());
    private void AtajoGanchos_Click(object sender, RoutedEventArgs e) => Aplicar(FiltroPrevia3D.SoloGanchos());
    private void AtajoTraslapos_Click(object sender, RoutedEventArgs e) => Aplicar(FiltroPrevia3D.SoloTraslapos());
}
