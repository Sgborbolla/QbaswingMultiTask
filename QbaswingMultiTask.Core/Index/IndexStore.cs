using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using QbaswingMultiTask.Util;

namespace QbaswingMultiTask.Index;

/// <summary>Una entrada del indice: la ficha de un fichero.</summary>
public sealed class IndexEntry
{
    public string Path { get; set; } = "";
    public long Length { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public string Hash { get; set; } = "";
    public string Extension { get; set; } = "";
}

/// <summary>Un grupo de ficheros iguales (por contenido, no por nombre).</summary>
public sealed class DuplicateGroup
{
    public string Nombre { get; set; } = "";
    public long Length { get; set; }
    public List<string> Ubicaciones { get; set; } = new();
    /// <summary>Bytes que se recuperan si se dejan solo una copia.</summary>
    public long Desperdicio => Length * Math.Max(0, Ubicaciones.Count - 1);
    public string UbicacionesTexto => string.Join(" · ", Ubicaciones);
}

/// <summary>Un fichero que esta en A y no en B.</summary>
public sealed class MissingItem
{
    public string Relativo { get; set; } = "";
    public long Length { get; set; }
    public bool EsCarpeta { get; set; }
    /// <summary>Si hay un fichero de nombre parecido en B, se apunta.</summary>
    public string? Parecido { get; set; }
}

/// <summary>
/// Indice incremental del escaner (§2.3). PLAN.md dice "base SQLite"; en esta
/// maquina no se pudo restaurar SQLitePCLRaw sin red (PROGRESO §desvios), asi que
/// se guarda como JSONL con el mismo contenido y se reutiliza el hash SHA-256 de
/// lo que no cambio (tamano + fecha iguales). El progreso del indice es real.
/// </summary>
public sealed class IndexStore
{
    private readonly Dictionary<string, IndexEntry> _mapa =
        new(StringComparer.OrdinalIgnoreCase);

    private static string Ruta => Path.Combine(AppPaths.DataDir, "indice.jsonl");

    public int Cuantas => _mapa.Count;

    /// <summary>Lee el indice del disco. Devuelve cuantas entradas hay.</summary>
    public int Cargar()
    {
        _mapa.Clear();
        try
        {
            if (!File.Exists(Ruta)) return 0;
            foreach (var linea in File.ReadLines(Ruta))
            {
                if (linea.Length == 0) continue;
                try
                {
                    var e = JsonSerializer.Deserialize<IndexEntry>(linea);
                    if (e is { Path.Length: > 0 }) _mapa[e.Path] = e;
                }
                catch { }
            }
        }
        catch { }
        return _mapa.Count;
    }

