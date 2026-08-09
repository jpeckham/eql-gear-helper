using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Xunit;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace EqlGearHelper.Tests;

public class CleanArchitectureTests
{
    private static readonly Architecture AppArchitecture =
        new ArchLoader().LoadAssemblies(typeof(ItemLookupUseCase).Assembly).Build();

    [Fact]
    public void UseCases_DoNotDependOnUIOrInfrastructureImplementations()
    {
        var useCases = Classes().That().HaveNameContaining("UseCase");
        useCases
            .Should()
            .NotDependOnAny(Classes().That().HaveNameContaining("MainWindow"))
            .Check(AppArchitecture);
        useCases
            .Should()
            .NotDependOnAny(Classes().That().HaveNameContaining("GearLookupService"))
            .Check(AppArchitecture);
    }

    [Fact]
    public void Controllers_DoNotDependOnUIOrServiceImplementations()
    {
        var controllers = Classes().That().HaveNameContaining("Controller");
        controllers
            .Should()
            .NotDependOnAny(Classes().That().HaveNameContaining("MainWindow"))
            .Check(AppArchitecture);
        controllers
            .Should()
            .NotDependOnAny(Classes().That().HaveNameContaining("GearLookupService"))
            .Check(AppArchitecture);
    }

    [Fact]
    public void Presenters_DoNotDependOnControllersOrUseCases()
    {
        var presenters = Classes().That().HaveNameContaining("Presenter");
        presenters
            .Should()
            .NotDependOnAny(Classes().That().HaveNameContaining("Controller"))
            .Check(AppArchitecture);
        presenters
            .Should()
            .NotDependOnAny(Classes().That().HaveNameContaining("UseCase"))
            .Check(AppArchitecture);
    }
}
