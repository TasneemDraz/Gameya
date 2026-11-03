using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Gameya.Application.Common.Specifications;

/// <summary>
/// The only permitted read path for handlers. Entity specs stay tracked (commands modify the result);
/// projection specs are untracked.
/// </summary>
public static class SpecificationQueryExtensions
{
    public static Task<T?> FirstOrDefaultAsync<T>(this DbSet<T> set, ISpecification<T> spec, CancellationToken ct)
        where T : class =>
        SpecificationEvaluator.Default.GetQuery(set.AsQueryable(), spec).FirstOrDefaultAsync(ct);

    public static Task<TResult?> FirstOrDefaultAsync<T, TResult>(
        this DbSet<T> set, ISpecification<T, TResult> spec, CancellationToken ct)
        where T : class where TResult : class =>
        SpecificationEvaluator.Default.GetQuery(set.AsQueryable(), spec).AsNoTracking().FirstOrDefaultAsync(ct);

    public static Task<List<T>> ListAsync<T>(this DbSet<T> set, ISpecification<T> spec, CancellationToken ct)
        where T : class =>
        SpecificationEvaluator.Default.GetQuery(set.AsQueryable(), spec).ToListAsync(ct);

    public static Task<List<TResult>> ListAsync<T, TResult>(
        this DbSet<T> set, ISpecification<T, TResult> spec, CancellationToken ct)
        where T : class where TResult : class =>
        SpecificationEvaluator.Default.GetQuery(set.AsQueryable(), spec).AsNoTracking().ToListAsync(ct);

    public static Task<bool> AnyAsync<T>(this DbSet<T> set, ISpecification<T> spec, CancellationToken ct)
        where T : class =>
        SpecificationEvaluator.Default.GetQuery(set.AsQueryable(), spec).AnyAsync(ct);
}
