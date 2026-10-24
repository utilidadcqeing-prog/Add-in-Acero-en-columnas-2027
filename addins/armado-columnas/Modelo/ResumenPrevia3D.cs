namespace practicacq.Modelo;

using System.Collections.Generic;

/// <summary>
/// Lo que la vista previa 3D (#123) le cuenta a la ventana al terminar: una
/// línea de estado y los números de varilla que hay, para armar las casillas
/// de diámetro con lo que de verdad se generaría.
/// </summary>
/// <param name="Numeros">Números de varilla presentes (-1 = diámetro fuera de la tabla), de menor a mayor.</param>
public sealed record ResumenPrevia3D(string Texto, IReadOnlyList<int> Numeros, bool Fallo = false);
