namespace RecipeOptimizer.Models;

public class PantryItem
{
    public int Id { get; set; }
    public string NameCanonical { get; set; } = "";
    public decimal Qty { get; set; }
    public string Unit { get; set; } = "g";
    public DateOnly? ExpiresOn { get; set; }
}