# Zimple PLC Stability Audit

Scope: bounded audit focused on the current production pain points: PLC timeouts, `Serial_MoveOutAndTestResults`, concurrent OP pressure, and evidence that changes reduce PLC reads. `MES_HAI` remains mandatory and is treated as the client-owned integration boundary.

## Main Finding

The biggest controllable pressure point is trigger 8, `Serial_MoveOutAndTestResults`. Before this change, the application read 8 header values plus 9 individual PLC string tags for every measurement row:

```text
legacy reads = 8 + (9 * LenghtMeasureData)
```

With 60 measurement rows, that is 548 PLC reads before the MES call, plus 2 PLC writes afterwards. If several OPs do this at the same time, the PLC and network get a burst of small synchronous requests.

## Implemented Evidence

`PlcTagStore` now counts every read/write operation made through the application PLC wrapper. Trigger 8 logs the real counts and timings for each request:

```text
MeasureReadMode
PlcReads
PlcWrites
PlcReadMs
MesMs
TotalMs
LegacyReadEstimate
PackedReadEstimate
EstimatedSavedReadsWhenPacked
```

This gives production evidence without needing PLC instrumentation. In legacy mode the expected reads are `8 + 9*N`. In packed mode the expected reads are `9` when there are measurements: 8 header reads plus 1 packed array read.

## Packed MeasureData Contract

The application can now read all measurement rows from one PLC string array when enabled:

```text
{OP}.Serial_MoveOutAndTestResults.MeasureDataPacked
```

Each row must contain 9 fields separated by `|`:

```text
HighLimit|LowLimit|MeasureKey|MeasureNotes|MeasureValue|Position|Result|Tolerance|UnitOfMeasure
```

The feature is disabled by default in `App.config`:

```xml
<add key="UsePackedMoveOutMeasureData" value="false"/>
```

Turn it to `true` only after the PLC exposes `MeasureDataPacked`. If the packed read fails, the application logs the error and falls back to the legacy field-by-field reads.

## Expected Read Reduction

Run this without a PLC:

```bash
python3 tools/moveout_plc_call_estimator.py
```

Example estimates:

```text
MeasureData rows | Legacy PLC reads | Packed PLC reads | Saved reads | Reduction
              49 |              449 |                9 |         440 |     98.0%
              60 |              548 |                9 |         539 |     98.4%
```

## Audit Notes

1. The current timeout pattern is consistent with too many small PLC reads during trigger 8, especially when several OPs finish close together.
2. The existing global gate and trigger queue reduce PLC overlap, but they are mitigations. The real fix is reducing the number of PLC reads per operation.
3. The application still combines UI, PLC protocol, MES calls, and logging in `ConnectionOP.xaml.cs`. A future refactor should split this into PLC service, MES service, trigger dispatcher, and UI layer.
4. Error paths in trigger 8 log exceptions but do not always write a deterministic error response/status back to the PLC. That is a production-risk item for a second pass.
5. The application has no automated PLC simulator tests. The new read/write counters and estimator are the first repeatable evidence layer; a fake `IPlcTagStore` interface would be the next testing improvement.

## Library Assessment

The current PLC library, `libplctag`, is still a reasonable short-term choice. The problem found here is primarily access pattern, not proof that the library itself is unstable.

Better long-term options depend on what the PLC/customer allows:

1. OPC UA via a server/middleware layer is the most industrially robust architecture. It decouples the app from raw tag polling, gives better diagnostics, and makes it easier to test. Kepware/KEPServerEX or an OPC UA server in the cell are typical options; the .NET app can then use the OPC Foundation .NET Standard stack.
2. Commercial Allen-Bradley .NET drivers may improve support and diagnostics, but they will not solve the `9 * N` read pattern by themselves.
3. Keep `MES_HAI` unchanged as the client-owned MES boundary.

Recommendation: keep `libplctag` for now, deploy this evidence layer, and ask the PLC side to expose `MeasureDataPacked`. Reassess the PLC communication stack only after logs show whether timeouts remain once trigger 8 is reduced from hundreds of reads to one packed measure read.

References checked:

- libplctag.NET: https://www.nuget.org/packages/libplctag/
- libplctag.NET source: https://github.com/libplctag/libplctag.NET
- OPC Foundation UA-.NETStandard: https://github.com/OPCFoundation/UA-.NETStandard
- OPC Foundation .NET Standard package: https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua
- PTC Kepware driver documentation: https://support.ptc.com/help/kepware/drivers/en/
