namespace EqlGearHelper.Application;

public sealed class CatalogPackageImportUseCase(ICatalogPackageImporter importer, ICatalogRepository repository)
{
    public async Task<CatalogPackage> ExecuteAsync(Stream input, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(input);
        var package = importer.Parse(input);
        package.Validate();
        await repository.ReplaceAsync(package, token);
        return package;
    }
}
