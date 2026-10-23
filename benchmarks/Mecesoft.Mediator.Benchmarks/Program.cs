using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Lightweight = Mecesoft.Mediator;
using Official = MediatR;

if (args.Contains("--cold"))
{
    ColdWarmMeasurements.Run();
    return;
}

BenchmarkRunner.Run<MediatorBenchmarks>();

[MemoryDiagnoser]
public class MediatorBenchmarks
{
    private IServiceProvider lightweightZero = null!;
    private IServiceProvider lightweightOne = null!;
    private IServiceProvider lightweightMultiple = null!;
    private IServiceProvider officialZero = null!;
    private IServiceProvider officialOne = null!;
    private IServiceProvider officialMultiple = null!;

    [GlobalSetup]
    public void Setup()
    {
        lightweightZero = CreateLightweightProvider();
        lightweightOne = CreateLightweightProvider(typeof(LightweightPipelineOne<,>));
        lightweightMultiple = CreateLightweightProvider(
            typeof(LightweightPipelineOne<,>), typeof(LightweightPipelineTwo<,>));

        officialZero = CreateMediatRProvider();
        officialOne = CreateMediatRProvider(typeof(OfficialPipelineOne<,>));
        officialMultiple = CreateMediatRProvider(
            typeof(OfficialPipelineOne<,>), typeof(OfficialPipelineTwo<,>));
    }

    [Benchmark(Baseline = true)]
    public Task<int> Mecesoft_ZeroBehaviors() =>
        lightweightZero.GetRequiredService<Lightweight.ISender>().Send(new LightweightZeroRequest());

    [Benchmark]
    public Task<int> MediatR_ZeroBehaviors() =>
        officialZero.GetRequiredService<Official.ISender>().Send(new OfficialZeroRequest());

    [Benchmark]
    public Task<int> Mecesoft_OneBehavior() =>
        lightweightOne.GetRequiredService<Lightweight.ISender>().Send(new LightweightOneRequest());

    [Benchmark]
    public Task<int> MediatR_OneBehavior() =>
        officialOne.GetRequiredService<Official.ISender>().Send(new OfficialOneRequest());

    [Benchmark]
    public Task<int> Mecesoft_MultipleBehaviors() =>
        lightweightMultiple.GetRequiredService<Lightweight.ISender>().Send(new LightweightMultipleRequest());

    [Benchmark]
    public Task<int> MediatR_MultipleBehaviors() =>
        officialMultiple.GetRequiredService<Official.ISender>().Send(new OfficialMultipleRequest());

    private static IServiceProvider CreateLightweightProvider(params Type[] behaviors)
    {
        var services = new ServiceCollection();
        Lightweight.ServiceRegistrar.AddMediator(services, options =>
        {
            options.AddRegisterAssemblies(typeof(LightweightRequestHandler).Assembly);
            foreach (var behavior in behaviors)
                options.AddOpenBehavior(behavior);
        });
        return services.BuildServiceProvider();
    }

    private static IServiceProvider CreateMediatRProvider(params Type[] behaviors)
    {
        var services = new ServiceCollection();
        services.AddMediatR(options =>
        {
            options.RegisterServicesFromAssemblyContaining<OfficialRequestHandler>();
            foreach (var behavior in behaviors)
                options.AddOpenBehavior(behavior);
        });
        return services.BuildServiceProvider();
    }
}

public sealed record LightweightZeroRequest : Lightweight.IRequest<int>;
public sealed record LightweightOneRequest : Lightweight.IRequest<int>;
public sealed record LightweightMultipleRequest : Lightweight.IRequest<int>;

