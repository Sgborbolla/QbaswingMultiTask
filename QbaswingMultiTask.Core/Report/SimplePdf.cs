using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace QbaswingMultiTask.Report;

/// <summary>
/// Generador de PDF minimo, sin dependencias. PLAN.md §2.8 y §3 piden "Guardar
/// PDF" del resumen y de las estadisticas. Escribe un A4 con lineas de texto
/// (Helvetica), saltando de pagina solo. Nada de librerias externas: en esta
/// maquina no hay red para bajarlas.
/// </summary>
public static class SimplePdf
{
    /// <summary>Anchura util en puntos (A4 = 595 x 842).</summary>
    private const double Ancho = 595;
    private const double Alto = 842;
    private const double Margen = 48;
    private const double Interlinea = 14;

    /// <summary>Crea el PDF y devuelve los bytes.</summary>
    public static byte[] Crear(string titulo, IEnumerable<string> lineas, string? pie = null)
    {
        var paginas = Paginar(titulo, lineas, pie);
        return Escribir(paginas);
    }

    /// <summary>Crea el PDF y lo guarda.</summary>
    public static void Guardar(string ruta, string titulo, IEnumerable<string> lineas, string? pie = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllBytes(ruta, Crear(titulo, lineas, pie));
    }

    private static List<string> Paginar(string titulo, IEnumerable<string> lineas, string? pie)
    {
        var contenido = new List<string>();
        var porPagina = (int)((Alto - 2 * Margen - 2 * Interlinea) / Interlinea);
        var actual = new List<string>();
        var n = 0;

        void Cerrar()
        {
            if (actual.Count == 0) return;
            contenido.Add(string.Join("\n", actual));
            actual = new List<string>();
        }

        void Cabecera(int pagina)
        {
            actual.Add($"BT /F1 15 Tf {Margen:0.#} {(Alto - Margen):0.#} Td ({Escapar(titulo)}) Tj ET");
            actual.Add($"BT /F1 9 Tf {Margen:0.#} {(Alto - Margen - 14):0.#} Td " +
                       $"({Escapar("QbaswingMultiTask · pagina " + pagina)}) Tj ET");
        }

        var pag = 1;
        Cabecera(pag);
        foreach (var linea in lineas)
        {
            n++;
            if (actual.Count >= porPagina + 2)
            {
                Cerrar();
                pag++;
                Cabecera(pag);
            }
            var y = Alto - Margen - 28 - (actual.Count - 2) * Interlinea;
            actual.Add($"BT /F1 10 Tf {Margen:0.#} {y:0.#} Td ({Escapar(linea)}) Tj ET");
        }

        if (pie is { Length: > 0 })
        {
            var y = Margen - 10;
            actual.Add($"BT /F1 9 Tf {Margen:0.#} {y:0.#} Td ({Escapar(pie)}) Tj ET");
        }
        Cerrar();
        if (contenido.Count == 0) contenido.Add("");
        return contenido;
    }

    private static byte[] Escribir(List<string> paginas)
    {
        // Objetos: 1 catalogo, 2 paginas, 3 fuente, luego por pagina (contenido, pagina).
        var sb = new StringBuilder();
        var offsets = new List<int>();
        sb.Append("%PDF-1.4\n");

        var objeto = 1;
        void Anotar(string cuerpo)
        {
            offsets.Add(sb.Length);
            sb.Append(objeto++).Append(" 0 obj\n").Append(cuerpo).Append("\nendobj\n");
        }

        Anotar("<< /Type /Catalog /Pages 2 0 R >>");

        var idsPagina = new List<int>();
        for (var i = 0; i < paginas.Count; i++) idsPagina.Add(4 + i * 2);
        Anotar("<< /Type /Pages /Kids [" +
               string.Join(" ", idsPagina.ConvertAll(id => $"{id} 0 R")) +
               "] /Count " + paginas.Count + " >>");

        Anotar("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        foreach (var (texto, i) in ConIndice(paginas))
        {
            var flujo = Encoding.Latin1.GetBytes(texto);
            var idContenido = 4 + i * 2;
            var idPagina = idContenido + 1;

            offsets.Add(sb.Length);
            sb.Append(objeto++).Append(" 0 obj\n");
            sb.Append("<< /Length ").Append(flujo.Length).Append(" >>\nstream\n");
            sb.Append(texto).Append("\nendstream\nendobj\n");

            offsets.Add(sb.Length);
            sb.Append(objeto++).Append(" 0 obj\n");
            sb.Append("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ")
              .Append(Ancho.ToString("0.#", CultureInfo.InvariantCulture)).Append(' ')
              .Append(Alto.ToString("0.#", CultureInfo.InvariantCulture))
              .Append("] /Resources << /Font << /F1 3 0 R >> >> /Contents ")
              .Append(idContenido).Append(" 0 R >>\nendobj\n");
        }

        var inicioXref = sb.Length;
        sb.Append("xref\n0 ").Append(objeto).Append('\n');
        sb.Append("0000000000 65535 f \n");
        foreach (var off in offsets)
            sb.Append(off.ToString("0000000000")).Append(" 00000 n \n");
        sb.Append("trailer\n<< /Size ").Append(objeto).Append(" /Root 1 0 R >>\n");
        sb.Append("startxref\n").Append(inicioXref).Append("\n%%EOF\n");

        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private static IEnumerable<(string, int)> ConIndice(List<string> paginas)
    {
        for (var i = 0; i < paginas.Count; i++) yield return (paginas[i], i);
    }

    /// <summary>Escapa lo que el PDF no admite y quita acentos raros.</summary>
    private static string Escapar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            switch (c)
            {
                case '(': sb.Append("\\("); break;
                case ')': sb.Append("\\)"); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append(' '); break;
                case '\r': break;
                default:
                    sb.Append(c <= 255 ? c : '?');
                    break;
            }
        }
        return sb.ToString();
    }
}
