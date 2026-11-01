<!-- markdownlint-disable MD043 -->
<!-- Independent measurement report; the project README heading template does not apply. -->

# Stream behavior, allocation and throughput comparison

Baseline: v2.0.2 (`8efb8de`). Changes: stream reads/flushes, buffer retention,
ValueTask reads, lazy/per-stream Huffman scratch, reset APIs, bounded cleared
compressor buffers, encoder scratch reuse.

Environment: .NET 8.0.28, Windows ARM64 (OS build 26200), Release build. The
host reports Apple Silicon. Runs use memory-backed streams or a counting sink;
they do not measure network/disk latency.

Each case warms for at least 350 ms, then takes seven batches of at least 100
ms. The per-run statistic is the median time per operation across batches. The
table reports the median of **three complete runs per version**, with run order
alternated. Bytes/op are measured with GC.GetAllocatedBytesForCurrentThread;
async MemoryStream operations complete synchronously, so allocations remain on
the measured thread. Runner overhead can add a few bytes per long operation.
This is a diagnostic runner, not a formal BenchmarkDotNet confidence interval.
Differences of a few percent should be treated as noise or workload-dependent
tradeoffs.

## Timing and allocations

Lower time and allocation are better. All data sizes use binary units.
Fixed-small is a seven-byte message; large decode/compress cases process 1 MiB.
The many-dynamic-block case flushes platform-compressed input every 8 KiB.
Fixtures are validated against their complete original data before timing.

<!-- markdownlint-disable MD013 -->
| Case | Before ms/op | After ms/op | Before B/op | After B/op | Time change |
|---|---:|---:|---:|---:|---:|
| compress/empty | 0.0143 | 0.0122 | 285,248 | 22,920 | -14.7% |
| compress/2KiB | 0.0357 | 0.0344 | 285,248 | 22,920 | -3.5% |
| compress/1MiB-array | 22.1698 | 21.9396 | 293,488 | 22,928 | -1.0% |
| compress/1MiB-span | 22.4229 | 22.0154 | 293,488 | 22,928 | -1.8% |
| inflate/fixed-small | 0.0013 | 0.0009 | 59,112 | 41,696 | -25.8% |
| inflate/fixed-10000-empty-blocks | 0.3432 | 0.2069 | 2,138,904 | 41,696 | -39.7% |
| inflate/fixed-one-empty-block | 0.0013 | 0.0009 | 59,112 | 41,696 | -27.5% |
| inflate/dynamic-many-blocks | 5.6057 | 5.5771 | 526,250 | 62,042 | -0.5% |
| inflate/stored | 0.1491 | 0.1550 | 58,904 | 41,616 | +4.0% |
| inflate/zeros-sync | 0.2464 | 0.2480 | 62,544 | 62,040 | +0.6% |
| inflate/text-sync | 5.3878 | 5.4400 | 241,482 | 62,042 | +1.0% |
| inflate/zeros-memory-async | 0.2512 | 0.2551 | 81,048 | 62,040 | +1.6% |
| inflate/zeros-array-async | 0.2514 | 0.2526 | 81,048 | 80,472 | +0.5% |
| flush/100-messages | 0.5689 | 0.6389 | 288,224 | 32,656 | +12.3% |
| compress/reused-2KiB | n/a | 0.0303 | n/a | 0 | n/a |
| compress/reused-empty | n/a | 0.0093 | n/a | 0 | n/a |
<!-- markdownlint-enable MD013 -->

The array-based async API must still return `Task<int>`; its per-read task
allocations remain. The Memory-based API now avoids those tasks on synchronously
completing reads. Reused compression excludes one-time construction, which is
intentional: the same finished writer is reset between independent messages.

The largest benefits are allocation reductions: fresh small compression is about
92% lower; 10,000 empty fixed blocks are about 98% lower and about 40% faster;
many dynamic blocks allocate about 88% less. Large-payload throughput is broadly
similar rather than universally faster: stored decode measured about 4% slower,
and other cases vary around parity. Explicit reuse reduces per-message
allocations to zero in this workload and regression test.

Flush timing is **not equivalent work** across versions. Before, Flush only
flushed the sink; after, it emits pending compression bytes. In the 100-message
case, output grows from 2,638 to 4,214 bytes because each message is made
available before Finish. The approximately 12% time increase buys correct
message visibility and is not a pure throughput regression on equal behavior.

## Behavior and retained memory

<!-- markdownlint-disable MD013 -->
| Probe | Before | After |
|---|---:|---:|
| Payload bytes returned before requesting a withheld checksum trailer | 17,029 / 50,000 | 50,000 / 50,000 |
| Seven-byte message readable after Flush, before Finish | 0 | 7 |
| 8 MiB caller input retained after Write | yes | no |
| 8 MiB caller output retained after Read | yes | no |
| Source retained by disposed input wrapper | yes | no |
| CanWrite after Finish / End | true / true | false / false |
| Pre-canceled empty async read | cancellation ignored | cancellation observed |
<!-- markdownlint-enable MD013 -->

The retention probes use weak references, no-inlining helpers, full GC and
GC.KeepAlive on the wrapper. They check unwanted references, not process
working-set changes. The trailer probe intentionally supplies all compressed
payload bytes but withholds the trailer; it tests whether decoded output is
delivered before another source read.

## Memory tradeoffs

The compressor cache retains at most four cleared bundles: 1 MiB of array
payload process-wide, plus small object/array headers. Each active compressor
owns a distinct lease; concurrent cache misses allocate new buffers and a full
cache discards returned bundles. Custom window sizes bypass the cache. A cold or
heavily concurrent workload can still allocate the full buffers, so warm
allocation savings do not imply a 92% drop in peak resident memory. Reset
retains one active codec's buffers until it is disposed and avoids per-message
construction.

## Reproduce

Build the library first; the runner references a built DLL and deliberately has
no ProjectReference so the same executable source can target a saved baseline.

```powershell
dotnet build zlib.net.csproj -c Release
# Save this DLL outside the checkout before applying the changes if testing a baseline.
dotnet run --project benchmarks/zlib.net.StreamMemory.Perf -c Release -- results.json
# Override LibraryPath with an absolute path to a saved v2.0.2 DLL:
dotnet run --project benchmarks/zlib.net.StreamMemory.Perf -c Release -p:LibraryPath=C:/path/to/baseline/zlib.net.dll -- baseline.json
```

The runner discovers the new Reset method once by reflection and skips reuse
cases when measuring the older DLL. This reflection is confined to the benchmark
runner; production code does not use it.

Raw per-run timing/allocation data and representative behavior logs are in
[results/](results/). The checked-in figures are observations from one host, not
performance guarantees. Regression tests also cover true asynchronous
completion, tiny output buffers, independent concurrent decoders, cache
pressure, malformed input, dictionary/reset state, and byte-identical reset
output. Coverage-guided fuzzing and cross-platform timings remain separate from
these local measurements.
