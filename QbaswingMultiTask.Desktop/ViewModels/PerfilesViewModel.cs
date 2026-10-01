using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using QbaswingMultiTask.Profiles;
using QbaswingMultiTask.Util;

namespace QbaswingMultiTask.Desktop;

/// <summary>
/// Pestana Perfiles (PLAN.md §2.6). Sustituye a las matrices .xcm del original:
/// cada perfil guarda origen, destinos, estructura, filtros de fecha y de
/// nombres, si mueve o espeja, y su horario (hora/dias, o al insertar una
/// unidad). Se guardan en JSON y se pueden importar/exportar.
/// </summary>
public sealed class PerfilesViewModel : Vm
{
    private readonly MainViewModel? _main;

    public PerfilesViewModel(MainViewModel? main = null)
    {
        _main = main;
        Perfiles = new ObservableCollection<Profile>(ProfileStore.Leer());

        NuevoCommand = new RelayCommand(Nuevo);
        GuardarCommand = new RelayCommand(Guardar);
        BorrarCommand = new RelayCommand(Borrar, () => Seleccionado is not null);
        DuplicarCommand = new RelayCommand(Duplicar, () => Seleccionado is not null);
        ExportarCommand = new RelayCommand(Exportar);
        ImportarCommand = new RelayCommand(Importar);
        AplicarCommand = new RelayCommand(Aplicar, () => Seleccionado is not null && _main is not null);

        if (Perfiles.Count > 0) Seleccionado = Perfiles[0];
    }

    public ObservableCollection<Profile> Perfiles { get; }

    public RelayCommand NuevoCommand { get; }
    public RelayCommand GuardarCommand { get; }
    public RelayCommand BorrarCommand { get; }
    public RelayCommand DuplicarCommand { get; }
    public RelayCommand ExportarCommand { get; }
    public RelayCommand ImportarCommand { get; }
    public RelayCommand AplicarCommand { get; }

    private Profile? _seleccionado;
    public Profile? Seleccionado
    {
        get => _seleccionado;
        set
        {
            if (!Set(ref _seleccionado, value)) return;
            CargarEditor();
            BorrarCommand.Avisar();
            DuplicarCommand.Avisar();
            AplicarCommand.Avisar();
        }
    }

    // ---------- editor ----------
    private string _nombre = "";
    public string Nombre { get => _nombre; set => Set(ref _nombre, value); }

    private string _origen = "";
    public string Origen { get => _origen; set => Set(ref _origen, value); }

    private string _destinos = "";
    public string Destinos { get => _destinos; set => Set(ref _destinos, value); }

    private string _estructura = "auto";
    public bool EsAuto { get => _estructura == "auto"; set { if (value) SetEstructura("auto"); } }
    public bool EsHdd { get => _estructura == "hdd"; set { if (value) SetEstructura("hdd"); } }
    public bool EsFlash { get => _estructura == "flash"; set { if (value) SetEstructura("flash"); } }
    private void SetEstructura(string v)
    {
        _estructura = v;
        Raise(nameof(EsAuto)); Raise(nameof(EsHdd)); Raise(nameof(EsFlash));
    }

    private string _incluir = "";
    public string Incluir { get => _incluir; set => Set(ref _incluir, value); }

    private string _excluir = "";
    public string Excluir { get => _excluir; set => Set(ref _excluir, value); }

    private string _expresion = "";
    public string Expresion { get => _expresion; set => Set(ref _expresion, value); }

    private bool _mover;
    public bool Mover { get => _mover; set => Set(ref _mover, value); }

    private bool _espejo;
    public bool Espejo { get => _espejo; set => Set(ref _espejo, value); }

    private bool _horarioActivo;
    public bool HorarioActivo { get => _horarioActivo; set => Set(ref _horarioActivo, value); }

    private string _horarioHora = "19:00";
    public string HorarioHora { get => _horarioHora; set => Set(ref _horarioHora, value); }

    private string _horarioDias = "";
    public string HorarioDias { get => _horarioDias; set => Set(ref _horarioDias, value); }

    private bool _porInsercion;
    public bool PorInsercion { get => _porInsercion; set => Set(ref _porInsercion, value); }

    private string _aviso = "los perfiles viven en %AppData%\\QbaswingMultiTask\\perfiles";
    public string Aviso { get => _aviso; set => Set(ref _aviso, value); }

    public string RutaPerfiles => ProfileStore.Ruta;

    private void CargarEditor()
    {
        var p = Seleccionado;
        Nombre = p?.Nombre ?? "";
        Origen = p?.Origen ?? "";
        Destinos = p is null ? "" : string.Join("  ", p.Destinos);
        SetEstructura(p?.Estructura ?? "auto");
        Incluir = p?.Incluir ?? "";
        Excluir = p?.Excluir ?? "";
        Expresion = p?.Expresion ?? "";
        Mover = p?.Mover ?? false;
        Espejo = p?.Espejo ?? false;
        HorarioActivo = p?.Horario.Activo ?? false;
        HorarioHora = p?.Horario.Hora ?? "19:00";
        HorarioDias = p is null ? "" : string.Join(" ", p.Horario.Dias.Select(AbrevDia));
        PorInsercion = p?.Horario.PorInsercion ?? false;
    }

