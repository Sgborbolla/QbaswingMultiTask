# PLAN — QbaswingMultiTask

> Especificación maestra del copiador/sincronizador "todo en uno".
> Reutiliza y renombra QBasPaquete. Objetivo: **un solo programa con todas las
> funciones de los 7 programas originales analizados**, entregado en un ZIP con
> instalador para Windows.

---

## 1. Identidad

| Dato | Valor |
|---|---|
| Nombre comercial | **QbaswingMultiTask** |
| Nombre exe (Desktop) | `QbaswingMultiTask.exe` |
| Nombre exe (CLI) | `qbaswingmultitask.exe` |
| Carpeta de datos | `%AppData%\QbaswingMultiTask` (Android/Termux `qbaswing-multitask`) |
| Tagline (pendiente de fijar) | "Copia, sincroniza, escanea. Todo en uno." *(candidato)* |
| Plataforma | Windows (principal) + Linux + macOS (mantener lo que ya se compila) |

### Paleta bandera (tema oscuro por defecto)

```
CELESTE   #41A5F5   progreso, resaltado, iconos, ☑
AZUL      #0E4FA6   barras llenas, marcos marca "destino", acentos
ROJO      #C8102E   punta de progreso, errores fuertes, marca
BLANCO    #FFFFFF   texto principal
NAVY      #0B1626   fondo general de ventana
TARJETA   #15233B   fondo de cada rectángulo/pantalla interna
BORDE     #243452   contorno de rectángulo inactivo
SILVER    #7A8AA6   texto secundario (tamaños, fechas)
```

Reglas de uso:
- Rectángulo inactivo: borde `BORDE`, icono `CELESTE`.
- Rectángulo marcado destino: borde `AZUL` 2px + casilla ☑ `CELESTE`.
- Rectángulo copiando: borde `CELESTE`.
- Rectángulo con error: borde `ROJO`.
- Barra de copia (por rectángulo): carril `NAVY`, relleno `CELESTE`, punta triangular `ROJO`.
- Barra de llenado/capacidad del disco: relleno `AZUL` sobre `NAVY` (siempre quieta).
- Barra general: la misma bandera, más ancha.
- Ok / Aviso / Error: `#2ECC71` / `#F5B041` / `ROJO`.

Barra de copia terminada: filete rojo delgado en la esquina derecha (100% legible sin leer el %).

---

## 2. Pantallas (7 pestañas + resumen)

```
╔═══ ════════════════════════════════════════════════════════════════╗
║ [ Copiar/Mover ] [ Sincronizador ] [ Escáner ] [ Estadísticas ]    ║
║ [ Errores ] [ Perfiles ] [ Ajustes ]                               ║
╚════════════════════════════════════════════════════════════════════╝
```

### 2.1 Pestaña "Copiar/Mover" (ya cerrada)

```
┌──────────────────────────────────────────────────────────────────────┐
│ QbaswingMultiTask  [ ☰ ]  [Lector primero] [Todo] [Insertar] [..]   │
├───────────────────────────┬──────────────────────────────────────────┤
│ EXPLORADOR (angosto)      │  ☑        ☑        ☐        ☐           │
│  ▲ Este equipo            │ ┌────────┐ ┌────────┐ ┌────────┐ ┌────┐ │
│    ▼ Disco local (C:)     │ │ ▉ E:   │ │ ▉ F:   │ │ ▉ G:   │ │ ▉ C│ │
│      ▼ Descargas          │ │ PENDRA │ │ PAQUE  │ │ NAS    │ │ ...│ │
│        ├ vídeo ...        │ │ 64–22% │ │128–55% │ │ 1T–75% │ │    │ │
│    ▼ PENDRA (E:)          │ │ ▓▓▓▓⌃░ │ │ ▓▓░░░░ │ │ —      │ │    │ │
│    ▼ PAQUE (F:)           │ └────────┘ └────────┘ └────────┘ └────┘ │
│    ▼ NAS (G:)             │ [Estructura ●HDD ○FLASH] [Fecha __→__]  │
├───────────────────────────┴──────────────────────────────────────────┤
│ Destinos ☑: E: PENDRA · F: PAQUE   [▶ Copiar][◈ Verificar]          │
│ [↺ Reintentar][⧉ Espejo][⏸][⏹]     ────────────────────────────── │
│ BARRA GENERAL ▓▓▓▓▓▓░░ 38% · 3.212 ficheros · 12,4 GB · restante    │
└──────────────────────────────────────────────────────────────────────┘
```

