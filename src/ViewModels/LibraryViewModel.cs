using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RecipeOptimizer.Data;
using RecipeOptimizer.Models;
using System.Collections.ObjectModel;

namespace RecipeOptimizer.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly IDbContextFactory<RecipeDbContext> _factory;

    [ObservableProperty] private bool isBusy;
    public ObservableCollection<Recipe> Recipes { get; } = new();

    public LibraryViewModel(IDbContextFactory<RecipeDbContext> factory)
    {
        _factory = factory;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            using var db = await _factory.CreateDbContextAsync();
            var items = await db.Recipes
                    .Include(r => r.Ingredients)
                    .AsNoTracking()
                    .OrderBy(r => r.Title)
                    .ToListAsync();

            Recipes.Clear();
            foreach (var r in items) Recipes.Add(r);
        }
        finally
        {
            IsBusy = false;
        }
    }
}