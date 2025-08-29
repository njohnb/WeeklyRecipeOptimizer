namespace RecipeOptimizer.Models;

public class Recipe
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int? Servings { get; set; }
    
    public int TotalTimeMin { get; set; } = 0;
    
    public List<string> Tags { get; set; } = new();
    
    public List<RecipeIngredient> Ingredients { get; set; } = new();

}