"""Compara dos capturas píxel a píxel (tablero de vigas, #83).

Uso: python tools/comparar-capturas.py referencia.png actual.png salida-diferencia.png

Escribe el % de píxeles distintos (un píxel es distinto si algún canal difiere en más de 16) y una imagen con la
referencia apagada y las diferencias en rojo. Sale con código 1 si las imágenes no miden lo mismo.
"""

import sys

from PIL import Image

TOLERANCIA = 16


def comparar(referencia: str, actual: str, salida: str) -> float:
    ref = Image.open(referencia).convert("RGB")
    act = Image.open(actual).convert("RGB")
    if ref.size != act.size:
        raise SystemExit(f"Tamaños distintos: {ref.size} y {act.size}")
    ancho, alto = ref.size
    pr, pa = ref.load(), act.load()
    fondo = Image.blend(ref, Image.new("RGB", ref.size, (255, 255, 255)), 0.75)
    pd = fondo.load()
    distintos = 0
    for y in range(alto):
        for x in range(ancho):
            a, b = pr[x, y], pa[x, y]
            if max(abs(a[0] - b[0]), abs(a[1] - b[1]), abs(a[2] - b[2])) > TOLERANCIA:
                distintos += 1
                pd[x, y] = (230, 0, 0)
    fondo.save(salida)
    return 100 * distintos / (ancho * alto)


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    porcentaje = comparar(*sys.argv[1:])
    print(f"{porcentaje:.3f} % de píxeles distintos")
