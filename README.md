# QbaswingMultiTask

Un solo programa que reúne la copia de ficheros, la sincronización tipo Total
Commander, el escáner de duplicados/faltantes, las estadísticas reales, el centro
de errores con reintento, los perfiles y los ajustes. Windows (escritorio),
Avalonia 11.3.5.

Este proyecto sigue `PLAN.md` al pie de la letra. Lo que se aparta de él está
listado y justificado en `PROGRESO.md` (sección «Decisiones que se apartan»).

---

## 1. Qué trae el ZIP

```
QbaswingMultiTask\
  Instalar-QbaswingMultiTask.exe   ← instalador (autocontenido, un solo .exe)
  app\                             ← la aplicación ya compilada (autocontenida)
  README.md                        ← este archivo
  PLAN.md                          ← la especificación
  PROGRESO.md                      ← estado real del proyecto
  build.ps1 / release.sh           ← scripts de compilación
  QbaswingMultiTask.slnx           ← solución (5 proyectos)
  QbaswingMultiTask.Core\          ← motor, dispositivos, índice, estadísticas…
  QbaswingMultiTask.Cli\           ← línea de comandos
  QbaswingMultiTask.Desktop\       ← la ventana (Avalonia)
  QbaswingMultiTask.Installer\     ← el instalador
  QbaswingMultiTask.Tools\         ← utilidades
```

`app\` y `Instalar-QbaswingMultiTask.exe` están compilados **autocontenidos**: no
hace falta instalar .NET para ejecutarlos.

## 2. Ejecutar sin instalar

Doble clic en `app\QbaswingMultiTask.exe`. No escribe nada fuera de
`%AppData%\QbaswingMultiTask\` (ajustes, perfiles, índice, estadísticas, cola de
errores).

## 3. Instalar

Doble clic en `Instalar-QbaswingMultiTask.exe`. Pregunta dónde instalar (por
defecto `%LocalAppData%\QbaswingMultiTask`), copia los ficheros, crea accesos
directos en el Escritorio y en el Menú Inicio, se registra en Windows para poder
desinstalarse y, al terminar, ofrece abrir el programa.

Opciones:

```
Instalar-QbaswingMultiTask.exe                 instala (pregunta)
Instalar-QbaswingMultiTask.exe --silencioso    instala sin preguntar
Instalar-QbaswingMultiTask.exe --dir CARPETA   elige la carpeta
```

Para desinstalar: **Configuración → Aplicaciones**, o el acceso «Desinstalar
QbaswingMultiTask» del Menú Inicio, o
`%LocalAppData%\QbaswingMultiTask\Desinstalar.exe`.

## 4. Las siete pestañas

1. **Copiar/Mover** — explorador a la izquierda (origen) y rectángulos grandes de
   dispositivo a la derecha (destinos), dos por fila y en vertical. Se marcan con
   un clic. La copia es «leer una vez, escribir a N» con verificación por hash.
   - **Ctrl+C / Ctrl+X / Ctrl+V** usan el portapapeles real de Windows
     (`CF_HDROP`): se entiende con el Explorador y con Total Commander.
   - Con destinos marcados, **pegar es multipaste** (a todos). Sin marcados, pega
     en la carpeta abierta en el explorador.
   - **Cajas**: arrastrar ficheros de Windows y soltarlos sobre un rectángulo
     concreto los manda solo a ese destino.
   - **Arrastrar hacia fuera**: arrastra la carpeta de origen y suéltala en el
     Escritorio o el Explorador.
   - Cortar + pegar = **mover**; el origen no se borra hasta que el hash cuadra.
2. **Sincronizador** — dos paneles tipo Total Commander, mini-discos, árbol
   desplegable, marcas `✓` (igual), `●` (distinto) y `=` (solo un lado), umbral y
   teclas del M2: `Ctrl+A` alinear, `Ctrl+S` revisar, `Ctrl+U/E/T` desalinear,
   `Entrar` aplicar.
3. **Escáner** — índice incremental, duplicados por hash, ficheros que faltan de
   A a B, búsqueda por comodín, y exportar a CSV y PDF.
4. **Estadísticas** — tarjetas con datos reales del motor, gráfica vectorial por
   día (no una imagen), tabla con filtro de fechas y exportar a CSV/PDF.
5. **Errores** — cada fallo con su causa; **reintentar solo lo que falló**,
   descartar, y guardar en CSV/PDF. La cola **persiste**: si cierras el programa
   con pendientes, al volver a abrirlo siguen ahí para reintentarlos.
6. **Perfiles** — alta, edición, duplicar, borrar, exportar/importar, y aplicar
   el perfil al Copiar/Mover (origen, destinos, estructura y fechas).
7. **Ajustes** — tema claro/oscuro en caliente, tamaño de letra, idioma,
   estructura por tipo de unidad, exclusiones y umbrales.

## 5. Compilar desde el código

Requisitos: SDK de .NET 10 (probado con 10.0.401) y Windows para publicar el
instalador.

```powershell
# Windows
.\build.ps1                 # compila todo y publica app\ + Instalar-QbaswingMultiTask.exe
.\build.ps1 -SoloCompilar   # solo compila (rápido)
```

```bash
# Linux / macOS (sin instalador de Windows)
./release.sh
```

Si no hay red, los paquetes NuGet necesarios deben estar en una carpeta
`paquetes\` junto a la solución; los scripts la usan como origen si existe.

## 6. Línea de comandos

```
QbaswingMultiTask.Cli listar                lista dispositivos reales
QbaswingMultiTask.Cli copiar ORIGEN DESTINO copia
QbaswingMultiTask.Cli mover  ORIGEN DESTINO mueve (con verificación)
QbaswingMultiTask.Cli expulsar LETRA        expulsa con seguridad
QbaswingMultiTask.Cli formatear LETRA       formatea
QbaswingMultiTask.Cli ayuda / version
```

## 7. Dónde guarda los datos

Todo en `%AppData%\QbaswingMultiTask\`:

| Qué | Dónde |
|---|---|
| Ajustes | `ajustes.json` |
| Perfiles | `perfiles\` |
| Estadísticas | `estadisticas\estadisticas.jsonl` |
| Cola de errores pendientes | `estadisticas\pendientes.json` |
| Índice del escáner | `indice\indice.jsonl` |
| Registros | `logs\` |

## 8. Aviso sobre el índice y las estadísticas

`PLAN.md` mencionaba SQLite para el índice y las estadísticas. En este entorno no
se pudo restaurar el paquete de SQLite sin red, así que se guardan en **JSONL**
(misma información, misma interfaz `StatsStore`/`IndexStore`). Es la única
desviación funcional, está documentada en `PROGRESO.md` y es reversible.

## 9. Repositorio

<https://github.com/Sgborbolla/QbaswingMultiTask>
