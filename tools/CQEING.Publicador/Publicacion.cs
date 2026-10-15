using CQEING.Gestor.Core;

namespace CQEING.Publicador;

public interface ICompilador
{
    /// <summary>Compila el proyecto en Release para ese año de Revit y devuelve la carpeta de salida.</summary>
    string Compilar(string rutaProyecto, string anio, string version);
}

/// <summary>De una etiqueta &lt;id&gt;-vX.Y.Z a los archivos de la release: un ZIP por año de Revit y manifest.json.</summary>
public sealed class Publicacion(ICompilador compilador)
{
    public Manifiesto Ejecutar(string raizRepo, string etiqueta, string notas, DateTimeOffset publicadoEn, string carpetaSalida)
    {
        EtiquetaRelease tag = EtiquetaRelease.Interpretar(etiqueta)
            ?? throw new InvalidOperationException($"La etiqueta '{etiqueta}' no tiene la forma <add-in>-vX.Y.Z.");
        string carpetaAddin = Path.Combine(raizRepo, "addins", tag.Id);
        string rutaConfig = Path.Combine(carpetaAddin, "addin.json");
        if (!File.Exists(rutaConfig))
            throw new InvalidOperationException($"No existe {rutaConfig}: la etiqueta no corresponde a ningún add-in.");
        ConfiguracionAddin config = ConfiguracionAddin.Leer(rutaConfig);
        if (config.Id != tag.Id)
            throw new InvalidOperationException($"{rutaConfig} dice id '{config.Id}' y la etiqueta es de '{tag.Id}'.");

        string version = tag.Version.ToString();
        Directory.CreateDirectory(carpetaSalida);
        var paquetes = new List<PaqueteManifiesto>();
        foreach (string anio in config.Revit)
        {
            string compilada = compilador.Compilar(Path.Combine(carpetaAddin, config.Proyecto), anio, version);
            string framework = File.ReadAllText(Path.Combine(compilada, Empaquetador.ArchivoFramework)).Trim();
            string archivo = $"{tag.Id}-{version}-revit{anio}.zip";
            string zip = Path.Combine(carpetaSalida, archivo);
            Empaquetador.CrearZip(compilada, zip);
            paquetes.Add(new PaqueteManifiesto(anio, framework, archivo, Sha256Archivo.Calcular(zip)));
        }

        var manifiesto = new Manifiesto(SerializadorManifiesto.EsquemaActual, tag.Id, config.Nombre, config.Descripcion,
            version, publicadoEn, notas.Trim(), config.Heredados, paquetes);
        string json = SerializadorManifiesto.Escribir(manifiesto);
        SerializadorManifiesto.Leer(json); // lo que el gestor rechazaría no se publica
        File.WriteAllText(Path.Combine(carpetaSalida, Repositorio.ArchivoManifiesto), json);
        return manifiesto;
    }
}
