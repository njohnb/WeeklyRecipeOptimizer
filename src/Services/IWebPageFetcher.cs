namespace RecipeOptimizer.Services;

public interface IWebPageFetcher
{
    Task<string> GetAsync(string url, CancellationToken ct = default);
}