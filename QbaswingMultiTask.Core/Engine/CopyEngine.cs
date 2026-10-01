using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using QbaswingMultiTask.Devices;
using QbaswingMultiTask.Util;

namespace QbaswingMultiTask.Engine;

/// <summary>Un destino de la copia: su raiz y, si se conoce, de que unidad se trata.</summary>
public sealed class Destino
{
    public required string Raiz { get; init; }
    public required string Etiqueta { get; init; }
    public DeviceInfo? Info { get; init; }

    /// <summary>Estructura de este destino. Si es null se usa la de las opciones.</summary>
    public StructureMode? Estructura { get; init; }
}

/// <summary>
/// El motor de copia. PLAN.md §3: leer UNA vez y escribir a N destinos, verificar
/// por SHA-256, reanudar sin duplicar, reintentar con espera creciente, y aplicar
/// de verdad las fechas, los atributos y el espacio libre minimo (que estaban
/// declarados y no se usaban).
/// </summary>
public sealed class CopyEngine
{
    private readonly string[] _excluye;
    private readonly string[] _incluye;

    public CopyEngine(CopyOptions opciones)
    {
        Opciones = opciones;
        _excluye = Matcher.Patrones(opciones.ExcludePatterns);
        _incluye = Matcher.Patrones(opciones.IncludePatterns);
    }

    public CopyOptions Opciones { get; }

    // ---------------------------------------------------------------- recorrido

    /// <summary>
    /// Lista los ficheros de una raiz aplicando los filtros de nombre y fecha.
    /// La cancelacion se propaga (plan 8.1: antes se la tragaba y el plan salia
    /// "completo" con medio arbol).
    /// </summary>
    public List<FileEntry> Enumerar(string raiz, CancellationToken ct = default)
    {
        var lista = new List<FileEntry>();
        if (File.Exists(raiz))
        {
            var e = Entrada(raiz, Path.GetFileName(raiz));
            if (e != null) lista.Add(e);
            return lista;
        }
        if (!Directory.Exists(raiz)) return lista;

        var pila = new Stack<string>();
        pila.Push(raiz);

        while (pila.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var actual = pila.Pop();

            string[] sub;
            try { sub = Directory.GetDirectories(actual); }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            foreach (var d in sub)
            {
                var nombre = Path.GetFileName(d);
                if (_excluye.Length > 0 && Matcher.LikeAny(nombre, _excluye)) continue;
                if (Opciones.ExcludeDirectories.Any(x =>
                        string.Equals(x, nombre, StringComparison.OrdinalIgnoreCase))) continue;
                pila.Push(d);
            }

            string[] ficheros;
            try { ficheros = Directory.GetFiles(actual); }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            foreach (var f in ficheros)
            {
                var rel = Path.GetRelativePath(raiz, f);
                var e = Entrada(f, rel);
                if (e != null) lista.Add(e);
            }
        }

        return lista;
    }

    private FileEntry? Entrada(string fichero, string relativo)
    {
        var nombre = Path.GetFileName(fichero);
        if (_excluye.Length > 0 && Matcher.LikeAny(nombre, _excluye)) return null;
        if (_incluye.Length > 0 && !Matcher.LikeAny(nombre, _incluye)) return null;

        FileInfo fi;
        try { fi = new FileInfo(fichero); } catch { return null; }
        if (!fi.Exists) return null;

        if (!PasaFecha(fi.LastWriteTime)) return null;

        return new FileEntry
        {
            FullPath = fi.FullName,
            RelativePath = relativo,
            Length = fi.Length,
            LastWriteUtc = fi.LastWriteTimeUtc,
            CreationUtc = fi.CreationTimeUtc,
            Attributes = fi.Attributes
        };
    }

    private bool PasaFecha(DateTime escritura)
    {
        var d = escritura.Date;
        return Opciones.DateMode switch
        {
            DateMode.MasNuevoQue => Opciones.DateFrom is { } desde && d >= desde.Date,
            DateMode.Entre => (Opciones.DateFrom is not { } a || d >= a.Date) &&
                              (Opciones.DateTo is not { } b || d <= b.Date),
            DateMode.Igual => Opciones.DateFrom is { } igual && d == igual.Date,
            _ => true
        };
    }

    // ------------------------------------------------------------------- ruta

