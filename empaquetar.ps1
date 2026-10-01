<#
    empaquetar.ps1 — arma el ZIP final de entrega (PLAN.md F10).

    Mete el codigo fuente (sin bin\obj\.git\paquetes\dist), la aplicacion ya
    compilada en app\ y el instalador. El ZIP queda en la carpeta de Descargas.

    Uso:
        .\empaquetar.ps1
        .\empaquetar.ps1 -Destino C:\ruta

    Antes hay que haber publicado:  .\build.ps1
#>
param(
    [string]$Destino = (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads')
)

$ErrorActionPreference = 'Stop'

$raiz = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $raiz 'dist'
$instalador = Join-Path $dist 'Instalar-QbaswingMultiTask.exe'
$app = Join-Path $dist 'app'

if (-not (Test-Path $instalador) -or -not (Test-Path $app)) {
    throw "No hay entrega publicada en dist\. Ejecuta antes: .\build.ps1"
}

if (-not (Test-Path $Destino)) { New-Item -ItemType Directory -Path $Destino -Force | Out-Null }

$trabajo = Join-Path $env:TEMP ('qbm-empaquetar-' + [Guid]::NewGuid().ToString('N'))
$raizZip = Join-Path $trabajo 'QbaswingMultiTask'
New-Item -ItemType Directory -Path $raizZip -Force | Out-Null

Write-Host '== Copiando el codigo fuente ==' -ForegroundColor Cyan
# /XD excluye carpetas por nombre en cualquier nivel.
robocopy $raiz $raizZip /E /XD bin obj .git .vs paquetes dist /XF *.user /NFL /NDL /NJH /NJS /NP | Out-Null

Write-Host '== Copiando la entrega compilada ==' -ForegroundColor Cyan
Copy-Item $instalador $raizZip -Force
robocopy $app (Join-Path $raizZip 'app') /E /NFL /NDL /NJH /NJS /NP | Out-Null

$zip = Join-Path $Destino 'QbaswingMultiTask-1.0.0.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }

Write-Host '== Comprimiendo ==' -ForegroundColor Cyan
Compress-Archive -Path $raizZip -DestinationPath $zip -CompressionLevel Optimal

Remove-Item $trabajo -Recurse -Force

$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ''
Write-Host "ZIP listo: $zip ($mb MB)" -ForegroundColor Green