Decisiones fijadas:
- Izquierda angosta = **Explorador de Windows**: raíz "Este equipo", discos como nodos dentro del árbol (NO mini-discos aquí).
- Derecha = **rectángulos de dispositivos**, cada uno con **casilla ☑ arriba**.
- **Solo se copia a los rectángulos marcados** (arrastrar o pegar).
- **Arrastrar y soltar**: en el programa y con Windows (Explorer/TC/etc.), en ambos sentidos.
- **Copiar/Cortar + Pegar** (Ctrl+C/X/V): portapapeles real de Windows (`IDataObject` + `CF_HDROP`), ida y vuelta con el Explorador.
  - Con rectángulos marcados → Ctrl+V = **multipaste a todos**.
  - Sin marcados → pega a la carpeta abierta en la izquierda.
  - Cortar+Pegar = **mover con verificación de hash** (no borra origen si no cuadra).
- Cada rectángulo: **barra propia** + **barra general** abajo (suma de todas).

### 2.2 Pestaña "Sincronizador" (ya cerrada) — lógica M2Sincronization

```
┌ [Sincronizador] ─────────────────────────────────────────────────────┐
│  PANEL IZQUIERDO                       PANEL DERECHO                 │
│ [C:][D:][E:][F:][G:]                  [C:][D:][E:][F:][G:]          │
│ E:\Paquetes [▾]                        F:\Paquetes_entr. [▾]         │
│ ┌─────┬───────────────┐                ┌─────┬─────────────────┐     │
│ │▲ E: │ SERIE A ✓12   │                │▲ F: │ SERIE A ●3      │     │
│ │  ▼E:\Paquetes  ..   │                │  ▼F:\Paquetes_entr.. │     │
│ │     └ serieA  ✓     │                │      └ serieA  =     │     │
│ │  ▼ F:               │                │  ▼ G:  ...           │     │
│ └─────┴───────────────┘                └─────┴─────────────────┘     │
│ Umbral 75% [≡Alinear][≡Alinear nuevo][∿Desalinear][Desal. todos]    │
│ [∑Revisar]  Vista: se copiarán 8 episodios · 2,1 GB                  │
│ [▶Aplicar]    se moverá el paquete a "ya copiados"                   │
└──────────────────────────────────────────────────────────────────────┘
```

Decisiones fijadas:
- Dos paneles iguales tipo **Total Commander**: **mini-discos arriba** (clic cambia el panel a esa unidad, estilo TC) + **árbol expandible/contraíble** (mini-explorador de Windows).
- Paneles **independientes, abiertos por raíz**; sin roles forzados.
- Marcado de coincidencias: `✓` ya lo tienes · `●` nuevo (se copia) · `=` repetido (se ignora).
- Umbral fijo **75 %**, orden de ficheros + bigrams (lo que ya hace `SeriesSynchronizer`).
- **Teclas del M2 mantenidas**: `Ctrl+A` alinear · `Ctrl+S` alinear automático · `Ctrl+U`/`Ctrl+E` desalinear · `Ctrl+T` desalinear todos · `Entrar` aplicar.

### 2.3 Pestaña "Escáner" — duplicados / faltantes / búsqueda

```
┌ [Escáner] ────────────────────────────────────────────────────────────┐
│ [Duplicados] [Faltantes] [Buscar: paquete_2026……] [🔎]                 │
│ Discos: [☑ C:][☑ E:][☐ F:]  [▶ Escanear]      índice ▓▓▓ 45%           │
│ ┌ fichero ───────────┬ tamaño─┬ ubicaciones ─────────┬ desperdicio ┐  │
│ │ serieA_cap01.mp4    │ 1,2 GB │ E:\Paq\ · F:\Paq\     │      1,2 GB │  │
│ │ capitulo_02.mkv     │ 750 MB │ C:\Vid\ · E:\Alca\    │    750 MB   │  │
│ └─────────────────────┴────────┴───────────────────────┴────────────┘ │
│ [🗑 Borrar duplicados] [☑ Marcar todo] [⇩ Exportar CSV] [🖨 PDF]      │
└────────────────────────────────────────────────────────────────────────┘
```
- Índice incremental: hash SHA-256 solo de lo que cambió (base SQLite `IndexStore`).
- "Desperdicio" = bytes recuperables borrando las copias de más.
- Teclas: `Ctrl+G` guardar la lista encontrados (legado M2Exported), `Ctrl+F` buscar.
- Llevar a la GUI lo que hoy solo hace el CLI (`escanear`, `duplicados`, `faltantes`).

