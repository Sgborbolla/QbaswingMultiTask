using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using QbaswingMultiTask.Util;

namespace QbaswingMultiTask.Sync;

/// <summary>Estado de una fila en el sincronizador (§2.2): ✓ ya lo tienes, ● nuevo, = repetido.</summary>
public enum SyncEstado
{
    /// <summary>Ya esta en los dos lados, igual.</summary>
    Igual,
    /// <summary>Nuevo: hay que copiarlo.</summary>
    Nuevo,
    /// <summary>Repetido: ya esta en el otro lado, se ignora (§2.2).</summary>
    Repetido,
    /// <summary>Esta en los dos lados pero no es igual.</summary>
    Diferente,
    /// <summary>Solo en el panel izquierdo.</summary>
    SoloIzquierda,
    /// <summary>Solo en el panel derecho.</summary>
    SoloDerecha
}

/// <summary>Una fila del arbol del sincronizador.</summary>
public sealed class SyncItem
{
    public string Relativo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public long Length { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public bool EsCarpeta { get; set; }
    public SyncEstado Estado { get; set; }
    /// <summary>Con quien se emparejo (nombre relativo en el otro panel).</summary>
    public string? Pareja { get; set; }
    public double Similitud { get; set; }

    /// <summary>La marca de §2.2: ✓ ya lo tienes · ● nuevo · = repetido.</summary>
    public string Marca => Estado switch
    {
        SyncEstado.Igual => "✓",
        SyncEstado.Nuevo => "●",
        SyncEstado.Repetido => "=",
        SyncEstado.Diferente => "≠",
        SyncEstado.SoloIzquierda => "◀",
        SyncEstado.SoloDerecha => "▶",
        _ => "·"
    };

    public string TamTexto => EsCarpeta ? "<carpeta>" : Human.Size(Length);
}

/// <summary>El plan de sincronizacion entre dos paneles.</summary>
public sealed class SyncPlan
{
    public string Izquierda { get; set; } = "";
    public string Derecha { get; set; } = "";
    public List<SyncItem> ItemsIzquierda { get; } = new();
    public List<SyncItem> ItemsDerecha { get; } = new();

    public int Nuevos => ItemsIzquierda.Count(i => i.Estado == SyncEstado.Nuevo);
    public int Iguales => ItemsIzquierda.Count(i => i.Estado == SyncEstado.Igual);
    public int Repetidos => ItemsDerecha.Count(i => i.Estado == SyncEstado.Repetido);
    public int Diferentes => ItemsIzquierda.Count(i => i.Estado == SyncEstado.Diferente);

    /// <summary>Lo que se va a copiar del panel derecho al izquierdo (los "nuevos").</summary>
    public IEnumerable<SyncItem> ACopiar => ItemsIzquierda.Where(i => i.Estado == SyncEstado.Nuevo);

    public long BytesACopiar => ACopiar.Sum(i => i.Length);

