using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RecipeOptimizer.Data;
using RecipeOptimizer.Models;
using RecipeOptimizer.Services;

namespace RecipeOptimizer.ViewModels;

public partial class AddRecipeViewModel : ObservableObject
{
    private readonly IDbContextFactory<RecipeDbContext> _factory;
    private readonly IPdfImportService _pdfService;

    [ObservableProperty] private int? recipeId;
    [ObservableProperty] private bool isEditMode;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string title = "";
    
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string? servings = null;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string ingredientsText = ""; // one per line
    
    [ObservableProperty]
    private string steps = "";

    [ObservableProperty]
    private string equipment = ""; // one per line
    
    [ObservableProperty] private bool isBusy;
    
    public IRelayCommand ImportFromPdfCommand { get; }
    
    public AddRecipeViewModel(IDbContextFactory<RecipeDbContext> factory, IPdfImportService pdfService)
    {
        _factory = factory;
        _pdfService = pdfService;
        ImportFromPdfCommand = new AsyncRelayCommand(ImportFromPdfAsync);

    }
    
    public event EventHandler<string>? SaveFailed;
    public event EventHandler? Saved;
    
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Title) &&
        !string.IsNullOrWhiteSpace(IngredientsText);
    
    public async Task LoadAsync(int id)
    {
        try
        {
            using var db = await _factory.CreateDbContextAsync();
            var recipe = await db.Recipes.Include(r => r.Ingredients)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (recipe is null) return;

            RecipeId = id;
            IsEditMode = true;

            Title = recipe.Title ?? "";
            Servings = recipe.Servings?.ToString();
            IngredientsText = string.Join(Environment.NewLine,
                (recipe.Ingredients ?? new()).Select(i =>
                    string.IsNullOrWhiteSpace(i.NameRaw) ? i.NameCanonical : i.NameRaw));
            // Steps: load when you persist them.
            
            // Equipment: load when you persist them.
            
        }
        catch
        {
            SaveFailed?.Invoke(this, "Couldn’t load the recipe.");
        }
    }
    
    [RelayCommand]
    public async Task SaveAsync()
    {
        try
        {
            if (!IsValid)
            {
                SaveFailed?.Invoke(this, "Please fill in all required fields.");
                return;
            }

            using var db = await _factory.CreateDbContextAsync();

            if (IsEditMode && RecipeId.HasValue)
            {
                // UPDATE path
                var recipe = await db.Recipes.Include(r => r.Ingredients)
                    .FirstOrDefaultAsync(r => r.Id == RecipeId.Value);
                if (recipe is null)
                {
                    SaveFailed?.Invoke(this, "Recipe not found.");
                    return;
                }

                recipe.Title = Title.Trim();
                recipe.Servings = int.TryParse(Servings, out var s) ? s : (int?)null;

                if (recipe.Ingredients?.Count > 0)
                    db.Ingredients.RemoveRange(recipe.Ingredients);

                foreach (var raw in (IngredientsText ?? "")
                         .Split(Environment.NewLine)
                         .Select(l => l.Trim())
                         .Where(l => !string.IsNullOrWhiteSpace(l)))
                {
                    db.Ingredients.Add(new RecipeIngredient
                    {
                        RecipeId = recipe.Id,
                        NameRaw = raw,
                        NameCanonical = raw,
                        Qty = 0,
                        Unit = ""
                    });
                }
                
                await db.SaveChangesAsync();
                Saved?.Invoke(this, EventArgs.Empty);
                return;
            }
            
            var newRecipe = new Recipe
            {
                Title = Title.Trim(),
                Servings = int.TryParse(Servings, out var s2) ? s2 : (int?)null,
                TotalTimeMin = 0,
                Tags = new(),
            };
            db.Recipes.Add(newRecipe);
            await db.SaveChangesAsync();

            foreach (var raw in (IngredientsText ?? "")
                     .Split(Environment.NewLine)
                     .Select(l => l.Trim())
                     .Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                db.Ingredients.Add(new RecipeIngredient
                {
                    RecipeId = newRecipe.Id,
                    NameRaw = raw,
                    NameCanonical = raw,
                    Qty = 0,
                    Unit = ""
                });
            }

            await db.SaveChangesAsync();
            Saved?.Invoke(this, EventArgs.Empty);
            
        }
        catch (Exception ex)
        {
            // Surface a user-friendly message. Keep ex for logs if needed.
            SaveFailed?.Invoke(this, "Couldn’t save the recipe. Please try again.");

        }
    }

    private async Task ImportFromPdfAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choose a recipe PDF",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".pdf" } },
                    { DevicePlatform.MacCatalyst, new[] { "com.adobe.pdf" } },
                    { DevicePlatform.iOS, new[] { "com.adobe.pdf" } },
                    { DevicePlatform.Android, new[] { "application/pdf" } }
                })
            });

            if (file == null) return;

            using var stream = await file.OpenReadAsync();
            var result = await _pdfService.ImportRecipeFromPdfAsync(stream);

            if (!result.Success)
            {
                await Shell.Current.DisplayAlert("Import Failed", result.Error ?? "Unknown error.", "OK");
                return;
            }

            Title = string.IsNullOrWhiteSpace(result.Title) ? Title : result.Title!.Trim();
            if (!string.IsNullOrWhiteSpace(result.Servings)) Servings = result.Servings!.Trim();
            if (!string.IsNullOrWhiteSpace(result.Equipment)) Equipment = result.Equipment!.Trim();
            if (!string.IsNullOrWhiteSpace(result.IngredientsText)) IngredientsText = result.IngredientsText!.Trim();
            if (!string.IsNullOrWhiteSpace(result.Steps)) Steps = result.Steps!.Trim();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Import Error", ex.Message, "OK");
        }
        finally { IsBusy = false; }
    }
}