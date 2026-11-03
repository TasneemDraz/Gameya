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
    /// <summary>Entity query: tracked, so commands can modify the result.</summary>
    public static IQueryable<T> BuildEntityQuery<T>(IQueryable<T> source, ISpecification<T> spec)
        where T : class =>
        SpecificationEvaluator.Default.GetQuery(source, spec);

    /// <summary>Projection query: the source is untracked and the spec's Select is applied.</summary>
    public static IQueryable<TResult> BuildProjectionQuery<T, TResult>(
        IQueryable<T> source, ISpecification<T, TResult> spec)
        where T : class =>
        SpecificationEvaluator.Default.GetQuery(source.AsNoTracking(), spec);

    public static Task<T?> FirstOrDefaultAsync<T>(this DbSet<T> set, ISpecification<T> spec, CancellationToken ct)
        where T : class =>
        BuildEntityQuery(set, spec).FirstOrDefaultAsync(ct);

    public static Task<TResult?> FirstOrDefaultAsync<T, TResult>(
        this DbSet<T> set, ISpecification<T, TResult> spec, CancellationToken ct)
        where T : class =>
        BuildProjectionQuery(set, spec).FirstOrDefaultAsync(ct);

    public static Task<List<T>> ListAsync<T>(this DbSet<T> set, ISpecification<T> spec, CancellationToken ct)
        where T : class =>
        BuildEntityQuery(set, spec).ToListAsync(ct);

    public static Task<List<TResult>> ListAsync<T, TResult>(
        this DbSet<T> set, ISpecification<T, TResult> spec, CancellationToken ct)
        where T : class =>
        BuildProjectionQuery(set, spec).ToListAsync(ct);

    public static Task<bool> AnyAsync<T>(this DbSet<T> set, ISpecification<T> spec, CancellationToken ct)
        where T : class =>
        BuildEntityQuery(set, spec).AnyAsync(ct);
}
