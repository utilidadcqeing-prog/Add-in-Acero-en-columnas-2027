using System.Text.RegularExpressions;
using CQEING.Compartido;

namespace CQEING.Gestor.Core.Tests;

public class CodigoEquipoTests
{
    [Fact]
    public void El_mismo_guid_da_el_mismo_codigo_con_formato_XXXX()
    {
        string codigo = CodigoEquipo.Desde("5d1f3c0e-8a2b-4c6d-9e7f-0a1b2c3d4e5f");

        Assert.Matches(new Regex("^[0-9A-F]{4}(-[0-9A-F]{4}){3}$"), codigo);
        Assert.Equal(codigo, CodigoEquipo.Desde("5d1f3c0e-8a2b-4c6d-9e7f-0a1b2c3d4e5f"));
    }

    [Fact]
    public void Guids_distintos_dan_codigos_distintos()
    {
        Assert.NotEqual(
            CodigoEquipo.Desde("5d1f3c0e-8a2b-4c6d-9e7f-0a1b2c3d4e5f"),
            CodigoEquipo.Desde("5d1f3c0e-8a2b-4c6d-9e7f-0a1b2c3d4e60"));
    }

    [Fact]
    public void No_expone_el_guid()
    {
        Assert.DoesNotContain("5D1F", CodigoEquipo.Desde("5d1f3c0e-8a2b-4c6d-9e7f-0a1b2c3d4e5f").Replace("-", ""));
    }

    [Fact]
    public void En_este_equipo_hay_codigo()
    {
        Assert.Matches(new Regex("^[0-9A-F]{4}(-[0-9A-F]{4}){3}$"), CodigoEquipo.Actual());
    }
}
