// /src/Services/DebugDumpService.cs
using System.Text;

namespace RecipeOptimizer.Services;

public sealed class DebugDumpService : IDebugDumpService
{
    // Flip this to true to force logging in Release while debugging
    public static bool ForceLogging { get; set; } = false;
    
    private string? _lastPath;

    public string LastLogFilePath => _lastPath ?? string.Empty;

    public async Task AppendAsync(string category, string content, CancellationToken ct = default)
    {
        if (!IsEnabled()) return;
        
        var now = DateTime.Now;
        var fileName = $"RecipeImports-{now:yyyyMMdd-HHmmss}.log"; // no slashes

        var baseDir = GetBaseDirectory();

        Directory.CreateDirectory(baseDir);
        var path = Path.Combine(baseDir, fileName);
        _lastPath = path;

        var header = $"===== [{now:HH:mm:ss}] {category} ====={Environment.NewLine}";
        await File.WriteAllTextAsync(path, header + content + Environment.NewLine + Environment.NewLine, Encoding.UTF8, ct);
        System.Diagnostics.Debug.WriteLine($"[LOG] wrote {new FileInfo(path).Length} bytes to device: {path}");

        //// mirror copy
        //try
        //{
        //    var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        //    var mirrorDir = Path.Combine(docs, "RecipeOptimizer", "Logs");
        //    Directory.CreateDirectory(mirrorDir);
        //    var mirrorPath = Path.Combine(mirrorDir, Path.GetFileName(path));
        //    await File.AppendAllTextAsync(mirrorPath, header + content + Environment.NewLine + Environment.NewLine, Encoding.UTF8, ct);
        //    System.Diagnostics.Debug.WriteLine($"[MirrorLog] Copied log to {mirrorPath}");
        //}
        //catch (Exception ex)
        //{
        //    System.Diagnostics.Debug.WriteLine($"[MirrorLog] Failed: {ex.Message}");
        //}
    }

    private static bool IsEnabled()
    {
#if DEBUG
        return true;
#else
        return ForceLogging;
#endif
    }
    private static string GetBaseDirectory()
    {
        // Always device-local & accessible with `run-as`
        return Path.Combine(FileSystem.AppDataDirectory, "Documents", "RecipeOptimizer", "Logs");
    }
}