using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using QbaswingMultiTask.App;
using QbaswingMultiTask.Util;

namespace QbaswingMultiTask.Desktop;

/// <summary>Pestana Ajustes (PLAN.md §2.7). Cambiar el tema es en caliente.</summary>
public sealed class AjustesViewModel : Vm
{
    public AjustesViewModel()
    {
        var s = SettingsStore.Actual;
        _tamanoLetra = s.TamanoLetra;
        _bufferMb = s.BufferMb;
        _verificar = s.Verificar;
        _minLibreMb = s.MinLibreMb;
        _umbralLentoMb = s.UmbralLentoMb;
        _indiceMinutos = s.IndiceMinutos;
        _copiarFechas = s.CopiarFechas;
        _copiarAtributos = s.CopiarAtributos;
        _paralelo = s.Paralelo;
        _teclasM2 = s.TeclasM2;
        _umbralSeries = s.UmbralSeries * 100;
        _exclusiones = string.Join(Environment.NewLine, s.Exclusiones);
        _idioma = s.Idioma;
        _tema = s.Tema;

        GuardarCommand = new RelayCommand(Guardar);
        CancelarCommand = new RelayCommand(Cancelar);
        AbrirDatosCommand = new RelayCommand(AbrirDatos);
        MigrarCommand = new RelayCommand(Migrar);
    }

    public RelayCommand GuardarCommand { get; }
    public RelayCommand CancelarCommand { get; }
    public RelayCommand AbrirDatosCommand { get; }
    public RelayCommand MigrarCommand { get; }

    /// <summary>Se avisa a la ventana para que repinte el tema al momento.</summary>
    public event Action<Settings>? Guardado;

    private string _tema;
    public bool TemaOscuro { get => _tema == "oscuro"; set { if (value) { _tema = "oscuro"; Raise(nameof(TemaOscuro)); Raise(nameof(TemaClaro)); } } }
    public bool TemaClaro { get => _tema == "claro"; set { if (value) { _tema = "claro"; Raise(nameof(TemaOscuro)); Raise(nameof(TemaClaro)); } } }

    private int _tamanoLetra;
    public int TamanoLetra { get => _tamanoLetra; set { if (Set(ref _tamanoLetra, value)) Raise(nameof(TamanoLetraTexto)); } }
    public string TamanoLetraTexto => $"{TamanoLetra} pt";

    private int _bufferMb;
    public int BufferMb { get => _bufferMb; set => Set(ref _bufferMb, value); }

    private bool _verificar;
    public bool Verificar { get => _verificar; set => Set(ref _verificar, value); }

    private long _minLibreMb;
    public long MinLibreMb { get => _minLibreMb; set => Set(ref _minLibreMb, value); }

    private double _umbralLentoMb;
    public double UmbralLentoMb { get => _umbralLentoMb; set => Set(ref _umbralLentoMb, value); }

    private int _indiceMinutos;
    public int IndiceMinutos { get => _indiceMinutos; set => Set(ref _indiceMinutos, value); }

    public bool IndiceAuto => IndiceMinutos > 0;

    private bool _copiarFechas;
    public bool CopiarFechas { get => _copiarFechas; set => Set(ref _copiarFechas, value); }

    private bool _copiarAtributos;
    public bool CopiarAtributos { get => _copiarAtributos; set => Set(ref _copiarAtributos, value); }

    private int _paralelo;
    public int Paralelo { get => _paralelo; set => Set(ref _paralelo, value); }

    private bool _teclasM2;
    public bool TeclasM2 { get => _teclasM2; set => Set(ref _teclasM2, value); }

    private double _umbralSeries;
    public double UmbralSeries { get => _umbralSeries; set { if (Set(ref _umbralSeries, value)) Raise(nameof(UmbralSeriesTexto)); } }
    public string UmbralSeriesTexto => $"{UmbralSeries:0} %";

