using System;
using System.Collections.Generic;

namespace CQEING_CuadroCantidades.UI.Utils
{
    /// <summary>
    /// Campos de puertas y ventanas que no salen de Revit y se llenan a mano en la tabla de la actividad.
    /// Se guardan en el ElementDataJson de cada puerta o ventana, junto a los datos extraídos de Revit.
    /// </summary>
    public static class DoorWindowManualFields
    {
        public const string LeavesKey = "Hojas";
        public const string OpeningKey = "Apertura";

        public const string LeavesColumn = "HOJAS";
        public const string OpeningColumn = "APERTURA";

        /// <summary>
        /// Opciones de la lista de apertura. Pendientes de confirmar con la arquitecta.
        /// La opción vacía permite borrar el valor.
        /// </summary>
        public static IReadOnlyList<string> OpeningOptions { get; } = new[]
        {
            "",
            "Hacia el interior",
            "Hacia el exterior",
            "Vaivén",
            "Corrediza",
            "Plegable",
            "Batiente",
            "Proyectante",
            "Fija",
        };

        /// <summary>
        /// Clave del JSON para una columna editable, o null si la columna no es editable.
        /// </summary>
        public static string? KeyForColumn(string? columnName) => columnName switch
        {
            LeavesColumn => LeavesKey,
            OpeningColumn => OpeningKey,
            _ => null,
        };

        public static bool IsEditableColumn(string? columnName) => KeyForColumn(columnName) != null;
    }
}
