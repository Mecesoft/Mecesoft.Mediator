using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Mecesoft.Mediator;

public static class ServiceRegistrar
{
    public static IServiceCollection AddMediator(
        this IServiceCollection services,
        Action<MediatorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var config = new MediatorOptions();
        options(config);
        services.AddTransient<ISender>(provider =>
            new Sender(provider, config.PublishNotificationsConcurrently));

        var registered = new HashSet<(Type Service, Type Implementation)>();
        foreach (var assembly in config.Assemblies.Distinct())
        {
            foreach (var implementation in GetSafeTypes(assembly)
                .Where(type => !type.IsInterface && !type.IsAbstract))
            {
                foreach (var service in implementation.GetInterfaces().Where(IsRequestHandler))
                    AddHandler(services, registered, service, implementation);

                foreach (var service in implementation.GetInterfaces().Where(IsNotificationHandler))
                    AddHandler(services, registered, service, implementation);
            }
        }

        foreach (var pipeline in config.PipelineBehaviors.Distinct())
        {
            if (!pipeline.IsClass || !pipeline.IsGenericTypeDefinition)
                throw new ArgumentException($"Pipeline behavior '{pipeline}' must be an open generic class.", nameof(options));

            var argumentCount = pipeline.GetGenericArguments().Length;
            var serviceType = argumentCount switch
            {
                1 when ImplementsOpenGeneric(pipeline, typeof(IPipelineBehavior<>)) => typeof(IPipelineBehavior<>),
                2 when ImplementsOpenGeneric(pipeline, typeof(IPipelineBehavior<,>)) => typeof(IPipelineBehavior<,>),
                _ => throw new ArgumentException(
                    $"Pipeline behavior '{pipeline}' does not implement a supported pipeline interface.", nameof(options))
            };
            services.AddTransient(serviceType, pipeline);
        }

        return services;
    }

    private static void AddHandler(
        IServiceCollection services,
        HashSet<(Type Service, Type Implementation)> registered,
        Type service,
        Type implementation)
    {
        if (registered.Add((service, implementation)))
            services.AddTransient(service, implementation);
    }

    private static bool IsRequestHandler(Type type) =>
        type.IsGenericType &&
        (type.GetGenericTypeDefinition() == typeof(IRequestHandler<>) ||
         type.GetGenericTypeDefinition() == typeof(IRequestHandler<,>));

    private static bool IsNotificationHandler(Type type) =>
        type.IsGenericType &&
        type.GetGenericTypeDefinition() == typeof(INotificationHandler<>);

    private static IEnumerable<Type> GetSafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            var loaderErrors = exception.LoaderExceptions
                .Where(error => error is not null)
                .Select(error => error!.Message);
            throw new InvalidOperationException(
                $"Unable to scan assembly '{assembly.FullName}': {string.Join(" | ", loaderErrors)}", exception);
        }
    }

    private static bool ImplementsOpenGeneric(Type type, Type openInterface) =>
        type.GetInterfaces().Any(interfaceType =>
            interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == openInterface);
}
