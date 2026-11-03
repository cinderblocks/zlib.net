<!-- markdownlint-disable MD043 -->
<!-- Independent benchmark report; the project README template does not apply. -->

# Additional codec performance and memory experiments

This round starts at PR #13's previous head, `bf13e1d`, rather than v2.0.2.
The earlier [stream report](../zlib.net.StreamMemory.Perf/README.md) remains a
historical comparison against v2.0.2. These gains must not be added as
percentages.

## Method

Windows ARM64, .NET 8.0.28, Release, memory-backed streams. The same signed
diagnostic runner loads a saved baseline or candidate assembly. It uses internal
initialization to measure memory levels on the old assembly as well. There is no
reflection inside measured operations. The deterministic fixtures cover text,
random bytes, mixed data, zeros, 2 KiB messages, and empty messages.

Each operation warms for at least one second and 64 invocations, followed by
seven batches lasting at least 100 ms each. Each run reports the median batch
time and median allocated bytes per operation. Final comparisons use three runs
per assembly. The baseline was sampled in an interleaved initial series; that
series exposed a regression in the by-value Span prototype. After isolating it,
the final candidate was measured three more times. Processes run sequentially.
Compression output is platform-decoded before timing; SHA-256
hashes record compressed output for the low-level compression cases.

Compression, level, Span-write and direct-decode cases reuse initialized codecs.
They isolate steady-state CPU and allocate zero bytes in the measured body.
The reader cases construct and dispose a new decoder for every operation and
therefore measure warmed cache allocation savings. Buffer payload is an exact
array-size calculation, not a process working-set measurement. These tests do
not measure peak RSS, concurrent throughput, disk/network I/O, or x64 timings.
Small percentage differences are not proof of a portable speed improvement.

A baseline control was rerun after the final candidate series and saved as
`results/baseline-control.json`; it is not included in the three-run medians.
It confirms the reset/allocation conclusions, but also shows host variability:
the text-compression case rose from a 26.44 ms median to 36.17 ms in that run,
and raw stored decoding with 64 KiB output moved from 0.0279 to 0.0348 ms.
This is why the small CPU differences are presented as observations. The
low-level level-0 slowdown also persisted in this full-suite control. However,
fresh processes filtered to level 0 reverse the result: the baseline takes about
0.247 ms and the retained candidate about 0.215 ms. An attempted array-specific
copy/checksum restoration took about 0.248 ms and was rejected. These
warmup-history-sensitive CPU timings do not establish a universal level-0 gain
or loss. The filtered runs are preserved separately in the exploratory data.

The largest repeatable differences are warmed allocation reductions and smaller
message/reset costs. Dynamic reader allocation falls from 62,040 to 11,896 bytes
(80.8%); stored reader allocation falls from 41,616 to 8,824 bytes (78.8%).
The additional idle decoder cache is capped at 50,048 payload bytes, and memory
level 6 saves 96 KiB per active compressor.

Empty reset/compression measured 88.6% less time; reused 2 KiB compression
measured about 40% less time. The many-block text reader measured about 14% less
time, and 1 MiB Span writes at compression level 0 about 10-12% less time.
Large-data CPU results are mixed: low-level level-0 compression measured about
11-12% slower, while array random compression at level 6 measured about 3%
slower. These regressions are included in the table rather than hidden behind
the allocation savings. Timings are single-host observations, not portable
speed guarantees or formal confidence intervals.

The rejected combined by-value Span prototype regressed zero-filled compression
from about 2.0 to 2.7 ms. Readonly-reference input passing restored it to about
2.05 ms in the final run set. Removing the match intrinsic did not fix that
regression. Raw exploratory results preserve this investigation.

The compressed hash changes only for `compress/mixed/mem8` in these low-level
cases: 363,985 bytes become 363,321 bytes. Span level-0 writes also now follow
array-write block boundaries instead of the old 16 KiB staging chunks; valid
encodings need not be byte-identical across versions. Fresh versus Reset output
remains identical for the same options and write pattern.

## Repeated measurements

Median of three runs; negative time change is faster. All large inputs are
1 MiB. Allocation figures for reader cases include construction/disposal.

