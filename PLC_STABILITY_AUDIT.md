# Zimple PLC Stability Audit

Scope: bounded audit focused on current production risks: PLC timeouts, `Serial_MoveOutAndTestResults`, concurrent OP pressure, and evidence for the packed measurement-data PLC contract. `MES_HAI` remains mandatory and is treated as the client-owned integration boundary.

## Main Finding

The biggest controllable pressure point is trigger 8, `Serial_MoveOutAndTestResults`. With the legacy PLC contract, the application must read 8 header values plus 9 individual PLC string tags for every measurement row:

```text
current reads = 8 + (9 * LenghtMeasureData)
```

With 60 measurement rows, that is 548 PLC reads before the MES call, plus 2 PLC writes afterwards. This branch changes only trigger 8 measurement-data reads to a packed string array:

```text
packed reads = 8 + 1
```

The stability strategy is therefore:

1. measure the actual PLC read/write count in production logs;
2. serialize and pace the heaviest request per PLC/IP so each PLC is not hit by overlapping bursts;
3. cap dangerous measurement lengths;
4. keep trigger scanning/heartbeat away from active heavy work.

## Implemented Evidence

`PlcTagStore` counts every read/write operation made through the application PLC wrapper. Trigger 8 logs the real counts and timings for each request:

```text
PlcReads
PlcWrites
PlcReadMs
MesMs
TotalMs
LegacyReadEstimate
PackedReadEstimate
EstimatedSavedReads
```

This gives production evidence without needing PLC instrumentation. For trigger 8, expected packed reads are `9` when `LenghtMeasureData > 0`.

## Offline Evidence

Estimate the current PLC request pressure without a PLC:

```bash
python3 tools/moveout_plc_call_estimator.py
```

Run a concurrent stress simulation without a PLC:

```bash
python3 tools/plc_stress_simulator.py --plcs 3 --ops-per-plc 40 --moveout-ratio 0.70 --min-measures 45 --max-measures 60 --read-ms 8 --write-ms 8 --measure-contract packed
```

The simulator models the current application behavior:

1. one execution queue per PLC tab;
2. trigger 8 serialized by the per-PLC heavy request gate;
3. packed `MeasureDataPacked` reads;
4. configurable PLC read/write and MES latency.

This is not a replacement for a real PLC acceptance test, but it is useful to stress the request pattern and prove whether the application would generate excessive PLC operations.

## Audit Notes

1. The timeout pattern is consistent with too many small PLC reads during trigger 8, especially when several OPs finish close together.
2. The current per-PLC gate protects each PLC from simultaneous trigger 8 bursts without blocking unrelated PLCs.
3. This branch requires the PLC structure change described in `PACKED_MOVEOUT_CONTRACT.md`.
4. Error paths in trigger 8 log exceptions but do not always write a deterministic error response/status back to the PLC. That is a production-risk item for a second pass.
5. The application still combines UI, PLC protocol, MES calls, and logging in `ConnectionOP.xaml.cs`. A future refactor should split this into PLC service, MES service, trigger dispatcher, and UI layer.
6. The application has no automated PLC simulator tests wired into the C# project. The Python simulator is a practical first layer until we can introduce a fake PLC abstraction.

## Library Assessment

The current PLC library, `libplctag`, is still a reasonable short-term choice. The problem found here is primarily the access pattern and concurrency pressure, not proof that the library itself is unstable.

Better long-term options depend on what the PLC/customer allows:

1. OPC UA via a server/middleware layer is the most industrially robust architecture. It decouples the app from raw tag polling, gives better diagnostics, and makes it easier to test. Kepware/KEPServerEX or an OPC UA server in the cell are typical options; the .NET app can then use the OPC Foundation .NET Standard stack.
2. Commercial Allen-Bradley .NET drivers may improve support and diagnostics, but they will not solve the `9 * N` read pattern by themselves.
3. Keep `MES_HAI` unchanged as the client-owned MES boundary.

Recommendation: keep `libplctag` for now, deploy the evidence layer, run stress/production logs, and only reassess the PLC communication stack if timeouts remain after the per-PLC heavy request gate and pacing are confirmed under real load.

References checked:

- libplctag.NET: https://www.nuget.org/packages/libplctag/
- libplctag.NET source: https://github.com/libplctag/libplctag.NET
- OPC Foundation UA-.NETStandard: https://github.com/OPCFoundation/UA-.NETStandard
- OPC Foundation .NET Standard package: https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua
- PTC Kepware driver documentation: https://support.ptc.com/help/kepware/drivers/en/
