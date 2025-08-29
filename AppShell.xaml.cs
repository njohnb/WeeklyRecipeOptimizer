using RecipeOptimizer.Views;


namespace RecipeOptimizer;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        
        Routing.RegisterRoute(nameof(AddRecipePage), typeof(AddRecipePage));
    }
}