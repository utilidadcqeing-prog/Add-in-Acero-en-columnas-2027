using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CQEING.Gestor;

/// <summary>Base de los modelos de la ventana: avisa a WPF cuando cambia una propiedad.</summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Cambiar<T>(ref T campo, T valor, [CallerMemberName] string? propiedad = null)
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor)) return false;
        campo = valor;
        Avisar(propiedad);
        return true;
    }

    /// <summary>Sin nombre (cadena vacía), WPF vuelve a leer todas las propiedades del objeto.</summary>
    protected void Avisar([CallerMemberName] string? propiedad = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
}
