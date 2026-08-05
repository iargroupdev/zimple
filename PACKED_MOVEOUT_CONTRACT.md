# Packed MoveOutAndTestResults Contract

This branch is a separate Zimple variant for a changed PLC contract. It keeps the application behavior the same, except trigger 8, `Serial_MoveOutAndTestResults`, now reads measurement data from one PLC string array instead of reading 9 fields per measurement row.

## PLC Tag

For each OP, the PLC must expose:

```text
{OP}.Serial_MoveOutAndTestResults.MeasureDataPacked
```

Type: string array  
Length: `LenghtMeasureData`

The application still reads the same header fields:

```text
{OP}.Station
{OP}.Serial_MoveOutAndTestResults.LenghtMeasureData
{OP}.Serial_MoveOutAndTestResults.SerialNumber
{OP}.Serial_MoveOutAndTestResults.GroupId
{OP}.Serial_MoveOutAndTestResults.GroupVersion
{OP}.Serial_MoveOutAndTestResults.Results
{OP}.Serial_MoveOutAndTestResults.Layer
{OP}.Serial_MoveOutAndTestResults.CheckMultiBoard
```

## Row Format

Each array element must contain 9 fields separated by `/`:

```text
HighLimit/LowLimit/MeasureKey/MeasureNotes/MeasureValue/Position/Result/Tolerance/UnitOfMeasure
```

Example:

```text
10.5/9.5/WIDTH_A/OK/10.1/1/1/0.5/mm
```

`Result` mapping:

```text
0 = Fail
1 = Pass
2 = False
3 = Retouch
```

## Expected PLC Reads

Legacy contract:

```text
8 + (9 * LenghtMeasureData)
```

Packed contract:

```text
8 + 1
```

For 60 measurement rows:

```text
Legacy: 548 reads
Packed: 9 reads
Saved: 539 reads
```

## Operational Notes

There is no automatic fallback to the legacy `MeasureData[i].Field` reads in this branch. If `MeasureDataPacked` is missing or malformed, trigger 8 fails loudly instead of silently returning to the high-pressure read pattern.

The per-PLC heavy gate remains in place. It protects each PLC from overlapping heavy trigger 8 requests while allowing different PLCs to progress in parallel.

Important PLC-side check: each packed row must fit inside the PLC string type exposed to the application. If the combined 9 fields can exceed the string capacity, increase the PLC string size or choose shorter field values before using this version in production. A truncated row will be parsed incorrectly or rejected.

## Test Mode Without CIMPLE

For PLC/parsing tests without the live CIMPLE server, set these values in `Zimple/App.config` before building/running:

```xml
<add key="LogPackedMoveOutParsedMeasures" value="true"/>
<add key="UseMockMesForMoveOutAndTestResults" value="true"/>
```

`LogPackedMoveOutParsedMeasures=true` writes every parsed measure to the live OP log.

`UseMockMesForMoveOutAndTestResults=true` skips the real `MES_HAI.Serial_MoveOutAndTestResults` call and returns a local success response:

```text
MOCK MES OK - parsed N packed measures
```

Keep both values `false` for production.
