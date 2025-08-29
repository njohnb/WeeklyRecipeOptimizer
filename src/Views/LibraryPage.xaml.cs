using Microsoft.EntityFrameworkCore;
using RecipeOptimizer.Data;
using RecipeOptimizer.Models;

namespace RecipeOptimizer.Views;

public partial class LibraryPage : ContentPage
{
    private readonly RecipeDbContext _db; // only for phase 0

    public LibraryPage(RecipeDbContext db)
    {
        InitializeComponent();
        _db = db;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _db.Database.MigrateAsync(); // creates DB & applies seed
        RecipesView.ItemsSource = await _db.Recipes.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
    }

    private async void OnAddDummyClicked(object sender, EventArgs e)
    {
        _db.Recipes.Add(new Recipe { Title = "Tomato Soup", Servings = 3 });
        await _db.SaveChangesAsync();
        RecipesView.ItemsSource = await _db.Recipes.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
    }
}