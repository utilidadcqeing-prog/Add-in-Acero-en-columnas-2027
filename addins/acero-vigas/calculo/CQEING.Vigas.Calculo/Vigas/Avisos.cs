using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Vigas;

/// <summary>Avisos sobre las capas en uso (decisión 9 del plan).</summary>
public static class Avisos
{
    /// <summary>Un traslapo que supera el mínimo en más de esto se avisa (regla 15 de #80; no está en Reglas).</summary>
    public const int ExcesoParaAviso = 300;

    /// <param name="otraCara">La opción no usada de la capa de la cara, para decir si la evita y cuánto ahorra.</param>
    /// <param name="varillaNormal">La varilla de las reglas; una barra más larga avisa que usa la larga.</param>
    public static IEnumerable<Aviso> De(SolucionCapa capa, SolucionCapa? otraCara, Cara caraDeLaOtra, int varillaNormal)
    {
        int ultimo = capa.Traslapos.Count - 1;
        for (int i = 0; i <= ultimo; i++)
        {
            Traslapo t = capa.Traslapos[i];
            int exceso = t.Largo - t.Minimo;
            // Un traslapo alargado a mano es largo a propósito: no se avisa.
            if (exceso <= ExcesoParaAviso || t.Manual)
                continue;
            CausaAviso causa = i == 0 || i == ultimo ? CausaAviso.VecinaDeFrontera : CausaAviso.Ninguna;
            AlternativaCara? alternativa = otraCara is null ? null
                : new AlternativaCara(caraDeLaOtra, !TieneTraslapoLargo(otraCara), capa.Kg - otraCara.Kg);
            yield return new Aviso(TipoAviso.TraslapoLargo, t.Id, t.Largo, exceso, causa, alternativa);
        }

        foreach (Barra b in capa.Barras.Where(b => b.Largo > varillaNormal))
            yield return new Aviso(TipoAviso.VarillaLarga, b.Id, Largo: b.Largo);
    }

    private static bool TieneTraslapoLargo(SolucionCapa capa) => capa.Traslapos.Any(t => t.Largo - t.Minimo > ExcesoParaAviso);
}