<!-- markdownlint-disable MD013 -->
| Case | Before ms | After ms | Before B | After B | Time |
| --- | ---: | ---: | ---: | ---: | ---: |
| compress/text/mem6 | 27.7651 | 26.2212 | 0 | 0 | -5.6% |
| compress/text/mem8 | 26.4354 | 25.2555 | 0 | 0 | -4.5% |
| compress/random/mem6 | 30.9256 | 31.2582 | 0 | 0 | +1.1% |
| compress/random/mem8 | 17.1522 | 17.4884 | 0 | 0 | +2.0% |
| compress/mixed/mem6 | 10.5761 | 10.7421 | 0 | 0 | +1.6% |
| compress/mixed/mem8 | 7.5076 | 7.3204 | 0 | 0 | -2.5% |
| compress/zeros/mem6 | 2.0269 | 2.0318 | 0 | 0 | +0.2% |
| compress/zeros/mem8 | 2.0207 | 2.0511 | 0 | 0 | +1.5% |
| compress/small/mem6 | 0.0163 | 0.0133 | 0 | 0 | -18.2% |
| compress/small/mem8 | 0.0218 | 0.0132 | 0 | 0 | -39.6% |
| compress/empty/mem6 | 0.0027 | 0.0009 | 0 | 0 | -67.2% |
| compress/empty/mem8 | 0.0086 | 0.0010 | 0 | 0 | -88.6% |
| level/text/0 | 0.2299 | 0.2546 | 0 | 0 | +10.8% |
| level/text/1 | 5.8749 | 5.2263 | 0 | 0 | -11.0% |
| level/text/9 | 66.3924 | 63.7218 | 0 | 0 | -4.0% |
| level/random/0 | 0.2290 | 0.2569 | 0 | 0 | +12.2% |
| level/random/1 | 17.2812 | 16.8784 | 0 | 0 | -2.3% |
| level/random/9 | 17.3177 | 17.5133 | 0 | 0 | +1.1% |
| level/zeros/0 | 0.2279 | 0.2536 | 0 | 0 | +11.3% |
| level/zeros/1 | 0.3262 | 0.3398 | 0 | 0 | +4.2% |
| level/zeros/9 | 2.0187 | 2.0691 | 0 | 0 | +2.5% |
| inflate/text/single/4096 | 1.0500 | 1.0627 | 62,040 | 11,896 | +1.2% |
| inflate/text/single/65536 | 1.0719 | 1.0438 | 62,040 | 11,896 | -2.6% |
| inflate/text/many/4096 | 1.4668 | 1.2677 | 62,040 | 11,896 | -13.6% |
| inflate/text/many/65536 | 1.4697 | 1.2641 | 62,040 | 11,896 | -14.0% |
| inflate/random/single/4096 | 0.1395 | 0.1381 | 41,616 | 8,824 | -1.0% |
| inflate/random/single/65536 | 0.1420 | 0.1377 | 41,616 | 8,824 | -3.1% |
| inflate/random/many/4096 | 0.1355 | 0.1319 | 41,696 | 8,904 | -2.6% |
| inflate/random/many/65536 | 0.1409 | 0.1366 | 41,696 | 8,904 | -3.1% |
| inflate/zeros/single/4096 | 0.2911 | 0.2692 | 62,040 | 11,896 | -7.5% |
| inflate/zeros/single/65536 | 0.2646 | 0.2478 | 62,040 | 11,896 | -6.3% |
| inflate/zeros/many/4096 | 0.3951 | 0.3774 | 62,040 | 11,896 | -4.5% |
| inflate/zeros/many/65536 | 0.3961 | 0.3728 | 62,040 | 11,896 | -5.9% |
| flush/group1 | 0.4673 | 0.4545 | 31,640 | 31,640 | -2.7% |
| flush/group10 | 0.4162 | 0.4173 | 28,224 | 28,224 | +0.2% |
| flush/group100 | 0.4165 | 0.3990 | 28,432 | 28,432 | -4.2% |
| write/text/level0/array | 0.2400 | 0.2483 | 0 | 0 | +3.5% |
| write/text/level0/span | 0.2651 | 0.2392 | 0 | 0 | -9.8% |
| write/text/level6/array | 26.4589 | 25.1069 | 0 | 0 | -5.1% |
| write/text/level6/span | 26.9232 | 25.2713 | 0 | 0 | -6.1% |
| write/random/level0/array | 0.2443 | 0.2352 | 0 | 0 | -3.7% |
| write/random/level0/span | 0.2684 | 0.2355 | 0 | 0 | -12.3% |
| write/random/level6/array | 16.8337 | 17.3913 | 0 | 0 | +3.3% |
| write/random/level6/span | 16.9290 | 17.5213 | 0 | 0 | +3.5% |
| write/small/level0/array | 0.0085 | 0.0008 | 0 | 0 | -90.8% |
| write/small/level0/span | 0.0085 | 0.0008 | 0 | 0 | -90.7% |
| write/small/level6/array | 0.0216 | 0.0133 | 0 | 0 | -38.5% |
| write/small/level6/span | 0.0216 | 0.0134 | 0 | 0 | -37.8% |
| decode-direct/stored/wrapped/4096 | 0.1180 | 0.1200 | 0 | 0 | +1.8% |
| decode-direct/stored/raw/4096 | 0.0359 | 0.0387 | 0 | 0 | +7.7% |
| decode-direct/stored/wrapped/65536 | 0.1144 | 0.1129 | 0 | 0 | -1.4% |
| decode-direct/stored/raw/65536 | 0.0279 | 0.0323 | 0 | 0 | +15.5% |
| decode-direct/stored/wrapped/1048576 | 0.1126 | 0.1124 | 0 | 0 | -0.2% |
| decode-direct/stored/raw/1048576 | 0.0294 | 0.0303 | 0 | 0 | +2.9% |
<!-- markdownlint-enable MD013 -->

