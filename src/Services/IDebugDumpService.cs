// /src/Services/IDebugDumpService.cs
namespace RecipeOptimizer.Services;

public interface IDebugDumpService
{
    Task AppendAsync(string category, string content, CancellationToken ct = default);
    string LastLogFilePath { get; } // helpful to show in Debug output
}