using RecipeOptimizer.ViewModels;

namespace RecipeOptimizer.Views;

[QueryProperty(nameof(Id), "id")] 
public partial class AddRecipePage : ContentPage
{
    private readonly AddRecipeViewModel _vm;

    public AddRecipePage(AddRecipeViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        
        // Listen for VM events
        _vm.Saved += (_, __) => Shell.Current.GoToAsync("..");
        _vm.SaveFailed += (_, msg) => DisplayAlert("Error", msg, "OK");
    }
    
    public string? Id
    {
        set
        {
            if (int.TryParse(value, out var id))
                _ = _vm.LoadAsync(id); // prefill
        }
    }
}