    /// <summary>Calcula donde va el fichero dentro del destino, segun la estructura.</summary>
    public static string RutaDestino(string raizDestino, StructureMode modo, FileEntry f)
    {
        var rel = modo switch
        {
            StructureMode.Plana => f.Nombre,
            StructureMode.PorExtension => Path.Combine(CarpetaExtension(f.Nombre), f.Nombre),
            StructureMode.PorTipo => Path.Combine(CarpetaTipo(f.Nombre), f.Nombre),
            StructureMode.PorFecha => Path.Combine(f.LastWriteUtc.ToString("yyyy"),
                                                   f.LastWriteUtc.ToString("MM"), f.Nombre),
            StructureMode.PorTamano => Path.Combine(CarpetaTamano(f.Length), f.Nombre),
            StructureMode.PorCarpetaOrigen => f.RelativePath,
            StructureMode.SoloNombres => Path.Combine(Path.GetDirectoryName(f.RelativePath) ?? "",
                                                      f.Nombre + ".0kb"),
            _ => f.Nombre
        };
        return Path.Combine(raizDestino, rel);
    }

    public static string CarpetaExtension(string nombre)
    {
        var e = Extensiones.De(nombre);
        return e.Length == 0 ? "SIN-EXTENSION" : e.ToUpperInvariant();
    }

    public static string CarpetaTipo(string nombre)
    {
        var e = Extensiones.De(nombre);
        return e switch
        {
            "mp3" or "m4a" or "flac" or "wav" or "ogg" or "wma" or "aac" or "opus" => "Musica",
            "mp4" or "mkv" or "avi" or "mov" or "wmv" or "flv" or "webm" or "ts" or "m4v" => "Videos",
            "jpg" or "jpeg" or "png" or "gif" or "bmp" or "tif" or "tiff" or "webp" or "heic" => "Fotos",
            "exe" or "msi" or "apk" or "dmg" or "deb" or "rpm" or "appx" => "Instaladores",
            "zip" or "rar" or "7z" or "tar" or "gz" or "xz" => "Comprimidos",
            "pdf" or "doc" or "docx" or "xls" or "xlsx" or "ppt" or "pptx" or "txt" or "odt" => "Documentos",
            "iso" or "img" or "bin" or "nrg" => "Imagenes",
            "" => "SIN-EXTENSION",
            _ => "Otros"
        };
    }

    public static string CarpetaTamano(long bytes)
    {
        if (bytes < 10L * 1024 * 1024) return "Pequenos";          // < 10 MB
        if (bytes < 1024L * 1024 * 1024) return "Medianos";         // < 1 GB
        return "Grandes";
    }

    // ------------------------------------------------------------------ copiar

