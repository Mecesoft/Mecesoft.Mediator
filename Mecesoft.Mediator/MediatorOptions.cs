using System.Reflection;
namespace Mecesoft.Mediator;
public sealed class MediatorOptions
{
    internal List<Assembly> Assemblies { get; set; } = new();
    internal List<Type> PipelineBehaviors { get; set; } = new();
    private bool publishNotificationsConcurrently = true;

    public void AddRegisterAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Any(assembly => assembly is null))
            throw new ArgumentException("Assemblies cannot contain null values.", nameof(assemblies));

        Assemblies.AddRange(assemblies);
    }

    public void AddOpenBehavior(Type behaviorType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);
        PipelineBehaviors.Add(behaviorType);
    }

    /// <summary>Controls whether notification handlers are invoked concurrently.</summary>
    /// <remarks>
    /// Concurrent execution is the default. Set this property to
    /// <see langword="false"/> to execute handlers sequentially when they share scoped dependencies.
    /// </remarks>
    public bool PublishNotificationsConcurrently
    {
        get => publishNotificationsConcurrently;
        set => publishNotificationsConcurrently = value;
    }
}