### 2.4 Pestaña "Estadísticas"

```
┌ [Estadísticas] ──────────────────────────────────────────────────────┐
│ Hoy ║ Semana ║ Mes ║ Todo    Dispositivo: [Todos ▾]   [⟳]            │
│ ┌ 2,3 TB copiado ┐ ┌ 3.212 ficheros ┐ ┌ 38 MB/s ┐ ┌ 00:04 todo hoy ┐ │
│ └────────────────┘ └────────────────┘ └─────────┘ └────────────────┘ │
│ Gráfica por día  ▄▄ █▄ ██▄ ███▄ (barras bandera)                       │
│ ┌ fecha ─┬ dispositivo ┬ ficheros ┬ GB ┬ vel media ┬ vel real ┬      │
│ └────────┴─────────────┴──────────┴────┴───────────┴──────────┘      │
│ [⇩ Exportar CSV]  [🖨 PDF]                                             │
└───────────────────────────────────────────────────────────────────────┘
```
- **Arreglar las estadísticas falsas**: hoy `files:0`, `avg=real`, `wait=0`. Medir de verdad ficheros, tiempo de espera y velocidad media/real.

### 2.5 Pestaña "Errores" y cola

```
┌ [Errores ⚠ 3]  [Cola pendiente] ─────────────────────────────────────┐
│ ┌ fichero ──────┬ origen ─┬ destino ─┬ motivo ────────┬ hora ─┐      │
│ │ cap02.mkv     │ E:\Paq\ │ F:\      │ no cabe        │ 14:02 │      │
│ │ serieB_05.avi │ E:\Paq\ │ G:\      │ dispositivo se │ 14:03 │      │
│ │               │         │          │ desconectó     │       │      │
│ └───────────────┴──────────┴──────────┴───────────────┴───────┘      │
│ [↺ Reintentar seleccionados] [↺ Reintentar todos] [✖ Descartar]      │
│ El plan de copia se autoguarda (se retoma si se cierra la app).      │
└───────────────────────────────────────────────────────────────────────┘
```

### 2.6 Pestaña "Perfiles" (sustituye a las matrices .xcm)

```
┌ [Perfiles] ─────────────────────────────────────────────────────────┐
│ ┌ nombre ─┬ orígenes ──┬ destinos ─┬ estructura ─┬ auto ──┐         │
│ │ PaqueteA│ E:\Paq\    │ F: G: H:  │ HDD         │ 19:00  │         │
│ └─────────┴────────────┴───────────┴─────────────┴────────┘         │
│ [＋ Nuevo][✎ Editar][✖ Eliminar][⇧ Importar .json][⇩ Exportar]       │
│ Programación: 19:00 → "PaqueteA" (días seleccionables)               │
└──────────────────────────────────────────────────────────────────────┘
```
- El perfil guarda: origen, destinos ☑, estructura (HDD/FLASH), filtros de fecha, expresiones, horario.

### 2.7 Pestaña "Ajustes"

```
Tema [●Oscuro ○Claro] · Tamaño de letra ──
Copia: buffer ▾ · verificar [●SHA-256 ○ninguna] · velocidad límite ▾
       espacio libre mínimo · estructura por tipo [●auto ○manual]
Sincroniza: umbral ──▒── · teclas M2 [●sí ○no]
Índice: auto cada [30 min ▾] · ubicación de la base
Idioma [Español ▾] · exclusiones del sistema (defecto, editables)
Almacén: [Abrir carpeta de datos] [Migrar datos viejos ▸]
[✓ Guardar][✖ Cancelar]
```

