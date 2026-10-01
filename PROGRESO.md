# QbaswingMultiTask — Estado del proyecto

Archivo vivo: actualizarlo al final de cada jornada para retomar al día siguiente.

> **Aviso importante (1-oct-2026):** el código anterior se borró por decisión del
> autor y el programa se está reescribiendo desde cero siguiendo `PLAN.md`.
> Por eso lo que decía este archivo antes (F0–F2 «hecho») ya no describe lo que
> hay en disco. Lo de abajo es el estado real, comprobado con compilaciones y
> con la aplicación arrancada.

## Objetivo
Un solo programa, **QbaswingMultiTask**: copia «leer una vez escribir a N»,
sincronizador tipo Total Commander, escáner de duplicados/faltantes, estadísticas
reales, errores con reintento, perfiles y ajustes. Entrega en ZIP con instalador
`Instalar-QbaswingMultiTask.exe`.

## Especificación
`PLAN.md` es la autoridad. Nada de lo que se escriba aquí lo contradice.

## Días ejecutados

### 2026-10-01 — Borrado y arranque desde cero

**Hecho y comprobado:**
- Copia de seguridad del código borrado: `%TEMP%\opencode\respaldo-antes-de-borrar`.
- Copia de los activos irremplazables (música + logotipos):
  `%TEMP%\opencode\activos-intocables`. Recuperados ya en
  `Desktop\Assets\` y `Installer\Assets\`.
- Los 5 proyectos recreados: `Core`, `Cli`, `Desktop`, `Tools`, `Installer`.
- `QbaswingMultiTask.slnx` con los 5 proyectos (`PLAN.md` §6).
- `Directory.Build.props`: versión `1.0.0` en **un solo sitio** (sección 8.3 pedía
  sacarla de los 5 sitios donde estaba repetida).
- Los 5 proyectos **compilan**. Los `.exe` salen con el nombre de `PLAN.md` §1:
  `QbaswingMultiTask.exe` y `qbaswingmultitask.exe`.
- La aplicación **arranca y abre ventana** (`consola limpia`, sin excepciones).
- Paleta de §1 cargada como recursos: los 10 colores resuelven bien. (§8.1: sin
  este diccionario los `{DynamicResource}` daban `null` y el programa salía sin
  los colores de la marca.)
- Pestaña Copiar/Mover: rectángulos de dispositivo **grandes, dos por fila en
  vertical** (pedido del autor; el boceto de §2.1 dibuja cuatro, así que es una
  decisión deliberada que se aparta del boceto), franja de abajo compacta,
  explorador angosto a la izquierda, barras de capacidad y de copia con la
  bandera de §1.
- Dispositivos: se leen **solos**. `DeviceManager.Scan()` da todos los volúmenes
  montados (sistema, internos, USB, memoria, red y volúmenes sin letra
  `\\?\Volume{GUID}`), y un reloj de 1 s los va añadiendo y quitando al enchufar
  o desconectar, sin perder las casillas marcadas (empareja por `Id`).
  §3 pide refresco de 1 s, no de 1 ms, para no robarle CPU al motor.

**Errores encontrados y corregidos en esta jornada:**
| Dónde | Fallo | Corrección |
|---|---|---|
| `Core/Devices/DeviceManager.cs` | `Scan(includeSystem:false)` descartaba el disco del sistema. En un PC sin USB la lista quedaba vacía y **no se dibujaba ningún rectángulo**. | El disco del sistema entra por defecto; §2.1 lo dibuja (`▉ C`) como destino. |
| `Core/Devices/DeviceManager.cs` | `Root` guardaba el nombre en vez de la ruta (`C:\`), así que no se podía copiar dentro. | `Root` = la ruta real que da `DriveInfo`. |
| `Core/Devices/DeviceManager.cs` | Guid de `\\?\Volume{...}` mal recortado y volúmenes duplicados sin filtrar. | Recorte por índice de `{`/`}` y deduplicación por `Id`. |
| `Desktop/Pct.cs` | Faltaban `using System` y `using Avalonia.Data.Converters`; no compilaba. | Directivas puestas. |
| `Desktop/ViewModels/DeviceViewModel.cs` | Sin `using System/Linq/Collections.Generic` (el proyecto no tiene `ImplicitUsings`). | Directivas puestas. |
| `Desktop/Program.cs` | `WithDeveloperTools()` sin el paquete de diagnóstico. | Quitado. |
| `Desktop/MainWindow.axaml` | Estilos dentro de `Window.Resources`: en Avalonia 11 los estilos van en `Window.Styles`. | Movidos. |
| `Desktop/MainWindow.axaml` | Comentarios XML con `---` dentro: **XML inválido**, error `AVLN1001`. | Comentarios reescritos. |
| Avalonia 11.3.5 | La plantilla genera 12.1.3, que no se puede restaurar aquí (red a ~70 KB/s). | Fijado a 11.3.5, que sí está en `paquetes\`. |

**Aviso de verificación (actualizado):** la apariencia se comprueba leyendo el
XAML y los recursos, y **arrancando la aplicación** en este PC. Se han guardado
capturas (`%TEMP%\opencode\captura-f2.png`, `captura-f4.png`) para que el autor
las vea; el modelo que escribe este archivo **no puede ver imágenes**, así que
las capturas son para revisión humana.

### 2026-10-01 (2.ª parte) — pestañas F3 a F9

**Hecho y comprobado (compila con 0 avisos y 0 errores; app arrancada):**
- Las 7 pestañas existen y cada una tiene su vista y su ViewModel: Copiar/Mover,
  Sincronizador, Escáner, Estadísticas, Errores, Perfiles, Ajustes.
- **F3 portapapeles real de Windows** (`Desktop/Platform/WindowsClipboard.cs`,
  P/Invoke `CF_HDROP` + `Preferred DropEffect`): Ctrl+C copia, Ctrl+X corta,
  Ctrl+V pega. Comprobado con una prueba aislada: se escriben 2 rutas con efecto
  «cortar» y se leen de vuelta idénticas.
  - Con rectángulos marcados → **multipaste a todos**.
  - Sin marcados → pega a la carpeta abierta en el explorador.
  - Cortar + pegar = **mover** (el motor no borra el origen hasta cuadrar hash).
  - **Cajas (§3):** soltar ficheros de Windows sobre un rectángulo los manda
    **solo a ese** destino.
  - Falta: arrastrar **hacia fuera** (del programa al Explorador).
- **F4 Sincronizador:** dos paneles con su raíz, mini-discos, árbol, marcas
  `✓/●/=`, umbral y atajos M2 (`Ctrl+A/S/U/E/T`, `Entrar`). Corregido: el panel
  derecho se quedaba vacío cuando solo había una unidad lista; ahora siempre
  muestra una raíz.
- **F5 Errores:** lista con causa, **reintentar solo lo fallido**, descartar,
  CSV y PDF. `MainViewModel` guarda el último plan para poder reintentarlo.
- **F6 Escáner:** índice real, duplicados por hash, faltantes A→B, búsqueda por
  comodín, CSV y PDF.
- **F7 Estadísticas:** tarjetas con datos **reales** del motor y gráfica vectorial
  por día (controles, no imagen), tabla, filtro y CSV/PDF.
- **F8 Perfiles:** alta/edición/duplicar/borrar/exportar/importar y **aplicar al
  Copiar/Mover** (origen, destinos, estructura, fechas).
- **F9 Ajustes:** vista completa con guardado real y aplicación del tema al
  arrancar. **Pendiente:** que el tema claro repinte (hoy los colores son fijos
  en oscuro).

**Errores encontrados y corregidos en esta parte:**
| Dónde | Fallo | Corrección |
|---|---|---|
| `ViewModels/ErroresViewModel.cs` | Se llamaba `Avisar()` como si fuera del VM; es de cada `RelayCommand`. No compilaba. | Método `Notificar()` que avisa a los 5 comandos. |
| `ViewModels/SincronizadorViewModel.cs` | El panel derecho no mostraba raíz si solo había una unidad lista. | El derecho usa la misma raíz que el izquierdo si no hay segunda. |
| `MainWindow.axaml.cs` | `DataContext` nulo daba aviso `CS8600/CS8603`. | `(MainViewModel)DataContext!`. |

## Fases (orden de `PLAN.md` §5)

| Fase | Descripción | Estado |
|------|-------------|--------|
| F0 | Renombre a QbaswingMultiTask + solución de 5 proyectos | **HECHO** — los 5 compilan |
| F1 | Motor: timestamps/atributos, espacio libre, multihilo, lentitud USB, zero-kb, `indexPercent`, estadísticas reales, limpieza, comodines, tiempo restante | **HECHO** — probado con arnés (`motor`) y usado por la GUI |
| F2 | Dispositivos: expulsar, formatear, velocidad, estructura por tipo, red | **HECHO** — CLI (`listar/copiar/mover/expulsar/formatear`) y GUI |
| F3 | GUI principal: 7 pestañas, explorador, rectángulos ☑, barras, arrastre, portapapeles, multipaste | **EN CURSO** — portapapeles real CF_HDROP (Ctrl+C/X/V), pegado a marcados o a la carpeta abierta, y cajas (soltar sobre un rectángulo). Falta arrastrar **hacia fuera** (a Explorer) |
| F4 | Sincronizador Total Commander: dos paneles, mini-discos, árbol, `✓/●/=`, teclas M2, revisar/aplicar | **HECHO** — dos paneles con raíz, marcas y atajos M2 |
| F5 | Errores + cola persistente + reintento de lo fallado + cancelar en curso | **HECHO (GUI)** — centro de errores con reintento de solo lo fallido, descartar y CSV/PDF. Falta persistir la cola entre sesiones |
| F6 | Escáner en GUI: duplicados/faltantes/buscar + exportar + Ctrl+G | **HECHO (GUI)** — índice real, duplicados por hash, faltantes A→B, búsqueda, CSV/PDF |
| F7 | Estadísticas reales + resumen + PDF + gráfica por día | **HECHO (GUI)** — tarjetas reales, tabla, gráfica vectorial por día, filtro, CSV/PDF |
| F8 | Perfiles + horario + expresiones + índice automático | **HECHO (GUI)** — alta/edición/duplicar/borrar/exportar/importar y **aplicar al Copiar/Mover**; horario por hora/días o al insertar |
| F9 | Ajustes: tema, letra, idioma, estructura, exclusiones | **PARCIAL** — vista completa y guardado real. El tema claro **aún no repinta** los colores (fijos en oscuro); falta `ThemeDictionaries` |
| F10 | Cierre: README + ZIP completo + instalador | PENDIENTE |

## Decisiones que se apartan de `PLAN.md` a propósito

- **Dos rectángulos por fila**, no cuatro (§2.1 dibuja cuatro). Lo pidió el autor:
  «que sean grandecitos que vayan de dos en dos con orientación vertical». Se
  cumple en `Desktop/MainWindow.axaml` con un `WrapPanel` de ancho fijo por hueco.
- **Avalonia 11.3.5** en lugar de la 12.1.3 de la plantilla: es la versión
  disponible en la carpeta local `paquetes\` y aquí la red no da para más.
- **Índice y estadísticas en JSONL, no SQLite.** §3 y §5 mencionan SQLite
  (`Índice SQLite incremental`, `Estadísticas SQLite`), pero aquí no se puede
  restaurar el paquete de SQLite con la red disponible. Se guardan en JSONL en
  `%AppData%\QbaswingMultiTask\` (`stats\estadisticas.jsonl`,
  `indice\indice.jsonl`) y las estadísticas usan **datos reales** del motor, no
  simulados. Desviación consciente y reversible: la interfaz `StatsStore` /
  `IndexStore` es la misma.

## Reglas de trabajo

1. Comparar siempre contra `PLAN.md` antes de escribir una vista o un motor.
2. No afirmar que algo funciona sin compilarlo y, si es GUI, arrancarlo.
3. `paquetes\` es la única fuente de NuGet aquí: siempre `--source` y nunca
   versionado (un `nuget.config` committed rompió el restore en otros equipos).
4. La versión y el producto salen de `Directory.Build.props`, no de los `.csproj`.