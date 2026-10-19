# Solicitud al equipo: unidades y tablas del add-in de cantidades

Un mismo elemento puede enviar **cantidades distintas según la unidad de la actividad**. Por ejemplo, una columna enlazada a una actividad en M3 debe enviar su volumen, pero si la actividad está en M2 (formaleta) debe enviar un área, y si está en ML, una longitud.

Por eso necesitamos que, **para cada elemento y para cada unidad posible**, nos digan:

- **Columnas:** qué columnas debe mostrar la tabla cuando la actividad está en esa unidad.
- **Cantidad enviada:** qué número exacto se envía al servicio en `cantidad_obra` y cómo se obtiene (dato de Revit o fórmula).

Si una unidad no aplica a un elemento, márquenla con **N/A**. Unidades: M3, M2, ML, KG, UND, GL. Si usan otra, agréguenla en la fila "Otra".

---

## 1. Elementos que ya maneja el add-in

### Ejemplo de cómo llenarla: Columnas

| Unidad | Columnas de la tabla | Cantidad enviada | ¿De dónde sale? |
|---|---|---|---|
| M3 | No., Tipo, Nivel, B, H, Longitud, Volumen | Suma de Volumen | Revit: volumen |
| M2 | No., Tipo, Nivel, B, H, Longitud, Área formaleta | Suma de Área formaleta | Fórmula: 2 × (B + H) × Longitud |
| ML | | | |
| KG | | | |
| UND | | | |
| GL | | | |

Usen esta misma tabla para cada elemento:

### Columnas

| Unidad | Columnas de la tabla | Cantidad enviada | ¿De dónde sale? |
|---|---|---|---|
| M3 | | | |
| M2 | | | |
| ML | | | |
| KG | | | |
| UND | | | |
| GL | | | |
| Otra: | | | |

### Vigas

| Unidad | Columnas de la tabla | Cantidad enviada | ¿De dónde sale? |
|---|---|---|---|
| M3 | | | |
| M2 | | | |
| ML | | | |
| KG | | | |
| UND | | | |
| GL | | | |
| Otra: | | | |

### Perfiles metálicos (vigas y columnas)

| Unidad | Columnas de la tabla | Cantidad enviada | ¿De dónde sale? |
|---|---|---|---|
| M3 | | | |
| M2 | | | |
| ML | | | |
| KG | | | |
| UND | | | |
| GL | | | |
| Otra: | | | |

### Muros

| Unidad | Columnas de la tabla | Cantidad enviada | ¿De dónde sale? |
|---|---|---|---|
| M3 | | | |
| M2 | | | |
| ML | | | |
| KG | | | |
| UND | | | |
| GL | | | |
| Otra: | | | |

### Zapatas / cimentación

| Unidad | Columnas de la tabla | Cantidad enviada | ¿De dónde sale? |
|---|---|---|---|
| M3 | | | |
| M2 | | | |
| ML | | | |
| KG | | | |
| UND | | | |
| GL | | | |
| Otra: | | | |

### Losas / pisos

| Unidad | Columnas de la tabla | Cantidad enviada | ¿De dónde sale? |
|---|---|---|---|
| M3 | | | |
| M2 | | | |
| ML | | | |
| KG | | | |
| UND | | | |
| GL | | | |
| Otra: | | | |

### Escaleras

| Unidad | Columnas de la tabla | Cantidad enviada | ¿De dónde sale? |
|---|---|---|---|
| M3 | | | |
| M2 | | | |
| ML | | | |
| KG | | | |
| UND | | | |
| GL | | | |
| Otra: | | | |

### Acero de refuerzo

| Unidad | Columnas de la tabla | Cantidad enviada | ¿De dónde sale? |
|---|---|---|---|
| M3 | | | |
| M2 | | | |
| ML | | | |
| KG | | | |
| UND | | | |
| GL | | | |
| Otra: | | | |

### Puertas

| Unidad | Columnas de la tabla | Cantidad enviada | ¿De dónde sale? |
|---|---|---|---|
| M3 | | | |
| M2 | | | |
| ML | | | |
| KG | | | |
| UND | | | |
| GL | | | |
| Otra: | | | |

### Ventanas

| Unidad | Columnas de la tabla | Cantidad enviada | ¿De dónde sale? |
|---|---|---|---|
| M3 | | | |
| M2 | | | |
| ML | | | |
| KG | | | |
| UND | | | |
| GL | | | |
| Otra: | | | |

---

## 2. Elementos que faltan

Hoy el add-in solo reconoce los elementos de la sección 1. Si necesitan cuantificar otros que **todavía no aparecen** (por ejemplo, cielos rasos, pisos de acabado, cubiertas, barandas, aparatos sanitarios, mobiliario, tuberías), copien este bloque por cada uno:

### Elemento: _______________ · Categoría en Revit (si la conocen): _______________

| Unidad | Columnas de la tabla | Cantidad enviada | ¿De dónde sale? |
|---|---|---|---|
| M3 | | | |
| M2 | | | |
| ML | | | |
| KG | | | |
| UND | | | |
| GL | | | |
| Otra: | | | |

Si un elemento sí aparece pero sus datos no salen bien (columnas incorrectas, cantidad equivocada, unidad que no corresponde), anótenlo también aquí: qué sale hoy y qué esperaban ver.

---

## 3. Preguntas generales

1. **Totales en la tabla.** Para cada elemento y unidad, ¿qué columnas se deben sumar para mostrar su total en la fila final de la tabla? Por ejemplo, en columnas M3: sumar Cantidad y Volumen, pero no B ni H.
2. **Formato de referencia.** ¿Tienen un cuadro de cantidades actual (Excel) que debamos imitar? Si es así, compártanlo.

---
