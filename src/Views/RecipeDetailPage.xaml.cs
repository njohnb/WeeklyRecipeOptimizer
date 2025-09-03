// /src/Views/RecipeDetailPage.xaml.cs
using RecipeOptimizer.ViewModels;

namespace RecipeOptimizer.Views;
public partial class RecipeDetailPage : ContentPage
{
    public RecipeDetailPage(RecipeDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}