    private void Nuevo()
    {
        var p = new Profile { Nombre = "Perfil " + (Perfiles.Count + 1) };
        Perfiles.Add(p);
        Seleccionado = p;
        Nombre = p.Nombre;
        Aviso = "perfil nuevo: ponle nombre, origen y destinos, y pulsa Guardar";
    }

    private void Guardar()
    {
        var p = Seleccionado;
        if (p is null) { Aviso = "no hay perfil seleccionado"; return; }

        p.Nombre = string.IsNullOrWhiteSpace(Nombre) ? "Sin nombre" : Nombre.Trim();
        p.Origen = Origen.Trim();
        p.Destinos = Destinos
            .Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        p.Estructura = _estructura;
        p.Incluir = Incluir.Trim();
        p.Excluir = Excluir.Trim();
        p.Expresion = Expresion.Trim();
        p.Mover = Mover;
        p.Espejo = Espejo;
        p.Horario.Activo = HorarioActivo;
        p.Horario.Hora = HorarioHora;
        p.Horario.Dias = LeerDias(HorarioDias);
        p.Horario.PorInsercion = PorInsercion;

        ProfileStore.Guardar(Perfiles);
        // Refresca la lista para que el nombre nuevo se vea al momento.
        var idx = Perfiles.IndexOf(p);
        if (idx >= 0) { Perfiles[idx] = p; Seleccionado = p; }
        Aviso = "perfil guardado: " + p.Nombre;
    }

    private void Borrar()
    {
        var p = Seleccionado;
        if (p is null) return;
        Perfiles.Remove(p);
        ProfileStore.Guardar(Perfiles);
        Seleccionado = Perfiles.FirstOrDefault();
        Aviso = "perfil borrado";
    }

    private void Duplicar()
    {
        var p = Seleccionado;
        if (p is null) return;
        var copia = new Profile
        {
            Nombre = p.Nombre + " (copia)",
            Origen = p.Origen,
            Destinos = new List<string>(p.Destinos),
            Estructura = p.Estructura,
            Desde = p.Desde, Hasta = p.Hasta,
            Incluir = p.Incluir, Excluir = p.Excluir, Expresion = p.Expresion,
            Mover = p.Mover, Espejo = p.Espejo,
            Horario = new Horario
            {
                Activo = p.Horario.Activo, Hora = p.Horario.Hora,
                Dias = new List<DayOfWeek>(p.Horario.Dias),
                PorInsercion = p.Horario.PorInsercion,
                EtiquetasInsercion = new List<string>(p.Horario.EtiquetasInsercion)
            }
        };
        Perfiles.Add(copia);
        ProfileStore.Guardar(Perfiles);
        Seleccionado = copia;
        Aviso = "perfil duplicado";
    }

    private void Exportar()
    {
        try
        {
            var ruta = Path.Combine(AppPaths.DataDir,
                $"perfiles-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.WriteAllText(ruta,
                JsonSerializer.Serialize(Perfiles.ToList(), new JsonSerializerOptions { WriteIndented = true }));
            Aviso = "perfiles exportados a " + ruta;
        }
        catch (Exception ex) { Aviso = "no se pudo exportar: " + ex.Message; }
    }

    private void Importar()
    {
        try
        {
            var ultimo = Directory.EnumerateFiles(AppPaths.DataDir, "perfiles-*.json")
                .OrderByDescending(f => f).FirstOrDefault();
            if (ultimo is null) { Aviso = "no hay ningun perfiles-*.json que importar"; return; }

            var leidos = JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(ultimo)) ?? new();
            foreach (var p in leidos)
                if (!Perfiles.Any(x => string.Equals(x.Nombre, p.Nombre, StringComparison.OrdinalIgnoreCase)))
                    Perfiles.Add(p);
            ProfileStore.Guardar(Perfiles);
            Aviso = $"importados {leidos.Count} perfil(es) de {Path.GetFileName(ultimo)}";
        }
        catch (Exception ex) { Aviso = "no se pudo importar: " + ex.Message; }
    }

    private void Aplicar()
    {
        if (Seleccionado is null || _main is null) return;
        _main.AplicarPerfil(Seleccionado);
        Aviso = "perfil aplicado a la pestana Copiar/Mover";
    }

    // ---------- dias ----------
    private static string AbrevDia(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "L", DayOfWeek.Tuesday => "M", DayOfWeek.Wednesday => "X",
        DayOfWeek.Thursday => "J", DayOfWeek.Friday => "V", DayOfWeek.Saturday => "S",
        _ => "D"
    };

    private static List<DayOfWeek> LeerDias(string texto)
    {
        var dias = new List<DayOfWeek>();
        if (string.IsNullOrWhiteSpace(texto)) return dias;
        foreach (var c in texto.ToUpperInvariant())
            switch (c)
            {
                case 'L': dias.Add(DayOfWeek.Monday); break;
                case 'M': dias.Add(DayOfWeek.Tuesday); break;
                case 'X': dias.Add(DayOfWeek.Wednesday); break;
                case 'J': dias.Add(DayOfWeek.Thursday); break;
                case 'V': dias.Add(DayOfWeek.Friday); break;
                case 'S': dias.Add(DayOfWeek.Saturday); break;
                case 'D': dias.Add(DayOfWeek.Sunday); break;
            }
        return dias.Distinct().ToList();
    }
}
