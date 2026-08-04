# Zimple

WPF/.NET Framework 4.7.2 application for PLC communication and MES integration.

## Build

1. Download the repository ZIP from GitHub and extract it.
2. Open `Zimple.sln` on Windows with Visual Studio 2022.
3. If Visual Studio asks, restore NuGet packages.
4. Run `Build > Clean Solution`.
5. Run `Build > Rebuild Solution`.
6. Start the `Zimple` project.

The MES/runtime DLLs required by this project are included under `Zimple/Lib/net462`, so the solution does not depend on a local `Downloads\MES_HAI...` folder.

## Current stabilization

This version includes a stabilization pass focused on the recurring PLC communication and high CPU problems:

- serialized PLC tag access inside `PlcTagStore`;
- trigger scan slowed to a stable interval and protected against re-entry;
- queued trigger worker is now started and cancelled with the UI lifecycle;
- heavy `Serial_MoveOutAndTestResults` requests are serialized and paced to reduce PLC pressure;
- heartbeat runs off the UI thread and skips while trigger execution is using the PLC;
- PLC array reads/writes go through safe helpers;
- measurement data length from PLC is bounded;
- passwords are no longer written to UI/file logs;
- binding redirects are enabled for .NET Framework assembly conflicts;
- generated files, build output, logs, and signing material are ignored by Git.

See `STABILITY_NOTES.md` for details and follow-up checks.

## Offline PLC stress tools

The repository includes offline tools to estimate/stress the current PLC request pattern without a real PLC:

```bash
python3 tools/moveout_plc_call_estimator.py
python3 tools/plc_stress_simulator.py --plcs 3 --ops-per-plc 40 --moveout-ratio 0.70 --min-measures 45 --max-measures 60 --read-ms 8 --write-ms 8 --speedup 50 --gate-scope plc --measure-contract packed
```

See `PLC_STABILITY_AUDIT.md`, `PLC_STRESS_RESULTS.md`, and `PACKED_MOVEOUT_CONTRACT.md`.
