<#
    build.ps1 — compila QbaswingMultiTask y publica la entrega autocontenida.

    Uso:
        .\build.ps1                 compila todo y deja la entrega en dist\
        .\build.ps1 -SoloCompilar   solo compila (rapido)
        .\build.ps1 -Configuracion Debug

    La entrega queda asi:
        dist\Instalar-QbaswingMultiTask.exe
        dist\app\QbaswingMultiTask.exe          (autocontenida, sin .NET aparte)

    Se compila proyecto a proyecto porque restaurar el .slnx directamente con esta
    version del SDK falla (MSB4057).
#>
param(
    [switch]$SoloCompilar,
    [string]$Configuracion = 'Release'
)

$ErrorActionPreference = 'Stop'

$raiz = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $raiz 'dist'

function Buscar-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    if ($env:DOTNET_ROOT) {
        $exe = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
        if (Test-Path $exe) { return $exe }
    }
    throw 'No encuentro "dotnet". Instala el SDK de .NET 10 o ponlo en el PATH.'
}

$dotnet = Buscar-Dotnet

# Si hay "paquetes" al lado (modo sin red), se usa como origen de NuGet.
$fuente = @()
$paquetes = Join-Path $raiz 'paquetes'
if (Test-Path $paquetes) { $fuente = @('--source', $paquetes) }

$proyectos = @('Core', 'Cli', 'Desktop', 'Installer', 'Tools')

Write-Host "== Compilando ($Configuracion) ==" -ForegroundColor Cyan
foreach ($p in $proyectos) {
    $csproj = Join-Path $raiz "QbaswingMultiTask.$p\QbaswingMultiTask.$p.csproj"
    Write-Host "  - QbaswingMultiTask.$p"
    & $dotnet build $csproj -c $Configuracion --nologo @fuente
    if ($LASTEXITCODE -ne 0) { throw "Fallo al compilar QbaswingMultiTask.$p" }
}

if ($SoloCompilar) {
    Write-Host 'Compilado.' -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host '== Publicando la entrega ==' -ForegroundColor Cyan
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

& $dotnet publish (Join-Path $raiz 'QbaswingMultiTask.Installer\QbaswingMultiTask.Installer.csproj') `
    -c $Configuracion -r win-x64 --self-contained true -o $dist --nologo @fuente
if ($LASTEXITCODE -ne 0) { throw 'Fallo al publicar el instalador.' }

& $dotnet publish (Join-Path $raiz 'QbaswingMultiTask.Desktop\QbaswingMultiTask.Desktop.csproj') `
    -c $Configuracion -r win-x64 --self-contained true -o (Join-Path $dist 'app') --nologo @fuente
if ($LASTEXITCODE -ne 0) { throw 'Fallo al publicar la aplicacion.' }

Write-Host ''
Write-Host "Entrega lista en: $dist" -ForegroundColor Green
Write-Host '  Instalar-QbaswingMultiTask.exe'
Write-Host '  app\QbaswingMultiTask.exe'
Write-Host ''
Write-Host 'Para el ZIP final:  .\empaquetar.ps1'