### 2.8 Resumen final (modal al terminar la copia)

```
┌ Resumen de la operación ─────────────────────────────── ╳ ┐
│ ✅ Completada · 3.212 ficheros · 12,4 GB · 00:04:18          │
│ ┌ destino ─┬ ficheros ┬ GB ┬ vel ┬ errores ┐                │
│ │ E: PENDRA│ 1.654    │ 6,2 │ 38MB│ 0      │                │
│ │ F: PAQUE │ 1.558    │ 6,2 │ 37MB│ 0      │                │
│ └──────────┴──────────┴─────┴─────┴────────┘                │
│ ⚠ 3 no cabían (ver [Errores])                                │
│ [🖨 Guardar PDF][⟳ Copiar otro][✖ Cerrar]                    │
└────────────────────────────────────────────────────────────┘
```

---

## 3. Funciones de los 7 (fusionadas) — estado en QBasPaquete

### Ya implementado (mantener y pulir)
- Copia "leer una vez escribir a N" (`CopyFanOutAsync`).
- Verificación SHA-256 en cada destino.
- Mover seguro: hash antes de borrar origen.
- Reanudar sin duplicar (`CompareContent`) + 3 reintentos con backoff.
- 3 modos de fecha: `DateNewer`, `DateRange`, `DateEqual`.
- 7 modos de estructura de destino.
- Series/paquetes: umbral 75 %, bigrams, `Plan()` (simular), `Apply()` (mover), limpieza del paquete entrante.
- Índice SQLite incremental + `Duplicates` con bytes desperdiciados + `Missing(A,B)` + `Search`.
- Division/unión de ficheros grandes (`VolumeTools`).
- CLI con 12 comandos + alias EN, `-v` verifica, `-q` silenciosa.
- Volúmenes sin letra (`\\?\Volume{GUID}`) como id estable.
- Perfiles JSON (sustituye matrices .xcm).
- Estadísticas SQLite + export CSV.
- Instalador propio, scripts de release Windows/Linux/macOS.

### Huecos del motor a arreglar (F1)
- `CopyTimestamps` y `CopyAttributes` declaradas, NO aplicadas → copiar y fijar `SetLastWriteTimeUtc` + `SetAttributes`.
- `MinFreeSpace` declarada, nunca comprobada → comprobar antes de empezar (`RequireFreeSpace`).
- `ParallelCopies` declarada, nunca usada → implementar copia de ficheros paralela limitada (N ficheros simultáneos, cada uno fan-out).
- `UsbFlashSlow`/`UsbExternalSlow` nunca devueltos por `Classify()` → detectar lentitud; además auto-marca desde la velocidad real medida.
- `StructureMode.ZeroKbFiles` no manejado → crear el fichero vacío.
- `IndexStore.indexPercent()` `=> 0` placeholder → reportar progreso real.
- `StatsStore.Add` con `files:0, avg=real, wait=0` (solo Desktop `SaveStats`) → medir de verdad; que el CLI también grabe.
- `IndexStore` solo en CLI → llevarlo a la GUI (pestaña Escáner).
- `CleanupEmptySources()` escrito, nunca llamado → llamarlo al terminar.
- `AppPaths.ProfilesDir` declarado sin uso → usarlo.
- `Match()` con `Contains` (patrón inexacto) → matcher de comodines real (`*` y `?`).
- La GUI no muestra el tiempo restante → calcular y mostrar `Remaining`.

### Nuevo (de los 7) — F2 en adelante
- **Expulsar/desmontar el dispositivo** al terminar: Windows `mountvol X: /d` (o SetupAPI), Linux `umount`, macOS `diskutil eject`. CLI: nuevo comando `expulsar`.
- **Reintentar solo lo que falló**: usando `CopyResult.Errors`.
- **Cola persistente / autoguardado**: guardar plan parcial (JSON) y retomar.
- **Estructura distinta según tipo de unidad** (HDD vs FLASH): elegir `StructureMode` según `DeviceInfo.Kind` (auto).
- **Detección física del dispositivo** (`DeviceProbe`):
  - Tipo (disco interno / USB / memoria / red / óptico), **marca y modelo** (model vendor/product).
  - **SSD o mecánico** (Windows: `IOCTL_STORAGE_QUERY_PROPERTY` seek-penalty; Linux: `/sys/block/…/queue/rotational`; macOS: `diskutil info`).
  - **Velocidad máxima**: bus (USB 2.0/3.x, SATA, NVMe…) y velocidad de enlace real donde sea legible; más **velocidad medida** durante la copia.
