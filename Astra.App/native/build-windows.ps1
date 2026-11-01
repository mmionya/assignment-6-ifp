$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
New-Item -ItemType Directory -Force -Path 'runtimes/win-x64/native' | Out-Null
& gcc -std=c11 -O2 -Wall -Wextra -Werror -shared astra_native.c -o runtimes/win-x64/native/astra_native.dll
if ($LASTEXITCODE -ne 0) { throw "Native build failed ($LASTEXITCODE)" }
