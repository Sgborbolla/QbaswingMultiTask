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

**Aviso de verificación:** la apariencia solo se ha comprobado leyendo el XAML
y los recursos. **No se ha mirado una captura de la ventana**, así que de cómo
se ve de verdad no se puede dar fe todavía. Falta captura y revisión visual.

## Fases (orden de `PLAN.md` §5)

| Fase | Descripción | Estado |
|------|-------------|--------|
| F0 | Renombre a QbaswingMultiTask + solución de 5 proyectos | **HECHO** — los 5 compilan |
| F1 | Motor: timestamps/atributos, espacio libre, multihilo, lentitud USB, zero-kb, `indexPercent`, estadísticas reales, limpieza, comodines, tiempo restante | PENDIENTE — reescritura desde cero |
| F2 | Dispositivos: expulsar, formatear, velocidad, estructura por tipo, red | PENDIENTE — ya existe la lectura automática (parte de F3) |
| F3 | GUI principal: 7 pestañas, explorador, rectángulos ☑, barras, arrastre, portapapeles, multipaste | EN CURSO — Copiar/Mover a medias |
| F4 | Sincronizador Total Commander: dos paneles, mini-discos, árbol, `✓/●/=`, teclas M2, revisar/aplicar | PENDIENTE |
| F5 | Errores + cola persistente + reintento de lo fallado + cancelar en curso | PENDIENTE |
| F6 | Escáner en GUI: duplicados/faltantes/buscar + exportar + Ctrl+G | PENDIENTE |
| F7 | Estadísticas reales + resumen + PDF + gráfica por día | PENDIENTE |
| F8 | Perfiles + horario + expresiones + índice automático | PENDIENTE |
| F9 | Ajustes: tema, letra, idioma, estructura, exclusiones | PENDIENTE |
| F10 | Cierre: README + ZIP completo + instalador | PENDIENTE |

## Decisiones que se apartan de `PLAN.md` a propósito

- **Dos rectángulos por fila**, no cuatro (§2.1 dibuja cuatro). Lo pidió el autor:
  «que sean grandecitos que vayan de dos en dos con orientación vertical». Se
  cumple en `Desktop/MainWindow.axaml` con un `WrapPanel` de ancho fijo por hueco.
- **Avalonia 11.3.5** en lugar de la 12.1.3 de la plantilla: es la versión
  disponible en la carpeta local `paquetes\` y aquí la red no da para más.

## Reglas de trabajo

1. Comparar siempre contra `PLAN.md` antes de escribir una vista o un motor.
2. No afirmar que algo funciona sin compilarlo y, si es GUI, arrancarlo.
3. `paquetes\` es la única fuente de NuGet aquí: siempre `--source` y nunca
   versionado (un `nuget.config` committed rompió el restore en otros equipos).
4. La versión y el producto salen de `Directory.Build.props`, no de los `.csproj`.