# Party statistics checks

Build to a local staging folder, then run checks without launching the game:

```powershell
dotnet build DPSMeter.csproj -c ExportRelease -p:Sts2ModsDir=dist/DPSMeter
dotnet run --project checks/SupportChecks.csproj
python -m unittest discover -s tests -v
```

Set `-p:GameData=/path/to/game/data` on the check command for a different location
or platform. Set `Sts2DataDir` on the mod build as needed. `MeterAssembly` can point
to a different built DLL. Game assemblies must be obtained from a local installation;
none are included in this repository. Use matching `TargetFramework` overrides on
both projects when testing a different game runtime.

The console checks validate accounting, per-life death deduplication, effective healing,
kill attribution, archive identity, disk persistence, menu clearing, replay rollback,
and Harmony binding in the isolated test process. They do not launch or modify the game.
Python checks also require Bash for the existing shell-script syntax checks.

Accounting checks run by default. To include Harmony bindings, pass `--bindings`
under the runtime supported by the game's bundled Harmony. For v0.107.1, validation
used .NET 9; that Harmony build rejects CoreCLR 10.0.12. With a .NET 9 runtime installed:

```powershell
dotnet restore DPSMeter.csproj -p:TargetFramework=net9.0 --force
dotnet build DPSMeter.csproj --no-restore -c ExportRelease -p:TargetFramework=net9.0 -p:Sts2ModsDir=dist/DPSMeter
dotnet restore checks/SupportChecks.csproj -p:TargetFramework=net9.0 --force
dotnet build checks/SupportChecks.csproj --no-restore -p:TargetFramework=net9.0
dotnet checks/bin/Debug/net9.0/SupportChecks.dll --bindings
```

These overrides only affect the local validation build. They do not install a mod
or change the repository's default target framework.
