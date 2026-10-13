using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using practicacq.Imagenes.Almacen;
using practicacq.Imagenes.Api;
using practicacq.Imagenes.Configuracion;
using practicacq.Imagenes.Modelo;

namespace practicacq.Imagenes.Vistas;

/// <summary>
/// Ventana principal del add-in de imágenes por actividad (issue #183). Flujo:
/// presupuesto → actividad → Nueva imagen → Ir a Revit (seleccionar) → colores → nombre → Guardar.
///
/// No referencia <c>Autodesk.Revit</c>: todo lo de Revit pasa por <see cref="IPuenteRevit"/>.
/// </summary>
public partial class VentanaImagenesActividad : Window
{
    private readonly IPuenteRevit _puente;
    private readonly IntPtr _ventanaRevit;
    private readonly ConfiguracionImagenes _config;
    private readonly ClienteApi _api;
    private readonly SubidaCloudinary _subida;

    private List<PresupuestoResumen> _presupuestos = new();
    private PresupuestoResumen? _presupuesto;
    private RegistroLocal? _registro;
    private List<Capitulo> _estructura = new();
    private ActividadPresupuesto? _actividad;

    private BarraSeleccion? _barra;
    private bool _mismoTipo;
    private ResultadoSeleccion? _seleccion;
    private List<FilaTipo> _filasTipo = new();
    private bool _vistaPreviaVigente;
    private bool _cargandoPresupuesto;

    private static string RutaVistaPrevia => Path.Combine(ConfiguracionImagenes.Carpeta, "vista-previa", "vista-previa");

    public VentanaImagenesActividad(IPuenteRevit puente, IntPtr ventanaRevit)
    {
        InitializeComponent();
        _puente = puente;
        _ventanaRevit = ventanaRevit;
        _config = ConfiguracionImagenes.Cargar();
        _api = new ClienteApi(_config);
        _subida = new SubidaCloudinary(_config);

        Loaded += async (_, _) => await CargarPresupuestosAsync(null);
        Closing += (_, _) =>
        {
            if (_barra is not null)
            {
                _puente.CancelarSeleccion();
                _barra.Close();
            }
        };
    }

    // ───────────────────────────── Presupuesto ─────────────────────────────

    private async Task CargarPresupuestosAsync(string? codigoAMantener)
    {
        try
        {
            Estado("Cargando presupuestos…");
            _presupuestos = await _api.ObtenerPresupuestosAsync(_puente.IdModeloActivo);
            FiltrarPresupuestos(codigoAMantener ?? _presupuesto?.Codigo);

            // "Actualizar códigos" recarga también la estructura y el vínculo del presupuesto abierto.
            if (CmbPresupuesto.SelectedItem is PresupuestoResumen abierto)
                await AbrirPresupuestoAsync(abierto);
            else if (CmbPresupuesto.Items.Count > 0)
                CmbPresupuesto.SelectedIndex = 0;

            if (_api.ModoEjemplo)
                Estado($"DATOS DE EJEMPLO: falta API_URL o API_KEY en {ConfiguracionImagenes.RutaEnv}. Las imágenes sí se generan y se guardan de verdad.");
        }
        catch (Exception ex)
        {
            Estado("No se pudieron cargar los presupuestos.");
            Avisar($"No se pudieron cargar los presupuestos de la API:\n\n{ex.Message}");
        }
    }

    private void FiltrarPresupuestos(string? codigoSeleccionado)
    {
        string filtro = TxtBuscarPresupuesto.Text.Trim();
        List<PresupuestoResumen> visibles = _presupuestos
            .Where(p => filtro.Length == 0
                || p.Codigo.Contains(filtro, StringComparison.OrdinalIgnoreCase)
                || p.Nombre.Contains(filtro, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Solo cambia la lista visible: filtrar no debe reabrir el presupuesto.
        _cargandoPresupuesto = true;
        CmbPresupuesto.ItemsSource = visibles;
        CmbPresupuesto.SelectedItem = visibles.FirstOrDefault(p => p.Codigo == codigoSeleccionado);
        _cargandoPresupuesto = false;
    }

    private void TxtBuscarPresupuesto_TextChanged(object sender, TextChangedEventArgs e) => FiltrarPresupuestos(_presupuesto?.Codigo);

    private async void CmbPresupuesto_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_cargandoPresupuesto || CmbPresupuesto.SelectedItem is not PresupuestoResumen p) return;
        if (p.Codigo == _presupuesto?.Codigo) return;
        await AbrirPresupuestoAsync(p);
    }

