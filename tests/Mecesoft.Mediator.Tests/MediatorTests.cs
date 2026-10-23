using Mecesoft.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mecesoft.Mediator.Tests;

public sealed class MediatorTests
{
    [Fact]
    public async Task Sends_response_and_void_requests()
    {
        await using var provider = CreateProvider();
        var sender = provider.GetRequiredService<ISender>();

        Assert.Equal(42, await sender.Send(new AddRequest(40, 2)));
        await sender.Send(new VoidRequest());
        Assert.True(provider.GetRequiredService<Recording>().VoidHandled);
    }

    [Fact]
    public async Task Executes_multiple_response_behaviors_in_registration_order()
    {
        await using var provider = CreateProvider(options =>
        {
            options.AddOpenBehavior(typeof(FirstBehavior<,>));
            options.AddOpenBehavior(typeof(SecondBehavior<,>));
        });

        Assert.Equal(3, await provider.GetRequiredService<ISender>().Send(new AddRequest(1, 2)));
        Assert.Equal(["first-before", "second-before", "handler", "second-after", "first-after"],
            provider.GetRequiredService<Recording>().Events);
    }

    [Fact]
    public async Task Executes_void_pipeline_behavior_in_registration_order()
    {
        await using var provider = CreateProvider(options => options.AddOpenBehavior(typeof(VoidBehavior<>)));

        await provider.GetRequiredService<ISender>().Send(new VoidRequest());

        Assert.Equal(["void-before", "void-handler", "void-after"],
            provider.GetRequiredService<Recording>().Events);
    }

    [Fact]
    public async Task Response_behavior_can_short_circuit_without_constructing_handler()
    {
        await using var provider = CreateProvider(options =>
            options.AddOpenBehavior(typeof(ShortCircuitResponseBehavior<,>)));
        var recording = provider.GetRequiredService<Recording>();

        var result = await provider.GetRequiredService<ISender>().Send(new ShortCircuitResponseRequest());

        Assert.Equal(123, result);
        Assert.Equal(0, recording.ResponseHandlerConstructions);
    }

    [Fact]
    public async Task Void_behavior_can_short_circuit_without_constructing_handler()
    {
        await using var provider = CreateProvider(options =>
            options.AddOpenBehavior(typeof(ShortCircuitVoidBehavior<>)));
        var recording = provider.GetRequiredService<Recording>();

        await provider.GetRequiredService<ISender>().Send(new ShortCircuitVoidRequest());

        Assert.Equal(0, recording.VoidHandlerConstructions);
    }

    [Fact]
    public async Task Publishes_notifications_sequentially()
    {
        await using var provider = CreateProvider(options =>
            options.PublishNotificationsConcurrently = false);

        await provider.GetRequiredService<ISender>().Publish(new TestNotification());

        Assert.Equal(["one", "two"], provider.GetRequiredService<Recording>().Events);
    }

    [Fact]
    public async Task Publishes_notifications_in_parallel_by_default()
    {
        await using var provider = CreateProvider();
        var recording = provider.GetRequiredService<Recording>();

        await provider.GetRequiredService<ISender>().Publish(new ParallelNotification());

        Assert.Equal(2, recording.ParallelHandlersCompleted);
        Assert.Contains("parallel-one", recording.Events);
        Assert.Contains("parallel-two", recording.Events);
    }

    [Fact]
    public async Task Propagates_parallel_notification_exception()
    {
        await using var provider = CreateProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<ISender>().Publish(new FailingNotification()));

