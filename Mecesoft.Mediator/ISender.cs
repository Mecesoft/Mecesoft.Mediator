using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Mecesoft.Mediator;

public interface ISender
{
    Task Send(IRequest request, CancellationToken cancellationToken = default);
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
    Task Publish(INotification notification, CancellationToken cancellationToken = default);
}

public sealed class Sender : ISender
{
    private readonly IServiceProvider serviceProvider;
    private readonly bool publishNotificationsConcurrently;

    // These caches contain only immutable compiled dispatchers. They never retain a
    // scoped handler or IServiceProvider, and are shared safely between Sender instances.
    private static readonly ConcurrentDictionary<Type, Func<Sender, object, CancellationToken, Task>> RequestDispatchers = new();
    private static readonly ConcurrentDictionary<(Type Request, Type Response), Func<Sender, object, CancellationToken, object>> ResponseDispatchers = new();
    private static readonly ConcurrentDictionary<Type, Func<object, object, CancellationToken, Task>> NotificationHandlers = new();

    public Sender(IServiceProvider serviceProvider)
        : this(serviceProvider, true)
    {
    }

    internal Sender(IServiceProvider serviceProvider, bool publishNotificationsConcurrently)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        this.publishNotificationsConcurrently = publishNotificationsConcurrently;
    }

    public Task Send(IRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RequestDispatchers.GetOrAdd(request.GetType(), CreateRequestDispatcher)(
            this, request, cancellationToken);
    }

    public Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = (request.GetType(), typeof(TResponse));
        return (Task<TResponse>)ResponseDispatchers.GetOrAdd(key, CreateResponseDispatcher)(
            this, request, cancellationToken);
    }

    public async Task Publish(INotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var notificationType = notification.GetType();
        var handlerType = typeof(INotificationHandler<>).MakeGenericType(notificationType);
        var handlers = serviceProvider.GetServices(handlerType).ToArray();

        if (publishNotificationsConcurrently)
        {
            await Task.WhenAll(handlers.Select(handler =>
                NotificationHandlers.GetOrAdd(handlerType, CreateNotificationHandler)(
                    handler!, notification, cancellationToken))).ConfigureAwait(false);
            return;
        }

        foreach (var handler in handlers)
            await NotificationHandlers.GetOrAdd(handlerType, CreateNotificationHandler)(
                handler!, notification, cancellationToken).ConfigureAwait(false);
    }

    private static Task SendRequestCore<TRequest>(
        Sender sender,
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest
    {
        var behaviors = sender.serviceProvider.GetServices<IPipelineBehavior<TRequest>>().ToArray();
        return Execute(0);

        Task Execute(int index)
        {
            if (index == behaviors.Length)
            {
                var handler = sender.serviceProvider.GetRequiredService<IRequestHandler<TRequest>>();
                return handler.Handle(request, cancellationToken);
            }

            return behaviors[index].Handle(request, () => Execute(index + 1), cancellationToken);
        }
    }

    private static Task<TResponse> SendResponseCore<TRequest, TResponse>(
        Sender sender,
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        var behaviors = sender.serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>().ToArray();
        return Execute(0);

        Task<TResponse> Execute(int index)
        {
            if (index == behaviors.Length)
            {
                var handler = sender.serviceProvider.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
                return handler.Handle(request, cancellationToken);
            }

            return behaviors[index].Handle(request, () => Execute(index + 1), cancellationToken);
        }
    }

    private static Func<Sender, object, CancellationToken, Task> CreateRequestDispatcher(Type requestType)
    {
        var method = typeof(Sender).GetMethod(nameof(SendRequestCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(requestType);
        return CreateDispatcher<Func<Sender, object, CancellationToken, Task>>(method);
    }

    private static Func<Sender, object, CancellationToken, object> CreateResponseDispatcher(
        (Type Request, Type Response) key)
    {
        var method = typeof(Sender).GetMethod(nameof(SendResponseCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(key.Request, key.Response);
        return CreateDispatcher<Func<Sender, object, CancellationToken, object>>(method);
    }

    private static TDelegate CreateDispatcher<TDelegate>(MethodInfo method)
        where TDelegate : Delegate
    {
        var sender = Expression.Parameter(typeof(Sender));
        var request = Expression.Parameter(typeof(object));
        var cancellationToken = Expression.Parameter(typeof(CancellationToken));
        var call = Expression.Call(method, sender,
            Expression.Convert(request, method.GetParameters()[1].ParameterType),
            cancellationToken);
        return Expression.Lambda<TDelegate>(
            Expression.Convert(call, typeof(TDelegate).GetMethod("Invoke")!.ReturnType),
            sender, request, cancellationToken).Compile();
    }

    private static Func<object, object, CancellationToken, Task> CreateNotificationHandler(Type type)
    {
        var method = type.GetMethod("Handle")!;
        var instance = Expression.Parameter(typeof(object));
        var argument = Expression.Parameter(typeof(object));
        var token = Expression.Parameter(typeof(CancellationToken));
        var call = Expression.Call(Expression.Convert(instance, method.DeclaringType!), method,
            Expression.Convert(argument, method.GetParameters()[0].ParameterType), token);
        return Expression.Lambda<Func<object, object, CancellationToken, Task>>(
            call, instance, argument, token).Compile();
    }
}
