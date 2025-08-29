using Microsoft.EntityFrameworkCore;
using RecipeOptimizer.Data; // for DbContext

namespace RecipeOptimizer.Services;

public class EfRepository<T> : IRepository<T> where T : class
{
    private readonly RecipeDbContext _db;

    public EfRepository(RecipeDbContext db)
    {
        _db = db;
    }

    public async Task<T> AddAsync(T entity)
    {
        _db.Set<T>().Add(entity);
        await _db.SaveChangesAsync();
        return entity;
    }

    public async Task<T?> GetAsync(int id)
    {
        return await _db.Set<T>().FindAsync(id);
    }

    public async Task<List<T>> GetAllAsync()
    {
        return await _db.Set<T>().ToListAsync();
    }

    public async Task UpdateAsync(T entity)
    {
        _db.Set<T>().Update(entity);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(T entity)
    {
        _db.Set<T>().Remove(entity);
        await _db.SaveChangesAsync();
    }
}