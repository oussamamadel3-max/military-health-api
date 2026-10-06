using Application.Interfaces.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Infrastructure.Repositories;

public abstract class RepositoryBase<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : class
{
    protected readonly AppDbContext Context;

    protected RepositoryBase(AppDbContext context) => Context = context;

    protected abstract DbSet<TEntity> DbSet { get; }
    protected abstract Expression<Func<TEntity, bool>> IdPredicate(TKey id);

    public async Task<TEntity?> GetByIdAsync(TKey id, bool trackChanges = false, CancellationToken ct = default)
    {
        var query = DbSet.Where(IdPredicate(id));
        if (!trackChanges)
            query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(ct);
    }

    public void Add(TEntity entity) => DbSet.Add(entity);
    public void Update(TEntity entity) => DbSet.Update(entity);
    public void Remove(TEntity entity) => DbSet.Remove(entity);
}