    private async Task AbrirPresupuestoAsync(PresupuestoResumen presupuesto)
    {
        string? actividadAMantener = presupuesto.Codigo == _presupuesto?.Codigo ? _actividad?.Codigo : null;
        _presupuesto = presupuesto;
        _registro = new RegistroLocal(presupuesto.Codigo);
        MostrarEstadoVinculo(presupuesto);

        try
        {
            Estado($"Cargando la estructura de {presupuesto.Codigo}…");
            _estructura = await _api.ObtenerEstructuraAsync(presupuesto.Codigo);
            _actividad = _estructura
                .SelectMany(c => c.Subcapitulos).SelectMany(s => s.Actividades)
                .FirstOrDefault(a => a.Codigo == actividadAMantener);

            RefrescarEstructura();
            MostrarTablero();
            int total = _estructura.Sum(c => c.Subcapitulos.Sum(s => s.Actividades.Count));
            if (!_api.ModoEjemplo)
                Estado($"Estructura de {presupuesto.Codigo} cargada · {total} actividades · {_registro.Todas.Count} imágenes guardadas en esta PC");
        }
        catch (Exception ex)
        {
            _estructura = new();
            RefrescarEstructura();
            Estado("No se pudo cargar la estructura.");
            Avisar($"No se pudo cargar la estructura de {presupuesto.Codigo}:\n\n{ex.Message}");
        }

        ActualizarPendientes();
    }

    private void MostrarEstadoVinculo(PresupuestoResumen p)
    {
        TxtEstadoVinculo.Text = p.TextoEstado;
        (string fondo, string texto) = p.Estado switch
        {
            EstadoVinculo.VinculadoAEsteModelo => ("#DCF2E9", "#0F8660"),
            EstadoVinculo.VinculadoAOtroModelo => ("#FBEFD9", "#B86A0C"),
            _ => ("#E3E8EE", "#586777"),
        };
        PildoraEstado.Background = Pincel(fondo);
        TxtEstadoVinculo.Foreground = Pincel(texto);
    }

    private async void BtnActualizar_Click(object sender, RoutedEventArgs e) => await CargarPresupuestosAsync(_presupuesto?.Codigo);

    // ───────────────────────────── Estructura ─────────────────────────────

    private void RefrescarEstructura()
    {
        string filtro = TxtBuscarActividad.Text.Trim();
        bool Coincide(ActividadPresupuesto a) => filtro.Length == 0
            || a.Codigo.Contains(filtro, StringComparison.OrdinalIgnoreCase)
            || a.Descripcion.Contains(filtro, StringComparison.OrdinalIgnoreCase);

        var filas = new List<FilaEstructura>();
        foreach (Capitulo capitulo in _estructura)
        {
            var filasCapitulo = new List<FilaEstructura>();
            foreach (Subcapitulo sub in capitulo.Subcapitulos)
            {
                List<ActividadPresupuesto> actividades = sub.Actividades.Where(Coincide).ToList();
                if (actividades.Count == 0) continue;

                filasCapitulo.Add(new FilaEstructura { Nivel = 1, Codigo = sub.Codigo, Texto = sub.Nombre });
                filasCapitulo.AddRange(actividades.Select(a => new FilaEstructura
                {
                    Nivel = 2,
                    Codigo = a.Codigo,
                    Texto = a.Descripcion,
                    Actividad = a,
                    CantidadImagenes = _registro?.CantidadDe(a.Codigo) ?? 0,
                }));
            }
            if (filasCapitulo.Count == 0) continue;

            filas.Add(new FilaEstructura { Nivel = 0, Codigo = capitulo.Codigo, Texto = capitulo.Nombre });
            filas.AddRange(filasCapitulo);
        }

        LstEstructura.ItemsSource = filas;
        LstEstructura.SelectedItem = filas.FirstOrDefault(f => f.Actividad?.Codigo == _actividad?.Codigo && f.EsActividad);
    }

    private void TxtBuscarActividad_TextChanged(object sender, TextChangedEventArgs e) => RefrescarEstructura();

