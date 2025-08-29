namespace RecipeOptimizer.Models;

public class Recipe
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int Servings { get; set; } = 1;
    
    public List<RecipeIngredient> Ingredients { get; set; } = new();

}