- **Copiar/mover a velocidad máxima**: buffer adaptativo (1 MB en USB lento, 4 MB en SSD/NVMe/red), lectura una vez, escritura en paralelo a N, y sin throttling salvo opción expresa.
- **Red**: destinos de red (`DriveType.Network`) y rutas UNC (`\\servidor\carpeta`) tratadas como dispositivo.
- **Refresco de dispositivos cada 1 s** (no 1 ms: evita robar CPU al motor).
- **Barra en vivo real**: los porcentajes se llenan con **bytes reales** contados; el progreso se empuja a la UI cada ~100–250 ms (8–10 fps), nunca frenando la escritura.

### Extras — la unión perfecta y la identidad propia
1. **Expresiones / filtros por regla** (legado M2 v2.0): `nombre contiene "cap" Y tamaño > 500 MB`, `serie = X Y capítulo = Y`, etc. Pequeño motor de expresiones en `CopyOptions`.
2. **Cajas / asignar ficheros a destinos concretos** (legado XpressCopy "Box"): no todo va a todos. Un fichero/carpeta se arrastra a un rectángulo concreto → ese va solo ahí; el resto multicopia como siempre.
3. **Prioridad de destinos**: orden de llenado; si uno se llena, el siguiente atiende solo. Etiqueta de peso/coste visual en el rectángulo.
4. **Aviso al terminar**: notificación del sistema + sonido + minimizar a bandeja mientras copia en segundo plano.
5. **"Pasó el USB" (rectificación)** — original tuyo: conectadas las memorias devueltas, se comparan contra el **paquete maestro**: qué falta, qué sobró, qué cambió (tamaño/fecha/hash), y opción de **arreglar** (copiar lo que falta, avisar de lo extra). Modo nuevo en el motor (`SyncMode.Rectify`) + comando CLI `rectificar` + de donde se hace en la GUI.
6. **Sincronización por inserción**: un perfil marcado "auto por inserción" se ejecuta solo cuando conectas esa memoria (además del horario fijo).
7. **Mapa de unidades** (original tuyo): vista "pájaro" desde el índice SQLite: qué USBs existen, espacio, tipos dominantes, duplicados, última vez tocada. Pestaña propia o subsección de Escáner.
8. **Vista Series** (original tuyo): navegar el índice por **nombres de serie/paquete** (carátulas tipo M2Exported), no por carpetas. Pestaña opcional.

**Identidad innegociable (no imitable por los 7):** leer una vez escribir a N · mover con hash · dividir/unir · índice SQLite real · CLI+GUI · colores bandera.
- **Reporte PDF** de la operación (desde el resumen y desde estadísticas).
- **Escáner en GUI** (duplicados/faltantes/búsqueda) + exportar lista de encontrados (Ctrl+G).
- **Programación horaria** de perfiles + **expresiones/filtros avanzados** (legado M2 v2.0).
- **Índice automático cada X minutos** + "próximas carpetas / continuar donde quedaste" (legado XpressCopy).
- **Mini-explorer desde rectángulos**: clic → ver su contenido; renombrar, eliminar, mover selección.
- **Arrastrar y soltar + portapapeles** con Windows en ambos sentidos.
- **Multipaste**: Ctrl+V con rectángulos marcados pegada a todos.
- **Ventana de errores con reintento individual**.
- **Cancelar el fichero en curso** sin abortar la cola.
- **Resumen al terminar** con opción PDF.

### Lo que NO se copia (decisiones)
- Clonar disco sector a sector (peligroso, requiere elevación) → NO.
- Desactivar UAC (`DisableLUA.reg`, CHKDSK con privilegios) → NO.
- Cobro por dispositivo / licencias → NO (libre).
- Skins con 14 brushes / narrador de voz / blur → NO (estético); tema + tamaño de letra sencillos.

