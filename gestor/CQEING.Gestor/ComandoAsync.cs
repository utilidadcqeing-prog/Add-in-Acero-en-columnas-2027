using System.Windows.Input;

namespace CQEING.Gestor;

/// <summary>
/// Comando de un botón que corre una tarea sin congelar la ventana. Mientras corre, el propio botón queda
/// deshabilitado; <paramref name="puede"/> decide el resto (por ejemplo, que no haya otra operación en curso).
/// </summary>
public sealed class ComandoAsync(Func<object?, Task> accion, Func<object?, bool>? puede = null) : ICommand
{
    private bool _corriendo;

    public ComandoAsync(Func<Task> accion, Func<bool>? puede = null)
        : this(_ => accion(), puede is null ? null : _ => puede())
    {
    }

    /// <summary>WPF vuelve a preguntar CanExecute ante cada acción del usuario o con <see cref="Refrescar"/>.</summary>
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parametro) => !_corriendo && (puede?.Invoke(parametro) ?? true);

    public async void Execute(object? parametro) => await EjecutarAsync(parametro);

    public async Task EjecutarAsync(object? parametro = null)
    {
        if (!CanExecute(parametro)) return;
        _corriendo = true;
        Refrescar();
        try
        {
            await accion(parametro);
        }
        finally
        {
            _corriendo = false;
            Refrescar();
        }
    }

    public static void Refrescar() => CommandManager.InvalidateRequerySuggested();
}
