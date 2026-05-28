# KioskClinicaPC — notas para Claude

## Build

**No intentar `dotnet build` ni MSBuild desde CLI.** Solo runtime .NET 9.0 instalado en `C:\Program Files\dotnet` (sin SDK). Ni VS2022 Community ni Rider 2025.2.3 exponen su SDK al MSBuild externo:

- `dotnet build` → "No .NET SDKs were found"
- VS2022 MSBuild (`C:\Program Files\Microsoft Visual Studio\2022\Community\Msbuild\Current\Bin\MSBuild.exe`) → MSB4236 "Microsoft.NET.Sdk no encontrado"
- Rider MSBuild (`C:\Program Files\JetBrains\JetBrains Rider 2025.2.3\tools\MSBuild\Current\Bin\MSBuild.exe`) → mismo error

Verificacion de build/run la hace el usuario desde Rider (run config interna gestiona el SDK bundle). Tras editar XAML/C# no perder tiempo intentando compilar.

## Stack

- WPF .NET 8 (`net8.0-windows`), proyecto en `KioskClinicaPC/KioskClinicaPC.csproj`
- Paleta naranja monocromo: `CyanColor=#F37A4A`, `MagentaColor=#FFB069` (nombres confusos, mantener)
- Pantallas en `MainWindow.xaml`: `Screen0_Attract`, `Screen1_Scan`, `Screen2_Main`, `Screen3_Detail`