    public bool HayAlgo => ItemsIzquierda.Count > 0 || ItemsDerecha.Count > 0;
}

/// <summary>
/// Compara dos carpetas y dice que falta en cada lado. PLAN.md §2.2: los dos
/// paneles son independientes y abiertos por raiz, sin roles forzados.
/// </summary>
public static class Synchronizer
{
    /// <summary>Recorre un arbol y devuelve (relativo, ficha) de cada fichero y carpeta.</summary>
    public static List<SyncItem> Recorrer(string raiz, CancellationToken ct = default)
    {
        var lista = new List<SyncItem>();
        if (string.IsNullOrEmpty(raiz) || !Directory.Exists(raiz)) return lista;

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
                lista.Add(new SyncItem
                {
                    Relativo = Path.GetRelativePath(raiz, s),
                    Nombre = nombre,
                    EsCarpeta = true,
                    ModifiedUtc = SafeFecha(s)
                });
                pila.Push(s);
            }
            IEnumerable<string> archivos;
            try { archivos = Directory.EnumerateFiles(dir); } catch { continue; }
            foreach (var a in archivos)
            {
                var fi = new FileInfo(a);
                lista.Add(new SyncItem
                {
                    Relativo = Path.GetRelativePath(raiz, a),
                    Nombre = Path.GetFileName(a),
                    Length = fi.Length,
                    ModifiedUtc = fi.LastWriteTimeUtc
                });
            }
        }
        return lista;
    }

    private static DateTime SafeFecha(string dir)
    {
        try { return Directory.GetLastWriteTimeUtc(dir); } catch { return DateTime.MinValue; }
    }

    /// <summary>Compara los dos arboles y asigna el estado de cada fila.</summary>
    public static SyncPlan Comparar(string izquierda, string derecha, double umbral = 0.75,
        IProgress<double>? progreso = null, CancellationToken ct = default)
    {
        var plan = new SyncPlan { Izquierda = izquierda, Derecha = derecha };

        progreso?.Report(5);
        var izq = Recorrer(izquierda, ct);
        progreso?.Report(40);
        var der = Recorrer(derecha, ct);
        progreso?.Report(70);

        var enIzq = izq.ToDictionary(x => x.Relativo, StringComparer.OrdinalIgnoreCase);
        var enDer = der.ToDictionary(x => x.Relativo, StringComparer.OrdinalIgnoreCase);

        // 1) Coincidencia exacta de ruta relativa.
        foreach (var i in izq)
        {
            ct.ThrowIfCancellationRequested();
            if (enDer.TryGetValue(i.Relativo, out var j))
            {
                var iguales = i.EsCarpeta == j.EsCarpeta && i.Length == j.Length &&
                              i.ModifiedUtc == j.ModifiedUtc;
                i.Estado = iguales ? SyncEstado.Igual : SyncEstado.Diferente;
                i.Pareja = j.Relativo;
                j.Estado = iguales ? SyncEstado.Repetido : SyncEstado.Diferente;
                j.Pareja = i.Relativo;
            }
            else
            {
                i.Estado = SyncEstado.SoloIzquierda;
            }
        }

        // 2) Los de la derecha sin pareja son nuevos para la izquierda (●).
        foreach (var j in der)
            if (j.Pareja is null)
                j.Estado = SyncEstado.Nuevo;

        // 3) Alineacion por parecido de nombre (§2.2: umbral 75 %, bigrams).
        AlinearPorNombre(izq, der, umbral, ct);

        plan.ItemsIzquierda.AddRange(izq);
        plan.ItemsDerecha.AddRange(der);
        progreso?.Report(100);
        return plan;
    }

    /// <summary>
    /// Empareja por nombre parecido lo que quedo suelto: asi "cap01" de un lado
    /// encuentra "cap01-rip" del otro y no se duplica (§2.2).
    /// </summary>
    public static void AlinearPorNombre(List<SyncItem> izq, List<SyncItem> der, double umbral,
        CancellationToken ct = default)
    {
        var sueltosIzq = izq.Where(i => i.Estado == SyncEstado.SoloIzquierda).ToList();
        var sueltosDer = der.Where(j => j.Estado == SyncEstado.Nuevo).ToList();

        foreach (var j in sueltosDer)
        {
            ct.ThrowIfCancellationRequested();
            SyncItem? mejor = null;
            var mejorPuntos = umbral;
            foreach (var i in sueltosIzq)
            {
                if (i.Pareja is not null) continue;
                var puntos = SeriesSynchronizer.Similitud(i.Nombre, j.Nombre);
                if (puntos > mejorPuntos) { mejorPuntos = puntos; mejor = i; }
            }
            if (mejor is null) continue;

            mejor.Pareja = j.Relativo;
            mejor.Similitud = mejorPuntos;
            j.Pareja = mejor.Relativo;
            j.Similitud = mejorPuntos;

            var iguales = mejor.Length == j.Length && mejor.ModifiedUtc == j.ModifiedUtc;
            mejor.Estado = iguales ? SyncEstado.Igual : SyncEstado.Diferente;
            j.Estado = iguales ? SyncEstado.Repetido : SyncEstado.Diferente;
        }
    }

    /// <summary>Deja todos los "nuevos" como "ya lo tienes" (boton Desalinear).</summary>
    public static void MarcarTodoAlineado(List<SyncItem> items)
    {
        foreach (var i in items)
            if (i.Estado is SyncEstado.Nuevo or SyncEstado.SoloIzquierda or SyncEstado.Diferente)
                i.Estado = SyncEstado.Igual;
    }

    /// <summary>Copia los "nuevos" del panel origen al destino usando el motor de F1.</summary>
    public static async System.Threading.Tasks.Task<Engine.CopyResult> AplicarAsync(
        SyncPlan plan,
        bool deDerechaAIzquierda,
        Engine.CopyOptions opciones,
        IProgress<Engine.CopyProgress>? progreso = null,
        CancellationToken ct = default)
    {
        var origenRaiz = deDerechaAIzquierda ? plan.Derecha : plan.Izquierda;
        var destinoRaiz = deDerechaAIzquierda ? plan.Izquierda : plan.Derecha;
        var fuente = deDerechaAIzquierda ? plan.ItemsDerecha : plan.ItemsIzquierda;

        // Los ficheros a copiar: los "nuevos" del lado origen. Si se va de
        // derecha a izquierda, son las filas de la derecha sin pareja.
        var aCopiar = fuente
            .Where(i => !i.EsCarpeta &&
                        (deDerechaAIzquierda
                            ? i.Estado == SyncEstado.Nuevo
                            : i.Estado == SyncEstado.SoloIzquierda))
            .ToList();

        var motor = new Engine.CopyEngine(opciones);
        var entradas = new List<Engine.FileEntry>();
        foreach (var i in aCopiar)
        {
            var completo = Path.Combine(origenRaiz, i.Relativo);
            if (!File.Exists(completo)) continue;
            entradas.Add(new Engine.FileEntry
            {
                FullPath = completo,
                RelativePath = i.Relativo,
                Length = i.Length,
                LastWriteUtc = i.ModifiedUtc,
                CreationUtc = i.ModifiedUtc,
                Attributes = FileAttributes.Normal
            });
        }

        var destinos = new List<Engine.Destino>
        {
            new()
            {
                Raiz = destinoRaiz,
                Etiqueta = Path.GetFileName(destinoRaiz.TrimEnd('\\', '/')),
                Estructura = Engine.StructureMode.PorCarpetaOrigen
            }
        };

        return await motor.CopiarAsync(entradas, destinos, progreso, null, null, ct)
            .ConfigureAwait(false);
    }
}