    private void LstEstructura_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Los encabezados de capítulo/subcapítulo no se eligen: solo las actividades.
        if (LstEstructura.SelectedItem is not FilaEstructura { Actividad: { } actividad }) return;
        if (actividad.Codigo == _actividad?.Codigo) return;
        _actividad = actividad;
        MostrarTablero();
    }

    // ───────────────────────────── Tablero ─────────────────────────────

    private void MostrarTablero()
    {
        PanelEditor.Visibility = Visibility.Collapsed;
        PanelTablero.Visibility = Visibility.Visible;
        BloquearNavegacion(false);

        bool soloLectura = _presupuesto?.SoloLectura == true;
        TxtSoloLectura.Visibility = soloLectura ? Visibility.Visible : Visibility.Collapsed;

        if (_actividad is null || _registro is null)
        {
            TxtCodigoActividad.Text = string.Empty;
            TxtDescripcionActividad.Text = "Elige una actividad";
            TxtResumenActividad.Text = "Elige una actividad a la izquierda, pulsa Nueva imagen, selecciona los elementos en Revit y ajusta los colores.";
            LstImagenes.ItemsSource = null;
            TxtSinImagenes.Visibility = Visibility.Collapsed;
            BtnNuevaImagen.IsEnabled = false;
            return;
        }

        List<TarjetaImagen> tarjetas = _registro.DeActividad(_actividad.Codigo).Select(i => new TarjetaImagen(i)).ToList();
        TxtCodigoActividad.Text = _actividad.Codigo;
        TxtDescripcionActividad.Text = _actividad.Descripcion;
        TxtResumenActividad.Text = $"Unidad: {_actividad.Unidad} · {(tarjetas.Count == 1 ? "1 imagen" : $"{tarjetas.Count} imágenes")}";
        LstImagenes.ItemsSource = tarjetas;
        TxtSinImagenes.Visibility = tarjetas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnNuevaImagen.IsEnabled = !soloLectura;
    }

    private void BtnNuevaImagen_Click(object sender, RoutedEventArgs e)
    {
        if (_actividad is null || _registro is null) return;

        _seleccion = null;
        _filasTipo = new();
        _vistaPreviaVigente = false;
        LstTipos.ItemsSource = null;
        ImgVistaPrevia.Source = null;
        TxtVistaPrevia.Visibility = Visibility.Visible;
        TxtVistaPrevia.Text = "Aquí aparece la vista previa cuando selecciones los elementos en Revit.";
        TxtResumenSeleccion.Text = "Todavía no hay elementos seleccionados.";
        TxtTituloEditor.Text = $"Nueva imagen · {_actividad.Codigo} {_actividad.Descripcion}";
        TxtCodigoImagen.Text = SiguienteCodigo();
        TxtNombre.Text = string.Empty;
        TxtDescripcion.Text = string.Empty;
        BtnGuardar.IsEnabled = false;
        BtnVistaPrevia.IsEnabled = false;

        PanelTablero.Visibility = Visibility.Collapsed;
        PanelEditor.Visibility = Visibility.Visible;
        BloquearNavegacion(true);
    }

    private string SiguienteCodigo() =>
        CodigoImagen.Armar(_actividad!.Codigo, CodigoImagen.SiguienteNumero(_actividad.Codigo, _registro!.Todas.Select(i => i.Codigo)));

    private void BloquearNavegacion(bool bloquear)
    {
        // Mientras se arma una imagen no se cambia de presupuesto ni de actividad: la imagen quedaría
        // guardada con un código que no corresponde.
        CmbPresupuesto.IsEnabled = !bloquear;
        TxtBuscarPresupuesto.IsEnabled = !bloquear;
        LstEstructura.IsEnabled = !bloquear;
        TxtBuscarActividad.IsEnabled = !bloquear;
    }

    private void BtnCancelarEditor_Click(object sender, RoutedEventArgs e) => MostrarTablero();

    // ───────────────────────────── 1 · Selección en Revit ─────────────────────────────

    private void BtnIrARevit_Click(object sender, RoutedEventArgs e)
    {
        _barra = new BarraSeleccion(_mismoTipo);
        // Dueña = ventana de Revit (no esta, que se esconde: si no, la barra se escondería con ella).
        new WindowInteropHelper(_barra).Owner = _ventanaRevit;

        _barra.MismoTipoCambiado += mismoTipo =>
        {
            _mismoTipo = mismoTipo;
            _puente.CambiarMismoTipo(mismoTipo);
        };
        _barra.Aceptada += () =>
        {
            Estado("Leyendo la selección de Revit…");
            _puente.TerminarSeleccion(AlTerminarSeleccion);
        };
        _barra.Cancelada += () =>
        {
            _puente.CancelarSeleccion();
            VolverDeRevit();
        };

        Hide();
        _barra.Show();
        _puente.IniciarSeleccion(_mismoTipo, cantidad => _barra?.MostrarCantidad(cantidad));
    }

    private void VolverDeRevit()
    {
        _barra = null;
        Show();
        Activate();
    }

    private void AlTerminarSeleccion(ResultadoSeleccion? resultado, string? error)
    {
        VolverDeRevit();
        if (resultado is null)
        {
            Estado("Selección sin elementos.");
            Avisar(error ?? "No se pudo leer la selección de Revit.");
            return;
        }

        // Si se vuelve a seleccionar, los tipos que ya tenían color lo conservan.
        Dictionary<long, string> coloresPrevios = _seleccion?.Tipos.ToDictionary(t => t.IdTipo, t => t.ColorHex) ?? new();
        int siguiente = coloresPrevios.Count;
        foreach (TipoSeleccionado tipo in resultado.Tipos)
        {
            tipo.ColorHex = coloresPrevios.TryGetValue(tipo.IdTipo, out string? previo) ? previo : PaletaColores.PorIndice(siguiente++);
        }

        _seleccion = resultado;
        _filasTipo = resultado.Tipos.Select(t => new FilaTipo(t)).ToList();
        LstTipos.ItemsSource = _filasTipo;
        TxtResumenSeleccion.Text =
            $"{resultado.TotalElementos} elementos de {resultado.Tipos.Count} tipo(s) · vista {resultado.TipoVista} \"{resultado.NombreVista}\". " +
            "Puedes volver a Revit para cambiar la selección.";
        BtnIrARevit.Content = "Volver a Revit para cambiar la selección";
        GenerarVistaPrevia();
    }

    // ───────────────────────────── 2 · Colores ─────────────────────────────

    private void BtnColorPaleta_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OpcionColor opcion)
            CambiarColor(opcion.Fila, opcion.Hex);
    }

    private void TxtColorLibre_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AplicarColorLibre((TextBox)sender);
    }

    private void TxtColorLibre_LostFocus(object sender, RoutedEventArgs e) => AplicarColorLibre((TextBox)sender);

    private void AplicarColorLibre(TextBox caja)
    {
        if (caja.Tag is not FilaTipo fila) return;
        string? hex = PaletaColores.Normalizar(caja.Text);
        if (hex is null)
        {
            caja.Text = fila.Tipo.ColorHex;
            Estado("Color no válido: escribe #RRGGBB, por ejemplo #E0453A.");
            return;
        }
        if (hex != fila.Tipo.ColorHex) CambiarColor(fila, hex);
    }

    private void CambiarColor(FilaTipo fila, string hex)
    {
        fila.Tipo.ColorHex = hex;
        LstTipos.Items.Refresh();
        _vistaPreviaVigente = false;
        Estado("Cambiaste un color: pulsa \"Actualizar vista previa\" para verlo (al guardar se genera con los colores nuevos).");
    }

    // ───────────────────────────── Vista previa y guardado ─────────────────────────────

    private void BtnVistaPrevia_Click(object sender, RoutedEventArgs e) => GenerarVistaPrevia();

    private void GenerarVistaPrevia()
    {
        if (_seleccion is null) return;

        Ocupado(true, "Generando la vista previa en Revit…");
        _puente.Capturar(_seleccion, RutaVistaPrevia, (ruta, error) =>
        {
            Ocupado(false, null);
            if (ruta is null)
            {
                _vistaPreviaVigente = false;
                Estado("No se pudo generar la vista previa.");
                Avisar(error ?? "No se pudo generar la imagen en Revit.");
                return;
            }

            ImgVistaPrevia.Source = TarjetaImagen.CargarSinBloquear(ruta);
            TxtVistaPrevia.Visibility = Visibility.Collapsed;
            _vistaPreviaVigente = true;
            Estado("Vista previa lista. Escribe un nombre y guarda.");
        });
    }

    private void BtnGuardar_Click(object sender, RoutedEventArgs e)
    {
        if (_seleccion is null || _actividad is null || _registro is null || _presupuesto is null) return;
        if (string.IsNullOrWhiteSpace(TxtNombre.Text))
        {
            Avisar("Escribe un nombre para la imagen.");
            TxtNombre.Focus();
            return;
        }

        string codigo = SiguienteCodigo();
        string rutaSinExtension = _registro.RutaParaImagen(codigo);

        if (_vistaPreviaVigente && File.Exists(RutaVistaPrevia + ".png"))
        {
            // La vista previa ya tiene la selección y los colores actuales: no hace falta volver a Revit.
            File.Copy(RutaVistaPrevia + ".png", rutaSinExtension + ".png", overwrite: true);
            _ = RegistrarYSubirAsync(codigo, rutaSinExtension + ".png");
            return;
        }

        Ocupado(true, "Generando la imagen en Revit…");
        _puente.Capturar(_seleccion, rutaSinExtension, async (ruta, error) =>
        {
            Ocupado(false, null);
            if (ruta is null)
            {
                Avisar(error ?? "No se pudo generar la imagen en Revit.");
                return;
            }
            await RegistrarYSubirAsync(codigo, ruta);
        });
    }

    private async Task RegistrarYSubirAsync(string codigo, string rutaPng)
    {
        var imagen = new ImagenActividad
        {
            Codigo = codigo,
            CodigoActividad = _actividad!.Codigo,
            CodigoPresupuesto = _presupuesto!.Codigo,
            Nombre = TxtNombre.Text.Trim(),
            Descripcion = TxtDescripcion.Text.Trim(),
            TipoVista = _seleccion!.TipoVista,
            NombreVistaRevit = _seleccion.NombreVista,
            Colores = _seleccion.Tipos.Select(t => new ColorDeTipo { NombreTipo = t.Nombre, ColorHex = t.ColorHex }).ToList(),
            RutaLocal = rutaPng,
            Fecha = DateTime.Now,
        };
        _registro!.Agregar(imagen);
        RefrescarEstructura();
        MostrarTablero();
        Estado($"{imagen.Codigo} guardada en esta PC. Subiendo a Cloudinary…");

        await SubirAsync(imagen, esNueva: true);
    }

    /// <summary>
    /// Sube una imagen. Si es nueva y su código ya existe en Cloudinary (otra PC lo usó), pasa al
    /// siguiente número y renombra la copia local, hasta 5 veces.
    /// </summary>
    private async Task SubirAsync(ImagenActividad imagen, bool esNueva)
    {
        for (int intento = 0; intento < 5; intento++)
        {
            ResultadoSubida r = await _subida.SubirAsync(imagen);

            if (r.Exito || (r.CodigoOcupado && !esNueva))
            {
                imagen.Url = r.Url;
                _registro?.Guardar();
                Estado($"{imagen.Codigo} subida a Cloudinary.");
                break;
            }

            if (r.CodigoOcupado)
            {
                string anterior = imagen.Codigo;
                var codigos = (_registro?.Todas.Select(i => i.Codigo) ?? Enumerable.Empty<string>()).Append(anterior);
                imagen.Codigo = CodigoImagen.Armar(imagen.CodigoActividad, CodigoImagen.SiguienteNumero(imagen.CodigoActividad, codigos));
                string nuevaRuta = Path.Combine(Path.GetDirectoryName(imagen.RutaLocal)!, imagen.Codigo + ".png");
                File.Move(imagen.RutaLocal, nuevaRuta, overwrite: true);
                imagen.RutaLocal = nuevaRuta;
                _registro?.Guardar();
                Estado($"{anterior} ya existía en Cloudinary (otra PC). Se renombró a {imagen.Codigo} y se reintenta…");
                continue;
            }

            Estado($"{imagen.Codigo} quedó guardada solo en esta PC: {r.Error}");
            break;
        }

        if (_actividad?.Codigo == imagen.CodigoActividad && PanelTablero.Visibility == Visibility.Visible) MostrarTablero();
        RefrescarEstructura();
        ActualizarPendientes();
    }

    private void ActualizarPendientes()
    {
        int pendientes = _registro?.Todas.Count(i => i.SubidaPendiente) ?? 0;
        BtnReintentar.Visibility = pendientes > 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnReintentar.Content = $"Reintentar subidas pendientes ({pendientes})";
    }

    private async void BtnReintentar_Click(object sender, RoutedEventArgs e)
    {
        if (_registro is null) return;
        BtnReintentar.IsEnabled = false;
        foreach (ImagenActividad imagen in _registro.Todas.Where(i => i.SubidaPendiente).ToList())
        {
            Estado($"Subiendo {imagen.Codigo}…");
            await SubirAsync(imagen, esNueva: false);
        }
        BtnReintentar.IsEnabled = true;
    }

    // ───────────────────────────── Utilidades ─────────────────────────────

    private void Ocupado(bool ocupado, string? mensaje)
    {
        BtnIrARevit.IsEnabled = !ocupado;
        BtnVistaPrevia.IsEnabled = !ocupado && _seleccion is not null;
        BtnGuardar.IsEnabled = !ocupado && _seleccion is not null;
        Cursor = ocupado ? Cursors.Wait : null;
        if (mensaje is not null) Estado(mensaje);
    }

    private void Estado(string mensaje) => TxtEstado.Text = mensaje;

    private void Avisar(string mensaje) =>
        MessageBox.Show(this, mensaje, "CQEING · Imágenes por Actividad", MessageBoxButton.OK, MessageBoxImage.Warning);

    private static SolidColorBrush Pincel(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
