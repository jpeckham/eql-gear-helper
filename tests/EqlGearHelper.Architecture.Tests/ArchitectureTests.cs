using System.Reflection;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using EqlGearHelper.Application;
using EqlGearHelper.Domain;
using EqlGearHelper.Infrastructure;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace EqlGearHelper.Architecture.Tests;

public sealed class ArchitectureTests
{
    private const string DomainNamespaceRecursively = @"^EqlGearHelper\.Domain(\..*)?$";
    private const string ApplicationNamespaceRecursively = @"^EqlGearHelper\.Application(\..*)?$";

    private static readonly Assembly DomainAssembly = typeof(DomainAssemblyMarker).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(ApplicationAssemblyMarker).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(InfrastructureAssemblyMarker).Assembly;
    private static readonly Assembly WpfAssembly = typeof(global::App).Assembly;

    private static readonly ArchUnitNET.Domain.Architecture Architecture = new ArchLoader()
        .LoadAssemblies(DomainAssembly, ApplicationAssembly, InfrastructureAssembly, WpfAssembly)
        .Build();

    [Fact]
    public void Domain_HasNoFrameworkDependencies()
    {
        var domain = Classes().That().ResideInNamespaceMatching(DomainNamespaceRecursively);

        domain.Should().NotDependOnAny(Classes().That().ResideInNamespaceMatching(@"^System\.Windows(\..*)?$")).Check(Architecture);
        domain.Should().NotDependOnAny(Classes().That().ResideInNamespaceMatching(@"^Microsoft\.Data\.Sqlite(\..*)?$")).Check(Architecture);
        domain.Should().NotDependOnAny(Classes().That().ResideInNamespaceMatching(@"^System\.Net\.Http(\..*)?$")).Check(Architecture);
        domain.Should().NotDependOnAny(Classes().That().ResideInNamespaceMatching(@"^System\.IO(\..*)?$")).Check(Architecture);
    }

    [Fact]
    public void Application_HasNoFrameworkOrIoDependencies()
    {
        var application = Classes().That().ResideInNamespaceMatching(ApplicationNamespaceRecursively);

        application.Should().NotDependOnAny(Classes().That().ResideInNamespaceMatching(@"^System\.Windows(\..*)?$")).Check(Architecture);
        application.Should().NotDependOnAny(Classes().That().ResideInNamespaceMatching(@"^Microsoft\.Data\.Sqlite(\..*)?$")).Check(Architecture);
        application.Should().NotDependOnAny(Classes().That().ResideInNamespaceMatching(@"^System\.Net\.Http(\..*)?$")).Check(Architecture);
        application.Should().NotDependOnAny(Classes().That().ResideInNamespaceMatching(@"^System\.IO(\..*)?$")).Check(Architecture);
    }

    [Fact]
    public void Wpf_DependsOnApplicationButNotInnerOrInfrastructureLayers()
    {
        AssertReferences(WpfAssembly, ApplicationAssembly);
        AssertDoesNotReference(WpfAssembly, DomainAssembly, InfrastructureAssembly);
    }

    [Fact]
    public void Application_DependsOnDomainButNotOuterLayers()
    {
        AssertReferences(ApplicationAssembly, DomainAssembly);
        AssertDoesNotReference(ApplicationAssembly, WpfAssembly, InfrastructureAssembly);
    }

    [Fact]
    public void Infrastructure_DependsOnApplicationAndDomainButNotWpf()
    {
        AssertReferences(InfrastructureAssembly, ApplicationAssembly, DomainAssembly);
        AssertDoesNotReference(InfrastructureAssembly, WpfAssembly);
    }

    private static void AssertReferences(Assembly source, params Assembly[] expectedDependencies)
    {
        var references = source.GetReferencedAssemblies().Select(reference => reference.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var dependency in expectedDependencies)
        {
            Assert.Contains(dependency.GetName().Name, references);
        }
    }

    private static void AssertDoesNotReference(Assembly source, params Assembly[] forbiddenDependencies)
    {
        var references = source.GetReferencedAssemblies().Select(reference => reference.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var dependency in forbiddenDependencies)
        {
            Assert.DoesNotContain(dependency.GetName().Name, references);
        }
    }
}