---

## 4. Conversiones y renombrado (F0)

- Carpetas: `QBasPaquete.*` → `QbaswingMultiTask.*`
  - `QBasPaquete.Core` → `QbaswingMultiTask.Core`
  - `QBasPaquete.Cli` → `QbaswingMultiTask.Cli`
  - `QBasPaquete.Desktop` → `QbaswingMultiTask.Desktop`
  - `QBasPaquete.Installer` → `QbaswingMultiTask.Installer`
  - `QBasPaquete.Tools` → `QbaswingMultiTask.Tools`
- Namespace raíz: `QBasPaquete.*` → `QbaswingMultiTask.*`.
- `.slnx`, `.csproj` (`RootNamespace`, `AssemblyName`, `Product`, `ProjectReference`), `app.manifest`, `x:Class` en `.axaml`, `releases/linux/*.desktop`, scripts, README.
- `Brand.cs`: `Name = "QbaswingMultiTask"`, `Copyright`, tagline nuevo.
- Carpeta de datos: `%AppData%\QbaswingMultiTask`; **migrar** desde `QBasPaquete` si existe (perfiles, índice, estadísticas, log).

---

## 5. Fases de construcción y "terminado" por fase

- **F0 Renombre**: compila igual, se abren los datos migrados. ✅ = build OK.
- **F1 Motor**: timestamps/atributos, libre mínimo, multihilo real, slow detect, zero-kb, `indexPercent`, stats reales (Desktop+CLI), limpieza, match real, tiempo restante. ✅ = copia de prueba con fechas conservadas y stats coherentes.
- **F2 Dispositivos**: expulsar, formatear (confirmado), velocidad USB, estructura por tipo. ✅ = expulsar un USB por comando.
- **F3 GUI principal**: pestañas puestas + explorador + rectángulos ☑ + barras bandera + arrastre/portapapeles con Windows + multipaste. ✅ = copia con ratón y con Ctrl+V.
- **F4 Sincronizador TC**: paneles TC, mini-discos, árbol, `✓/●/=`, teclas M2, revisar/aplicar. ✅ = sincronizar un paquete de prueba.
- **F5 Errores + cola persistente**: reintento de solo fallidos + autoguardado/retomar + cancelar fichero en curso. ✅ = interrupción y retoma sin duplicar.
- **F6 Escáner en GUI**: duplicados/faltantes/buscar + exportar + Ctrl+G. ✅ = comparar dos carpetas.
- **F7 Estadísticas reales + resumen + PDF**: tarjetas, gráfica por día, CSV, resumen, guardar PDF. ✅ = PDF genera.
- **F8 Perfiles + horario + expresiones + índice auto + próximas carpetas**. ✅ = programar perfil.
- **F9 Ajustes**: tema, letra, idioma, estructura por tipo, exclusiones. ✅ = cambiar tema en caliente.
- **F10 Cierre y entrega**: README del proyecto + ZIP completo + instalador Windows compilado. ✅ = ZIP probado desde cero en un Windows limpio.

---

## 6. Entrega

- Un **ZIP** (y `.rar` si se puede sin herramientas extras) con **todo el proyecto** terminado:
  - Solución `QbaswingMultiTask.slnx` + los 5 proyectos (Core, Cli, Desktop, Installer, Tools).
  - Scripts: `build.ps1`/`release.sh` (sin bin/obj/dist) para Windows/Linux/macOS.
  - **Instalador para Windows incluido**: proyecto `QbaswingMultiTask.Installer` que genera `Instalar-QbaswingMultiTask.exe` (y su exe ya compilado de la última release, si el entorno lo permite).
  - `README.md` de uso + `PLAN.md`.
- Excluir: `bin/`, `obj/`, `.git/`, `dist/` de fuentes previas.

---

## 7. Pendientes abiertos (decisión del autor)
- Tagline definitivo.
- Idioma: mantener solo español o añadir EN (multi-idioma F9).
- Si la GUI nueva hereda el tema oscuro por defecto (sí) y si habrá tema claro en v1.1.
- Formato de entrega: ZIP (preferido) o RAR.

---

## 8. Estado de ejecución (parada del día 2026-09-30)

