# Zimple

WPF/.NET Framework 4.7.2 application for PLC communication and MES integration.

## Build

Open `Zimple.sln` on Windows with Visual Studio 2022, restore NuGet packages if prompted, then run `Clean Solution` and `Rebuild Solution`.

## Current stabilization

This version includes a stabilization pass focused on the recurring PLC communication and high CPU problems:

- serialized PLC tag access inside `PlcTagStore`;
- trigger scan slowed to a stable interval and protected against re-entry;
- queued trigger worker is now started and cancelled with the UI lifecycle;
- heartbeat runs off the UI thread and skips while trigger execution is using the PLC;
- PLC array reads/writes go through safe helpers;
- measurement data length from PLC is bounded;
- passwords are no longer written to UI/file logs;
- binding redirects are enabled for .NET Framework assembly conflicts;
- generated files, build output, logs, and signing material are ignored by Git.

See `STABILITY_NOTES.md` for details and follow-up checks.
