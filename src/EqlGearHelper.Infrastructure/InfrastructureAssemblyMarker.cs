namespace EqlGearHelper.Infrastructure;

public static class InfrastructureAssemblyMarker
{
    public static IReadOnlyList<Type> InnerLayerAssemblies =>
    [
        typeof(Application.ApplicationAssemblyMarker),
        typeof(Domain.DomainAssemblyMarker)
    ];
}