Este apartado NO es especificación: es el registro de lo que se ha tocado y de lo
que falta. Las secciones 1 a 7 siguen siendo el plan de referencia.

### 8.1 Cerrado hoy (correcciones con riesgo de pérdida de datos)

Estos fallos existían de verdad y ya están corregidos en el código:

| Fichero | Fallo | Corrección |
|---|---|---|
| `Core/Engine/CopyEngine.cs` | **Mover borraba el origen sin verificar**: si un destino no existía, se saltaba la comprobación y aun así borraba el origen. Pérdida de la única copia al desconectar un USB a mitad. | Ahora exige que **todos** los destinos existan y su hash cuadre antes de borrar, y comprueba que el borrado ocurrió de verdad. |
| `Core/Engine/CopyEngine.cs` | **Espejo borraba aunque la copia hubiera fallado**. | El borrado de sobrantes solo corre si `Success && FilesFailed == 0 && Errors.Count == 0`. |
| `Core/Engine/CopyEngine.cs` | **El conjunto "esperados" comparaba rutas con mayúsculas/minúsculas**, así que en Windows un fichero presente salía como sobrante y lo borraba. | `OrdinalIgnoreCase` + `Path.GetFullPath`. |
| `Core/Engine/CopyEngine.cs` | **`DeleteExtras` entraba en carpetas del sistema** (`System Volume Information`, `$RECYCLE.BIN`) y borraba su contenido. | Guardas de directorios del sistema, respeto a `ExcludeDirectories`, borrado de carpetas vacías y recorrido iterativo. |
| `Core/Engine/CopyEngine.cs` | **`CopyFanOutAsync` abandonaba tareas nativas** al primer fallo y luego abría un `FileStream` sobre los mismos ficheros: dos escritores. | Se esperan todas las tareas y se capturan sus excepciones. |
| `Core/Engine/NativeCopy.cs` | `_keepAlive` era **estático**: dos copias simultáneas se pisaban el delegado y una lo ponía a `null` con la otra dentro de la llamada nativa. | Delegado local a cada llamada + `GC.KeepAlive`. |
| `Core/Devices/DeviceProbe.cs` | Leía `StorageTrimProperty` (id 8) en vez de `StorageSeekPenaltyProperty` (id **7**): **SSD y HDD salían invertidos**, y el flag elige el búfer de copia. | PropertyId 7, con el criterio correcto. |
| `Core/Devices/DeviceProbe.cs` | Offsets de `STORAGE_DEVICE_DESCRIPTOR` erroneous: `BusType` se leía del byte alto (siempre 0), así que `Bus`/`MaxMbps` **no se rellenaban nunca** y la "velocidad real" salía siempre `—`. | Layout real (vendor 12, product 16, bus 28), strings acotadas al `Size` del descriptor, `GENERIC_READ` solo (fallaba en ópticos y medios con protección de escritura) y guardia `OperatingSystem.IsWindows()`. |
| `Core/Devices/NetDrive.cs` | **Inyección de comandos**: `letter`, `user` y `pass` sin comillas en `cmd.exe /c`. | `net.exe` directo con `ArgumentList`, sin shell. |
| `Core/Devices/NetDrive.cs` | `ReadToEnd()` síncrono antes de esperar: el `WaitForExit(15000)` era **código muerto**. | Lectura asíncrona de ambos flujos + espera con `CancellationTokenSource`. |
| `Core/Engine/VolumeTools.cs` | Orden `Ordinal` al unir: `volumen-1000` se unía **antes** que `volumen-999` → fichero corrupto a partir de 1000 partes. | Orden por número de parte. |
| `Core/Sync/SeriesSynchronizer.cs` + `DirSynchronizer.cs` | `Walk` se tragaba la cancelación: el plan salía "completo" con medio árbol y aplicarlo podía **borrar lo no visto**. | `OperationCanceledException` se propaga. |
| `Core/Index/IndexStore.cs` | `EscapeLike` sin `ESCAPE`: buscar `100%` encontraba `1000%`. | `ESCAPE '\'` en las tres consultas. |
| `Core/Devices/DeviceManager.cs` | `IsSystemVolume` terminaba en `&& false` (código muerto). | Devuelve `false` con el criterio documentado. |
| `Desktop/App.axaml` | **No existía `Application.Resources`**: todos los `{DynamicResource}` resolvían `null`, así que no se veía ninguno de los colores de la sección 1. | Diccionario con la paleta de §1. **Este era el motivo de que la app no se viera como el plan.** |
| `Desktop/App.axaml` | `BorderThickness="1,5"` se interpreta como (horizontal, vertical): bordes de 5 px arriba y abajo. | `"1"` y `"2"`. |
| `Installer/Install.cs` | `OutputType=WinExe` con código de consola: **el instalador abría una ventana en blanco y no decía nada**. Sin idioma, sin "¿ejecutar?", errores invisibles. | `OutputType=Exe`. |
| `Installer/Install.cs` | Borraba la instalación anterior **antes** de copiar, sin red: si la copia fallaba el usuario se quedaba sin nada. | Copia a `_stage` y solo intercambia cuando está entero. |
| `Installer/Install.cs` | `UninstallString` apuntaba al ejecutable dentro del ZIP: al borrar el ZIP, "Desinstalar" dejaba de funcionar. | Apunta a `installDir\Desinstalar.exe`. |
| `Installer/Install.cs` | Se copiaba a sí mismo **dos veces** y volcaba el payload entero otra vez dentro de `installDir\programa`: la app duplicada dentro de sí misma. | Eliminado. |
| `Desktop/MainWindow.axaml` | **No había forma de elegir la carpeta de origen**, pese a que `SourcePath` movía el arranque, el horario, el auto al insertar y el modo series. | Campo + botón con las claves de idioma que ya existían sin usar. |