    public void Guardar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
            var tmp = Ruta + ".tmp";
            using (var w = new StreamWriter(tmp, false, Encoding.UTF8))
                foreach (var e in _mapa.Values)
                    w.WriteLine(JsonSerializer.Serialize(e));
            File.Copy(tmp, Ruta, true);
            File.Delete(tmp);
        }
        catch { }
    }

    /// <summary>
    /// Recorre las raices y deja el indice al dia. Solo vuelve a hashear lo que
    /// cambio (mismo tamano y fecha = se reaprovecha el hash). El progreso se
    /// informa de verdad: numeros recorridos sobre el total estimado.
    /// </summary>
    public int Indexar(IEnumerable<string> raices, IProgress<double>? progreso = null,
        IProgress<string>? actual = null, CancellationToken ct = default)
    {
        var lista = raices.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var ficheros = new List<FileInfo>();

        foreach (var raiz in lista)
        {
            ct.ThrowIfCancellationRequested();
            var pila = new Stack<string>();
            pila.Push(raiz);
            while (pila.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var dir = pila.Pop();
                IEnumerable<string> subdirs;
                try { subdirs = Directory.EnumerateDirectories(dir); } catch { continue; }
                foreach (var s in subdirs)
                {
                    var nombre = Path.GetFileName(s);
                    if (AppPaths.EsCarpetaSistema(nombre)) continue;
                    pila.Push(s);
                }
                IEnumerable<string> archivos;
                try { archivos = Directory.EnumerateFiles(dir); } catch { continue; }
                foreach (var a in archivos)
                {
                    try { ficheros.Add(new FileInfo(a)); } catch { }
                }
            }
        }

        var total = Math.Max(1, ficheros.Count);
        var hechos = 0;
        var nuevas = new Dictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in ficheros)
        {
            ct.ThrowIfCancellationRequested();
            IndexEntry? e;
            try
            {
                var clave = f.FullName;
                if (_mapa.TryGetValue(clave, out var viejo) &&
                    viejo.Length == f.Length && viejo.ModifiedUtc == f.LastWriteTimeUtc &&
                    viejo.Hash.Length > 0)
                {
                    e = viejo;                         // no cambio: se reaprovecha el hash
                }
                else
                {
                    e = new IndexEntry
                    {
                        Path = clave,
                        Length = f.Length,
                        ModifiedUtc = f.LastWriteTimeUtc,
                        Extension = f.Extension.ToLowerInvariant(),
                        Hash = f.Length > 0 ? Hash(f.FullName, ct) : ""
                    };
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { e = null; }

            if (e is not null) nuevas[e.Path] = e;
            hechos++;
            if (hechos % 16 == 0)
            {
                progreso?.Report(hechos * 100.0 / total);
                actual?.Report(f.Name);
            }
        }

        _mapa.Clear();
        foreach (var p in nuevas) _mapa[p.Key] = p.Value;
        progreso?.Report(100);
        return _mapa.Count;
    }

    /// <summary>Duplicados de verdad: primero por tamano, y solo los candidatos se hashean.</summary>
    public List<DuplicateGroup> Duplicados(IProgress<double>? progreso = null,
        CancellationToken ct = default)
    {
        var porTamano = _mapa.Values.Where(e => e.Length > 0)
            .GroupBy(e => e.Length)
            .Where(g => g.Count() > 1)
            .ToList();

        var total = Math.Max(1, porTamano.Count);
        var hechos = 0;
        var resultado = new List<DuplicateGroup>();

        foreach (var grupo in porTamano)
        {
            ct.ThrowIfCancellationRequested();
            var porHash = grupo.GroupBy(e =>
            {
                if (e.Hash.Length > 0) return e.Hash;
                try { return Hash(e.Path, ct); } catch { return "?"; }
            }).Where(g => g.Key != "?" && g.Count() > 1);

            foreach (var h in porHash)
            {
                var items = h.ToList();
                resultado.Add(new DuplicateGroup
                {
                    Nombre = Path.GetFileName(items[0].Path),
                    Length = items[0].Length,
                    Ubicaciones = items.Select(x => x.Path).ToList()
                });
            }

            hechos++;
            progreso?.Report(hechos * 100.0 / total);
        }

        progreso?.Report(100);
        return resultado.OrderByDescending(g => g.Desperdicio).ToList();
    }

    /// <summary>Ficheros de A que no estan en B (comparando rutas relativas).</summary>
    public List<MissingItem> Faltantes(string a, string b)
    {
        var faltan = new List<MissingItem>();
        if (!Directory.Exists(a)) return faltan;

        var enB = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(b))
        {
            var pila = new Stack<string>();
            pila.Push(b);
            while (pila.Count > 0)
            {
                var dir = pila.Pop();
                try
                {
                    foreach (var s in Directory.EnumerateDirectories(dir))
                    {
                        if (AppPaths.EsCarpetaSistema(Path.GetFileName(s))) continue;
                        pila.Push(s);
                    }
                    foreach (var f in Directory.EnumerateFiles(dir))
                        enB.Add(Path.GetRelativePath(b, f));
                }
                catch { }
            }
        }

        var pilaA = new Stack<string>();
        pilaA.Push(a);
        while (pilaA.Count > 0)
        {
            var dir = pilaA.Pop();
            try
            {
                foreach (var s in Directory.EnumerateDirectories(dir))
                {
                    var nombre = Path.GetFileName(s);
                    if (AppPaths.EsCarpetaSistema(nombre)) continue;
                    var rel = Path.GetRelativePath(a, s);
                    if (!enB.Contains(rel))
                        faltan.Add(new MissingItem { Relativo = rel, EsCarpeta = true });
                    pilaA.Push(s);
                }
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    var rel = Path.GetRelativePath(a, f);
                    if (enB.Contains(rel)) continue;
                    long len = 0;
                    try { len = new FileInfo(f).Length; } catch { }
                    faltan.Add(new MissingItem
                    {
                        Relativo = rel,
                        Length = len,
                        Parecido = ParecidoMasCercano(Path.GetFileName(f), enB)
                    });
                }
            }
            catch { }
        }

        return faltan;
    }

    public List<IndexEntry> Buscar(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return new List<IndexEntry>();
        var patrones = Matcher.Patrones(texto);
        return _mapa.Values
            .Where(e => patrones.Length == 0
                ? e.Path.Contains(texto, StringComparison.OrdinalIgnoreCase)
                : patrones.Any(p => Matcher.Like(p, Path.GetFileName(e.Path))))
            .Take(5000)
            .ToList();
    }

    private static string? ParecidoMasCercano(string nombre, IEnumerable<string> candidatos)
    {
        string? mejor = null;
        var mejorPuntos = 0.0;
        foreach (var c in candidatos)
        {
            var puntos = Sync.SeriesSynchronizer.Similitud(nombre, Path.GetFileName(c));
            if (puntos > mejorPuntos) { mejorPuntos = puntos; mejor = c; }
        }
        return mejorPuntos >= 0.6 ? mejor : null;
    }

    /// <summary>SHA-256 de un fichero, leido a bloques.</summary>
    public static string Hash(string ruta, CancellationToken ct = default)
    {
        using var sha = SHA256.Create();
        using var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            1 << 20, FileOptions.SequentialScan);
        var bufer = new byte[1 << 20];
        int leidos;
        while ((leidos = fs.Read(bufer, 0, bufer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            sha.TransformBlock(bufer, 0, leidos, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash ?? Array.Empty<byte>());
    }
}
