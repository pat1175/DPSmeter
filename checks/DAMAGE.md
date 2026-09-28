# Outgoing damage checks

With the .NET SDK and game installed, build without installing into the game:

```powershell
dotnet build DPSMeter.csproj -c ExportRelease -p:Sts2ModsDir=dist/DPSMeter
dotnet run --project checks/DamageChecks.csproj
python -m unittest discover -s tests -v
```

Set `-p:GameData=/path/to/game/data` on the check command for a different location
or platform. Set `Sts2DataDir` on the mod build as needed. The check project accepts
`MeterAssembly` for a custom build output path. No game files are bundled or changed.
Tests exercise the actual mod damage resolver using fully blocked, partially blocked,
unblocked, overkill and zero-damage results. They do not launch the game.
