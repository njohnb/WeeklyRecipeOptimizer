namespace RecipeOptimizer.Models;

public class ShoppingList
{
    public int Id { get; set; }
    public int WeekPlanId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}