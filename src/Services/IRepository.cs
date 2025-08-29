namespace RecipeOptimizer.Services;

public interface IRepository<T> where T : class
{
    Task<T> AddAsync(T entity);
    Task<T?> GetAsync(int id);
    Task<List<T>> GetAllAsync();
    Task UpdateAsync(T entity);
    Task DeleteAsync(T entity);
}