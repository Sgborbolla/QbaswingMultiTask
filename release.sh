#!/usr/bin/env bash
# release.sh — compila QbaswingMultiTask en Linux/macOS/Windows (Git Bash, WSL).
#
# La ventana (Desktop) y el instalador (Installer) son de Windows; en otros
# sistemas solo se compilan Core, Cli y Tools.
#
# Uso:  ./release.sh [Release|Debug]
set -euo pipefail

raiz="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
config="${1:-Release}"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "No encuentro 'dotnet'. Instala el SDK de .NET 10." >&2
    exit 1
fi

fuente=()
if [ -d "$raiz/paquetes" ]; then
    fuente=(--source "$raiz/paquetes")
fi

compilar() {
    local p="$1"
    echo "- QbaswingMultiTask.$p"
    dotnet build "$raiz/QbaswingMultiTask.$p/QbaswingMultiTask.$p.csproj" \
        -c "$config" --nologo ${fuente[@]+"${fuente[@]}"}
}

for p in Core Cli Tools; do
    compilar "$p"
done

case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*|Windows_NT)
        compilar Desktop
        compilar Installer
        ;;
    *)
        echo "Nota: Desktop e Installer son de Windows; en $(uname -s) solo se compilan Core, Cli y Tools." >&2
        ;;
esac

echo "Hecho ($config)."