### Memory level tradeoff

Both settings reuse a live codec. The 96 KiB saving is in active array payload;
it is separate from cache allocation savings.

<!-- markdownlint-disable MD013 -->
| Input | Level 8 ms | Level 6 ms | Time | Level 8 bytes | Level 6 bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| text | 25.2555 | 26.2212 | +3.8% | 169,469 | 170,325 |
| random | 17.4884 | 31.2582 | +78.7% | 1,048,902 | 1,049,862 |
| mixed | 7.3204 | 10.7421 | +46.7% | 363,321 | 366,597 |
| zeros | 2.0511 | 2.0318 | -0.9% | 1,039 | 1,039 |
| small | 0.0132 | 0.0133 | +1.4% | 552 | 552 |
| empty | 0.0010 | 0.0009 | -8.9% | 8 | 8 |
<!-- markdownlint-enable MD013 -->

### Flush batching

<!-- markdownlint-disable MD013 -->
| Messages per flush | ms / 100 messages | Compressed bytes |
| --- | ---: | ---: |
| 1 | 0.4545 | 3,431 |
| 10 | 0.4173 | 1,887 |
| 100 | 0.3990 | 1,719 |
<!-- markdownlint-enable MD013 -->

## Evaluation of the proposed ideas

1. **Compression match searching:** use the runtime's trailing-zero intrinsic
   to locate the differing byte of a word on .NET 8. Older targets retain the
   portable implementation. Tests cover every mismatch position and bit for
   eight unaligned offsets, alongside full compression roundtrips.
2. **Smaller active compressors:** expose memory levels 1-9 through both APIs.
   Level 6 uses 163,840 bytes for the main buffers instead of 262,144 bytes at
   level 8, saving 98,304 bytes (37.5%). The history window stays 32 KiB.
   Lower levels are opt-in because speed and compressed size can worsen.
   Tests cover every memory level, compression levels 0/1/6/9, random/text/mixed
   and empty inputs, small output buffers, independent resets, and invalid
input.
3. **Dynamic Huffman construction:** replace three-element array copies with
   scalar stores, remove the temporary entry array, and stop clearing stack and
   offset slots that are assigned before use. Keep zeroing the code counts.
   Mixed-block reuse, malformed input, fragmented reads, concurrency, and fuzz
   corpus replay cover the state transitions.
4. **Initialization and clearing:** use runtime bulk clearing for the hash table
   on reset and full flush. Do not use generation tags: they would add per-entry
   memory and a check on every hash access. Keep clearing cached arrays before
   publication. Existing reset/dictionary and full-flush tests cover isolation.
5. **Span-native compression:** pass a call-scoped input ref struct through the
   encoder and copy directly into its window. A span never enters heap state or
   survives an await. Checksum calculation uses the copied window bytes, keeping
   the array-based checksum implementation. Tests cover stack-backed input,
   overwriting it after Write, sliced input, tiny buffers, reset and flush
modes.
   Span decompression still stages through a bounded pooled array.
6. **Rental and reuse:** cache one cleared default decoder window and one lazy
   Huffman table. The additional cache holds at most 50,048 payload bytes. Cache
   misses allocate and custom windows bypass the window slot. Active streams
   own separate arrays. This lowers allocation pressure, not active buffer size.
   Tests cover exclusive leases, cleared data, custom windows, parallel streams,
   malformed-input disposal and repeated End. Wrapper buffers remain ordinary
   arrays: protected buffer fields can be retained by subclasses, making shared
   rental a compatibility/ownership problem. General ArrayPool buckets would
   also round up the non-power-of-two Huffman table.
