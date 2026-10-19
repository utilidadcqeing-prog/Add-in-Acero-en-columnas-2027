using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using CQEING_CuadroCantidades.Core.Domain.Entities;
using CQEING_CuadroCantidades.Core.Application.Services;
using CQEING_CuadroCantidades.Services;

namespace CQEING_CuadroCantidades.UI.Utils
{
    public static class DynamicTableBuilder
    {
        /// <param name="elements">Elementos vinculados a la actividad.</param>
        /// <param name="unit">Unidad de la actividad: las columnas de caras solo aplican en M2.</param>
        public static DataTable BuildDynamicTable(IEnumerable<LinkedElement> elements, string? unit)
        {
            var table = new DataTable();
            if (!elements.Any()) return table;

            table.Columns.Add("RevitUniqueId", typeof(string));

            string primaryCategory = elements.FirstOrDefault()?.Category?.ToLowerInvariant() ?? "";
            
            bool isColumnBeam = primaryCategory.Contains("column") || primaryCategory.Contains("pilar") || primaryCategory.Contains("viga") || primaryCategory.Contains("framing") || primaryCategory.Contains("armazón");
            bool isWall = primaryCategory.Contains("muro") || primaryCategory.Contains("wall");
            bool isFooting = primaryCategory.Contains("zapata") || primaryCategory.Contains("foundation") || primaryCategory.Contains("cimentación");
            bool isFloor = primaryCategory.Contains("suelo") || primaryCategory.Contains("floor") || primaryCategory.Contains("slab") || primaryCategory.Contains("losa");
            bool isStair = primaryCategory.Contains("escalera") || primaryCategory.Contains("stair");
            // Refuerzo de área (parrilla o malla de losa): se mide por el área de su contorno, no por peso
            bool isAreaReinforcement = primaryCategory.Contains("refuerzo de área") || primaryCategory.Contains("armadura de área")
                || primaryCategory.Contains("area reinforcement");
            bool isRebar = !isAreaReinforcement
                && (primaryCategory.Contains("acero") || primaryCategory.Contains("rebar") || primaryCategory.Contains("armadura"));
            bool isDoorWindow = primaryCategory.Contains("ventana") || primaryCategory.Contains("window")
                || primaryCategory.Contains("puerta") || primaryCategory.Contains("door");

            var parsedElements = elements
                .Select(el => (
                    Element: el,
                    Params: QuantificationEngine.DeserializeElementData(el.ElementDataJson),
                    Faces: isColumnBeam || isFooting ? FaceAreaCalculator.Read(el.ElementDataJson, el.Category) : null
                ))
                .ToList();

            // Perfiles metálicos: vigas o columnas cuyo JSON trae peso lineal (lo escribe SteelProfileQuantityExtractor).
            // La tabla de vigas y columnas se conserva igual y solo se le agregan KG/M y PESO al final.
            bool hasSteelProfiles = isColumnBeam && parsedElements.Any(p => HasLinearWeight(p.Params));

            // Pilares: el JSON trae el área lateral (lo escribe ColumnLateralAreaExtractor) y se agrega ÁREA LATERAL al final.
            bool hasLateralArea = isColumnBeam && parsedElements.Any(p => p.Params.ContainsKey(QuantificationEngine.LateralAreaKey));

            // Columnas, vigas y zapatas en M2 medidas por caras (FaceAreaExtractor y la selección de caras): CARAS y ÁREA CARAS al final.
            bool hasFaces = QuantificationEngine.IsSquareMeters(unit) && parsedElements.Any(p => p.Faces?.HasData == true);

            bool isCustomTable = isColumnBeam || isWall || isFooting || isFloor || isStair || isRebar || isDoorWindow || isAreaReinforcement;

            if (isCustomTable)
            {
                table.Columns.Add("No.", typeof(string));

                // Puertas y ventanas: el código y la descripción del tipo reemplazan a TIPO, como en el cuadro de la arquitecta
                if (isDoorWindow)
                {
                    table.Columns.Add("CÓDIGO", typeof(string));
                    table.Columns.Add("DESCRIPCIÓN", typeof(string));
                }
                else
                {
                    table.Columns.Add("TIPO", typeof(string));
                }

                if (isRebar)
                {
                    table.Columns.Add("ANFITRIÓN", typeof(string));
                }
                else
                {
                    table.Columns.Add("CANTIDAD", typeof(double));
                }

                table.Columns.Add("NIVEL", typeof(string));

                if (isColumnBeam)
                {
                    table.Columns.Add("BASE B", typeof(double));
                    table.Columns.Add("ALTURA H", typeof(double));
                    table.Columns.Add("LONGITUD", typeof(double));
                    table.Columns.Add("VOLUMEN", typeof(double));

                    if (hasSteelProfiles)
                    {
                        table.Columns.Add("KG/M", typeof(double));
                        table.Columns.Add("PESO", typeof(double));
                    }

                    if (hasLateralArea)
                    {
                        table.Columns.Add("ÁREA LATERAL", typeof(double));
                    }

                    if (hasFaces)
                    {
                        table.Columns.Add(FaceAreaCalculator.SelectionColumn, typeof(string));
                        table.Columns.Add(FaceAreaCalculator.AreaColumn, typeof(double));
                    }
                }
                else if (isWall)
                {
                    table.Columns.Add("LONGITUD", typeof(double));
                    table.Columns.Add("ÁREA", typeof(double));
                }
                else if (isFooting)
                {
                    table.Columns.Add("LARGO", typeof(double));
                    table.Columns.Add("ANCHO", typeof(double));
                    table.Columns.Add("GROSOR", typeof(double));
                    table.Columns.Add("VOLUMEN", typeof(double));

                    if (hasFaces)
                    {
                        table.Columns.Add(FaceAreaCalculator.SelectionColumn, typeof(string));
                        table.Columns.Add(FaceAreaCalculator.AreaColumn, typeof(double));
                    }
                }
                else if (isFloor)
                {
                    table.Columns.Add("ÁREA", typeof(double));
                    table.Columns.Add("VOLUMEN", typeof(double));
                }
                else if (isStair)
                {
                    table.Columns.Add("VOLUMEN", typeof(double));
                }
                else if (isRebar)
                {
                    table.Columns.Add("LONGITUD", typeof(double));
                    table.Columns.Add("KG/M", typeof(double));
                    table.Columns.Add("PESO", typeof(double));
                }
                else if (isDoorWindow)
                {
                    table.Columns.Add("LUGAR", typeof(string));
                    table.Columns.Add("MEDIDAS", typeof(string));
                    table.Columns.Add("MATERIAL", typeof(string));
                    table.Columns.Add(DoorWindowManualFields.LeavesColumn, typeof(string));
                    table.Columns.Add(DoorWindowManualFields.OpeningColumn, typeof(string));
                    table.Columns.Add("ÁREA", typeof(double));
                }
                else if (isAreaReinforcement)
                {
                    table.Columns.Add("ÁREA", typeof(double));
                    table.Columns.Add("PESO", typeof(double));
                }
            }
            else
            {
                table.Columns.Add("Categoría", typeof(string));
                table.Columns.Add("Nombre Elemento", typeof(string));
                table.Columns.Add("NIVEL", typeof(string));
            }

            var allKeys = new HashSet<string>(parsedElements.SelectMany(p => p.Params.Keys));

            if (!isCustomTable)
            {
                foreach (var key in allKeys.OrderBy(k => k))
                {
                    table.Columns.Add(key, typeof(double));
                }
            }

            int index = 1;
            double sumCantidad = 0;
            double sumVolumen = 0;
            double sumArea = 0;
            double sumLongitud = 0;
            double sumKgM = 0;
            double sumPeso = 0;
            double sumLateralArea = 0;
            double sumFaceArea = 0;

            foreach (var item in parsedElements)
            {
                var row = table.NewRow();
                row["RevitUniqueId"] = item.Element.RevitUniqueId;

                string levelValue = ElementDataJsonReader.GetString(item.Element.ElementDataJson, "Nivel", "Level") ?? "Sin Nivel";
                string hostValue = ElementDataJsonReader.GetString(item.Element.ElementDataJson, "Anfitrión") ?? "";

                if (isCustomTable)
                {
                    row["No."] = (index++).ToString();

                    if (isDoorWindow)
                    {
                        string json = item.Element.ElementDataJson;
                        row["CÓDIGO"] = ElementDataJsonReader.GetString(json, DoorWindowDataExtractor.CodeKey) ?? "";
                        row["DESCRIPCIÓN"] = ElementDataJsonReader.GetString(json, DoorWindowDataExtractor.DescriptionKey) ?? item.Element.ElementName;
                        row["LUGAR"] = ElementDataJsonReader.GetString(json, DoorWindowDataExtractor.PlaceKey) ?? "";
                        row["MEDIDAS"] = ElementDataJsonReader.GetString(json, DoorWindowDataExtractor.SizeKey) ?? "";
                        row["MATERIAL"] = ElementDataJsonReader.GetString(json, DoorWindowDataExtractor.MaterialKey) ?? "";
                        row[DoorWindowManualFields.LeavesColumn] = ElementDataJsonReader.GetString(json, DoorWindowManualFields.LeavesKey) ?? "";
                        row[DoorWindowManualFields.OpeningColumn] = ElementDataJsonReader.GetString(json, DoorWindowManualFields.OpeningKey) ?? "";

                        double area = GetParamValue(item.Params, "Area", "Área");
                        row["ÁREA"] = area;
                        sumArea += area;
                    }
                    else
                    {
                        row["TIPO"] = item.Element.ElementName;
                    }

                    if (isRebar)
                    {
                        row["ANFITRIÓN"] = hostValue; 
                    }
                    else
                    {
                        row["CANTIDAD"] = 1.0;
                        sumCantidad += 1.0;
                    }

                    row["NIVEL"] = levelValue;

                    if (isColumnBeam)
                    {
                        row["BASE B"] = GetParamValue(item.Params, "Base", "b", "Width");
                        row["ALTURA H"] = GetParamValue(item.Params, "Altura", "h", "Height", "Depth");
                        double len = GetParamValue(item.Params, "Longitud", "Length");
                        row["LONGITUD"] = len;
                        sumLongitud += len;
                        
                        double vol = GetParamValue(item.Params, "Volume", "Volumen");
                        row["VOLUMEN"] = vol;
                        sumVolumen += vol;

                        if (hasSteelProfiles)
                        {
                            row["KG/M"] = GetParamValue(item.Params, "kg/m", "Mass per unit length");

                            double peso = GetParamValue(item.Params, "Peso", "Weight");
                            row["PESO"] = peso;
                            sumPeso += peso;
                        }

                        if (hasLateralArea)
                        {
                            double lateralArea = GetParamValue(item.Params, QuantificationEngine.LateralAreaKey);
                            row["ÁREA LATERAL"] = lateralArea;
                            sumLateralArea += lateralArea;
                        }

                        if (hasFaces && item.Faces is { } faces)
                        {
                            sumFaceArea += SetFaceCells(row, faces);
                        }
                    }
                    else if (isWall)
                    {
                        double len = GetParamValue(item.Params, "Longitud", "Length");
                        row["LONGITUD"] = len;
                        sumLongitud += len;
                        
                        double area = GetParamValue(item.Params, "Area", "Área");
                        row["ÁREA"] = area;
                        sumArea += area;
                    }
                    else if (isFooting)
                    {
                        row["LARGO"] = GetParamValue(item.Params, "Largo", "Length");
                        row["ANCHO"] = GetParamValue(item.Params, "Ancho", "Width");
                        row["GROSOR"] = GetParamValue(item.Params, "Grosor", "Thickness", "Foundation Thickness");
                        
                        double vol = GetParamValue(item.Params, "Volume", "Volumen");
                        row["VOLUMEN"] = vol;
                        sumVolumen += vol;

                        if (hasFaces && item.Faces is { } faces)
                        {
                            sumFaceArea += SetFaceCells(row, faces);
                        }
                    }
                    else if (isFloor)
                    {
                        double area = GetParamValue(item.Params, "Area", "Área");
                        row["ÁREA"] = area;
                        sumArea += area;
                        
                        double vol = GetParamValue(item.Params, "Volume", "Volumen");
                        row["VOLUMEN"] = vol;
                        sumVolumen += vol;
                    }
                    else if (isStair)
                    {
                        double vol = GetParamValue(item.Params, "Volume", "Volumen");
                        row["VOLUMEN"] = vol;
                        sumVolumen += vol;
                    }
                    else if (isRebar)
                    {
                        double len = GetParamValue(item.Params, "Longitud", "Length");
                        row["LONGITUD"] = len;
                        sumLongitud += len;

                        double kgm = GetParamValue(item.Params, "kg/m", "Mass per unit length");
                        row["KG/M"] = kgm;
                        sumKgM += kgm;

                        double peso = GetParamValue(item.Params, "Peso", "Weight");
                        row["PESO"] = peso;
                        sumPeso += peso;
                    }
                    else if (isAreaReinforcement)
                    {
                        double area = GetParamValue(item.Params, "Area", "Área");
                        row["ÁREA"] = area;
                        sumArea += area;

                        // Peso de las varillas que genera el refuerzo, para actividades en KG
                        double peso = GetParamValue(item.Params, "Peso", "Weight");
                        row["PESO"] = peso;
                        sumPeso += peso;
                    }
                }
                else
                {
                    row["Categoría"] = item.Element.Category;
                    row["Nombre Elemento"] = item.Element.ElementName;
                    row["NIVEL"] = levelValue;

                    foreach (var key in allKeys)
                    {
                        row[key] = item.Params.TryGetValue(key, out double val) ? val : 0.0;
                    }
                }

                table.Rows.Add(row);
            }

            if (isCustomTable && parsedElements.Count > 0)
            {
                var totalRow = table.NewRow();
                totalRow["No."] = "TOTAL";

                if (table.Columns.Contains("CANTIDAD"))
                {
                    totalRow["CANTIDAD"] = sumCantidad;
                }

                if (table.Columns.Contains("VOLUMEN"))
                {
                    totalRow["VOLUMEN"] = Math.Round(sumVolumen, 2);
                }

                if (table.Columns.Contains("ÁREA"))
                {
                    totalRow["ÁREA"] = Math.Round(sumArea, 2);
                }

                if (table.Columns.Contains("LONGITUD"))
                {
                    totalRow["LONGITUD"] = Math.Round(sumLongitud, 2);
                }

                // En perfiles la suma de pesos lineales no tiene significado: la celda queda vacía
                if (table.Columns.Contains("KG/M") && !hasSteelProfiles)
                {
                    totalRow["KG/M"] = sumKgM;
                }

                if (table.Columns.Contains("PESO"))
                {
                    totalRow["PESO"] = Math.Round(sumPeso, 2);
                }

                if (table.Columns.Contains("ÁREA LATERAL"))
                {
                    totalRow["ÁREA LATERAL"] = Math.Round(sumLateralArea, 2);
                }

                if (table.Columns.Contains(FaceAreaCalculator.AreaColumn))
                {
                    totalRow[FaceAreaCalculator.AreaColumn] = FaceAreaCalculator.RoundArea(sumFaceArea);
                }

                table.Rows.Add(totalRow);
            }

            return table;
        }

