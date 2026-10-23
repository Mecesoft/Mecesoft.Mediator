# Mecesoft.Mediator

> **Package version:** 1.0.0 (not yet published on NuGet)
>
> Supports **.NET 8** and **.NET 10**.
> Includes optimized request dispatch, improved pipeline execution,
> and configurable notification publishing.

Mecesoft.Mediator is a lightweight, dependency-injection-friendly mediator library
for .NET. It supports requests, notifications, dependency-injection scopes, and
ordered pipeline behaviors without an external mediator dependency.

## Project links

- [GitHub repository](https://github.com/Mecesoft/Mecesoft.Mediator)
- NuGet package (draft; version 1.0.0 is not published yet): `https://www.nuget.org/packages/Mecesoft.Mediator`

## Installation

Version 1.0.0 is not published yet. The intended installation command after publication is:

```bash
dotnet add package Mecesoft.Mediator
```

## Registration

Register handlers from one or more assemblies and, optionally, open pipeline
behaviors:

```csharp
using Mecesoft.Mediator;

builder.Services.AddMediator(options =>
{
    options.AddRegisterAssemblies(typeof(Program).Assembly);
    options.AddOpenBehavior(typeof(LoggingBehavior<,>));
});
```

`ISender` and discovered handlers are registered as transient services. Resolve
`ISender` from the request scope so handlers can safely consume scoped services
such as `DbContext`.

## Requests

Requests with a response implement `IRequest<TResponse>`:

```csharp
public sealed record GetUser(Guid Id) : IRequest<User>;

public sealed class GetUserHandler(IUserRepository repository)
    : IRequestHandler<GetUser, User>
{
    public Task<User> Handle(
        GetUser request,
        CancellationToken cancellationToken) =>
        repository.GetAsync(request.Id, cancellationToken);
}

User user = await sender.Send(new GetUser(id), cancellationToken);
```

Void requests implement `IRequest` and use `IRequestHandler<TRequest>`.
Cancellation tokens are passed to handlers and pipeline behaviors unchanged.

## Pipeline behaviors

For response requests, a pipeline behavior declares both the request and response
types:

```csharp
public sealed class LoggingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        return await next();
    }
}
```

Void requests use `IPipelineBehavior<TRequest>` and `RequestHandlerDelegate`.
Pipeline behaviors execute in registration order and may short-circuit without
constructing the request handler.

## Notifications

Notification handlers run concurrently by default. Applications whose handlers
share scoped dependencies that are not concurrency-safe can opt into sequential
execution during registration:

```csharp
builder.Services.AddMediator(options =>
{
    options.AddRegisterAssemblies(typeof(Program).Assembly);
    options.PublishNotificationsConcurrently = false;
});
```

Notification and request handler failures propagate to the caller without being
wrapped in reflection exceptions.

## API

The 1.0.0 public API uses:

`IRequest`, `IRequest<TResponse>`, `IRequestHandler`, `ISender`, `Sender`,
`INotification`, `INotificationHandler`, `IPipelineBehavior`, `AddMediator`,
`MediatorOptions`, `RequestHandlerDelegate`, and
`MediatorOptions.PublishNotificationsConcurrently`.

No legacy aliases are provided. Consumer code must use `AddMediator`,
`MediatorOptions`, and `RequestHandlerDelegate`.

## License

Mecesoft.Mediator is available under the [MIT License](LICENSE).
