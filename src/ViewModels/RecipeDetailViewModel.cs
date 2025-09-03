// /src/ViewModels/RecipeDetailViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using RecipeOptimizer.Data;
using RecipeOptimizer.Models;

namespace RecipeOptimizer.ViewModels;

[QueryProperty(nameof(Id), "id")]
public partial class RecipeDetailViewModel : ObservableObject
{
    private readonly IDbContextFactory<RecipeDbContext> _factory;

    [ObservableProperty] private int id;
    [ObservableProperty] private Recipe? recipe;

    public RecipeDetailViewModel(IDbContextFactory<RecipeDbContext> factory)
    {
        _factory = factory;
    }

    partial void OnIdChanged(int value) => _ = LoadAsync();

    public async Task LoadAsync()
    {
        using var db = await _factory.CreateDbContextAsync();
        Recipe = await db.Recipes
            .Include(r => r.Ingredients)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == Id);
    }
}