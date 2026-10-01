using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace QbaswingMultiTask.Installer;

/// <summary>
/// Instalador/desinstalador de QbaswingMultiTask (PLAN.md §6 y F10).
///
/// Es el mismo programa en los dos papeles: sin argumentos instala; con
/// <c>--desinstalar</c> desinstala. Al instalar se copia a si mismo dentro de la
/// carpeta de instalacion como <c>Desinstalar.exe</c>, para que el registro de
/// Windows apunte a un sitio que sigue existiendo aunque se borre el ZIP
/// (era justo el fallo del plan §8.1: "UninstallString apuntaba al ejecutable
/// dentro del ZIP").
///
/// Uso:
///   Instalar-QbaswingMultiTask.exe                 instala (pregunta)
///   Instalar-QbaswingMultiTask.exe --silencioso    instala sin preguntar
///   Instalar-QbaswingMultiTask.exe --dir CARPETA   elige la carpeta de destino
///   Desinstalar.exe [--silencioso]                 desinstala
/// </summary>
internal static class Program
{
    private const string Nombre = "QbaswingMultiTask";
    private const string Version = "1.0.0";
    private const string Editor = "Sgborbolla";
    private const string AppExe = "QbaswingMultiTask.exe";
    private const string ClaveDesinstalacion =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Nombre;

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = Nombre + " · instalador";

        var silencioso = args.Any(a => Igual(a, "--silencioso") || Igual(a, "/s") || Igual(a, "-s"));

        // El papel se decide por el nombre del ejecutable ADEMAS del argumento:
        // el registro de Windows llama a "Desinstalar.exe" a secas, sin argumentos,
        // y si solo mirase el argumento volveria a instalar en vez de desinstalar.
        var nombreExe = Path.GetFileName(Environment.ProcessPath ?? "");
        var desinstalar = args.Any(a => Igual(a, "--desinstalar") || Igual(a, "/desinstalar") ||
                                        Igual(a, "-desinstalar")) ||
                          nombreExe.StartsWith("desinstalar", StringComparison.OrdinalIgnoreCase);

