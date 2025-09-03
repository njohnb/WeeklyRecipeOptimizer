using RecipeOptimizer.ViewModels;
using RecipeOptimizer.Models;
using RecipeOptimizer.Data;
using Microsoft.EntityFrameworkCore;

namespace RecipeOptimizer.Views;

public partial class LibraryPage : ContentPage
{
    private readonly LibraryViewModel _vm;
    private readonly IDbContextFactory<RecipeDbContext> _factory;

    public LibraryPage(LibraryViewModel vm,  IDbContextFactory<RecipeDbContext> factory)
    {
        InitializeComponent();
        _vm = vm;
        _factory = factory;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
    private async void OnAddRecipeClicked(object sender, EventArgs e)
    {
        // Shell navigation (recommended)
        await Shell.Current.GoToAsync(nameof(AddRecipePage));
    }
    private async void OnEditRecipeClicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is Recipe r)
            await Shell.Current.GoToAsync($"{nameof(AddRecipePage)}?id={r.Id}");
    }

    private async void OnRecipeTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not RecipeOptimizer.Models.Recipe recipe) return;
        try
        {
            await Shell.Current.GoToAsync($"{nameof(RecipeDetailPage)}?id={recipe.Id}");
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Couldn't open recipe detail page: {exception.Message}");
            throw;
        }
    }
    
    private async void OnDeleteRecipeClicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not Recipe r) return;

        var ok = await DisplayAlert("Delete Recipe",
            $"Delete “{r.Title}”?", "Delete", "Cancel");
        if (!ok) return;

        try
        {
            using var db = await _factory.CreateDbContextAsync();
            var recipe = await db.Recipes.Include(x => x.Ingredients)
                .FirstOrDefaultAsync(x => x.Id == r.Id);
            if (recipe is null) return;

            if (recipe.Ingredients?.Count > 0)
                db.Ingredients.RemoveRange(recipe.Ingredients);

            db.Recipes.Remove(recipe);
            await db.SaveChangesAsync();

            await _vm.LoadAsync(); // refresh list
        }
        catch
        {
            await DisplayAlert("Error", "Couldn’t delete the recipe.", "OK");
        }
    }
}