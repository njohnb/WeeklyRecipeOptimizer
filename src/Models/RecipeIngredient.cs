using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace RecipeOptimizer.Models;

public class RecipeIngredient
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }               // <-- PRIMARY KEY

    public int RecipeId { get; set; }         // <-- FK column
    public decimal Qty { get; set; }
    public string Unit { get; set; } = "";
    public string NameRaw { get; set; } = "";
    public string NameCanonical { get; set; } = "";
    
    public Recipe? Recipe { get; set; }
}