# Endpoints para Add-in Revit

Documentación de los endpoints HTTP que consume el add-in de Revit.

- **Base URL local:** `http://localhost:5187`
- **Auth:** API Key en header `X-Api-Key` (ver sección abajo)
- **Formato:** JSON con `snake_case` en peticiones y respuestas

---

## Autenticación

Todos los endpoints `/api/*` requieren el header:

```
X-Api-Key: <clave entregada por el equipo backend>
```

Si la clave no se envía o es incorrecta, el servidor responde:

```
HTTP 401 Unauthorized
{ "message": "API Key inválida o ausente." }
```

**Ejemplo (.NET HttpClient):**

```csharp
httpClient.DefaultRequestHeaders.Add("X-Api-Key", "TU_KEY_AQUI");
```

**Ejemplo (curl):**

```bash
curl -H "X-Api-Key: TU_KEY_AQUI" http://localhost:5187/api/presupuestos/PROY_001/estructura
```

---

## 1. Listar presupuestos

Devuelve la lista de presupuestos disponibles. Opcionalmente se puede filtrar por la ruta del documento de Revit.

### Petición

```
GET /api/presupuestos?revit_path={ruta_documento}
```

| Parámetro | Tipo | Descripción |
|-----------|------|-------------|
| `revit_path` | string (opcional) | Ruta del documento de Revit activo para filtrar presupuestos |

### Respuesta `200 OK`

```json
[
  {
    "id": 1,
    "codigo_presupuesto": "PROY_001",
    "nombre": "Presupuesto Estructura Edificio",
    "fecha_creacion": "2026-05-28T14:30:00Z"
  }
]
```

---

## 2. Estructura del presupuesto

Devuelve el árbol jerárquico de un presupuesto (capítulos → subcapítulos → ítems/actividades) para que el add-in muestre el selector al usuario.

### Petición

```
GET /api/presupuestos/{codigo_presupuesto}/estructura
```

| Parámetro | Tipo | Descripción |
|-----------|------|-------------|
| `codigo_presupuesto` | string | Código del presupuesto (ej. `PROY_001`) |

### Respuesta `200 OK`

```json
{
  "codigo_presupuesto": "PROY_001",
  "nombre": "Presupuesto Estructura Edificio",
  "capitulos": [
    {
      "codigo_capitulo": "PROY_001_C001",
      "nombre": "ESTRUCTURA",
      "subcapitulos": [
        {
          "codigo_subcapitulo": "PROY_001_C001_S001",
          "nombre": "ESTRUCTURA CONCRETO",
          "items": [
            {
              "id_item": 1,
              "codigo_actividad": "ACT_001",
              "descripcion": "Concreto de zapatas de cimentación",
              "unidad": "M3",
              "cantidad_obra": 12.50
            }
          ]
        }
      ]
    }
  ]
}
```

### Errores

| Código | Causa |
|--------|-------|
| `404 Not Found` | El `codigo_presupuesto` no existe |

### Ejemplo curl

```bash
curl http://localhost:5187/api/presupuestos/PROY_001/estructura
```

---

## 3. Guardar elementos vinculados a una actividad

Sobreescribe los elementos de Revit vinculados a una actividad. Borra los anteriores y guarda los nuevos en una sola transacción.

### Petición

```
POST /api/presupuestos/items/{id_item}/elementos
Content-Type: application/json
```

```json
[
  {
    "revit_unique_id": "8b51d8e1-d36c-487c-bd5f-682136a6cfb1-00049281",
    "categoria": "Structural Foundations",
    "nombre_elemento": "Zapata Rectangular: 1.20 x 1.20 x 0.40",
    "datos_json": "{\"Volumen\": 0.576, \"Area\": 1.44 \"Cantidad\": 9.24 }",
    "url_imagen": ["https://res.cloudinary.com/cqing/.../abc.jpg", "https://res.cloudinary.com/cqing/.../abc.jpg"],
    "eliminado_en_revit": false
  }
]
```

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `revit_unique_id` | string | UniqueId del elemento dentro del modelo de Revit |
| `categoria` | string | Categoría de Revit (ej. `Structural Foundations`) |
| `nombre_elemento` | string | Nombre legible del elemento |
| `datos_json` | string | JSON libre con metadatos (volumen, área, etc.) |
| `url_imagen` | string \| null | URL pública de la imagen del elemento (Cloudinary u otro) |
| `eliminado_en_revit` | bool | Marca si el usuario eliminó el elemento en Revit |

### Respuesta `201 Created`

```json
{
  "id_item": 1,
  "total_guardados": 1,
  "elementos": [
    {
      "id": "e81d7ad7-1bcf-46d5-a8ad-15de5b2938cf",
      "id_item": 1,
      "revit_unique_id": "8b51d8e1-...",
      "categoria": "Structural Foundations",
      "nombre_elemento": "Zapata Rectangular: 1.20 x 1.20 x 0.40",
      "datos_json": "{\"Volumen\": 0.576, \"Area\": 1.44}",
      "url_imagen": "https://res.cloudinary.com/cqing/.../abc.jpg",
      "eliminado_en_revit": false,
      "fecha_actualizacion": "2026-05-28T14:30:00Z"
    }
  ]
}
```

### Errores

| Código | Causa |
|--------|-------|
| `400 Bad Request` | Body vacío o mal formado |
| `404 Not Found` | El `id_item` no existe |

---

## 4. Leer elementos vinculados a una actividad

Devuelve los elementos de Revit guardados para un `id_item` específico, ordenados del más reciente al más antiguo.

### Petición

```
GET /api/presupuestos/items/{id_item}/elementos
```

### Respuesta `200 OK`

```json
[
  {
    "id": "e81d7ad7-1bcf-46d5-a8ad-15de5b2938cf",
    "id_item": 1,
    "revit_unique_id": "8b51d8e1-...",
    "categoria": "Structural Foundations",
    "nombre_elemento": "Zapata Rectangular: 1.20 x 1.20 x 0.40",
    "datos_json": "{\"Volumen\": 0.576, \"Area\": 1.44}",
    "url_imagen": "https://res.cloudinary.com/cqing/.../abc.jpg",
    "eliminado_en_revit": false,
    "fecha_actualizacion": "2026-05-28T14:30:00Z"
  }
]
```

### Errores

| Código | Causa |
|--------|-------|
| `404 Not Found` | El `id_item` no existe |

---

## Manejo de imágenes

Las imágenes de Revit **no se guardan en la base de datos**, solo su URL pública.

**Flujo recomendado:**

1. Add-in captura la imagen del elemento (screenshot de la vista).
2. Add-in sube la imagen a un servicio externo (recomendado: **Cloudinary**, plan gratis 25 GB).
3. El servicio devuelve una URL pública.
4. Add-in envía esa URL en el campo `url_imagen` del endpoint `POST .../elementos`.

Esto mantiene la BD ligera y rápida.

---

## Convenciones

- **Códigos HTTP** siguen estándar REST: `200 OK`, `201 Created`, `400 Bad Request`, `404 Not Found`, `409 Conflict`.
- **Errores** devuelven JSON con la forma:

  ```json
  { "message": "Mensaje legible", "reasons": ["razón 1", "razón 2"] }
  ```

- **Fechas** en formato ISO 8601 UTC.
- **Decimales** con punto, no coma.