### 8.2 Añadido hoy
- `Views/SummaryWindow` (`.axaml` + `.cs`): el resumen modal de §2.8.
- Claves de idioma de las tres pestañas que faltaban (`main.tabErrors`, `main.tabProfiles`, `main.tabSettings`) y del resumen (`sum.*`). ES y EN verificados **sincronizados: 229 claves cada uno**.
- `Dialogs.SaveFileAsync` para el "Guardar PDF".

### 8.3 Lo que sigue pendiente

**Funcionalidad del plan que no existe:**
- Pestañas **Errores**, **Perfiles** y **Ajustes** (3 de las 7). Hoy hay 8 pestañas y tres no son del plan (Cajas, Series, Mapa).
- **Escáner** sin duplicados, sin faltantes, sin búsqueda, sin exportar; `IndexStore` no se usa desde la GUI.
- **Estadísticas** sin selector de periodo, sin filtro por dispositivo y sin gráfica por día.
- **Perfiles** sin horario, sin estructura HDD/FLASH ni importación/exportación JSON.
- **Resumen final** creado pero **no conectado** a `RunOperationAsync`.
- Cajas y prioridad de destinos siguen sin afectar a la copia (`PriorityBox` es código muerto).
- Corrección formal: `NetDrive.Connect` es `async` y quien lo llama debe esperar.

**Higiene (menor):** `I18n.UiSave` pisa `settings.json` entero; `Settings` usa `ajustes.json`; exportación de estadísticas con cabeceras fijas en español y CSV sin escapar; `SimplePdf` sin ajuste de línea; `_make_icons.py` revienta con `%d` sobre un `float`; `slnx` con 3 de 5 proyectos; `.gitignore` ignora `releases/*` sin negación; versión `1.0.0` escrita en 5 sitios.

### 8.4 Entorno (bloqueo actual)

En la máquina de trabajo **no había `dotnet` ni `git`**. Se instaló el SDK .NET 10.0.401 (verificado, SHA-512 correcto). La red cae a ~70 KB/s y corta las conexiones cada pocos MB, así que:

- La descarga del SDK hubo que hacerla por rangos en paralelo.
- **`dotnet restore` aún no termina**: NuGet no logra descargar `SQLitePCLRaw.lib.e_sqlite3`. Es un problema de red, no del código.
- `git` no está instalado, así que el commit y el push se hacen desde otro equipo o tras instalarlo.

**En consecuencia: nada de esto está compilado ni probado todavía.** Los cambios están escritos y revisados a mano, pero hasta que `restore` funcione no se puede afirmar que compilen.
