using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

    /// <summary>No es readonly: "Reintentar" vuelve a leer el .env por si se agregaron las claves.</summary>
    private SubidaCloudinary _subida;

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
    private ICollectionView? _vistaPresupuestos;
    private string _filtroPresupuesto = string.Empty;
    private bool _actualizando;
    private bool _cancelarActualizacion;

    private static string RutaVistaPrevia => Path.Combine(ConfiguracionImagenes.Carpeta, "vista-previa", "vista-previa");

    public VentanaImagenesActividad(IPuenteRevit puente, IntPtr ventanaRevit)
    {
        InitializeComponent();
        _puente = puente;
        _ventanaRevit = ventanaRevit;
        _config = ConfiguracionImagenes.Cargar();
        _api = new ClienteApi(_config);
        _subida = new SubidaCloudinary(_config);
        TxtModeloActivo.Text = $"Modelo abierto: {_puente.NombreModeloActivo}";

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
            _presupuestos = await _api.ObtenerPresupuestosAsync();
            MostrarPresupuestos(codigoAMantener ?? _presupuesto?.Codigo);

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

    // La lista de presupuestos es también el buscador (pedido del 2026-10-13, en vez de una caja de
    // búsqueda aparte): el ComboBox es editable y al escribir se filtra por código o nombre. Detalles:
    // - el filtro va sobre una ICollectionView, así no se reemplaza la lista mientras se escribe;
    // - mientras la lista está desplegada no se abre ningún presupuesto (moverse con las flechas no
    //   debe consultar la API por cada uno): se abre al cerrarla, con el que quedó elegido;
    // - si se cierra sin elegir nada válido, vuelve a mostrar el presupuesto abierto.

    private void MostrarPresupuestos(string? codigoSeleccionado)
    {
        _filtroPresupuesto = string.Empty;
        _vistaPresupuestos = CollectionViewSource.GetDefaultView(_presupuestos);
        _vistaPresupuestos.Filter = o => o is PresupuestoResumen p && CoincideConFiltro(p);

        _cargandoPresupuesto = true;
        CmbPresupuesto.ItemsSource = _vistaPresupuestos;
        CmbPresupuesto.SelectedItem = _presupuestos.FirstOrDefault(p => p.Codigo == codigoSeleccionado);
        _cargandoPresupuesto = false;
    }

    private bool CoincideConFiltro(PresupuestoResumen p) =>
        _filtroPresupuesto.Length == 0
        || p.Codigo.Contains(_filtroPresupuesto, StringComparison.OrdinalIgnoreCase)
        || p.Nombre.Contains(_filtroPresupuesto, StringComparison.OrdinalIgnoreCase);

    private void CmbPresupuesto_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_cargandoPresupuesto || _vistaPresupuestos is null) return;

        string texto = CmbPresupuesto.Text;
        // El texto cambió porque se eligió un presupuesto, no porque el usuario esté escribiendo.
        if (CmbPresupuesto.SelectedItem is PresupuestoResumen elegido && elegido.TextoLista == texto) return;

        TextBox? caja = CmbPresupuesto.Template.FindName("PART_EditableTextBox", CmbPresupuesto) as TextBox;
        int cursor = caja?.CaretIndex ?? texto.Length;

        _filtroPresupuesto = texto.Trim();
        _cargandoPresupuesto = true;
        _vistaPresupuestos.Refresh();
        CmbPresupuesto.Text = texto; // Refresh puede borrar lo escrito si el elegido quedó fuera del filtro.
        _cargandoPresupuesto = false;

        CmbPresupuesto.IsDropDownOpen = true;
        if (caja is not null)
        {
            caja.SelectionLength = 0;
            caja.CaretIndex = Math.Min(cursor, texto.Length);
        }
    }

    private async void CmbPresupuesto_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_cargandoPresupuesto || CmbPresupuesto.IsDropDownOpen) return;
        await AbrirElegidoAsync();
    }

    private async void CmbPresupuesto_DropDownClosed(object sender, EventArgs e) => await AbrirElegidoAsync();

    private async Task AbrirElegidoAsync()
    {
        PresupuestoResumen? elegido = CmbPresupuesto.SelectedItem as PresupuestoResumen;

        // Al cerrar se quita el filtro y el texto vuelve a ser el del presupuesto (el elegido o, si no
        // se eligió ninguno válido, el que ya estaba abierto).
        MostrarPresupuestos(elegido?.Codigo ?? _presupuesto?.Codigo);

        if (elegido is not null && elegido.Codigo != _presupuesto?.Codigo)
            await AbrirPresupuestoAsync(elegido);
    }

    private async Task AbrirPresupuestoAsync(PresupuestoResumen presupuesto)
    {
        string? actividadAMantener = presupuesto.Codigo == _presupuesto?.Codigo ? _actividad?.Codigo : null;
        _presupuesto = presupuesto;
        _registro = new RegistroLocal(presupuesto.Codigo);
        BtnActualizarTodas.IsEnabled = true;

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
        // Durante "Actualizar" el tablero se redibuja tras cada subida, pero la navegación sigue bloqueada.
        BloquearNavegacion(_actualizando);

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

        // Se muestran todas las imágenes de la actividad, de cualquier modelo; las de este modelo
        // primero. El modelo va en cada imagen, no en el presupuesto.
        List<TarjetaImagen> tarjetas = _registro.DeActividad(_actividad.Codigo)
            .Select(i => new TarjetaImagen(i, _puente.IdModeloActivo))
            .OrderByDescending(t => t.EsDeEsteModelo)
            .ToList();
        int deEsteModelo = tarjetas.Count(t => t.EsDeEsteModelo);
        TxtCodigoActividad.Text = _actividad.Codigo;
        TxtDescripcionActividad.Text = _actividad.Descripcion;
        TxtResumenActividad.Text =
            $"Unidad: {_actividad.Unidad} · {(tarjetas.Count == 1 ? "1 imagen" : $"{tarjetas.Count} imágenes")}" +
            (tarjetas.Count > 0 ? $" ({deEsteModelo} de este modelo)" : string.Empty);
        LstImagenes.ItemsSource = tarjetas;
        TxtSinImagenes.Visibility = tarjetas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnNuevaImagen.IsEnabled = !_actualizando;
    }

    private void TarjetaImagen_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TarjetaImagen tarjeta) return;
        if (LstImagenes.ItemsSource is not List<TarjetaImagen> tarjetas) return;

        var visor = new VisorImagen(tarjetas, tarjetas.IndexOf(tarjeta), ActualizarUnaAsync) { Owner = this };
        visor.Show();
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

    /// <summary>
    /// Un color de la paleta ampliada (botón "+"). Al elegirlo, la paleta se cierra sola: el
    /// <c>Items.Refresh()</c> de <see cref="CambiarColor"/> vuelve a armar la fila y su Popup.
    /// </summary>
    private void BtnColorAmpliado_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OpcionColor opcion)
            CambiarColor(opcion.Fila, opcion.Hex);
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
            IdModelo = _puente.IdModeloActivo,
            NombreModelo = _puente.NombreModeloActivo,
            Colores = _seleccion.Tipos.Select(t => new ColorDeTipo
            {
                NombreTipo = t.Nombre,
                ColorHex = t.ColorHex,
                IdTipo = t.IdTipo,
                UniqueIdsElementos = t.UniqueIdsElementos.ToList(),
            }).ToList(),
            UniqueIdVista = _seleccion.UniqueIdVista,
            MismoTipo = _seleccion.MismoTipo,
            Camara = _seleccion.Camara,
            Version = 1,
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
                imagen.PublicIdCloudinary = r.PublicId;
                _registro?.Guardar();
                Estado($"{imagen.Codigo} subida a Cloudinary.");
                // La versión anterior se borra solo cuando la nueva ya está arriba (decisión 3 del plan).
                await BorrarVersionesAnterioresAsync(imagen);
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

    /// <summary>
    /// Borra en Cloudinary las versiones anteriores de una imagen. Si no se puede (faltan las claves o
    /// falla la red), quedan en <see cref="ImagenActividad.PublicIdsPorBorrar"/> para "Reintentar".
    /// </summary>
    private async Task BorrarVersionesAnterioresAsync(ImagenActividad imagen)
    {
        if (!imagen.BorradoPendiente || !_subida.PuedeBorrar) return;

        foreach (string publicId in imagen.PublicIdsPorBorrar.ToList())
        {
            // Nunca borrar la versión vigente, aunque por error haya quedado en la lista.
            if (publicId == imagen.PublicIdCloudinary)
            {
                imagen.PublicIdsPorBorrar.Remove(publicId);
                continue;
            }

            string? error = await _subida.BorrarAsync(publicId);
            if (error is null) imagen.PublicIdsPorBorrar.Remove(publicId);
            else Estado($"{imagen.Codigo}: no se pudo borrar la versión anterior en Cloudinary ({error}).");
        }
        _registro?.Guardar();
    }

    private void ActualizarPendientes()
    {
        int pendientes = _registro?.Todas.Count(i => i.SubidaPendiente || i.BorradoPendiente) ?? 0;
        BtnReintentar.Visibility = pendientes > 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnReintentar.Content = $"Reintentar pendientes ({pendientes})";
    }

    private async void BtnReintentar_Click(object sender, RoutedEventArgs e)
    {
        if (_registro is null) return;
        BtnReintentar.IsEnabled = false;

        // Se vuelve a leer el .env: si se agregaron las claves de Cloudinary, ya se pueden usar sin
        // cerrar el add-in.
        _subida = new SubidaCloudinary(ConfiguracionImagenes.Cargar());

        foreach (ImagenActividad imagen in _registro.Todas.Where(i => i.SubidaPendiente || i.BorradoPendiente).ToList())
        {
            if (imagen.SubidaPendiente)
            {
                Estado($"Subiendo {imagen.Codigo}…");
                await SubirAsync(imagen, esNueva: false);
            }
            else
            {
                Estado($"Borrando versiones anteriores de {imagen.Codigo}…");
                await BorrarVersionesAnterioresAsync(imagen);
            }
        }

        if (!_subida.PuedeBorrar && _registro.Todas.Any(i => i.BorradoPendiente))
            Estado($"Hay versiones anteriores por borrar: falta CLOUDINARY_API_KEY o CLOUDINARY_API_SECRET en {ConfiguracionImagenes.RutaEnv}.");

        if (PanelTablero.Visibility == Visibility.Visible) MostrarTablero();
        ActualizarPendientes();
        BtnReintentar.IsEnabled = true;
    }

    // ───────────────────────────── Actualizar imágenes (issue #186) ─────────────────────────────

    private async void BtnActualizarImagen_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TarjetaImagen tarjeta)
            await ActualizarUnaAsync(tarjeta.Imagen);
    }

    /// <summary>"Actualizar esta": desde la tarjeta o desde el visor en grande.</summary>
    private async Task ActualizarUnaAsync(ImagenActividad imagen)
    {
        if (_actualizando || _registro is null) return;

        Actualizando(true);
        Estado($"Actualizando {imagen.Codigo} en Revit…");
        ResultadoActualizacion r = await ActualizarImagenAsync(imagen);
        Actualizando(false);
        if (PanelTablero.Visibility == Visibility.Visible) MostrarTablero();
        ActualizarPendientes();

        if (r.Estado != EstadoActualizacion.Actualizada)
        {
            Estado($"{imagen.Codigo} no se actualizó.");
            Avisar($"{imagen.Codigo} no se actualizó: {r.Detalle}");
        }
        else if (r.Faltantes > 0)
        {
            Avisar($"{imagen.Codigo} se actualizó, pero {TextoFaltantes(r.Faltantes)} y no sale en la imagen.");
        }
    }

    /// <summary>"Actualizar imágenes": todas las del presupuesto (decisión 6 del plan).</summary>
    private async void BtnActualizarTodas_Click(object sender, RoutedEventArgs e)
    {
        if (_actualizando || _registro is null || _presupuesto is null) return;

        List<ImagenActividad> todas = _registro.Todas.OrderBy(i => i.Codigo, StringComparer.Ordinal).ToList();
        List<ImagenActividad> deOtroModelo = todas.Where(i => !i.EsDelModelo(_puente.IdModeloActivo)).ToList();
        List<ImagenActividad> sinDatos = todas.Except(deOtroModelo).Where(i => !i.TieneDatosParaActualizar).ToList();
        List<ImagenActividad> aActualizar = todas.Except(deOtroModelo).Except(sinDatos).ToList();

        if (aActualizar.Count == 0)
        {
            Avisar(todas.Count == 0
                ? $"El presupuesto {_presupuesto.Codigo} no tiene imágenes guardadas en esta PC."
                : $"Ninguna imagen de {_presupuesto.Codigo} se puede actualizar desde este modelo " +
                  $"({deOtroModelo.Count} de otro modelo, {sinDatos.Count} anteriores a la actualización automática).");
            return;
        }

        if (MessageBox.Show(this,
                $"Se van a volver a generar {aActualizar.Count} imagen(es) de {_presupuesto.Codigo} con el modelo como está ahora. " +
                "Cada imagen conserva su código, nombre, descripción y colores; en Cloudinary se reemplaza la versión anterior.\n\n¿Continuar?",
                "CQEING · Actualizar imágenes", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        Actualizando(true);
        var actualizadas = new List<string>();
        var conFaltantes = new List<string>();
        var conError = new List<string>();
        int hechas = 0;
        foreach (ImagenActividad imagen in aActualizar)
        {
            if (_cancelarActualizacion) break;
            hechas++;
            Estado($"Actualizando {hechas} de {aActualizar.Count} · {imagen.Codigo}…");

            ResultadoActualizacion r = await ActualizarImagenAsync(imagen);
            if (r.Estado != EstadoActualizacion.Actualizada) conError.Add($"{imagen.Codigo}: {r.Detalle}");
            else if (r.Faltantes > 0) conFaltantes.Add($"{imagen.Codigo}: {TextoFaltantes(r.Faltantes)}");
            else actualizadas.Add(imagen.Codigo);
        }
        bool cancelada = _cancelarActualizacion;
        Actualizando(false);
        if (PanelTablero.Visibility == Visibility.Visible) MostrarTablero();
        RefrescarEstructura();
        ActualizarPendientes();

        var resumen = new System.Text.StringBuilder();
        if (cancelada) resumen.AppendLine($"Cancelada: quedaron {aActualizar.Count - hechas} imagen(es) sin actualizar.").AppendLine();
        resumen.AppendLine($"Actualizadas: {actualizadas.Count + conFaltantes.Count}");
        foreach (string linea in conFaltantes) resumen.AppendLine($"   · {linea}");
        if (deOtroModelo.Count > 0) resumen.AppendLine($"Omitidas por ser de otro modelo: {deOtroModelo.Count}");
        if (sinDatos.Count > 0)
            resumen.AppendLine($"Omitidas por ser anteriores a la actualización automática (rehacer con Nueva imagen): {sinDatos.Count}");
        if (conError.Count > 0)
        {
            resumen.AppendLine($"Con error: {conError.Count}");
            foreach (string linea in conError) resumen.AppendLine($"   · {linea}");
        }
        int pendientes = aActualizar.Count(i => i.SubidaPendiente);
        if (pendientes > 0)
            resumen.AppendLine().AppendLine($"{pendientes} quedaron solo en esta PC (subida pendiente): usa \"Reintentar pendientes\".");

        Estado($"Actualización terminada: {actualizadas.Count + conFaltantes.Count} actualizadas, {conError.Count} con error.");
        MessageBox.Show(this, resumen.ToString(), "CQEING · Actualizar imágenes", MessageBoxButton.OK,
            conError.Count > 0 || conFaltantes.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void BtnCancelarActualizacion_Click(object sender, RoutedEventArgs e)
    {
        _cancelarActualizacion = true;
        BtnCancelarActualizacion.IsEnabled = false;
        Estado("Cancelando: se termina la imagen en curso…");
    }

    /// <summary>
    /// Rehace una imagen: Revit genera el PNG nuevo, se guarda como la versión siguiente, se sube y,
    /// ya arriba, se borra la anterior. El código, el nombre, la descripción y los colores no cambian.
    /// </summary>
    private async Task<ResultadoActualizacion> ActualizarImagenAsync(ImagenActividad imagen)
    {
        if (!imagen.EsDelModelo(_puente.IdModeloActivo))
            return ResultadoActualizacion.NoSe("es de otro modelo; ábrela con el modelo del que salió.");
        if (!imagen.TieneDatosParaActualizar)
            return ResultadoActualizacion.NoSe("es anterior a la actualización automática; rehazla con Nueva imagen.");

        int versionNueva = imagen.Version + 1;
        string rutaSinExtension = _registro!.RutaParaImagen(imagen.PublicIdDeVersion(versionNueva));

        // La respuesta de Revit llega por callback; se espera sin bloquear la ventana.
        var tcs = new TaskCompletionSource<(ResultadoRecaptura?, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _puente.Recapturar(imagen, rutaSinExtension, (r, error) => tcs.TrySetResult((r, error)));
        var (recaptura, errorRevit) = await tcs.Task;
        if (recaptura is null) return ResultadoActualizacion.NoSe(errorRevit ?? "Revit no generó la imagen.");

        // La versión que está en Cloudinary pasa a "por borrar". Las imágenes subidas antes de guardar
        // el public_id se identifican por su URL.
        string publicIdAnterior = imagen.PublicIdCloudinary.Length > 0
            ? imagen.PublicIdCloudinary
            : SubidaCloudinary.PublicIdDesdeUrl(imagen.Url);
        if (publicIdAnterior.Length > 0 && !imagen.PublicIdsPorBorrar.Contains(publicIdAnterior))
            imagen.PublicIdsPorBorrar.Add(publicIdAnterior);

        string rutaAnterior = imagen.RutaLocal;
        imagen.Version = versionNueva;
        imagen.RutaLocal = recaptura.RutaPng;
        imagen.NombreVistaRevit = recaptura.NombreVista;
        imagen.Fecha = DateTime.Now;
        imagen.Url = null;
        imagen.PublicIdCloudinary = string.Empty;
        _registro.Guardar();

        if (!string.Equals(rutaAnterior, recaptura.RutaPng, StringComparison.OrdinalIgnoreCase) && File.Exists(rutaAnterior))
        {
            try { File.Delete(rutaAnterior); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Abierto en otro programa: queda en la carpeta, no estorba.
            }
        }

        await SubirAsync(imagen, esNueva: false);
        return ResultadoActualizacion.Hecha(recaptura.Faltantes);
    }

    private void Actualizando(bool actualizando)
    {
        _actualizando = actualizando;
        _cancelarActualizacion = false;
        BtnActualizarTodas.IsEnabled = !actualizando && _registro is not null;
        BtnNuevaImagen.IsEnabled = !actualizando && _actividad is not null;
        BtnReintentar.IsEnabled = !actualizando;
        LstImagenes.IsEnabled = !actualizando;
        BtnCancelarActualizacion.IsEnabled = true;
        BtnCancelarActualizacion.Visibility = actualizando ? Visibility.Visible : Visibility.Collapsed;
        BloquearNavegacion(actualizando);
    }

    private static string TextoFaltantes(int n) =>
        n == 1 ? "1 elemento ya no está en el modelo" : $"{n} elementos ya no están en el modelo";

    private enum EstadoActualizacion { Actualizada, NoActualizada }

    private sealed record ResultadoActualizacion(EstadoActualizacion Estado, string Detalle, int Faltantes)
    {
        public static ResultadoActualizacion Hecha(int faltantes) => new(EstadoActualizacion.Actualizada, string.Empty, faltantes);
        public static ResultadoActualizacion NoSe(string motivo) => new(EstadoActualizacion.NoActualizada, motivo, 0);
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
}
