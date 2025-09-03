using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RecipeOptimizer.Data;
using RecipeOptimizer.ViewModels;
using RecipeOptimizer.Views;
using RecipeOptimizer.Models;
using RecipeOptimizer.Services;

namespace RecipeOptimizer;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
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

        // 1) SQLite path and EF Core factory
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "recipes.db");
        builder.Services.AddDbContextFactory<RecipeDbContext>(opt => { opt.UseSqlite($"Data Source={dbPath}"); });

        // 2) Pages and VM via DI
        builder.Services.AddSingleton<LibraryPage>();
        builder.Services.AddSingleton<LibraryViewModel>();
        
        // register services
        
        // html import service
        const bool EnableNetworkImports = true;
        builder.Services.AddSingleton<IWebPageFetcher>(sp =>
            new HttpWebPageFetcher(new HttpClient(), EnableNetworkImports));
        builder.Services.AddSingleton<IHtmlImportService, HtmlImportService>();
        
        // pdf import service
        builder.Services.AddSingleton<IPdfImportService, PdfImportService>();
        
        // debug dump service
        builder.Services.AddSingleton<IDebugDumpService, DebugDumpService>();
        
        
        
        // Optional: other pages/VMs can be added here
        // builder.Services.AddSingleton<HomePage>();
        // builder.Services.AddSingleton<HomeViewModel>();

        builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        
        builder.Services.AddTransient<LibraryViewModel>();
        builder.Services.AddTransient<AddRecipeViewModel>();
        builder.Services.AddTransient<AddRecipePage>();
        builder.Services.AddTransient<RecipeDetailViewModel>(); // for Shell to resolve
        builder.Services.AddTransient<RecipeDetailPage>();
        
        var app = builder.Build();

        // 3) Ensure SQLite bundle + migrations/crations
        try
        {
            SQLitePCL.Batteries_V2.Init();
        }
        catch (Exception ex)
        {

        }

        using (var scope = app.Services.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<RecipeDbContext>>();
            using var db = factory.CreateDbContext();
            // use one of the following
            // db.Database.Migrate(); // creates DB & applies seed
            db.Database.EnsureCreated(); // creates DB but no seed

            if (!db.Recipes.Any())
            {
                var r = new Recipe
                {
                    Title = "Test Pasta",
                    Servings = 4,
                    Ingredients =
                    {
                        new RecipeIngredient
                        {
                            NameCanonical = "pasta-spaghetti",
                            NameRaw = "spaghetti",
                            Qty = 200,
                            Unit = "g"
                        },
                        new RecipeIngredient
                        {
                            NameCanonical = "tomatoes",
                            NameRaw = "tomato (diced)",
                            Qty = (decimal)2.5,
                            Unit = "cup"
                        },
                        new RecipeIngredient
                        {
                            NameCanonical = "basil",
                            NameRaw = "fresh basil",
                            Qty = 5,
                            Unit = "g"
                        }
                    },
                    Equipment =
                    {
                        "Gallon Sized Pot"
                    },
                    Steps =
                    {
                        "Bring water to a boil",
                        "Add pasta and cook for 10 minutes",
                        "Add tomatoes and basil and cook for 5 minutes"
                    }
                };
                db.Recipes.Add(r);
                db.SaveChanges();

            }
        }
        return app;
    }
}