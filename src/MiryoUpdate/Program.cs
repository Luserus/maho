using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using Maho.Server;

namespace Miryo.Update;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? pipeHandle = null;
        string? targetPath = null;
        string? sourcePath = null;
        string? newVersion = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pipe-handle" when i + 1 < args.Length:
                    pipeHandle = args[++i];
                    break;
                case "--target" when i + 1 < args.Length:
                    targetPath = args[++i];
                    break;
                case "--source" when i + 1 < args.Length:
                    sourcePath = args[++i];
                    break;
                case "--version" when i + 1 < args.Length:
                    newVersion = args[++i];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(pipeHandle) || string.IsNullOrWhiteSpace(targetPath) || string.IsNullOrWhiteSpace(sourcePath))
        {
            Console.Error.WriteLine("miryo-update: missing required arguments (--pipe-handle, --target, --source).");
            return 1;
        }

        try
        {
            // Connect to anonymous pipe passed from miryo
            using (var pipe = new AnonymousPipeClientStream(PipeDirection.In, pipeHandle))
            {
                // This read blocks until miryo exits and closes the write end of the pipe
                int _ = pipe.ReadByte();
            }

            // miryo has now exited. Safely overwrite target binary on disk.
            if (!File.Exists(sourcePath))
            {
                Console.Error.WriteLine($"miryo-update: source binary not found at '{sourcePath}'.");
                return 1;
            }

            bool updated = false;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    File.Copy(sourcePath, targetPath, overwrite: true);
                    updated = true;
                    break;
                }
                catch (IOException)
                {
                    Thread.Sleep(100);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(100);
                }
            }

            if (!updated)
            {
                Console.Error.WriteLine($"miryo-update: failed to replace '{targetPath}'.");
                return 1;
            }

            // Update bin manifest if present
            try
            {
                string binDir = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
                string binManifestPath = Path.Combine(binDir, "manifest.json");
                var manifest = BinManifest.Load(binManifestPath);
                if (!string.IsNullOrWhiteSpace(newVersion))
                {
                    manifest = manifest with { Version = newVersion };
                }
                manifest.Save(binManifestPath);
            }
            catch
            {
            }

            // Clean up temporary staged source
            try
            {
                if (File.Exists(sourcePath))
                    File.Delete(sourcePath);
            }
            catch
            {
            }

            Console.Out.WriteLine("miryo updated successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"miryo-update error: {ex.Message}");
            return 1;
        }
    }
}
