using System;
using System.IO;
using SharpFuzz;

namespace zlib.net.Fuzz
{
    internal static class Program
    {
        private const string Usage =
            "Usage:\n" +
            "  zlib.net.Fuzz <inflate|roundtrip>                       fuzz target, input on stdin (run under afl-fuzz)\n" +
            "  zlib.net.Fuzz <inflate|roundtrip> --replay <file|dir>   run saved inputs in-process (corpus, crashes)\n" +
            "  zlib.net.Fuzz corpus <dir>                              write the seed corpus";

        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "corpus")
            {
                Corpus.Write(args[1]);
                return 0;
            }

            Action<byte[]>? target = args.Length == 0 ? null : args[0] switch
            {
                "inflate" => Targets.Inflate,
                "roundtrip" => Targets.RoundTrip,
                _ => null,
            };
            if (target == null)
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }

            if (args.Length == 3 && args[1] == "--replay")
                return Replay(target, args[2]);

            if (args.Length != 1)
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }

            Fuzzer.OutOfProcess.Run(stream => target(Targets.ReadAll(stream)));
            return 0;
        }

        private static int Replay(Action<byte[]> target, string path)
        {
            var files = Directory.Exists(path) ? Directory.GetFiles(path) : new[] { path };
            Array.Sort(files, StringComparer.Ordinal);
            int failures = 0;
            foreach (var file in files)
            {
                try
                {
                    target(Targets.ReadAll(new MemoryStream(File.ReadAllBytes(file))));
                }
                catch (Exception e)
                {
                    failures++;
                    Console.Error.WriteLine($"FAIL {file}: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                }
            }
            Console.WriteLine($"{files.Length - failures} of {files.Length} inputs passed");
            return failures == 0 ? 0 : 1;
        }
    }
}
