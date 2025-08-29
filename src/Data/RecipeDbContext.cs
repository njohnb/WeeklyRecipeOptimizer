using Microsoft.EntityFrameworkCore;
using RecipeOptimizer.Models;

namespace RecipeOptimizer.Data
{
    public class RecipeDbContext : DbContext
    {
        public RecipeDbContext(DbContextOptions<RecipeDbContext> options) : base(options) { }

        public DbSet<Recipe> Recipes => Set<Recipe>();
        public DbSet<RecipeIngredient> Ingredients => Set<RecipeIngredient>();
        public DbSet<PantryItem> Pantry => Set<PantryItem>();
        public DbSet<WeekPlan> WeekPlans => Set<WeekPlan>();
        public DbSet<ShoppingList> ShoppingLists => Set<ShoppingList>();
        public DbSet<ShoppingListItem> ShoppingListItems => Set<ShoppingListItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Minimal keys/relations if not already set
            modelBuilder.Entity<Recipe>().HasKey(r => r.Id);
            modelBuilder.Entity<RecipeIngredient>().HasKey(i => i.Id);
            
            modelBuilder.Entity<Recipe>()
                .HasMany(r => r.Ingredients)
                .WithOne(i => i.Recipe!)
                .HasForeignKey(i => i.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);
            
            modelBuilder.Entity<ShoppingListItem>().HasKey(i => i.Id);
            modelBuilder.Entity<PantryItem>().HasKey(i => i.Id);
            modelBuilder.Entity<WeekPlan>().HasKey(i => i.Id);
            modelBuilder.Entity<ShoppingList>().HasKey(i => i.Id);
        }
    }
}