    private string _idioma;
    public bool IdiomaEspanol { get => _idioma == "es"; set { if (value) { _idioma = "es"; Raise(nameof(IdiomaEspanol)); Raise(nameof(IdiomaIngles)); } } }
    public bool IdiomaIngles { get => _idioma == "en"; set { if (value) { _idioma = "en"; Raise(nameof(IdiomaEspanol)); Raise(nameof(IdiomaIngles)); } } }

    private string _exclusiones;
    public string ExclusionesTexto { get => _exclusiones; set => Set(ref _exclusiones, value); }

    private string _aviso = "";
    public string Aviso { get => _aviso; set => Set(ref _aviso, value); }

    public string CarpetaDatos => AppPaths.DataDir;

    private void Guardar()
    {
        var s = SettingsStore.Actual.Copia();
        s.Tema = _tema;
        s.TamanoLetra = Math.Clamp(_tamanoLetra, 9, 22);
        s.BufferMb = Math.Max(0, _bufferMb);
        s.Verificar = _verificar;
        s.MinLibreMb = Math.Max(0, _minLibreMb);
        s.UmbralLentoMb = Math.Max(0.5, _umbralLentoMb);
        s.IndiceMinutos = Math.Max(0, _indiceMinutos);
        s.CopiarFechas = _copiarFechas;
        s.CopiarAtributos = _copiarAtributos;
        s.Paralelo = Math.Clamp(_paralelo, 1, 8);
        s.TeclasM2 = _teclasM2;
        s.UmbralSeries = Math.Clamp(_umbralSeries, 10, 100) / 100.0;
        s.Idioma = _idioma;
        s.Exclusiones = _exclusiones
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

        SettingsStore.Guardar(s);
        Guardado?.Invoke(s);
        Aviso = "ajustes guardados";
    }

    private void Cancelar()
    {
        var s = SettingsStore.Actual;
        _tamanoLetra = s.TamanoLetra; _bufferMb = s.BufferMb; _verificar = s.Verificar;
        _minLibreMb = s.MinLibreMb; _umbralLentoMb = s.UmbralLentoMb;
        _indiceMinutos = s.IndiceMinutos; _copiarFechas = s.CopiarFechas;
        _copiarAtributos = s.CopiarAtributos; _paralelo = s.Paralelo;
        _teclasM2 = s.TeclasM2; _umbralSeries = s.UmbralSeries * 100;
        _exclusiones = string.Join(Environment.NewLine, s.Exclusiones);
        _idioma = s.Idioma; _tema = s.Tema;
        Raise(nameof(TamanoLetra)); Raise(nameof(BufferMb)); Raise(nameof(Verificar));
        Raise(nameof(MinLibreMb)); Raise(nameof(UmbralLentoMb)); Raise(nameof(IndiceMinutos));
        Raise(nameof(CopiarFechas)); Raise(nameof(CopiarAtributos)); Raise(nameof(Paralelo));
        Raise(nameof(TeclasM2)); Raise(nameof(UmbralSeries)); Raise(nameof(ExclusionesTexto));
        Raise(nameof(IdiomaEspanol)); Raise(nameof(IdiomaIngles));
        Raise(nameof(TemaOscuro)); Raise(nameof(TemaClaro));
        Aviso = "cambios descartados";
    }

    private void AbrirDatos()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppPaths.DataDir, UseShellExecute = true
            });
        }
        catch (Exception ex) { Aviso = "no se pudo abrir la carpeta: " + ex.Message; }
    }

    private void Migrar()
    {
        if (!AppPaths.HayDatosAntiguos()) { Aviso = "no hay datos antiguos que migrar"; return; }
        try
        {
            var copiados = 0;
            foreach (var origen in Directory.EnumerateFiles(AppPaths.LegacyRoot, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(AppPaths.LegacyRoot, origen);
                var destino = Path.Combine(AppPaths.DataDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
                if (!File.Exists(destino)) { File.Copy(origen, destino, false); copiados++; }
            }
            Aviso = $"migrados {copiados} fichero(s) desde la carpeta antigua";
        }
        catch (Exception ex) { Aviso = "la migracion fallo: " + ex.Message; }
    }
}
