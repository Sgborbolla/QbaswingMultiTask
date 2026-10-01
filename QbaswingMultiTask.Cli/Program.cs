using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QbaswingMultiTask;
using QbaswingMultiTask.Devices;
using QbaswingMultiTask.Engine;
using QbaswingMultiTask.Util;

// CLI real de QbaswingMultiTask (PLAN.md §3: "CLI con comandos, alias EN, -v
// verifica, -q silenciosa"). Sin dependencias externas: solo Core.

var cultura = new CultureInfo("es-ES");
CultureInfo.DefaultThreadCurrentCulture = cultura;
CultureInfo.DefaultThreadCurrentUICulture = cultura;

return await Cli.RunAsync(args);

internal static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || EsAyuda(args[0])) { Ayuda(); return 0; }

        var comando = args[0].ToLowerInvariant();
        var resto = args.Skip(1).ToArray();

        try
        {
            return comando switch
            {
                "listar" or "list" or "ls" => Listar(resto),
                "copiar" or "copy" or "cp" => await CopiarAsync(resto, mover: false),
                "mover" or "move" or "mv" => await CopiarAsync(resto, mover: true),
                "expulsar" or "eject" => await ExpulsarAsync(resto),
                "formatear" or "format" => await FormatearAsync(resto),
                "ayuda" or "help" or "--help" or "-h" => AyudaCodigo(),
                "version" or "--version" or "-V" => Version(),
                _ => Desconocido(comando)
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelado.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    // ------------------------------------------------------------------ listar

    private static int Listar(string[] args)
    {
        var silenciosa = Hay(args, "-q", "--quiet");
        var dispositivos = new DeviceManager().Scan();

        if (dispositivos.Count == 0)
        {
            if (!silenciosa) Console.WriteLine("No se ha encontrado ningun dispositivo.");
            return 0;
        }

        if (silenciosa)
        {
            foreach (var d in dispositivos) Console.WriteLine(d.Root);
            return 0;
        }

        Console.WriteLine($"{"LETRA",-6} {"ETIQUETA",-14} {"TIPO",-18} {"LIBRE",-10} {"TAMANO",-10} FISICO");
        Console.WriteLine(new string('-', 100));
        foreach (var d in dispositivos)
        {
            Console.WriteLine(
                $"{d.DriveLetter ?? "—",-6} {Recorta(d.Label ?? "—", 14),-14} " +
                $"{d.KindLabel,-18} {Human.Size(d.FreeSpace),-10} {Human.Size(d.Capacity),-10} " +
                $"{d.Fisico}");
        }
        Console.WriteLine();
        Console.WriteLine($"{dispositivos.Count} dispositivo(s).");
        return 0;
    }

    // ------------------------------------------------------------------ copiar

    private static async Task<int> CopiarAsync(string[] args, bool mover)
    {
        var posicionales = args.Where(a => !a.StartsWith('-')).ToList();
        if (posicionales.Count < 2)
        {
            Console.Error.WriteLine(mover
                ? "Uso: qbaswingmultitask mover <origen> <destino1> [destino2...] [opciones]"
                : "Uso: qbaswingmultitask copiar <origen> <destino1> [destino2...] [opciones]");
            return 2;
        }

        var origen = posicionales[0];
        var destinos = posicionales.Skip(1).ToList();

        var silenciosa = Hay(args, "-q", "--quiet");
        var espejo = Hay(args, "--espejo", "--mirror");
        var estructura = ValorOpcion(args, "--estructura", "--structure") is { } e
            ? EstructuraDe(e) : StructureMode.Plana;
        var desde = Fecha(ValorOpcion(args, "--desde", "--from"));
        var hasta = Fecha(ValorOpcion(args, "--hasta", "--to"));
        var paralelo = Entero(ValorOpcion(args, "--paralelo", "--parallel"), 1);
        var libreMb = Entero(ValorOpcion(args, "--libre", "--min-free"), 0);
        var incluir = ValorOpcion(args, "--incluir", "--include");
        var excluir = ValorOpcion(args, "--excluir", "--exclude");

        var opciones = new CopyOptions
        {
            Move = mover,
            Mirror = espejo,
            Verify = true,
            CopyTimestamps = true,
            CopyAttributes = true,
            Structure = estructura,
            ParallelFiles = paralelo,
            MinFreeSpace = libreMb * 1024L * 1024L,
            IncludePatterns = incluir,
            ExcludePatterns = excluir,
            DateMode = desde is null && hasta is null
                ? DateMode.Todas
                : hasta is not null ? DateMode.Entre : DateMode.MasNuevoQue,
            DateFrom = desde,
            DateTo = hasta
        };

        var motor = new CopyEngine(opciones);
        if (!silenciosa) Console.WriteLine($"Origen: {origen}");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, ev) => { ev.Cancel = true; cts.Cancel(); };

        var ficheros = await Task.Run(() => motor.Enumerar(origen, cts.Token), cts.Token);
        if (ficheros.Count == 0)
        {
            Console.Error.WriteLine("No hay ficheros que copiar con esos filtros.");
            return 1;
        }

        var total = ficheros.Sum(f => f.Length);
        var listaDestinos = destinos.Select((d, i) => new Destino
        {
            Raiz = d,
            Etiqueta = d,
            Estructura = estructura
        }).ToList();

        if (!silenciosa)
            Console.WriteLine($"{ficheros.Count} ficheros · {Human.Size(total)} → {listaDestinos.Count} destino(s)");

        var progreso = new Progress<CopyProgress>(p =>
        {
            if (silenciosa) return;
            Console.Write($"\r{p.Percent,6:0.0} %  {Human.Size(p.BytesDone)} / {Human.Size(p.BytesTotal)}  " +
                          $"{Recorta(p.FicheroActual, 40)}".PadRight(45));
        });

        var resultado = await motor.CopiarAsync(ficheros, listaDestinos, progreso,
            avanceDestino: null, ct: cts.Token);

        if (!silenciosa) Console.WriteLine();

        foreach (var d in resultado.Destinos)
            Console.WriteLine($"  {d.Etiqueta,-18} {d.Ficheros,8} ficheros  {Human.Size(d.Bytes),10}  " +
                              $"{Human.Size((long)d.Velocidad),10}/s  {d.Errores} errores");

        foreach (var err in resultado.Errors.Take(20))
            Console.Error.WriteLine($"  ! {err.Fichero}: {err.Motivo} ({err.Destino})");

        Console.WriteLine(resultado.Success
            ? $"{(mover ? "Movido" : "Copiado")}: {Human.Numero(resultado.FilesCopied)} ficheros · " +
              $"{Human.Size(resultado.BytesCopied)} · {Human.Time(resultado.Duracion.TotalSeconds)}"
            : $"Termino con {resultado.Errors.Count} problema(s).");

        return resultado.Success ? 0 : 1;
    }

    // ---------------------------------------------------------------- expulsar

    private static async Task<int> ExpulsarAsync(string[] args)
    {
        var posicionales = args.Where(a => !a.StartsWith('-')).ToList();
        if (posicionales.Count == 0)
        {
            Console.Error.WriteLine("Uso: qbaswingmultitask expulsar <letra|ruta> [mas...]");
            return 2;
        }

        var dispositivos = new DeviceManager().Scan();
        var fallos = 0;

        foreach (var objetivo in posicionales)
        {
            var d = Buscar(dispositivos, objetivo);
            if (d is null)
            {
                Console.Error.WriteLine($"  ! {objetivo} no es un dispositivo conectado");
                fallos++;
                continue;
            }

            var r = await DeviceActions.ExpulsarAsync(d);
            Console.WriteLine(r.Ok
                ? $"  [OK ] {d.Display} expulsado"
                : $"  [MAL] {d.Display}: {r.Mensaje}");
            if (!r.Ok) fallos++;
        }

        return fallos == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- formatear

    private static async Task<int> FormatearAsync(string[] args)
    {
        var posicionales = args.Where(a => !a.StartsWith('-')).ToList();
        if (posicionales.Count == 0)
        {
            Console.Error.WriteLine("Uso: qbaswingmultitask formatear <letra> --si [--fs NTFS|exFAT|FAT32] [--etiqueta NOMBRE]");
            Console.Error.WriteLine("BORRA TODO. Hace falta --si para confirmar.");
            return 2;
        }

        if (!Hay(args, "--si", "--yes", "-y"))
        {
            Console.Error.WriteLine("Formatear borra todo el contenido. Repite con --si si estas seguro.");
            return 3;
        }

        var fs = ValorOpcion(args, "--fs", "--file-system") ?? "NTFS";
        var etiqueta = ValorOpcion(args, "--etiqueta", "--label");
        var completo = Hay(args, "--completo", "--full");
        var dispositivos = new DeviceManager().Scan();
        var fallos = 0;

        foreach (var objetivo in posicionales)
        {
            var d = Buscar(dispositivos, objetivo);
            if (d is null)
            {
                Console.Error.WriteLine($"  ! {objetivo} no es un dispositivo conectado");
                fallos++;
                continue;
            }

            var r = await DeviceActions.FormatearAsync(d, fs, etiqueta, rapido: !completo);
            Console.WriteLine(r.Ok
                ? $"  [OK ] {d.Display} formateado en {fs}"
                : $"  [MAL] {d.Display}: {r.Mensaje}");
            if (!r.Ok) fallos++;
        }

        return fallos == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ utiles

    private static DeviceInfo? Buscar(IReadOnlyList<DeviceInfo> lista, string objetivo)
    {
        var t = objetivo.Trim();
        foreach (var d in lista)
        {
            if (string.Equals(d.Root, t, StringComparison.OrdinalIgnoreCase)) return d;
            if (string.Equals(d.DriveLetter, t, StringComparison.OrdinalIgnoreCase)) return d;
            if (t.Length is 1 or 2 && d.DriveLetter is { Length: >= 2 } l &&
                string.Equals(l[..1], t.TrimEnd(':'), StringComparison.OrdinalIgnoreCase)) return d;
            if (string.Equals(d.Label, t, StringComparison.OrdinalIgnoreCase)) return d;
        }
        return null;
    }

    private static StructureMode EstructuraDe(string texto) => texto.ToLowerInvariant() switch
    {
        "plana" or "flat" => StructureMode.Plana,
        "extension" or "ext" => StructureMode.PorExtension,
        "tipo" or "type" => StructureMode.PorTipo,
        "fecha" or "date" => StructureMode.PorFecha,
        "origen" or "source" => StructureMode.PorCarpetaOrigen,
        "tamano" or "size" => StructureMode.PorTamano,
        "nombres" or "zero" => StructureMode.SoloNombres,
        _ => StructureMode.Plana
    };

    private static bool Hay(string[] args, params string[] claves) =>
        args.Any(a => claves.Contains(a, StringComparer.OrdinalIgnoreCase));

    private static string? ValorOpcion(string[] args, params string[] claves)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (claves.Contains(args[i], StringComparer.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static int Entero(string? texto, int porDefecto) =>
        int.TryParse(texto, out var n) ? n : porDefecto;

    private static DateTime? Fecha(string? texto) =>
        DateTime.TryParse(texto, new CultureInfo("es-ES"), DateTimeStyles.None, out var d) ? d : null;

    private static bool EsAyuda(string a) =>
        a is "ayuda" or "help" or "--help" or "-h" or "/?";

    private static string Recorta(string texto, int ancho) =>
        texto.Length <= ancho ? texto : "…" + texto[^(ancho - 1)..];

    private static int Version()
    {
        Console.WriteLine($"{Brand.Name} {Brand.Version} — {Brand.Tagline}");
        return 0;
    }

    private static int Desconocido(string comando)
    {
        Console.Error.WriteLine($"Comando desconocido: {comando}");
        Ayuda();
        return 2;
    }

    private static int AyudaCodigo() { Ayuda(); return 0; }

    private static void Ayuda()
    {
        Console.WriteLine($"{Brand.Name} {Brand.Version} — {Brand.Tagline}");
        Console.WriteLine();
        Console.WriteLine("Uso: qbaswingmultitask <comando> [opciones]");
        Console.WriteLine();
        Console.WriteLine("Comandos:");
        Console.WriteLine("  listar                                 Muestra los dispositivos y su datos fisicos");
        Console.WriteLine("  copiar <origen> <destino...>           Copia (lee una vez, escribe a todos)");
        Console.WriteLine("  mover  <origen> <destino...>           Copia y borra el origen tras verificar el hash");
        Console.WriteLine("  expulsar <letra|ruta> [...]            Expulsa el dispositivo con seguridad");
        Console.WriteLine("  formatear <letra> --si [--fs NTFS]     Formatea (BORRA TODO; exige --si)");
        Console.WriteLine("  version                                Muestra la version");
        Console.WriteLine();
        Console.WriteLine("Opciones de copiar/mover:");
        Console.WriteLine("  -v, --verificar      Verifica el hash SHA-256 (siempre activo)");
        Console.WriteLine("  -q, --quiet          Sin progreso por pantalla");
        Console.WriteLine("      --espejo         Borra del destino lo que no este en el origen");
        Console.WriteLine("      --estructura M   plana|extension|tipo|fecha|origen|tamano|nombres");
        Console.WriteLine("      --desde FECHA    Solo lo modificado desde esa fecha");
        Console.WriteLine("      --hasta FECHA    Solo lo modificado hasta esa fecha");
        Console.WriteLine("      --incluir P      Solo los nombres que casen el comodin (ej: *.mp4)");
        Console.WriteLine("      --excluir P      Los nombres que casen el comodin se saltan");
        Console.WriteLine("      --paralelo N     Ficheros a la vez (por defecto 1)");
        Console.WriteLine("      --libre MB       Espacio libre minimo que hay que dejar");
    }
}
