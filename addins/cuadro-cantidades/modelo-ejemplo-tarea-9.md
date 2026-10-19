# Modelo de ejemplo para la tarea 9

Tarea 9: ejemplos en Revit y guía para el modelador. Carlos lo pidió en la reunión del 08/10/2026 (31:42-32:58): un modelo de ejemplo, empezando por **una escalera con baranda**, para ver qué saca el add-in de cada caso y escribir una guía para el modelador. La lista sale de la reunión y de las tareas 10 a 14.

## Base del modelo

- **2 niveles** (Piso 1 y Piso 2), para comprobar que la ubicación por piso sale bien.
- **Muros exteriores e interiores** formando 3 o 4 espacios, y una **losa** en el Piso 2 con un borde abierto, tipo balcón.
- **Habitaciones** (Rooms) con nombre en cada espacio: "Sala", "Alcoba 1", "Cocina", "Baño". Son necesarias para la ubicación dentro del piso y para la apertura de puertas (tareas 11 y 12).
- Opcional: un **plano de áreas** con las mismas zonas, para comparar Habitaciones con Áreas, que fue lo que sugirió Carlos.

## Objetos a probar

| Objeto | Qué variantes poner | Qué comprobar |
|---|---|---|
| **Escalera con baranda** (caso que pidió Carlos) | 1 escalera entre Piso 1 y Piso 2, con la baranda automática | Volumen de la escalera; si la baranda sale como elemento aparte y con su longitud |
| **Barandas** | 2 tipos: una en el balcón y otra suelta en un borde de losa | Metros lineales, tipo, piso y material (tarea 13) |
| **Pasamanos** | 1, en la escalera o en un muro | Si sale separado de la baranda y con longitud |
| **Puertas** | Al menos 4 tipos: batiente sencilla de madera, **doble** (hojas), **corrediza**, metálica exterior. Una que abra hacia adentro y otra hacia afuera | Cantidad por tipo, Marca de tipo, ancho × alto, material, habitación de cada lado y sentido de apertura (tarea 12) |
| **Ventanas** | 2 o 3 tipos de distinto tamaño (1,00×1,00, 1,20×1,00…) | Lo mismo que las puertas; cantidad por tipo |
| **Vidrio / muro cortina** | 1 muro cortina pequeño | m² de vidrio, paneles y montantes |
| **Cubierta (techo)** | 1 cubierta sobre el Piso 2 | Área y volumen (tarea 14) |
| **Cielorraso** | 1 en una habitación | Área |
| **Rampa** | 1 rampa corta | Qué datos entrega |
| **Celosía** | 1, como modelo genérico o muro cortina con lamas | Qué datos entrega (tarea 10) |
| **Modelo genérico** | 1 de área y 1 de volumen | Qué datos entrega (tarea 10) |
| **División de acero inoxidable** | 1 tabique o panel | m² |
| **Perfiles metálicos** | Un **HEA 300 y un IPE 300**, ejemplo de Carlos, con "Peso lineal metálico" cargado, más una viga de concreto | Que se distingan por kg/m (tareas 1, 2 y 15) |

## Parámetros a llenar en las familias

Si no se llenan, salen vacíos y no se puede saber si el add-in los lee:

- **Marca de tipo** en puertas y ventanas (P1, P2, V1…) y **Descripción** del tipo.
- **Materiales con su clase**: madera en unas puertas, metal en otras y vidrio en ventanas. Con eso se prueba la separación carpintería metálica / madera.
- Parámetros compartidos **"Hojas"** y **"Observaciones"**. No usar "Comentarios": el add-in escribe ahí el código de la actividad.
- **"Peso lineal metálico"** en los perfiles.

## Qué hacer con el modelo

Para cada objeto:

1. Comprobar si aparece en la lista de elementos del add-in.
2. Asignarlo a una actividad.
3. Anotar qué tabla sale, qué datos vienen bien, cuáles vienen vacíos o mal, y por qué.

Eso se convierte en la guía para el modelador que pidió Carlos.

## Mínimo para la sesión con Carlos (09/10)

Si no hay tiempo de modelar todo: **la escalera con baranda, 2 tipos de puerta, 2 tipos de ventana, 2 habitaciones y los dos perfiles de acero** (HEA 300 e IPE 300).

Ver también `investigacion-puertas-ventanas.md` para qué datos de puertas y ventanas se pueden traer de forma automática.
