# PLC Stress Test Results - Packed MoveOut Variant

Date: 2026-08-04

These tests are offline simulations. They do not connect to a real PLC or MES. In this branch they model the packed `MoveOutAndTestResults` contract:

1. one queue per PLC tab;
2. packed `MeasureDataPacked` PLC reads;
3. trigger 8, `Serial_MoveOutAndTestResults`, protected by the per-PLC heavy request gate;
4. configurable PLC read/write latency and MES latency.

## Per-Request PLC Pressure

Command:

```bash
python3 tools/moveout_plc_call_estimator.py 45 49 60 100
```

Result:

```text
MeasureData rows | Legacy reads | Packed reads | Saved reads | Reduction | Packed total ops
              45 |          413 |            9 |         404 |      97.8% |              11
              49 |          449 |            9 |         440 |      98.0% |              11
              60 |          548 |            9 |         539 |      98.4% |              11
             100 |          908 |            9 |         899 |      99.0% |              11
```

## Aggressive Burst - Packed Contract

Command:

```bash
python3 tools/plc_stress_simulator.py --plcs 3 --ops-per-plc 40 --moveout-ratio 0.70 --min-measures 45 --max-measures 60 --read-ms 8 --write-ms 8 --speedup 50 --gate-scope plc --measure-contract packed --seed 200
```

Result:

```text
Requests: 120 total, 85 MoveOutAndTestResults, 35 other
PLC operations: 867 reads, 240 writes, 1107 total
Modeled wall time: 26795 ms
Max simultaneous MoveOutAndTestResults: 3
Max simultaneous MoveOutAndTestResults per PLC: 1
MoveOutAndTestResults: reads=765, avg_ms=785, p95_ms=907, max_ms=944
```

## Same Burst - Legacy Contract Baseline

Command:

```bash
python3 tools/plc_stress_simulator.py --plcs 3 --ops-per-plc 40 --moveout-ratio 0.70 --min-measures 45 --max-measures 60 --read-ms 8 --write-ms 8 --speedup 50 --gate-scope plc --measure-contract legacy --seed 200
```

Result:

```text
Requests: 120 total, 85 MoveOutAndTestResults, 35 other
PLC operations: 40544 reads, 240 writes, 40784 total
Modeled wall time: 145338 ms
Max simultaneous MoveOutAndTestResults: 3
Max simultaneous MoveOutAndTestResults per PLC: 1
MoveOutAndTestResults: reads=40442, avg_ms=4586, p95_ms=5201, max_ms=5281
```

## Conclusion

The packed PLC contract removes the main pressure source. In the aggressive 3-PLC scenario, PLC reads fell from `40544` to `867`, while keeping the per-PLC heavy gate in place.

The production check is to compare these modeled counts with real logs from the application:

```text
MeasureReadMode
PlcReads
PlcWrites
PlcReadMs
MesMs
TotalMs
LegacyReadEstimate
PackedReadEstimate
EstimatedSavedReads
```