        try
        {
            return desinstalar ? Desinstalar(silencioso) : Instalar(args, silencioso);
        }
        catch (Exception ex)
        {
            Error("No se pudo completar: " + ex.Message);
            Pausar(silencioso);
            return 1;
        }
    }

    // ------------------------------------------------------------------ instalar

    private static int Instalar(string[] args, bool silencioso)
    {
        Cabecera();

        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        exeDir = exeDir.TrimEnd('\\', '/');
        var origen = ResolverOrigen(exeDir);
        if (origen is null)
        {
            Error("No encuentro los ficheros de la aplicacion.");
            Console.WriteLine("Deja el instalador junto a la carpeta 'app' (la que trae " + AppExe + ").");
            Pausar(silencioso);
            return 2;
        }

        var destino = CarpetaDestino(args, silencioso);

        Console.WriteLine("Aplicacion : " + Nombre + " " + Version);
        Console.WriteLine("Origen     : " + origen);
        Console.WriteLine("Destino    : " + destino);
        Console.WriteLine();

        if (!silencioso && !Preguntar("¿Instalar ahora?", siPorDefecto: true))
        {
            Console.WriteLine("Instalacion cancelada.");
            Pausar(silencioso);
            return 0;
        }

        ReproducirMusica();

        Console.Write("Copiando ficheros... ");
        var copiados = CopiarArbol(origen, destino, exeDir);
        Console.WriteLine("ok (" + copiados + " ficheros)");

        // El propio instalador queda copiado como Desinstalar.exe en el destino.
        var desinstalador = Path.Combine(destino, "Desinstalar.exe");
        try
        {
            File.Copy(Environment.ProcessPath ?? Path.Combine(exeDir, "Instalar-QbaswingMultiTask.exe"),
                      desinstalador, overwrite: true);
            Console.WriteLine("Desinstalador: " + desinstalador);
        }
        catch (Exception ex)
        {
            Aviso("No se pudo copiar el desinstalador: " + ex.Message);
        }

        var appInstalada = Path.Combine(destino, AppExe);

        Console.Write("Creando accesos directos... ");
        var escritorio = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var menuInicio = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        CrearAccesoDirecto(Path.Combine(escritorio, Nombre + ".lnk"), appInstalada,
            "", destino, "QbaswingMultiTask", appInstalada + ",0");
        var carpetaMenu = Path.Combine(menuInicio, Nombre);
        Directory.CreateDirectory(carpetaMenu);
        CrearAccesoDirecto(Path.Combine(carpetaMenu, Nombre + ".lnk"), appInstalada,
            "", destino, "QbaswingMultiTask", appInstalada + ",0");
        CrearAccesoDirecto(Path.Combine(carpetaMenu, "Desinstalar " + Nombre + ".lnk"),
            desinstalador, "", destino, "Desinstalar QbaswingMultiTask", desinstalador + ",0");
        Console.WriteLine("ok");

        Console.Write("Registrando en Windows... ");
        RegistrarDesinstalacion(destino, desinstalador, appInstalada);
        Console.WriteLine("ok");

        PararMusica();

        Console.WriteLine();
        Console.WriteLine("Listo. " + Nombre + " quedo instalado en:");
        Console.WriteLine("  " + destino);
        Console.WriteLine();

        if (!silencioso && File.Exists(appInstalada) &&
            Preguntar("¿Ejecutar " + Nombre + " ahora?", siPorDefecto: true))
        {
            try { Process.Start(new ProcessStartInfo(appInstalada) { WorkingDirectory = destino }); }
            catch (Exception ex) { Aviso("No se pudo abrir: " + ex.Message); }
        }

        Pausar(silencioso);
        return 0;
    }

    /// <summary>Busca de donde salen los ficheros de la app: app\ junto al instalador, o el propio directorio.</summary>
    private static string? ResolverOrigen(string exeDir)
    {
        var app = Path.Combine(exeDir, "app");
        if (File.Exists(Path.Combine(app, AppExe))) return app;
        if (File.Exists(Path.Combine(exeDir, AppExe))) return exeDir;
        return null;
    }

    private static string CarpetaDestino(string[] args, bool silencioso)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (Igual(args[i], "--dir") || Igual(args[i], "/dir"))
                return Path.GetFullPath(args[i + 1]);

        var porDefecto = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Nombre);

        if (silencioso) return porDefecto;

        Console.WriteLine("Carpeta de instalacion (Entrar para la de por defecto):");
        Console.WriteLine("  " + porDefecto);
        Console.Write("> ");
        var linea = Console.ReadLine();
        return string.IsNullOrWhiteSpace(linea) ? porDefecto : Path.GetFullPath(linea.Trim().Trim('"'));
    }

    /// <summary>Copia recursiva. Devuelve cuantos ficheros copio.</summary>
    private static int CopiarArbol(string origen, string destino, string exeDir)
    {
        var n = 0;
        Directory.CreateDirectory(destino);

        foreach (var dir in Directory.GetDirectories(origen, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(origen, dir);
            Directory.CreateDirectory(Path.Combine(destino, rel));
        }

        foreach (var fichero in Directory.GetFiles(origen, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(origen, fichero);
            var nombre = Path.GetFileName(fichero);

            // No copiar el propio instalador ni un desinstalador viejo.
            if (string.Equals(Path.GetDirectoryName(rel), "", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetDirectoryName(rel), ".", StringComparison.OrdinalIgnoreCase))
            {
                if (nombre.StartsWith("Instalar-", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(nombre, "Desinstalar.exe", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var destinoFichero = Path.Combine(destino, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(destinoFichero)!);
            File.Copy(fichero, destinoFichero, overwrite: true);
            n++;
        }
        return n;
    }

    // -------------------------------------------------------------- desinstalar

    private static int Desinstalar(bool silencioso)
    {
        Cabecera();
        Console.WriteLine("Desinstalando " + Nombre + "...");

        var dir = RegLeer(ClaveDesinstalacion, "InstallLocation");
        if (string.IsNullOrWhiteSpace(dir))
            dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        dir = (dir ?? "").TrimEnd('\\', '/');

        if (!silencioso && !Preguntar("¿Seguro que quieres desinstalar " + Nombre + "?", siPorDefecto: false))
        {
            Console.WriteLine("Desinstalacion cancelada.");
            Pausar(silencioso);
            return 0;
        }

        // Cerrar el programa si esta abierto, para poder borrar sus ficheros.
        try
        {
            foreach (var p in Process.GetProcessesByName(Nombre))
            {
                try { p.CloseMainWindow(); p.WaitForExit(3000); if (!p.HasExited) p.Kill(); }
                catch { }
            }
        }
        catch { }

        Console.Write("Quitando accesos directos... ");
        BorrarSiExiste(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Nombre + ".lnk"));
        var carpetaMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Nombre);
        try { if (Directory.Exists(carpetaMenu)) Directory.Delete(carpetaMenu, recursive: true); } catch { }
        Console.WriteLine("ok");

        Console.Write("Quitando el registro... ");
        try { RegBorrarArbol(ClaveDesinstalacion); } catch { }
        Console.WriteLine("ok");

        Console.WriteLine();
        Console.WriteLine(Nombre + " desinstalado.");

        if (!silencioso)
        {
            Console.Write("Pulsa Entrar para terminar y borrar la carpeta...");
            Console.ReadLine();
        }

        // El propio Desinstalar.exe esta en uso mientras corre: se deja el borrado
        // de la carpeta a un cmd oculto que espera a que este proceso termine.
        ProgramarBorradoCarpeta(dir);
        return 0;
    }

    /// <summary>Deja a un cmd oculto el borrado de la carpeta, para no chocar con el exe en uso.</summary>
    private static void ProgramarBorradoCarpeta(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
        try
        {
            var guion = Path.Combine(Path.GetTempPath(), "qbaswing-desinstalar.cmd");
            File.WriteAllText(guion,
                "@echo off\r\n" +
                "timeout /t 2 /nobreak > nul\r\n" +
                "rmdir /s /q \"" + dir + "\"\r\n" +
                "del \"%~f0\"\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + guion + "\"")
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            });
        }
        catch { }
    }

    // --------------------------------------------------------------- registro

    private static void RegistrarDesinstalacion(string dir, string desinstalador, string appExe)
    {
        RegEscribir(ClaveDesinstalacion, new Dictionary<string, string>
        {
            ["DisplayName"] = Nombre,
            ["DisplayVersion"] = Version,
            ["Publisher"] = Editor,
            ["InstallLocation"] = dir,
            ["DisplayIcon"] = appExe + ",0",
            ["UninstallString"] = desinstalador,
            ["QuietUninstallString"] = "\"" + desinstalador + "\" --silencioso"
        });
    }

    private static readonly IntPtr HkeyCurrentUser = new(unchecked((int)0x80000001));
    private const int KeyWrite = 0x20006;
    private const int RegSz = 1;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegCreateKeyEx(IntPtr hKey, string subKey, int reserved,
        string? clase, int opciones, int sam, IntPtr seguridad, out IntPtr resultado, out int disposicion);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegSetValueEx(IntPtr hKey, string nombre, int reservado,
        int tipo, byte[] datos, int tam);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegCloseKey(IntPtr hKey);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegDeleteTree(IntPtr hKey, string subClave);

    private static bool RegEscribir(string subClave, Dictionary<string, string> valores)
    {
        var r = RegCreateKeyEx(HkeyCurrentUser, subClave, 0, null, 0, KeyWrite,
            IntPtr.Zero, out var h, out _);
        if (r != 0) return false;
        try
        {
            foreach (var kv in valores)
            {
                var bytes = Encoding.Unicode.GetBytes(kv.Value + "\0");
                RegSetValueEx(h, kv.Key, 0, RegSz, bytes, bytes.Length);
            }
            return true;
        }
        finally { RegCloseKey(h); }
    }

    private static void RegBorrarArbol(string subClave)
    {
        var r = RegDeleteTree(HkeyCurrentUser, subClave);
        _ = r; // si no existia, no pasa nada
    }

    private const int KeyRead = 0x20019;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegOpenKeyEx(IntPtr hKey, string subClave, int opciones,
        int sam, out IntPtr resultado);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegQueryValueEx(IntPtr hKey, string nombre, IntPtr reservado,
        out int tipo, byte[]? datos, ref int tam);

    /// <summary>Lee un valor de texto del registro. Devuelve null si no esta.</summary>
    private static string? RegLeer(string subClave, string valor)
    {
        if (RegOpenKeyEx(HkeyCurrentUser, subClave, 0, KeyRead, out var h) != 0) return null;
        try
        {
            var tam = 0;
            if (RegQueryValueEx(h, valor, IntPtr.Zero, out _, null, ref tam) != 0 || tam <= 0) return null;
            var buf = new byte[tam];
            if (RegQueryValueEx(h, valor, IntPtr.Zero, out _, buf, ref tam) != 0) return null;
            return Encoding.Unicode.GetString(buf).TrimEnd('\0');
        }
        finally { RegCloseKey(h); }
    }

    // ------------------------------------------------------------- accesos directos

    private static void CrearAccesoDirecto(string rutaLnk, string destino, string argumentos,
        string dirTrabajo, string descripcion, string icono)
    {
        try
        {
            var tipo = Type.GetTypeFromProgID("WScript.Shell");
            if (tipo is null) return;
            dynamic shell = Activator.CreateInstance(tipo)!;
            dynamic acceso = shell.CreateShortcut(rutaLnk);
            acceso.TargetPath = destino;
            acceso.Arguments = argumentos;
            acceso.WorkingDirectory = dirTrabajo;
            acceso.Description = descripcion;
            acceso.IconLocation = icono;
            acceso.Save();
        }
        catch { /* sin acceso directo, la instalacion sigue siendo valida */ }
    }

    // ------------------------------------------------------------------- musica

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string orden, StringBuilder? respuesta,
        int tamRespuesta, IntPtr ventana);

    private static bool _musicaAbierta;

    private static void ReproducirMusica()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("musica.m4a");
            if (stream is null) return;

            var tmp = Path.Combine(Path.GetTempPath(), "qbaswing-instalacion.m4a");
            using (var f = File.Create(tmp)) stream.CopyTo(f);

            mciSendString("open \"" + tmp + "\" type mpegvideo alias qbmus", null, 0, IntPtr.Zero);
            mciSendString("play qbmus", null, 0, IntPtr.Zero);
            _musicaAbierta = true;
        }
        catch { /* si el sistema no reproduce m4a, se sigue sin musica */ }
    }

    private static void PararMusica()
    {
        if (!_musicaAbierta) return;
        try
        {
            mciSendString("stop qbmus", null, 0, IntPtr.Zero);
            mciSendString("close qbmus", null, 0, IntPtr.Zero);
        }
        catch { }
        finally { _musicaAbierta = false; }
    }

    // -------------------------------------------------------------------- consola

    private static void Cabecera()
    {
        Console.WriteLine();
        Console.WriteLine("  " + Nombre + " " + Version);
        Console.WriteLine("  ------------------------------------");
        Console.WriteLine();
    }

    private static bool Preguntar(string pregunta, bool siPorDefecto)
    {
        Console.Write(pregunta + (siPorDefecto ? " [S/n] " : " [s/N] "));
        var linea = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(linea)) return siPorDefecto;
        var l = linea.Trim().ToLowerInvariant();
        return l == "s" || l == "si" || l == "y" || l == "yes";
    }

    private static void Pausar(bool silencioso)
    {
        if (silencioso) return;
        Console.WriteLine();
        Console.Write("Pulsa Entrar para salir...");
        Console.ReadLine();
    }

    private static void Error(string mensaje)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("ERROR: " + mensaje);
        Console.ResetColor();
    }

    private static void Aviso(string mensaje) => Console.WriteLine("aviso: " + mensaje);

    private static void BorrarSiExiste(string ruta)
    {
        try { if (File.Exists(ruta)) File.Delete(ruta); } catch { }
    }

    private static bool Igual(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
