using CQEING.Gestor.Core;
using CQEING.Publicador;

try
{
    Dictionary<string, string> argumentos = LeerArgumentos(args);
    Manifiesto m = new Publicacion(new CompiladorDotnet()).Ejecutar(
        Requerido(argumentos, "--repo"),
        Requerido(argumentos, "--etiqueta"),
        File.ReadAllText(Requerido(argumentos, "--notas-archivo")),
        DateTimeOffset.UtcNow,
        Requerido(argumentos, "--salida"));
    Console.WriteLine($"Listo {m.Id} {m.Version}: {string.Join(", ", m.Paquetes.Select(p => p.Archivo))}");
    return 0;
}
catch (Exception e) when (e is InvalidOperationException or GestorException or IOException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

static Dictionary<string, string> LeerArgumentos(string[] args)
{
    if (args.Length % 2 != 0)
        throw new InvalidOperationException("Uso: --repo <raíz> --etiqueta <id-vX.Y.Z> --notas-archivo <ruta> --salida <carpeta>");
    return args.Chunk(2).ToDictionary(par => par[0], par => par[1]);
}

static string Requerido(Dictionary<string, string> argumentos, string clave) =>
    argumentos.TryGetValue(clave, out string? valor) ? valor : throw new InvalidOperationException($"Falta el argumento {clave}.");