7. **Stack allocation:** tested both all small tree scratch on the stack and
   only the 288-entry value workspace. They saved about 1.2-1.5 KiB per dynamic
   decoder, but representative many-block trials were slower than reused arrays.
   Neither prototype is retained. Resumable history and decode tables cannot be
   replaced by method-local stack storage.
8. **Direct decompression output:** prototyped large stored runs into caller
   output with only trailing history copied to the ring. Crafted tests covered
   wrapped history, following matches at distances 1/7/32768, raw and wrapped
   streams, checksums, output offsets and small reads. It passed correctness
   checks but did not deliver a repeatable CPU improvement; one raw 64
KiB-output
   comparison regressed from 0.0364 to 0.0386 ms. It is not retained. General
   Huffman output directly into caller memory would require a different history
   lookup design; this experiment does not establish a gain for that rewrite.
9. **Common-path specialization:** the implementation already has separate
   stored/fast/slow encoders and a fast Huffman decoder. This round specializes
   modern compression Span input while preserving the portable array adapter.
   Raw and wrapped stored-decode timings serve as controls. Duplicating whole
   raw/wrapped engines is not justified by evidence from these measurements.
10. **Block decisions:** existing code compares stored, fixed and dynamic block
    costs, and skips tree construction at compression level 0. This is separate
    from the early block-boundary heuristic inherited from the old port.
    Native zlib 1.1.3 gated that heuristic behind `TRUNCATE_BLOCK`; the managed
    port executes it unconditionally. Native zlib 1.3.1 no longer includes it.
    Removing it reduces the 1 MiB mixed fixture from 363,985 to 363,321 bytes.
    Three mixed-data golden vectors at compression levels 4/6/9 deliberately
    change to smaller encodings; other existing golden vectors stay pinned.
    New tests check the size improvement and interoperability across changing
    entropy, small buffers, memory levels and resets.
    Native sources:
    [zlib 1.1.3](https://github.com/madler/zlib/blob/v1.1.3/trees.c),
    [zlib 1.3.1](https://github.com/madler/zlib/blob/v1.3.1/trees.c).
11. **Flush batching:** measure one sync flush per 1/10/100 messages. This is a
    caller-controlled latency/ratio tradeoff; the library must continue honoring
    each explicit Flush. No implicit batching or delayed message visibility is
    introduced.

## Reproduction

Build and save the baseline DLL before changing the checkout. Then build the
runner twice, once against each saved assembly, into separate directories:

```powershell
dotnet build zlib.net.csproj -c Release -f net8.0
dotnet build benchmarks/zlib.net.Algorithm.Perf -c Release `
  -p:LibraryPath=C:/absolute/baseline/zlib.net.dll -o baseline-runner
dotnet build benchmarks/zlib.net.Algorithm.Perf -c Release `
  -p:LibraryPath=C:/absolute/candidate/zlib.net.dll -o candidate-runner
dotnet baseline-runner/zlib.net.Benchmarks.dll baseline.json
dotnet candidate-runner/zlib.net.Benchmarks.dll candidate.json
```

A second runner argument filters case names, for example `inflate/` or `write/`.
Run comparison processes sequentially. Raw final JSON and console output are in
`results/`; exploratory measurements are separate and used different warmups in
some early trials. They must not be treated as a controlled final comparison.

## Validation

All local checks passed on the final retained implementation:

- net8.0 and netstandard2.1 libraries: 320 tests each on .NET 8.
- netstandard2.0 library: 277 tests on .NET 8 and 258 on .NET Framework 4.8.
- Fuzz corpus replay: 37 inflate inputs and 36 fresh/reset roundtrip inputs.
- Release package/symbol creation and the standalone benchmark build.
- Regression coverage for every memory level, word mismatch/bit/alignment,
  stack-backed Span input, flush modes, changing entropy and golden outputs.
- Ubuntu, Windows and macOS CI tests and the CI package job passed for the code
  changes. No sustained fuzz campaign or x64 benchmark was run locally.

The prior Ubuntu allocation-test failure was isolated to warmup-sensitive
measurement. The test now warms more calls and checks the minimum of three
batches: recurring per-message allocations still fail, while a one-time runtime
initialization does not count as codec scratch.

## Experimental patches

The `experiments/` directory contains reproducible rejected prototypes, excluded
from the production library. `stack-all.patch` and `stack-values.patch` were
measured against the scalar Huffman implementation at `54a67df`.
`direct-stored.patch` was measured against `95ad439` and includes its crafted
correctness tests. Each patch passes `git apply --check` separately against the
retained implementation. They are research artifacts, not recommendations.
