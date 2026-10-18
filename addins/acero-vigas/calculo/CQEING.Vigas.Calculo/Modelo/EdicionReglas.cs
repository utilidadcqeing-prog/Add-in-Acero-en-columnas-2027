namespace CQEING.Vigas.Calculo.Modelo;

/// <summary>
/// El panel de Reglas (#82, maqueta "Reglas generales"): siempre abre bloqueado. Para cambiar hay que desbloquear;
/// los cambios van a un borrador hasta "Guardar y bloquear", que los deja para todos los proyectos.
/// Si se sale con <see cref="HayCambiosSinGuardar"/>, la pantalla avisa antes de <see cref="Descartar"/>.
/// </summary>
public sealed class EdicionReglas
{
    private readonly AlmacenReglas _almacen;

    public EdicionReglas(AlmacenReglas almacen)
    {
        _almacen = almacen;
        ReglasCargadas cargadas = almacen.Cargar();
        Guardadas = Borrador = cargadas.Reglas;
        ProblemaAlCargar = cargadas.Problema;
    }

    public Reglas Guardadas { get; private set; }

    public Reglas Borrador { get; private set; }

    public bool Bloqueadas { get; private set; } = true;

    public string? ProblemaAlCargar { get; }

    public bool HayCambiosSinGuardar => !Borrador.Equivale(Guardadas);

    public void Desbloquear() => Bloqueadas = false;

    public void Cambiar(Reglas nuevas)
    {
        if (Bloqueadas) throw new InvalidOperationException("Las reglas están bloqueadas: hay que desbloquearlas.");
        Borrador = nuevas;
    }

    public void CambiarDiametro(int diametro, ReglaDiametro regla) =>
        Cambiar(Borrador with { PorDiametro = new Dictionary<int, ReglaDiametro>(Borrador.PorDiametro) { [diametro] = regla } });

    /// <summary>
    /// Con errores (valores inválidos o el archivo que no se pudo escribir) no guarda y sigue desbloqueada; los
    /// devuelve para mostrarlos. Vacía si guardó.
    /// </summary>
    public IReadOnlyList<string> GuardarYBloquear()
    {
        IReadOnlyList<string> errores = Borrador.Validar();
        if (errores.Count > 0) return errores;
        try
        {
            _almacen.Guardar(Borrador);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ["No se pudieron guardar las reglas en el PC."];
        }
        Guardadas = Borrador;
        Bloqueadas = true;
        return errores;
    }

    public void Descartar()
    {
        Borrador = Guardadas;
        Bloqueadas = true;
    }
}
