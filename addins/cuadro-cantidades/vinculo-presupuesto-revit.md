# Vínculo Presupuesto ↔ Modelo de Revit

Permite ligar **un presupuesto completo** con **un modelo/proyecto de Revit**
desde la addin. Cada modelo de Revit se identifica con un `revit_model_id`
(string que genera la addin, p. ej. el `UniqueId` del documento o un GUID propio).

---

## Nueva columna

Tabla **`presupuestos`**:

| Columna          | Tipo           | Nulo | Notas                                                        |
|------------------|----------------|------|--------------------------------------------------------------|
| `revit_model_id` | `VARCHAR(255)` | Sí   | Identificador del modelo de Revit vinculado. **Único** cuando tiene valor (índice `IX_presupuestos_revit_model_id`). |

- Un mismo `revit_model_id` **no puede repetirse** entre presupuestos: un modelo
  de Revit se liga a un solo presupuesto.
- Se permiten múltiples filas con `revit_model_id = NULL` (presupuestos sin
  vincular).

---

## Endpoints

Base: `/api/presupuestos`

### 1. Buscar el presupuesto vinculado a un modelo

La addin lo usa al abrir un modelo para saber si ya tiene presupuesto.

```
GET /api/presupuestos/by-revit-model/{revitModelId}
```

**200 OK** — devuelve el presupuesto:
```json
{
  "codigoPresupuesto": "PROY_004",
  "nombre": "Bodega Industrial Zona Norte",
  "descripcion": "Descripción del proyecto...",
  "fecha": "2026-06-10",
  "revitModelId": "a1b2c3d4-0000-1111-2222-333344445555"
}
```

**404 Not Found** — ningún presupuesto está vinculado a ese modelo:
```json
{ "message": "Ningún presupuesto está vinculado al modelo \"a1b2c3d4-...\"." }
```

---

### 2. Vincular un presupuesto a un modelo

```
PUT /api/presupuestos/{codigoPresupuesto}/revit-model
Content-Type: application/json
```

Body (`revitModelId` es obligatorio):
```json
{ "revitModelId": "a1b2c3d4-0000-1111-2222-333344445555" }
```

**200 OK** — devuelve el presupuesto actualizado (mismo shape que arriba).

**400 Bad Request** — `revitModelId` ausente o vacío.

**404 Not Found** — el `codigoPresupuesto` no existe.

**409 Conflict** — ese modelo ya está ligado a otro presupuesto:
```json
{ "message": "El modelo \"a1b2c3d4-...\" ya está vinculado a otro presupuesto." }
```

---

### 3. Desvincular un presupuesto de su modelo

```
DELETE /api/presupuestos/{codigoPresupuesto}/revit-model
```

**204 No Content** — desvinculado correctamente.

**404 Not Found** — el `codigoPresupuesto` no existe.

---

## Notas de comportamiento

- El `PUT /api/presupuestos/{id}` general (editar nombre/descripción/fecha)
  **no toca** `revit_model_id`. El vínculo se gestiona **solo** por
  `PUT {id}/revit-model`. Así, editar el presupuesto no rompe la liga.
- Al **duplicar** un presupuesto, la copia nace **sin** `revit_model_id`
  (no se hereda, porque debe ser único).
- El campo viaja como `revitModelId` (camelCase) en el JSON de la API.

---

## Flujo típico en la addin

1. Usuario abre un modelo en Revit → la addin calcula su `revit_model_id`.
2. `GET /api/presupuestos/by-revit-model/{id}`:
   - **200** → ya hay presupuesto, se carga.
   - **404** → no hay; se ofrece crear/elegir uno y luego
     `PUT /api/presupuestos/{codigo}/revit-model` con el `revitModelId`.
3. Para mover la liga a otro presupuesto: `DELETE {actual}/revit-model` y luego
   `PUT {nuevo}/revit-model` con el `revitModelId`.
