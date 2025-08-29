namespace RecipeOptimizer.Models;

public class WeekPlan
{
    public int Id { get; set; }
    public DateOnly StartDate { get; set; }
    public int TargetMeals { get; set; } = 6;
    public string Variant { get; set; } = "Overlap";
}