    /// <summary>
    /// Copia los ficheros a todos los destinos. Devuelve el resumen.
    /// </summary>
    public async Task<CopyResult> CopiarAsync(
        IReadOnlyList<FileEntry> ficheros,
        IReadOnlyList<Destino> destinos,
        IProgress<CopyProgress>? progreso = null,
        Action<Destino, long, long>? porDestino = null,
        Action<string, long>? avanceDestino = null,
        CancellationToken ct = default)
    {
        var reloj = Stopwatch.StartNew();
        var resumen = new CopyResult { FilesTotal = ficheros.Count };
        var totalBytes = ficheros.Sum(f => f.Length);

        foreach (var d in destinos)
            resumen.Destinos.Add(new DestinoResumen { Etiqueta = d.Etiqueta, Raiz = d.Raiz });

        if (destinos.Count == 0 || ficheros.Count == 0)
        {
            resumen.Duracion = reloj.Elapsed;
            return resumen;
        }

        // 1) Espacio libre. §3: MinFreeSpace estaba declarada y nunca se comprobaba.
        var activos = new List<Destino>();
        foreach (var d in destinos)
        {
            if (!Directory.Exists(d.Raiz))
            {
                resumen.Errors.Add(new CopyError
                {
                    Origen = "-", Destino = d.Raiz, Motivo = "el destino no esta disponible",
                    Reintentable = true
                });
                resumen.FilesFailed++;
                continue;
            }

            var libre = LibreDe(d.Raiz);
            if (libre >= 0 && totalBytes + Opciones.MinFreeSpace > libre)
            {
                resumen.Errors.Add(new CopyError
                {
                    Origen = "-", Destino = d.Raiz,
                    Motivo = $"no cabe: hacen falta {Human.Size(totalBytes)} y quedan {Human.Size(libre)}",
                    Reintentable = true
                });
                resumen.FilesFailed++;
                continue;
            }
            activos.Add(d);
        }

        if (activos.Count == 0)
        {
            resumen.Duracion = reloj.Elapsed;
            return resumen;
        }

        // 2) Los ficheros. ParallelFiles = cuantos a la vez, cada uno a N destinos.
        // Los bytes totales son los de todos los destinos juntos: la barra general
        // de §2.1 es "la suma de todas", asi que el porcentaje cuadra con lo escrito.
        var marcador = new Marcador(
            new CopyProgress
            {
                FilesTotal = ficheros.Count,
                BytesTotal = totalBytes * activos.Count
            }, progreso);
        var candado = new object();
        var pendientes = new List<(FileEntry F, Destino D, CopyError E)>();

        var carriles = Math.Max(1, Math.Min(Opciones.ParallelFiles, 16));
        using var puerta = new SemaphoreSlim(carriles);

        var tareas = new List<Task>();
        foreach (var f in ficheros)
        {
            ct.ThrowIfCancellationRequested();
            await puerta.WaitAsync(ct).ConfigureAwait(false);

            tareas.Add(Task.Run(async () =>
            {
                try
                {
                    marcador.ArchivoActual(f.RelativePath);

                    var fallosDeEste = new List<(Destino, CopyError)>();

                    foreach (var d in activos)
                    {
                        ct.ThrowIfCancellationRequested();
                        var r = await CopiarUnoAsync(f, d, marcador, ct).ConfigureAwait(false);

                        lock (candado)
                        {
                            switch (r.Outcome)
                            {
                                case FileOutcome.Copiado:
                                case FileOutcome.Movido:
                                    resumen.FilesCopied++;
                                    resumen.BytesCopied += f.Length;
                                    var i = activos.IndexOf(d);
                                    resumen.Destinos[i].Ficheros++;
                                    resumen.Destinos[i].Bytes += f.Length;
                                    if (r.Verificado) resumen.FilesVerified++;
                                    break;
                                case FileOutcome.Verificado:
                                    resumen.FilesVerified++;
                                    break;
                                case FileOutcome.Saltado:
                                    resumen.FilesSkipped++;
                                    break;
                                default:
                                    resumen.FilesFailed++;
                                    if (r.Error != null)
                                    {
                                        resumen.Errors.Add(r.Error);
                                        fallosDeEste.Add((d, r.Error));
                                    }
                                    break;
                            }
                        }

                        // Aviso por destino: la pestaña dibuja asi la barra de cada
                        // rectangulo, que en §2.1 es \"la suma de todas\" abajo y una
                        // barra propia en cada uno.
                        if (avanceDestino is not null &&
                            (r.Outcome == FileOutcome.Copiado || r.Outcome == FileOutcome.Movido))
                            avanceDestino(d.Raiz, f.Length);

                        if (r.Error is { Reintentable: true })
                            lock (candado) pendientes.Add((f, d, r.Error));
                    }

                    // Mover: solo se borra el origen si NO fallo ningun destino (plan 8.1).
                    if (Opciones.Move && fallosDeEste.Count == 0)
                        BorrarOrigenVerificado(f);

                    marcador.Terminado();
                }
                finally
                {
                    puerta.Release();
                }
            }, ct));
        }

        try
        {
            await Task.WhenAll(tareas).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { resumen.Cancelado = true; }

        // 3) Espejo. §8.1: solo si no hubo ningun fallo, o borraria de mas.
        if (Opciones.Mirror && !resumen.Cancelado && resumen.Success)
        {
            foreach (var d in activos)
            {
                ct.ThrowIfCancellationRequested();
                BorrarSobrantes(ficheros, d, resumen);
            }
        }

        // 4) Limpieza de carpetas vacias del origen. §3: estaba escrita y nunca se llamaba.
        if (Opciones.Move && Opciones.CleanupEmptySources && !resumen.Cancelado)
            LimpiarVacios(ficheros, resumen);

        var porDestinoSeg = reloj.Elapsed.TotalSeconds;
        foreach (var dr in resumen.Destinos) dr.Segundos = porDestinoSeg;

        resumen.Duracion = reloj.Elapsed;
        if (resumen.Destinos.Count > 0) porDestino?.Invoke(activos[0], resumen.FilesCopied, resumen.BytesCopied);

        return resumen;
    }

    private static CopyProgress Clonar(CopyProgress p) => new()
    {
        FilesTotal = p.FilesTotal,
        FilesDone = p.FilesDone,
        BytesTotal = p.BytesTotal,
        BytesDone = p.BytesDone,
        FicheroActual = p.FicheroActual
    };

    // -------------------------------------------------------------- un fichero

    private readonly record struct Resultado(FileOutcome Outcome, CopyError? Error,
                                            bool Verificado = false);

    /// <summary>
    /// Marcador de progreso. §3 pide empujar el avance a la ventana cada
    /// 100–250 ms sin frenar la escritura: por eso los bytes se van sumando trozo
    /// a trozo y solo se avisa cuando toca, no en cada bloque.
    /// </summary>
    private sealed class Marcador
    {
        private readonly CopyProgress _p;
        private readonly IProgress<CopyProgress>? _salida;
        private readonly Stopwatch _reloj = Stopwatch.StartNew();
        private readonly object _candado = new();
        private long _ultimoMs;

        public Marcador(CopyProgress p, IProgress<CopyProgress>? salida)
        {
            _p = p;
            _salida = salida;
        }

        public CopyProgress Datos => _p;

        public void ArchivoActual(string rel)
        {
            lock (_candado) _p.FicheroActual = rel;
        }

        public void Sumar(long bytes)
        {
            if (bytes <= 0) return;
            lock (_candado)
            {
                _p.BytesDone += bytes;
                if (_reloj.ElapsedMilliseconds - _ultimoMs < 150) return;
                _ultimoMs = _reloj.ElapsedMilliseconds;
            }
            Emitir();
        }

        public void Terminado()
        {
            lock (_candado) _p.FilesDone++;
            Emitir();
        }

        public void Emitir()
        {
            if (_salida is null) return;
            CopyProgress copia;
            lock (_candado)
            {
                copia = new CopyProgress
                {
                    FilesTotal = _p.FilesTotal,
                    FilesDone = _p.FilesDone,
                    BytesTotal = _p.BytesTotal,
                    BytesDone = _p.BytesDone,
                    FicheroActual = _p.FicheroActual
                };
            }
            _salida.Report(copia);
        }
    }

    private async Task<Resultado> CopiarUnoAsync(FileEntry f, Destino d, Marcador marcador,
                                                 CancellationToken ct)
    {
        var modo = d.Estructura ?? Opciones.Structure;
        var destino = RutaDestino(d.Raiz, modo, f);
        var carpeta = Path.GetDirectoryName(destino);
        if (string.IsNullOrEmpty(carpeta)) return new Resultado(FileOutcome.Error,
            Error(f, d, "no se pudo calcular la carpeta de destino"));

        for (var intento = 1; ; intento++)
        {
            try
            {
                Directory.CreateDirectory(carpeta);

                // Reanudar sin duplicar: si ya esta igual, no se toca.
                if (File.Exists(destino) && Opciones.Conflict != Conflict.Sobrescribir)
                {
                    var igual = await MismoContenidoAsync(f.FullPath, destino, f.Length, ct)
                        .ConfigureAwait(false);
                    if (igual) return new Resultado(FileOutcome.Saltado, null);

                    if (Opciones.Conflict == Conflict.Saltar)
                        return new Resultado(FileOutcome.Saltado, null);
                    if (Opciones.Conflict == Conflict.Renombrar)
                        destino = SiguienteNombre(destino);
                }

                // Inventario rapido: un fichero de 0 KB con el nombre.
                if (modo == StructureMode.SoloNombres)
                {
                    await File.WriteAllBytesAsync(destino, Array.Empty<byte>(), ct).ConfigureAwait(false);
                    if (Opciones.CopyTimestamps) FijarFechas(destino, f);
                    if (Opciones.CopyAttributes) FijarAtributos(destino, f);
                    return new Resultado(FileOutcome.Copiado, null);
                }

                await VolcarAsync(f, destino, d, marcador, ct).ConfigureAwait(false);

                if (Opciones.CopyTimestamps) FijarFechas(destino, f);
                if (Opciones.CopyAttributes) FijarAtributos(destino, f);

                if (Opciones.Verify)
                {
                    var cuadra = await MismoContenidoAsync(destino, f.FullPath, f.Length, ct)
                        .ConfigureAwait(false);
                    if (!cuadra)
                    {
                        try { File.Delete(destino); } catch { }
                        throw new IOException("el hash del destino no cuadra con el origen");
                    }
                    // Se copio Y se verifico: las dos cosas se cuentan.
                    return new Resultado(Opciones.Move ? FileOutcome.Movido : FileOutcome.Copiado,
                                         null, Verificado: true);
                }

                return new Resultado(Opciones.Move ? FileOutcome.Movido : FileOutcome.Copiado, null);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                if (intento > Opciones.Retries)
                    return new Resultado(FileOutcome.Error, Error(f, d, Motivo(ex)));

                // Espera creciente: 300 ms, 900 ms, 2,7 s.
                await Task.Delay(TimeSpan.FromMilliseconds(300 * Math.Pow(3, intento - 1)), ct)
                          .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// El corazon: se lee el origen UNA vez y se escribe a la vez en todos los
    /// destinos que atienden este fichero. El bloque se ajusta al tipo de unidad
    /// (§3: 1 MB en memorias lentas, 4 MB en SSD/NVMe/red).
    /// </summary>
    private async Task VolcarAsync(FileEntry f, string destino, Destino d, Marcador marcador,
                                   CancellationToken ct)
    {
        var bloque = Opciones.BufferFor(d.Info);
        var temporal = destino + ".qbtemp";

        var origen = new FileStream(f.FullPath, FileMode.Open, FileAccess.Read,
            FileShare.Read, bloque, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var salida = new FileStream(temporal, FileMode.Create, FileAccess.Write,
            FileShare.None, bloque, FileOptions.Asynchronous | FileOptions.SequentialScan);

        var bufer = ArrayPool<byte>.Shared.Rent(bloque);
        try
        {
            await using (origen.ConfigureAwait(false))
            await using (salida.ConfigureAwait(false))
            {
                int leidos;
                while ((leidos = await origen.ReadAsync(bufer.AsMemory(0, bloque), ct)
                                            .ConfigureAwait(false)) > 0)
                {
                    await salida.WriteAsync(bufer.AsMemory(0, leidos), ct).ConfigureAwait(false);
                    // El avance se empuja ya, sin esperar al final del fichero:
                    // asi un fichero de 20 GB tambien mueve la barra (§3).
                    marcador.Sumar(leidos);
                }
                await salida.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bufer);
        }

        // Solo se deja el fichero bueno cuando esta entero: nada de medios ficheros.
        if (File.Exists(destino)) File.Delete(destino);
        File.Move(temporal, destino);
    }

    /// <summary>SHA-256 por bloques: no carga el fichero entero en memoria.</summary>
    private static async Task<byte[]> HashAsync(string ruta, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var bufer = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            await using var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite, 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan);
            int leidos;
            while ((leidos = await fs.ReadAsync(bufer.AsMemory(0, 1 << 20), ct).ConfigureAwait(false)) > 0)
                sha.AppendData(bufer, 0, leidos);
            return sha.GetHashAndReset();
        }
        finally { ArrayPool<byte>.Shared.Return(bufer); }
    }

    /// <summary>
    /// Reanudar sin duplicar (§3): primero por tamano, que es gratis, y solo si
    /// coincide se comparan los hashes.
    /// </summary>
    private static async Task<bool> MismoContenidoAsync(string a, string b, long tamano,
                                                       CancellationToken ct)
    {
        try
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (!fa.Exists || !fb.Exists) return false;
            if (fa.Length != fb.Length || fa.Length != tamano) return false;

            var ha = await HashAsync(a, ct).ConfigureAwait(false);
            var hb = await HashAsync(b, ct).ConfigureAwait(false);
            return ha.AsSpan().SequenceEqual(hb);
        }
        catch { return false; }
    }

    private static void FijarFechas(string destino, FileEntry f)
    {
        try
        {
            File.SetLastWriteTimeUtc(destino, f.LastWriteUtc);
            File.SetCreationTimeUtc(destino, f.CreationUtc);
            File.SetLastAccessTimeUtc(destino, f.LastWriteUtc);
        }
        catch { }
    }

    /// <summary>Se copian los atributos normales; los del sistema no se tocan.</summary>
    private static void FijarAtributos(string destino, FileEntry f)
    {
        try
        {
            var permitidos = f.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden |
                                             FileAttributes.Archive | FileAttributes.NotContentIndexed);
            File.SetAttributes(destino, permitidos);
        }
        catch { }
    }

    private static string SiguienteNombre(string ruta)
    {
        var dir = Path.GetDirectoryName(ruta) ?? "";
        var nombre = Path.GetFileNameWithoutExtension(ruta);
        var ext = Path.GetExtension(ruta);
        for (var n = 2; n < 10000; n++)
        {
            var prueba = Path.Combine(dir, $"{nombre} ({n}){ext}");
            if (!File.Exists(prueba)) return prueba;
        }
        return ruta;
    }

    private static long LibreDe(string raiz)
    {
        try
        {
            var raizReal = Path.GetPathRoot(raiz);
            if (!string.IsNullOrEmpty(raizReal))
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (string.Equals(d.Name, raizReal, StringComparison.OrdinalIgnoreCase) && d.IsReady)
                            return d.AvailableFreeSpace;
                    }
                    catch { }
                }
            }
            return new DriveInfo(raiz).AvailableFreeSpace;
        }
        catch { return -1; }
    }

    private static void BorrarOrigenVerificado(FileEntry f)
    {
        try { if (File.Exists(f.FullPath)) File.Delete(f.FullPath); } catch { }
    }

    /// <summary>
    /// LIMPIEZA. §8.1: borrar del destino lo que sobra, sin entrar en carpetas del
    /// sistema y comparando rutas sin distinguir mayusculas (que era el fallo que
    /// borraba ficheros que si estaban).
    /// </summary>
    private void BorrarSobrantes(IReadOnlyList<FileEntry> origen, Destino d, CopyResult resumen)
    {
        var esperados = origen
            .Select(f => Path.GetFullPath(RutaDestino(d.Raiz, d.Estructura ?? Opciones.Structure, f)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var pila = new Stack<string>();
        pila.Push(d.Raiz);

        while (pila.Count > 0)
        {
            var actual = pila.Pop();
            string[] sub, arch;
            try
            {
                sub = Directory.GetDirectories(actual);
                arch = Directory.GetFiles(actual);
            }
            catch { continue; }

            foreach (var s in sub)
            {
                var n = Path.GetFileName(s);
                if (Opciones.ExcludeDirectories.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase)))
                    continue;
                pila.Push(s);
            }

            foreach (var a in arch)
            {
                if (esperados.Contains(Path.GetFullPath(a))) continue;
                try { File.Delete(a); } catch { }
            }

            // Carpetas que quedaron vacias.
            try
            {
                if (!Directory.EnumerateFileSystemEntries(actual).Any())
                    Directory.Delete(actual);
            }
            catch { }
        }
    }

    private void LimpiarVacios(IReadOnlyList<FileEntry> origen, CopyResult resumen)
    {
        var carpetas = origen
            .Select(f => Path.GetDirectoryName(f.FullPath))
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(c => c!.Length)
            .ToList();

        foreach (var c in carpetas)
        {
            try
            {
                if (Directory.Exists(c) && !Directory.EnumerateFileSystemEntries(c!).Any())
                {
                    Directory.Delete(c!);
                    resumen.OrigenesVacios.Add(c!);
                }
            }
            catch { }
        }
    }

    /// <summary>Motivo del fallo en palabras, tal como lo muestra §2.5.</summary>
    public static string Motivo(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "sin permiso",
        DirectoryNotFoundException => "no existe la carpeta",
        FileNotFoundException => "no existe el fichero",
        PathTooLongException => "ruta demasiado larga",
        IOException io when io.Message.Contains("space", StringComparison.OrdinalIgnoreCase)
            => "no queda espacio",
        IOException io when io.Message.Contains("disconnected", StringComparison.OrdinalIgnoreCase)
            => "el dispositivo se desconecto",
        IOException io => io.Message,
        _ => ex.GetType().Name
    };

    private static CopyError Error(FileEntry f, Destino d, string motivo) => new()
    {
        Fichero = f.Nombre,
        Origen = Path.GetDirectoryName(f.FullPath) ?? "",
        Destino = d.Raiz,
        Motivo = motivo,
        Reintentable = true
    };
}