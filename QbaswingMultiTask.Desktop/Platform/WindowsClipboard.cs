using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace QbaswingMultiTask.Desktop.Platform;

/// <summary>
/// Portapapeles real de Windows con CF_HDROP (PLAN.md §2.1): asi el Explorador y
/// Total Commander ven los ficheros copiados aqui, y viceversa (Ctrl+C/X/V en
/// ambos sentidos). Se hace con P/Invoke porque Avalonia no expone CF_HDROP.
/// El "Preferred DropEffect" distingue copiar (1) de cortar (2).
/// </summary>
public static class WindowsClipboard
{
    private const uint CF_HDROP = 15;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint DragQueryFileAll = 0xFFFFFFFF;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode,
               EntryPoint = "DragQueryFileW")]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder? lpszFile, uint cch);

    /// <summary>Pone rutas en el portapapeles como CF_HDROP. Devuelve true si lo logro.</summary>
    public static bool PonerArchivos(IEnumerable<string> rutas, bool cortar)
    {
        var lista = new List<string>();
        foreach (var r in rutas)
            if (!string.IsNullOrWhiteSpace(r)) lista.Add(r);
        if (lista.Count == 0) return false;

        // Estructura DROPFILES (20 bytes en 64 bits): pFiles, pt, fNC, fWide.
        const int cabecera = 20;
        var bytes = cabecera;
        foreach (var r in lista) bytes += (r.Length + 1) * 2;
        bytes += 2; // nulo extra de cierre

        var h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
        if (h == IntPtr.Zero) return false;

        var p = GlobalLock(h);
        if (p == IntPtr.Zero) { GlobalFree(h); return false; }

        try
        {
            Marshal.WriteInt32(p, 0, cabecera); // pFiles
            Marshal.WriteInt32(p, 4, 0);        // pt.x
            Marshal.WriteInt32(p, 8, 0);        // pt.y
            Marshal.WriteInt32(p, 12, 0);       // fNC
            Marshal.WriteInt32(p, 16, 1);       // fWide = true

            var punta = IntPtr.Add(p, cabecera);
            foreach (var r in lista)
            {
                var b = Encoding.Unicode.GetBytes(r + "\0");
                Marshal.Copy(b, 0, punta, b.Length);
                punta = IntPtr.Add(punta, b.Length);
            }
            Marshal.WriteInt16(punta, 0);
        }
        finally { GlobalUnlock(h); }

        if (!OpenClipboard(IntPtr.Zero)) { GlobalFree(h); return false; }
        try
        {
            EmptyClipboard();
            if (SetClipboardData(CF_HDROP, h) == IntPtr.Zero) { GlobalFree(h); return false; }
            // Ya es del portapapeles: no se libera.

            PonerEfecto(cortar ? 2u : 1u); // 2 = mover, 1 = copiar
            return true;
        }
        finally { CloseClipboard(); }
    }

    private static void PonerEfecto(uint efecto)
    {
        var h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)4);
        if (h == IntPtr.Zero) return;
        var p = GlobalLock(h);
        if (p == IntPtr.Zero) { GlobalFree(h); return; }
        Marshal.WriteInt32(p, 0, (int)efecto);
        GlobalUnlock(h);

        var fmt = RegisterClipboardFormat("Preferred DropEffect");
        if (SetClipboardData(fmt, h) == IntPtr.Zero) GlobalFree(h);
    }

    /// <summary>Lee las rutas del portapapeles. <paramref name="cortar"/> dice si era un cortar.</summary>
    public static List<string> LeerArchivos(out bool cortar)
    {
        cortar = false;
        var res = new List<string>();
        if (!IsClipboardFormatAvailable(CF_HDROP)) return res;
        if (!OpenClipboard(IntPtr.Zero)) return res;

        try
        {
            var h = GetClipboardData(CF_HDROP);
            if (h == IntPtr.Zero) return res;

            var n = DragQueryFile(h, DragQueryFileAll, null, 0);
            for (uint i = 0; i < n; i++)
            {
                var largo = DragQueryFile(h, i, null, 0);
                if (largo == 0) continue;
                var sb = new StringBuilder((int)largo + 1);
                if (DragQueryFile(h, i, sb, (uint)sb.Capacity) > 0) res.Add(sb.ToString());
            }

            var fmt = RegisterClipboardFormat("Preferred DropEffect");
            if (fmt != 0 && IsClipboardFormatAvailable(fmt))
            {
                var he = GetClipboardData(fmt);
                if (he != IntPtr.Zero)
                {
                    var pe = GlobalLock(he);
                    if (pe != IntPtr.Zero)
                    {
                        var v = Marshal.ReadInt32(pe);
                        GlobalUnlock(he);
                        cortar = (v & 2) != 0;
                    }
                }
            }
        }
        finally { CloseClipboard(); }

        return res;
    }

    /// <summary>True si ahora mismo hay ficheros en el portapapeles.</summary>
    public static bool HayArchivos() => IsClipboardFormatAvailable(CF_HDROP);
}
