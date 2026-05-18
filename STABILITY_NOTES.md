# Zimple stability notes

## Immediate causes found

- Triggers were enqueued but the queue worker was never started. This could leave PLC operations stuck in `processing`.
- Heartbeat, trigger scanning, and trigger execution could touch PLC tags at the same time.
- Trigger scan was running every 200 ms, faster than worst-case PLC reads.
- Operation names and measurement counts were trusted directly from PLC data.
- Passwords were written to UI/file logs during login.
- Single-instance protection was effectively commented out.
- Binding redirects were disabled, which can surface assembly conflicts such as `System.ValueTuple`.

## Changes made

- `PlcTagStore` now serializes tag creation/read/write/dispose per PLC instance.
- Trigger scanning now runs every 1 second and skips while queued work is executing.
- The trigger queue starts on control load and is cancelled on unload.
- Heartbeat is asynchronous and skips while queued work is executing.
- PLC string arrays are read/written through `PlcTagStore` helpers.
- Measurement data is capped at 100 entries.
- Login logs exclude password values.
- Binding redirect generation is enabled in the project.
- Git is configured to ignore generated output and local sensitive files.

## Validation checklist

- Open `Zimple.sln` in Visual Studio on Windows.
- Run `Clean Solution`, then `Rebuild Solution`.
- If the build fails, capture the full Build Output.
- Test with one PLC first, then multiple PLCs.
- Watch CPU, memory, and PLC comms while one trigger is processing and while the app is idle.
