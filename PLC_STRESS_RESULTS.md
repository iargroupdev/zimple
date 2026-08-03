# PLC Stress Test Results

Date: 2026-08-03

These tests are offline simulations. They do not connect to a real PLC or MES. They model the current application structure:

1. one queue per PLC tab;
2. current `MeasureData[i].Field` PLC reads;
3. trigger 8, `Serial_MoveOutAndTestResults`, protected by the per-PLC heavy request gate;
4. configurable PLC read/write latency and MES latency.

## Per-Request PLC Pressure

Command:

```bash
python3 tools/moveout_plc_call_estimator.py 45 49 60 100
```

Result:

```text
MeasureData rows | Current PLC reads | PLC writes | Total PLC ops
              45 |               413 |          2 |          415
              49 |               449 |          2 |          451
              60 |               548 |          2 |          550
             100 |               908 |          2 |          910
```

## Scenario 1 - Moderate Burst

Command:

```bash
python3 tools/plc_stress_simulator.py --plcs 2 --ops-per-plc 30 --moveout-ratio 0.60 --min-measures 45 --max-measures 60 --read-ms 8 --write-ms 8 --speedup 50 --seed 100
```

Result:

```text
Requests: 60 total, 26 MoveOutAndTestResults, 34 other
PLC operations: 12495 reads, 120 writes, 12615 total
Modeled wall time: 120030 ms
Max simultaneous MoveOutAndTestResults: 1
MoveOutAndTestResults: reads=12412, avg_ms=8258, p95_ms=19435, max_ms=21752
```

## Scenario 2 - Aggressive Burst With Old Global Gate

Command:

```bash
python3 tools/plc_stress_simulator.py --plcs 3 --ops-per-plc 40 --moveout-ratio 0.70 --min-measures 45 --max-measures 60 --read-ms 8 --write-ms 8 --speedup 50 --gate-scope global --seed 200
```

Result:

```text
Requests: 120 total, 85 MoveOutAndTestResults, 35 other
PLC operations: 40544 reads, 240 writes, 40784 total
Modeled wall time: 393153 ms
Max simultaneous MoveOutAndTestResults: 1
Max simultaneous MoveOutAndTestResults per PLC: 1
MoveOutAndTestResults: reads=40442, avg_ms=11781, p95_ms=41003, max_ms=95478
```

## Scenario 3 - Same Burst With Per-PLC Gate

Command:

```bash
python3 tools/plc_stress_simulator.py --plcs 3 --ops-per-plc 40 --moveout-ratio 0.70 --min-measures 45 --max-measures 60 --read-ms 8 --write-ms 8 --speedup 50 --gate-scope plc --seed 200
```

Result:

```text
Requests: 120 total, 85 MoveOutAndTestResults, 35 other
PLC operations: 40544 reads, 240 writes, 40784 total
Modeled wall time: 145444 ms
Max simultaneous MoveOutAndTestResults: 3
Max simultaneous MoveOutAndTestResults per PLC: 1
MoveOutAndTestResults: reads=40442, avg_ms=4585, p95_ms=5182, max_ms=5254
```

## Scenario 4 - Extreme Burst With Old Global Gate

Command:

```bash
python3 tools/plc_stress_simulator.py --plcs 4 --ops-per-plc 50 --moveout-ratio 0.80 --min-measures 55 --max-measures 60 --read-ms 12 --write-ms 12 --mes-min-ms 150 --mes-max-ms 400 --speedup 100 --gate-scope global --seed 300
```

Result:

```text
Requests: 200 total, 155 MoveOutAndTestResults, 45 other
PLC operations: 81481 reads, 400 writes, 81881 total
Modeled wall time: 1146832 ms
Max simultaneous MoveOutAndTestResults: 1
Max simultaneous MoveOutAndTestResults per PLC: 1
MoveOutAndTestResults: reads=81358, avg_ms=26421, p95_ms=124242, max_ms=408239
```

## Scenario 5 - Extreme Burst With Per-PLC Gate

Command:

```bash
python3 tools/plc_stress_simulator.py --plcs 4 --ops-per-plc 50 --moveout-ratio 0.80 --min-measures 55 --max-measures 60 --read-ms 12 --write-ms 12 --mes-min-ms 150 --mes-max-ms 400 --speedup 100 --gate-scope plc --seed 300
```

Result:

```text
Requests: 200 total, 155 MoveOutAndTestResults, 45 other
PLC operations: 81481 reads, 400 writes, 81881 total
Modeled wall time: 303438 ms
Max simultaneous MoveOutAndTestResults: 4
Max simultaneous MoveOutAndTestResults per PLC: 1
MoveOutAndTestResults: reads=81358, avg_ms=7297, p95_ms=7620, max_ms=8619
```

## Conclusion

The current PLC data contract creates hundreds of PLC reads per `MoveOutAndTestResults` request. A global heavy gate over-protects the system by blocking independent PLCs. The per-PLC gate is the better compromise: it allows independent PLCs to progress in parallel while keeping each PLC protected from more than one heavy `MoveOutAndTestResults` at a time.

The next production check is to compare these modeled counts with real logs from the application:

```text
PlcReads
PlcWrites
PlcReadMs
MesMs
TotalMs
ExpectedCurrentContractReads
```
