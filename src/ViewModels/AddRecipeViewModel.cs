using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RecipeOptimizer.Data;
using RecipeOptimizer.Models;

namespace RecipeOptimizer.ViewModels;

public partial class AddRecipeViewModel : ObservableObject
{
    private readonly IDbContextFactory<RecipeDbContext> _factory;

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
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string steps = "";

    public AddRecipeViewModel(IDbContextFactory<RecipeDbContext> factory)
    {
        _factory = factory;
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
}