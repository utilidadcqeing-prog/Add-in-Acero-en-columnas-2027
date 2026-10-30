// Los datos de ejemplo de la maqueta aprobada (#220). Se reemplazan por los del modelo en #221 y #226.
window.DATOS_EJEMPLO = {
  elementos: {
    viga: [
      { id: 'AB', n: 'V Cim A-B-C', sub: 'Mismo acero · 3 vigas · 0.50x0.90', aplica: 'V Cim A-B-C - (0.50x0.90)' },
      { id: '1', n: 'V Cim 1-2-3', sub: 'Mismo acero · 3 vigas · con traslapos', aplica: 'V Cim 1-2-3 - (0.50x0.60)' }
    ],
    columna: [
      { id: 'A2', n: 'Col A2-B2', sub: 'Mismo acero · 2 columnas', aplica: 'Col A2-B2 - (0.40x0.40)' },
      { id: 'A3', n: 'Col A3-B3', sub: 'Mismo acero · 2 columnas', aplica: 'Col A3-B3 - (0.40x0.40)' },
      { id: 'C1', n: 'Col C1', sub: 'Acero único', aplica: 'Col C1 - (0.40x0.40)' }
    ]
  },
  filas: {
    viga: [
      { id: 'r7s', c: '#9334E6', et: '7 #7 L= 11.52', d: 'Superior · recta 10.83' },
      { id: 'piel', c: '#EA4335', et: '5 #5 L= 10.83', d: 'Piel · 2 filas' },
      { id: 'r7i', c: '#9334E6', et: '7 #7 L= 11.52', d: 'Inferior · recta 10.83' },
      { id: 'e', c: '#4285F4', et: '95 #4 @0.10', d: 'Estribos' }
    ],
    viga1: [
      { id: 'r8', c: '#8B4513', et: '#8 · 2.65 + 5.50 + 2.65', d: 'Traslapos 1.80 y 1.70' },
      { id: 'bast', c: '#9334E6', et: '#7 · 1.15, 2.00, 0.95', d: 'Bastones' },
      { id: 'r7', c: '#9334E6', et: '#7 · 2.19 + 5.50 + 2.69', d: 'Traslapos 1.35 y 1.72' },
      { id: 'e', c: '#FBBC04', et: '20 #3', d: 'Estribos c/.10 y c/.20' }
    ],
    columna: [
      { id: 'c6', c: '#34A853', et: '#6 · 3.20 + 3.40', d: 'Externas · traslapo 1.20' },
      { id: 'c5', c: '#EA4335', et: '#5 · 2.80 + 3.20', d: 'Internas · traslapo 1.00' },
      { id: 'e', c: '#FBBC04', et: '24 #3', d: 'Estribos c/.10 y c/.20' }
    ]
  }
};