        /// <summary>
        /// Escribe en la fila las caras que se miden y su área. Devuelve el área para sumarla al total.
        /// Un elemento aún sin datos de caras (se mide por su área lateral) deja las celdas vacías.
        /// </summary>
        public static double SetFaceCells(DataRow row, FaceAreaData faces)
        {
            if (!faces.HasData)
            {
                row[FaceAreaCalculator.SelectionColumn] = DBNull.Value;
                row[FaceAreaCalculator.AreaColumn] = DBNull.Value;
                return 0;
            }

            // SelectedArea ya viene redondeada: el TOTAL suma los mismos valores que muestran las filas
            double area = faces.SelectedArea ?? 0;
            row[FaceAreaCalculator.SelectionColumn] = FaceAreaCalculator.Format(faces.MeasuredFaces);
            row[FaceAreaCalculator.AreaColumn] = area;
            return area;
        }

        private static bool HasLinearWeight(Dictionary<string, double> dict) =>
            GetParamValue(dict, "kg/m", "Mass per unit length") > 0;

        private static double GetParamValue(Dictionary<string, double> dict, params string[] keys)
        {
            foreach (var key in keys)
            {
                // La llave exacta del add-in ("Peso") va antes que la columna que devuelve la web ("PESO")
                var foundKey = dict.ContainsKey(key)
                    ? key
                    : dict.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                if (foundKey != null)
                {
                    if (key.Equals("kg/m", StringComparison.OrdinalIgnoreCase) || 
                        key.Equals("Mass per unit length", StringComparison.OrdinalIgnoreCase))
                    {
                        return dict[foundKey]; // Conservar precisión exacta para KG/M
                    }
                    return Math.Round(dict[foundKey], 2);
                }
            }
            return 0.0;
        }
    }
}
