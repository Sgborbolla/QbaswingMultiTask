using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace QbaswingMultiTask.Devices;

/// <summary>Lo que paso al expulsar o formatear.</summary>
public sealed class DeviceActionResult
{
    public bool Ok { get; init; }
    public string Mensaje { get; init; } = "";
    public int Codigo { get; init; }

    public static DeviceActionResult Bien(string m) => new() { Ok = true, Mensaje = m };
    public static DeviceActionResult Mal(string m, int codigo = -1) =>
        new() { Ok = false, Mensaje = m, Codigo = codigo };
}

/// <summary>
/// Expulsar y formatear dispositivos. PLAN.md §3 (F2): Windows con
/// <c>mountvol X: /d</c>, Linux con <c>umount</c>, macOS con <c>diskutil eject</c>.
/// Aqui NUNCA se pasa por el shell: se usa <c>ArgumentList</c>, que es lo que
/// evita la inyeccion de comandos que §8.1 encontro de verdad en la version
/// anterior de este proyecto.
/// </summary>
public static class DeviceActions
{
    /// <summary>
    /// Expulsa la unidad con seguridad. Devuelve Ok=false con el motivo en claro
    /// si Windows no la puede soltar (ficheros abiertos, etc.).
    /// </summary>
    public static Task<DeviceActionResult> ExpulsarAsync(DeviceInfo d, CancellationToken ct = default)
    {
        if (d.IsSystem || d.Kind == DeviceKind.SystemDisk)
            return Task.FromResult(DeviceActionResult.Mal("no se expulsa el disco del sistema"));
        if (d.Root.StartsWith(@"\\", StringComparison.Ordinal))
            return Task.FromResult(DeviceActionResult.Mal("una ruta de red no se expulsa"));

        if (d.DriveLetter is not { Length: >= 2 })
        {
            // Volumen sin letra: se desmonta con mountvol por la ruta.
            if (OperatingSystem.IsWindows())
                return EjecutarAsync("mountvol.exe", new[] { d.Root.TrimEnd('\\'), "/p" }, ct);
            return Task.FromResult(DeviceActionResult.Mal("la unidad no tiene letra ni punto de montaje"));
        }

        var l = d.DriveLetter[..2];   // "E:"

        if (OperatingSystem.IsWindows())
        {
            // mountvol /d quita el punto de montaje: es la expulsion segura.
            return EjecutarAsync("mountvol.exe", new[] { l, "/d" }, ct);
        }
        if (OperatingSystem.IsLinux())
        {
            return EjecutarAsync("umount", new[] { d.Root.TrimEnd('/') }, ct);
        }
        if (OperatingSystem.IsMacOS())
        {
            return EjecutarAsync("diskutil", new[] { "eject", l.TrimEnd(':') }, ct);
        }

        return Task.FromResult(DeviceActionResult.Mal("sistema no soportado para expulsar"));
    }

    /// <summary>
    /// Formatea una unidad. PELIGROSO: borra todo. Solo para USB/memoria, y el
    /// que llama tiene que haber confirmado (PLAN.md F2: "formatear (confirmado)").
    /// </summary>
    public static Task<DeviceActionResult> FormatearAsync(
        DeviceInfo d, string sistemaFicheros = "NTFS", string? etiqueta = null, bool rapido = true,
        CancellationToken ct = default)
    {
        if (!d.IsUsb && d.Kind != DeviceKind.Removable)
            return Task.FromResult(DeviceActionResult.Mal("solo se formatean memorias y discos USB"));

        if (d.DriveLetter is not { Length: >= 2 } letra)
            return Task.FromResult(DeviceActionResult.Mal("la unidad no tiene letra: no se puede formatear"));
        var l = letra.TrimEnd('\\', ':');

        var fs = sistemaFicheros.ToUpperInvariant() switch
        {
            "EXFAT" => "exFAT",
            "FAT32" => "FAT32",
            "REFS" => "ReFS",
            _ => "NTFS"
        };

        if (OperatingSystem.IsWindows())
        {
            // Format-Volume es idempotente y no pregunta. Sin shell: ArgumentList.
            var etiquetaArg = string.IsNullOrWhiteSpace(etiqueta)
                ? ""
                : $" -NewFileSystemLabel {Ps(etiqueta)}";
            var rapidoArg = rapido ? " -Full:$false" : " -Full:$true";
            var script = $"Format-Volume -DriveLetter {l} -FileSystem {fs}{etiquetaArg}{rapidoArg} -Confirm:$false";
            return EjecutarAsync("powershell.exe",
                new[] { "-NoProfile", "-NonInteractive", "-Command", script }, ct);
        }
        if (OperatingSystem.IsLinux())
        {
            var programa = fs switch
            {
                "exFAT" => "mkfs.exfat",
                "FAT32" => "mkfs.vfat",
                _ => "mkfs.ntfs"
            };
            return EjecutarAsync(programa, new[] { d.Root.TrimEnd('/') }, ct);
        }
        if (OperatingSystem.IsMacOS())
        {
            return EjecutarAsync("diskutil", new[] { "eraseVolume", fs, etiqueta ?? "SIN-NOMBRE", l }, ct);
        }

        return Task.FromResult(DeviceActionResult.Mal("sistema no soportado para formatear"));
    }

    /// <summary>Comillas simples para PowerShell (duplicando las internas).</summary>
    private static string Ps(string texto) => "'" + texto.Replace("'", "''") + "'";

    private static async Task<DeviceActionResult> EjecutarAsync(
        string programa, IReadOnlyList<string> argumentos, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = programa,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in argumentos) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null) return DeviceActionResult.Mal($"no se pudo ejecutar {programa}");

            var salida = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            var error = await p.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);

            using var espera = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await p.WaitForExitAsync(espera.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { p.Kill(); } catch { }
                return DeviceActionResult.Mal($"{programa} no respondio en 60 s");
            }

            var texto = string.IsNullOrWhiteSpace(error) ? salida : error;
            texto = texto.Trim();
            return p.ExitCode == 0
                ? DeviceActionResult.Bien(texto.Length == 0 ? "hecho" : texto)
                : DeviceActionResult.Mal(texto.Length == 0 ? $"codigo {p.ExitCode}" : texto, p.ExitCode);
        }
        catch (Exception ex)
        {
            return DeviceActionResult.Mal(ex.Message);
        }
    }
}
