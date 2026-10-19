# Preguntas para el equipo: cómo se miden las cantidades

Para que el add-in calcule bien las cantidades necesitamos que respondan estas preguntas. En la mayoría basta con elegir una letra.

Las secciones 1 y 2 son las más urgentes: sin ellas no se puede calcular la impermeabilización de vigas ni de zapatas.

Lo marcado como **"Ya implementado sin respuesta"** ya funciona en el add-in con una suposición nuestra. Solo confirmen si es correcto o digan cómo debe ser.

---

## 1. Impermeabilización de zapatas (M2)

```
              │col│
        ┌─────┴───┴─────┐  ← cara superior
costado │               │ costado
        └───────────────┘  ← fondo
```

**1.1** ¿Qué caras se impermeabilizan?

- **A.** Solo una cara en planta (largo × ancho)
- **B.** Cara superior + costados
- **C.** Fondo + costados
- **D.** Todas las caras

Respuesta: ____

**1.2** Si se incluye la cara superior, ¿se descuenta el área donde nace la columna? (Sí / No)

Respuesta: ____

---

## 2. Impermeabilización de vigas (M2)

```
        ┌─────────────┐  ← cara superior
costado │             │ costado
        └─────────────┘  ← fondo
```

**2.1** ¿Qué caras se impermeabilizan?

- **A.** Fondo + 2 costados
- **B.** Cara superior + 2 costados
- **C.** Las 4 caras

Respuesta: ____

**2.2** Si la viga tiene losa encima, ¿el costado se mide completo o solo la parte que queda a la vista bajo la losa?

Respuesta: ____

---

## 3. Pañete / pintura / estuco de columnas (M2)

```
   ═══════════════  ← losa
   ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓  ← viga
       │     │
       │     │  ← columna
       │     │
   ════╧═════╧════  ← piso
```

> **Ya implementado sin respuesta:** hoy el add-in mide las 4 caras de la columna de piso a piso y cuenta también las caras pegadas a muros.
>
> ¿Se deja así? (Sí / No) ____ · Corrección del equipo: _______________

**3.1** ¿La altura se mide **de piso a piso** o **hasta el fondo de la viga**?

Respuesta: ____

**3.2** Si una cara de la columna queda pegada a un muro, ¿se descuenta? (Sí / No)

Respuesta: ____

---

## 4. Acero de refuerzo de pisos (M2)

El acero de las losas está modelado como **refuerzo de área**: una zona de la losa cubierta con varillas en las dos direcciones.

```
   ┌───────────────────────────┐
   │ ┼ ┼ ┼ ┼ ┼ ┼ ┼ ┼ ┼ ┼ ┼ ┼ ┼ │
   │ ┼ ┼ ┼ ┌───────┐ ┼ ┼ ┼ ┼ │ ← zona con refuerzo
   │ ┼ ┼ ┼ │ vano  │ ┼ ┼ ┼ ┼ │
   │ ┼ ┼ ┼ └───────┘ ┼ ┼ ┼ ┼ │   (vano = escalera, ducto, etc.)
   │ ┼ ┼ ┼ ┼ ┼ ┼ ┼ ┼ ┼ ┼ ┼ ┼ ┼ │
   └───────────────────────────┘
```

> **Ya implementado sin respuesta:** hoy el add-in usa la opción **B**. Mide la losa en planta hasta la cara exterior de vigas y columnas, sin descontar columnas, y descuenta todos los vanos, sin importar su tamaño. Las vigas y columnas que tocan la zona con refuerzo se suman completas, aunque el contorno dibujado en Revit quede unos centímetros corto.
>
> ¿Se deja así? (Sí / No) ____ · Corrección del equipo: _______________

**4.1** ¿Qué área se debe enviar?

- **A.** Toda la zona con refuerzo, sin descontar vanos
- **B.** La zona con refuerzo, descontando los vanos de la losa
- **C.** Solo se descuentan los vanos grandes (más de ___ m²)

Respuesta: ____

**4.1.1** Las vigas que bordean un vano (por ejemplo, las del hueco de la escalera), ¿suman al área como el resto de vigas o se descuentan con el vano? Hoy suman.

Respuesta: ____

**4.2** ¿Esta actividad es **malla electrosoldada** o **parrilla de varillas**? Si es malla, ¿se modela como malla en Revit o como refuerzo de área?

Respuesta: ____

---