public sealed class LightweightRequestHandler :
    Lightweight.IRequestHandler<LightweightZeroRequest, int>,
    Lightweight.IRequestHandler<LightweightOneRequest, int>,
    Lightweight.IRequestHandler<LightweightMultipleRequest, int>,
    Lightweight.IRequestHandler<LightweightColdRequest, int>
{
    public Task<int> Handle(LightweightZeroRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(1);

    public Task<int> Handle(LightweightOneRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(1);

    public Task<int> Handle(LightweightMultipleRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(1);

    public Task<int> Handle(LightweightColdRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(1);
}

public sealed class LightweightPipelineOne<TRequest, TResponse> :
    Lightweight.IPipelineBehavior<TRequest, TResponse>
    where TRequest : Lightweight.IRequest<TResponse>
{
    public Task<TResponse> Handle(
        TRequest request,
        Lightweight.RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default) => next();
}

public sealed class LightweightPipelineTwo<TRequest, TResponse> :
    Lightweight.IPipelineBehavior<TRequest, TResponse>
    where TRequest : Lightweight.IRequest<TResponse>
{
    public Task<TResponse> Handle(
        TRequest request,
        Lightweight.RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default) => next();
}

public sealed record OfficialZeroRequest : Official.IRequest<int>;
public sealed record OfficialOneRequest : Official.IRequest<int>;
public sealed record OfficialMultipleRequest : Official.IRequest<int>;

public sealed class OfficialRequestHandler :
    Official.IRequestHandler<OfficialZeroRequest, int>,
    Official.IRequestHandler<OfficialOneRequest, int>,
    Official.IRequestHandler<OfficialMultipleRequest, int>,
    Official.IRequestHandler<OfficialColdRequest, int>
{
    public Task<int> Handle(OfficialZeroRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(1);

    public Task<int> Handle(OfficialOneRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(1);

    public Task<int> Handle(OfficialMultipleRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(1);

    public Task<int> Handle(OfficialColdRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(1);
}

public sealed class OfficialPipelineOne<TRequest, TResponse> :
    Official.IPipelineBehavior<TRequest, TResponse>
    where TRequest : Official.IRequest<TResponse>
{
    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default) => next();
}

public sealed class OfficialPipelineTwo<TRequest, TResponse> :
    Official.IPipelineBehavior<TRequest, TResponse>
    where TRequest : Official.IRequest<TResponse>
{
    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default) => next();
}

internal static class ColdWarmMeasurements
{
    public static void Run()
    {
        var lightweightServices = new ServiceCollection();
        Lightweight.ServiceRegistrar.AddMediator(lightweightServices, options =>
            options.AddRegisterAssemblies(typeof(LightweightRequestHandler).Assembly));
        using var lightweight = lightweightServices.BuildServiceProvider();
        var lightweightSender = lightweight.GetRequiredService<Lightweight.ISender>();

        var officialServices = new ServiceCollection();
        officialServices.AddMediatR(options =>
            options.RegisterServicesFromAssemblyContaining<OfficialRequestHandler>());
        using var official = officialServices.BuildServiceProvider();
        var officialSender = official.GetRequiredService<Official.ISender>();

        var lightweightCold = MeasureSingle(() =>
            lightweightSender.Send(new LightweightColdRequest()).GetAwaiter().GetResult());
        var officialCold = MeasureSingle(() =>
            officialSender.Send(new OfficialColdRequest()).GetAwaiter().GetResult());
        var lightweightWarm = MeasureAverage(() =>
            lightweightSender.Send(new LightweightZeroRequest()).GetAwaiter().GetResult());
        var officialWarm = MeasureAverage(() =>
            officialSender.Send(new OfficialZeroRequest()).GetAwaiter().GetResult());

        Console.WriteLine($"Cold dispatch (includes first dispatcher creation): Mecesoft.Mediator {lightweightCold:F2} ns; MediatR {officialCold:F2} ns");
        Console.WriteLine($"Warm dispatch average (100,000 calls): Mecesoft.Mediator {lightweightWarm:F2} ns; MediatR {officialWarm:F2} ns");
    }

    private static double MeasureSingle(Action action)
    {
        var start = Stopwatch.GetTimestamp();
        action();
        return Stopwatch.GetElapsedTime(start).TotalNanoseconds;
    }

    private static double MeasureAverage(Action action)
    {
        for (var i = 0; i < 10_000; i++)
            action();

        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < 100_000; i++)
            action();
        return Stopwatch.GetElapsedTime(start).TotalNanoseconds / 100_000;
    }
}

public sealed record LightweightColdRequest : Lightweight.IRequest<int>;
public sealed record OfficialColdRequest : Official.IRequest<int>;
