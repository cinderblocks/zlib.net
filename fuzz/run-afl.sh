#!/usr/bin/env bash
# Fuzzes one target with AFL++ and SharpFuzz. Needs Linux, afl-fuzz, and `dotnet tool install -g SharpFuzz.CommandLine`.
#
#   fuzz/run-afl.sh <inflate|roundtrip> [seconds]
#
# Exits non-zero if the fuzzer found a crash, or if it did not see the library's code at all (which means the wrong
# assembly was instrumented). Results are left in fuzz/work/<target>/findings.
set -euo pipefail

target="${1:?usage: fuzz/run-afl.sh <inflate|roundtrip> [seconds]}"
seconds="${2:-300}"
root="$(cd "$(dirname "$0")/.." && pwd)"
work="$root/fuzz/work/$target"

rm -rf "$work"
mkdir -p "$work"

dotnet publish "$root/fuzz/zlib.net.Fuzz" --configuration Release --output "$work/bin"
dotnet "$work/bin/zlib.net.Fuzz.dll" corpus "$work/corpus"

# Only the library is instrumented, never the harness or the runtime.
sharpfuzz "$work/bin/zlib.net.dll"

export AFL_SKIP_BIN_CHECK=1                 # the target is `dotnet`, which is not an instrumented binary
export AFL_I_DONT_CARE_ABOUT_MISSING_CRASHES=1
export AFL_SKIP_CPUFREQ=1
export AFL_NO_UI=1

afl-fuzz -V "$seconds" -i "$work/corpus/$target" -o "$work/findings" -t 10000 -m none -- \
  dotnet "$work/bin/zlib.net.Fuzz.dll" "$target"

# AFL++ puts a single fuzzer's output in a subdirectory called "default".
out="$work/findings/default"
[[ -d "$out" ]] || out="$work/findings"

echo "--- fuzzer_stats ($target) ---"
grep -E '^(run_time|execs_done|execs_per_sec|corpus_count|edges_found|total_edges|saved_crashes|saved_hangs)' "$out/fuzzer_stats" || true

edges="$(awk -F': *' '$1 ~ /^edges_found/ {print $2}' "$out/fuzzer_stats")"
if [[ -z "$edges" || "$edges" -lt 100 ]]; then
  echo "::error::AFL saw only ${edges:-0} edges. The library was probably not instrumented, so this run proves nothing."
  exit 1
fi

hangs="$(find "$out/hangs" -type f -name 'id:*' 2>/dev/null | wc -l | tr -d ' ')"
if [[ "$hangs" -gt 0 ]]; then
  echo "::warning::$hangs input(s) hit the 10 s timeout, see $out/hangs"
fi

crashes="$(find "$out/crashes" -type f -name 'id:*' 2>/dev/null | wc -l | tr -d ' ')"
if [[ "$crashes" -gt 0 ]]; then
  echo "::error::$crashes crashing input(s) found in $out/crashes. Replay one with:"
  echo "::error::  dotnet run --project fuzz/zlib.net.Fuzz -c Release -- $target --replay <file>"
  exit 1
fi
