using Microsoft.Extensions.Logging;

namespace RecipeOptimizer;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        try
        {
            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    // fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); // optional
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
        catch (Exception ex)
        {
            // If we crash this early, we can only log to Debug/Console.
            System.Diagnostics.Debug.WriteLine("==== MauiProgram.CreateMauiApp FAILED ====");
            System.Diagnostics.Debug.WriteLine(ex.ToString());
            throw; // rethrow so we see a fail fast with the log above
        }
    }
}