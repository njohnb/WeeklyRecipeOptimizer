using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace RecipeOptimizer.Services;

public class HttpWebPageFetcher : IWebPageFetcher
{
    private readonly HttpClient _http;
    private readonly bool _enabled;
    
    public HttpWebPageFetcher(HttpClient http, bool enabled)
    {
        _http = http;
        _enabled = enabled;
    }

    public async Task<string> GetAsync(string url, CancellationToken ct = default)
    {
        if(!_enabled) throw new InvalidOperationException("HTTP fetch disabled");
        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}