using Microsoft.EntityFrameworkCore;
using RecipeOptimizer.Models;

namespace RecipeOptimizer.Data;

public class RecipeDbContext : DbContext
{
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<WeekPlan> WeekPlans => Set<WeekPlan>();
    public DbSet<ShoppingList> ShoppingLists => Set<ShoppingList>();
    public DbSet<PantryItem> PantryItems => Set<PantryItem>();


    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, "recipes.db");
        optionsBuilder.UseSqlite($"Data Source={path}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // seed one dummy recipe so UI isn't empty on first run
        modelBuilder.Entity<Recipe>().HasData(new Recipe { Id = 1, Title = "Spaghetti", Servings = 4 });
    }
}