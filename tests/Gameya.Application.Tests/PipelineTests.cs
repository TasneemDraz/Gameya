using FluentValidation;
using Gameya.Application.Abstractions;
using Gameya.Application.Behaviors;
using Gameya.Application.Common;
using Gameya.Application.Security;
using Gameya.Domain.Activity;
using Gameya.Domain.Common;
using Gameya.Domain.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Gameya.Application.Tests;

public class PipelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    [PublicAccess]
    public sealed record PublicCmd(string Name) : ICommand<int>;

    [RequiresAuth]
    public sealed record AuthCmd : ICommand<int>;

    [RequiresRole("Admin")]
    public sealed record AdminCmd : ICommand<int>;

    [RequiresAuth]
    public sealed record AuthQuery : IQuery<int>;

    public sealed record Unmarked : IQuery<int>;

    private sealed class PublicCmdValidator : AbstractValidator<PublicCmd>
    {
        public PublicCmdValidator() => RuleFor(x => x.Name).NotEmpty();
    }

    private sealed class FakeEvent(Guid? actorOverride) : IActivityEvent
    {
        public string Action => "SOMETHING";
        public Guid? GameyaId => null;
        public string TargetType => "Thing";
        public string TargetId => "1";
        public object? Details => null;
        public Guid? ActorUserIdOverride { get; } = actorOverride;
        public string? Note => null;
    }

    private sealed class EventEntity : Entity
    {
        public void Do(IDomainEvent e) => Raise(e);
    }

    private sealed class Db
    {
        public IAppDbContext Context { get; } = Substitute.For<IAppDbContext>();
        public IDbContextTransaction Tx { get; } = Substitute.For<IDbContextTransaction>();
        public List<ActivityLogEntry> Logged { get; } = [];
        public List<Entity> Tracked { get; } = [];
        public List<string> Calls { get; } = [];
        public TransactionCallbacks Callbacks { get; } = new();

        public Db()
        {
            var set = Substitute.For<DbSet<ActivityLogEntry>>();
            set.When(s => s.Add(Arg.Any<ActivityLogEntry>())).Do(c => Logged.Add(c.Arg<ActivityLogEntry>()));
            Context.ActivityLog.Returns(set);
            Context.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                Calls.Add("begin");
                return Task.FromResult(Tx);
            });
            Context.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                Calls.Add("save");
                return Task.FromResult(1);
            });
            Context.TrackedEntitiesWithEvents().Returns(_ => Tracked.Where(t => t.DomainEvents.Count > 0).ToList());
            Tx.When(t => t.CommitAsync(Arg.Any<CancellationToken>())).Do(_ => Calls.Add("commit"));
        }
    }

    private static ICurrentUser CurrentUser(Guid? id, params string[] roles)
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(id);
        u.IsAuthenticated.Returns(id is not null);
        u.Roles.Returns(roles);
        return u;
    }

    private static IClock Clock()
    {
        var c = Substitute.For<IClock>();
        c.UtcNow.Returns(Now);
        return c;
    }

    [Fact]
    public async Task Validation_failure_throws_before_handler_runs()
    {
        var behavior = new ValidationBehavior<PublicCmd, int>([new PublicCmdValidator()]);
        var ran = false;

        var ex = await Should.ThrowAsync<ValidationFailedException>(() =>
            behavior.Handle(new PublicCmd(""), _ => { ran = true; return Task.FromResult(1); }, default));

        ran.ShouldBeFalse();
        ex.Errors.ShouldContainKey("Name");
    }

    [Fact]
    public async Task Validation_passes_valid_requests_through()
    {
        var behavior = new ValidationBehavior<PublicCmd, int>([new PublicCmdValidator()]);
        (await behavior.Handle(new PublicCmd("x"), _ => Task.FromResult(7), default)).ShouldBe(7);
    }

    [Fact]
    public async Task RequiresAuth_rejects_anonymous_with_UNAUTHENTICATED()
    {
        var behavior = new AuthorizationBehavior<AuthCmd, int>(CurrentUser(null));
        var ex = await Should.ThrowAsync<DomainException>(() =>
            behavior.Handle(new AuthCmd(), _ => Task.FromResult(1), default));
        ex.Code.ShouldBe(ErrorCodes.UNAUTHENTICATED);
    }

    [Fact]
    public async Task RequiresAuth_allows_authenticated_and_PublicAccess_allows_anonymous()
    {
        (await new AuthorizationBehavior<AuthCmd, int>(CurrentUser(Guid.NewGuid()))
            .Handle(new AuthCmd(), _ => Task.FromResult(1), default)).ShouldBe(1);
        (await new AuthorizationBehavior<PublicCmd, int>(CurrentUser(null))
            .Handle(new PublicCmd("x"), _ => Task.FromResult(2), default)).ShouldBe(2);
    }

    [Fact]
    public async Task RequiresRole_rejects_user_without_role_with_FORBIDDEN()
    {
        var behavior = new AuthorizationBehavior<AdminCmd, int>(CurrentUser(Guid.NewGuid(), "Member"));
        var ex = await Should.ThrowAsync<DomainException>(() =>
            behavior.Handle(new AdminCmd(), _ => Task.FromResult(1), default));
        ex.Code.ShouldBe(ErrorCodes.FORBIDDEN);
    }

    [Fact]
    public async Task RequiresRole_rejects_anonymous_with_UNAUTHENTICATED_and_allows_role_holder()
    {
        var anon = await Should.ThrowAsync<DomainException>(() =>
            new AuthorizationBehavior<AdminCmd, int>(CurrentUser(null))
                .Handle(new AdminCmd(), _ => Task.FromResult(1), default));
        anon.Code.ShouldBe(ErrorCodes.UNAUTHENTICATED);

        (await new AuthorizationBehavior<AdminCmd, int>(CurrentUser(Guid.NewGuid(), "Admin"))
            .Handle(new AdminCmd(), _ => Task.FromResult(3), default)).ShouldBe(3);
    }

    [Fact]
    public async Task Request_without_access_attribute_fails_closed()
    {
        var behavior = new AuthorizationBehavior<Unmarked, int>(CurrentUser(Guid.NewGuid()));
        await Should.ThrowAsync<InvalidOperationException>(() =>
            behavior.Handle(new Unmarked(), _ => Task.FromResult(1), default));
    }

    [Fact]
    public async Task Command_activity_events_become_log_entries_with_actor()
    {
        var db = new Db();
        var actor = Guid.NewGuid();
        var entity = new EventEntity();
        db.Tracked.Add(entity);
        var behavior = new ActivityLogBehavior<AuthCmd, int>(db.Context, CurrentUser(actor), Clock());

        await behavior.Handle(new AuthCmd(), _ => { entity.Do(new FakeEvent(null)); return Task.FromResult(1); }, default);

        var entry = db.Logged.ShouldHaveSingleItem();
        entry.ActorUserId.ShouldBe(actor);
        entry.ActorRole.ShouldBe("User");
        entry.Action.ShouldBe("SOMETHING");
        entry.OccurredAt.ShouldBe(Now);
        entity.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Registration_event_uses_actor_override_when_anonymous()
    {
        var db = new Db();
        var user = Domain.Users.User.Register(Guid.NewGuid(), PhoneNumber.Parse("01012345678"), Language.Ar, Now);
        db.Tracked.Add(user);
        var behavior = new ActivityLogBehavior<PublicCmd, int>(db.Context, CurrentUser(null), Clock());

        await behavior.Handle(new PublicCmd("x"), _ => Task.FromResult(1), default);

        var entry = db.Logged.ShouldHaveSingleItem();
        entry.Action.ShouldBe("USER_REGISTERED");
        entry.ActorUserId.ShouldBe(user.Id);
        entry.ActorRole.ShouldBe("User");
    }

    [Fact]
    public async Task Anonymous_event_without_override_is_logged_as_System()
    {
        var db = new Db();
        var entity = new EventEntity();
        db.Tracked.Add(entity);
        var behavior = new ActivityLogBehavior<PublicCmd, int>(db.Context, CurrentUser(null), Clock());

        await behavior.Handle(new PublicCmd("x"), _ => { entity.Do(new FakeEvent(null)); return Task.FromResult(1); }, default);

        var entry = db.Logged.ShouldHaveSingleItem();
        entry.ActorUserId.ShouldBeNull();
        entry.ActorRole.ShouldBe("System");
    }

    [Fact]
    public async Task Queries_do_not_open_transactions()
    {
        var db = new Db();
        var tx = new TransactionBehavior<AuthQuery, int>(db.Context, db.Callbacks, NullLogger<TransactionBehavior<AuthQuery, int>>.Instance);
        var log = new ActivityLogBehavior<AuthQuery, int>(db.Context, CurrentUser(Guid.NewGuid()), Clock());

        await tx.Handle(new AuthQuery(), _ => log.Handle(new AuthQuery(), _ => Task.FromResult(1), default), default);

        db.Calls.ShouldBeEmpty();
        await db.Context.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        db.Context.DidNotReceive().TrackedEntitiesWithEvents();
    }

    [Fact]
    public async Task Command_runs_in_transaction_saves_then_commits()
    {
        var db = new Db();
        var tx = new TransactionBehavior<AuthCmd, int>(db.Context, db.Callbacks, NullLogger<TransactionBehavior<AuthCmd, int>>.Instance);

        var result = await tx.Handle(new AuthCmd(), _ => { db.Calls.Add("handler"); return Task.FromResult(5); }, default);

        result.ShouldBe(5);
        db.Calls.ShouldBe(["begin", "handler", "save", "commit"]);
    }

    [Fact]
    public async Task DomainException_with_CommitStateChanges_commits_then_rethrows()
    {
        var db = new Db();
        var entity = new EventEntity();
        db.Tracked.Add(entity);
        var tx = new TransactionBehavior<AuthCmd, int>(db.Context, db.Callbacks, NullLogger<TransactionBehavior<AuthCmd, int>>.Instance);
        var log = new ActivityLogBehavior<AuthCmd, int>(db.Context, CurrentUser(Guid.NewGuid()), Clock());
        var thrown = new DomainException(ErrorCodes.OTP_INVALID, null, commitStateChanges: true);

        var ex = await Should.ThrowAsync<DomainException>(() => tx.Handle(new AuthCmd(), _ =>
            log.Handle(new AuthCmd(), _ =>
            {
                entity.Do(new FakeEvent(null));
                throw thrown;
            }, default), default));

        ex.ShouldBeSameAs(thrown);
        db.Calls.ShouldBe(["begin", "save", "commit"]);
        db.Logged.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task CommitStateChanges_path_saves_and_commits_with_a_non_cancelled_token_even_if_request_aborted()
    {
        var db = new Db();
        var tokens = new List<CancellationToken>();
        db.Context.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(c =>
        {
            tokens.Add(c.Arg<CancellationToken>());
            db.Calls.Add("save");
            return Task.FromResult(1);
        });
        db.Tx.When(t => t.CommitAsync(Arg.Any<CancellationToken>())).Do(c =>
        {
            tokens.Add(c.Arg<CancellationToken>()); // Db already records "commit"
        });
        var tx = new TransactionBehavior<AuthCmd, int>(db.Context, db.Callbacks, NullLogger<TransactionBehavior<AuthCmd, int>>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<DomainException>(() => tx.Handle(new AuthCmd(),
            _ => throw new DomainException(ErrorCodes.OTP_INVALID, null, commitStateChanges: true), cts.Token));

        db.Calls.ShouldBe(["begin", "save", "commit"]);
        tokens.Count.ShouldBe(2);
        tokens.ShouldAllBe(t => !t.IsCancellationRequested);
    }

    [Fact]
    public async Task Other_exceptions_roll_back()
    {
        var db = new Db();
        var entity = new EventEntity();
        db.Tracked.Add(entity);
        var tx = new TransactionBehavior<AuthCmd, int>(db.Context, db.Callbacks, NullLogger<TransactionBehavior<AuthCmd, int>>.Instance);
        var log = new ActivityLogBehavior<AuthCmd, int>(db.Context, CurrentUser(Guid.NewGuid()), Clock());

        await Should.ThrowAsync<InvalidOperationException>(() => tx.Handle(new AuthCmd(), _ =>
            log.Handle(new AuthCmd(), _ => throw new InvalidOperationException("boom"), default), default));
        await Should.ThrowAsync<DomainException>(() => tx.Handle(new AuthCmd(), _ =>
            log.Handle(new AuthCmd(), _ => throw new DomainException(ErrorCodes.NOT_FOUND), default), default));

        db.Calls.ShouldBe(["begin", "begin"]);
        db.Logged.ShouldBeEmpty();
        await db.Tx.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await db.Tx.Received(2).DisposeAsync();
    }

    private static TransactionBehavior<AuthCmd, int> Tx(Db db, Microsoft.Extensions.Logging.ILogger<TransactionBehavior<AuthCmd, int>>? logger = null) =>
        new(db.Context, db.Callbacks, logger ?? NullLogger<TransactionBehavior<AuthCmd, int>>.Instance);

    [Fact]
    public async Task Committed_actions_run_only_after_commit_and_rolled_back_actions_do_not_run()
    {
        var db = new Db();
        db.Callbacks.OnCommitted(_ => { db.Calls.Add("committed"); return Task.CompletedTask; });
        db.Callbacks.OnRolledBack(_ => { db.Calls.Add("rolledback"); return Task.CompletedTask; });

        await Tx(db).Handle(new AuthCmd(), _ => { db.Calls.Add("handler"); return Task.FromResult(1); }, default);

        db.Calls.ShouldBe(["begin", "handler", "save", "commit", "committed"]);
    }

    [Fact]
    public async Task Rolled_back_actions_run_only_on_rollback_and_the_exception_propagates()
    {
        var db = new Db();
        db.Callbacks.OnCommitted(_ => { db.Calls.Add("committed"); return Task.CompletedTask; });
        db.Callbacks.OnRolledBack(_ => { db.Calls.Add("rolledback"); return Task.CompletedTask; });

        await Should.ThrowAsync<InvalidOperationException>(() =>
            Tx(db).Handle(new AuthCmd(), _ => throw new InvalidOperationException("boom"), default));

        db.Calls.ShouldBe(["begin", "rolledback"]);
    }

    [Fact]
    public async Task A_failing_commit_counts_as_rollback()
    {
        var db = new Db();
        db.Tx.CommitAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromException(new InvalidOperationException("commit")));
        db.Callbacks.OnCommitted(_ => { db.Calls.Add("committed"); return Task.CompletedTask; });
        db.Callbacks.OnRolledBack(_ => { db.Calls.Add("rolledback"); return Task.CompletedTask; });

        await Should.ThrowAsync<InvalidOperationException>(() =>
            Tx(db).Handle(new AuthCmd(), _ => Task.FromResult(1), default));

        db.Calls.ShouldBe(["begin", "save", "commit", "rolledback"]); // "commit" = the attempt, which threw
    }

    [Fact]
    public async Task Committed_actions_run_on_the_CommitStateChanges_path_before_the_same_exception_is_rethrown()
    {
        var db = new Db();
        db.Callbacks.OnCommitted(_ => { db.Calls.Add("committed"); return Task.CompletedTask; });
        db.Callbacks.OnRolledBack(_ => { db.Calls.Add("rolledback"); return Task.CompletedTask; });
        var thrown = new DomainException(ErrorCodes.OTP_INVALID, null, commitStateChanges: true);

        var ex = await Should.ThrowAsync<DomainException>(() => Tx(db).Handle(new AuthCmd(), _ => throw thrown, default));

        ex.ShouldBeSameAs(thrown);
        db.Calls.ShouldBe(["begin", "save", "commit", "committed"]);
    }

    [Fact]
    public async Task A_throwing_callback_is_swallowed_logged_and_does_not_affect_the_result_or_later_actions()
    {
        var db = new Db();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<TransactionBehavior<AuthCmd, int>>>();
        db.Callbacks.OnCommitted(_ => throw new IOException("disk"));
        db.Callbacks.OnCommitted(_ => { db.Calls.Add("second"); return Task.CompletedTask; });

        var result = await Tx(db, logger).Handle(new AuthCmd(), _ => Task.FromResult(7), default);

        result.ShouldBe(7);
        db.Calls.ShouldContain("second");
        logger.ReceivedCalls().Any(c => c.GetArguments().Contains(Microsoft.Extensions.Logging.LogLevel.Warning))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task A_throwing_rollback_callback_does_not_replace_the_original_exception()
    {
        var db = new Db();
        db.Callbacks.OnRolledBack(_ => throw new IOException("disk"));

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            Tx(db).Handle(new AuthCmd(), _ => throw new InvalidOperationException("boom"), default));

        ex.Message.ShouldBe("boom");
    }

    [Fact]
    public void AddApplication_registers_behaviors_in_pipeline_order()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IAppDbContext>());
        services.AddSingleton(Substitute.For<ICurrentUser>());
        services.AddSingleton(Substitute.For<IClock>());
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        using var provider = DependencyInjection.AddApplication(services, config).BuildServiceProvider();

        provider.GetServices<IPipelineBehavior<AuthCmd, int>>()
            .Select(b => b.GetType().GetGenericTypeDefinition())
            .ShouldBe([typeof(ValidationBehavior<,>), typeof(AuthorizationBehavior<,>),
                typeof(TransactionBehavior<,>), typeof(ActivityLogBehavior<,>)]);
    }
}