        Assert.Equal("notification failure", exception.Message);
    }

    [Fact]
    public async Task Sequential_notification_exception_stops_later_handlers()
    {
        await using var provider = CreateProvider(options =>
            options.PublishNotificationsConcurrently = false);
        var recording = provider.GetRequiredService<Recording>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<ISender>().Publish(new SequentialFailingNotification()));

        Assert.Equal("sequential notification failure", exception.Message);
        Assert.False(recording.SequentialHandlerAfterFailureRan);
    }

    [Fact]
    public async Task Propagates_original_exception_and_cancellation_tokens()
    {
        await using var provider = CreateProvider();
        var sender = provider.GetRequiredService<ISender>();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.Send(new FailingRequest()));
        Assert.Equal("expected", exception.Message);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.Send(new AddRequest(1, 2), cancellation.Token));

        using var notificationCancellation = new CancellationTokenSource();
        notificationCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.Publish(new CancellationNotification(), notificationCancellation.Token));
        Assert.True(provider.GetRequiredService<Recording>().NotificationCancellationObserved);
    }

    [Fact]
    public async Task Missing_request_handler_fails_explicitly()
    {
        await using var provider = CreateProvider();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<ISender>().Send(new MissingRequest()));
    }

    [Fact]
    public void Invalid_pipeline_registration_fails_during_configuration()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddMediator(options =>
        {
            options.AddRegisterAssemblies(typeof(MediatorTests).Assembly);
            options.AddOpenBehavior(typeof(InvalidBehavior));
        }));
    }

    [Fact]
    public void Duplicate_assembly_registration_does_not_duplicate_handlers()
    {
        var services = new ServiceCollection();
        services.AddMediator(options =>
        {
            options.AddRegisterAssemblies(typeof(MediatorTests).Assembly, typeof(MediatorTests).Assembly);
        });

        var registrations = services.Count(descriptor =>
            descriptor.ServiceType == typeof(IRequestHandler<AddRequest, int>));
        Assert.Equal(1, registrations);
    }

    [Fact]
    public async Task Scoped_dependencies_are_isolated_and_concurrent_requests_are_safe()
    {
        await using var provider = CreateProvider();
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ =>
                first.ServiceProvider.GetRequiredService<ISender>().Send(new ScopeRequest())));
        var secondResult = await second.ServiceProvider.GetRequiredService<ISender>().Send(new ScopeRequest());

        Assert.All(results, result => Assert.Equal(results[0], result));
        Assert.All(results, result => Assert.NotEqual(secondResult, result));
    }

    private static ServiceProvider CreateProvider(Action<MediatorOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Recording>();
        services.AddScoped<ScopedValue>();
        services.AddMediator(options =>
        {
            options.AddRegisterAssemblies(typeof(MediatorTests).Assembly);
            configure?.Invoke(options);
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}

public sealed record AddRequest(int Left, int Right) : IRequest<int>;
public sealed record VoidRequest : IRequest;
public sealed record FailingRequest : IRequest<int>;
public sealed record MissingRequest : IRequest<int>;
public sealed record ScopeRequest : IRequest<Guid>;
public sealed record ShortCircuitResponseRequest : IRequest<int>;
public sealed record ShortCircuitVoidRequest : IRequest;
public sealed record TestNotification : INotification;
public sealed record ParallelNotification : INotification;
public sealed record FailingNotification : INotification;
public sealed record SequentialFailingNotification : INotification;
public sealed record CancellationNotification : INotification;

public sealed class AddHandler(Recording recording) : IRequestHandler<AddRequest, int>
{
    public async Task<int> Handle(AddRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        recording.AddEvent("handler");
        await Task.Yield();
        return request.Left + request.Right;
    }
}

public sealed class VoidHandler(Recording recording) : IRequestHandler<VoidRequest>
{
    public Task Handle(VoidRequest request, CancellationToken cancellationToken)
    {
        recording.AddEvent("void-handler");
        recording.VoidHandled = true;
        return Task.CompletedTask;
    }
}

public sealed class FailingHandler : IRequestHandler<FailingRequest, int>
{
    public Task<int> Handle(FailingRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("expected");
}

public sealed class ScopeHandler(ScopedValue value) : IRequestHandler<ScopeRequest, Guid>
{
    public Task<Guid> Handle(ScopeRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(value.Id);
}

public sealed class ShortCircuitResponseHandler : IRequestHandler<ShortCircuitResponseRequest, int>
{
    public ShortCircuitResponseHandler(Recording recording)
    {
        recording.ResponseHandlerConstructions++;
    }

    public Task<int> Handle(ShortCircuitResponseRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(-1);
}

public sealed class ShortCircuitVoidHandler : IRequestHandler<ShortCircuitVoidRequest>
{
    public ShortCircuitVoidHandler(Recording recording)
    {
        recording.VoidHandlerConstructions++;
    }

    public Task Handle(ShortCircuitVoidRequest request, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed class FirstBehavior<TRequest, TResponse>(Recording recording) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        recording.AddEvent("first-before");
        var result = await next();
        recording.AddEvent("first-after");
        return result;
    }
}

public sealed class SecondBehavior<TRequest, TResponse>(Recording recording) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
    {
        recording.AddEvent("second-before");
        var result = await next();
        recording.AddEvent("second-after");
        return result;
    }
}

public sealed class VoidBehavior<TRequest>(Recording recording) : IPipelineBehavior<TRequest>
    where TRequest : IRequest
{
    public async Task Handle(TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken = default)
    {
        recording.AddEvent("void-before");
        await next();
        recording.AddEvent("void-after");
    }
}

public sealed class ShortCircuitResponseBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default) =>
        request is ShortCircuitResponseRequest
            ? Task.FromResult((TResponse)(object)123)
            : next();
}

public sealed class ShortCircuitVoidBehavior<TRequest> : IPipelineBehavior<TRequest>
    where TRequest : IRequest
{
    public Task Handle(
        TRequest request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken = default) =>
        request is ShortCircuitVoidRequest ? Task.CompletedTask : next();
}

public sealed class InvalidBehavior
{
}

public sealed class FirstNotificationHandler(Recording recording) : INotificationHandler<TestNotification>
{
    public Task Handle(TestNotification notification, CancellationToken cancellationToken)
    {
        recording.AddEvent("one");
        return Task.CompletedTask;
    }
}

public sealed class SecondNotificationHandler(Recording recording) : INotificationHandler<TestNotification>
{
    public Task Handle(TestNotification notification, CancellationToken cancellationToken)
    {
        recording.AddEvent("two");
        return Task.CompletedTask;
    }
}

public sealed class ParallelNotificationHandlerOne(Recording recording) : INotificationHandler<ParallelNotification>
{
    public async Task Handle(ParallelNotification notification, CancellationToken cancellationToken)
    {
        await Task.Delay(20, cancellationToken);
        Interlocked.Increment(ref recording.ParallelHandlersCompleted);
        recording.AddEvent("parallel-one");
    }
}

public sealed class ParallelNotificationHandlerTwo(Recording recording) : INotificationHandler<ParallelNotification>
{
    public async Task Handle(ParallelNotification notification, CancellationToken cancellationToken)
    {
        await Task.Delay(20, cancellationToken);
        Interlocked.Increment(ref recording.ParallelHandlersCompleted);
        recording.AddEvent("parallel-two");
    }
}

public sealed class FailingNotificationHandler : INotificationHandler<FailingNotification>
{
    public Task Handle(FailingNotification notification, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("notification failure");
}

public sealed class FirstSequentialFailingNotificationHandler : INotificationHandler<SequentialFailingNotification>
{
    public Task Handle(SequentialFailingNotification notification, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("sequential notification failure");
}

public sealed class SecondSequentialFailingNotificationHandler(Recording recording)
    : INotificationHandler<SequentialFailingNotification>
{
    public Task Handle(SequentialFailingNotification notification, CancellationToken cancellationToken)
    {
        recording.SequentialHandlerAfterFailureRan = true;
        return Task.CompletedTask;
    }
}

public sealed class CancellationNotificationHandler(Recording recording) : INotificationHandler<CancellationNotification>
{
    public Task Handle(CancellationNotification notification, CancellationToken cancellationToken)
    {
        recording.NotificationCancellationObserved = cancellationToken.IsCancellationRequested;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public sealed class Recording
{
    public List<string> Events { get; } = [];

    public void AddEvent(string value)
    {
        lock (Events)
            Events.Add(value);
    }

    public bool VoidHandled { get; set; }
    public bool NotificationCancellationObserved { get; set; }
    public bool SequentialHandlerAfterFailureRan { get; set; }
    public int ResponseHandlerConstructions { get; set; }
    public int VoidHandlerConstructions { get; set; }
    public int ParallelHandlersCompleted;
}

public sealed class ScopedValue
{
    public Guid Id { get; } = Guid.NewGuid();
}
