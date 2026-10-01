using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QbaswingMultiTask.Sync;

/// <summary>
/// Comparacion de nombres por bigrams, como el M2 original (§2.2: "orden de
/// ficheros + bigrams", umbral 75 %). Sirve para emparejar "SERIE A cap01" con
/// "serieA_cap01-rip" sin confundirlo con "cap02".
/// </summary>
public static class SeriesSynchronizer
{
    /// <summary>Parecido entre 0 y 1. No distingue mayusculas ni la extension.</summary>
    public static double Similitud(string a, string b)
    {
        var x = Normalizar(a);
        var y = Normalizar(b);
        if (x.Length == 0 || y.Length == 0) return 0;
        if (x == y) return 1;

        var ba = Bigrams(x);
        var bb = Bigrams(y);
        if (ba.Count == 0 || bb.Count == 0) return 0;

        var comunes = ba.Intersect(bb).Count();
        var union = ba.Union(bb).Count();
        return union == 0 ? 0 : (double)comunes / union;
    }

    public static bool EsParecido(string a, string b, double umbral = 0.75) =>
        Similitud(a, b) >= umbral;

    /// <summary>Nombre en minusculas, sin extension y solo con letras y numeros.</summary>
    public static string Normalizar(string nombre)
    {
        var sinExt = nombre;
        var punto = nombre.LastIndexOf('.');
        if (punto > 0) sinExt = nombre[..punto];

        var sb = new StringBuilder(sinExt.Length);
        foreach (var c in sinExt.ToLowerInvariant())
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    private static HashSet<string> Bigrams(string s)
    {
        var set = new HashSet<string>();
        if (s.Length == 1) { set.Add(s); return set; }
        for (var i = 0; i < s.Length - 1; i++)
            set.Add(s.Substring(i, 2));
        return set;
    }
}
