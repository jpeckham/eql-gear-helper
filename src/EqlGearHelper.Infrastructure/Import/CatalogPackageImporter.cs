using System.Text.Json;
using EqlGearHelper.Application;
using EqlGearHelper.Domain;

namespace EqlGearHelper.Infrastructure.Import;

public sealed class CatalogPackageImporter : ICatalogPackageImporter
{
    public CatalogPackage Parse(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var document = JsonDocument.Parse(input);
        var root = document.RootElement;
        var catalogVersion = Required(root, "catalogVersion");
        var rulesetVersion = Required(root, "rulesetVersion");
        var items = root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array
            ? itemsElement.EnumerateArray().Select(ParseItem).ToArray()
            : throw new FormatException("Catalog packages require an items array.");
        var exaltations = root.TryGetProperty("exaltations", out var exaltationsElement) && exaltationsElement.ValueKind == JsonValueKind.Array
            ? exaltationsElement.EnumerateArray().Select(ParseExaltation).ToArray()
            : [];
        var package = new CatalogPackage(catalogVersion, rulesetVersion, items, exaltations);
        package.Validate();
        return package;
    }

    private static CatalogItem ParseItem(JsonElement element)
    {
        var positions = element.TryGetProperty("positions", out var positionsElement) && positionsElement.ValueKind == JsonValueKind.Array
            ? positionsElement.EnumerateArray().Select(position => new EquipmentPosition(
                Enum.Parse<SlotType>(Required(position, "type"), ignoreCase: true),
                position.TryGetProperty("index", out var index) ? index.GetInt32() : 0)).ToArray()
            : throw new FormatException("Catalog items require positions.");
        var statistics = element.TryGetProperty("statistics", out var statisticsElement) && statisticsElement.ValueKind == JsonValueKind.Object
            ? statisticsElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetInt32(), StringComparer.OrdinalIgnoreCase)
            : null;
        return new CatalogItem(Required(element, "id"), Required(element, "name"), Classes(element), positions, statistics,
            element.TryGetProperty("isLore", out var isLore) && isLore.GetBoolean());
    }

    private static ExaltationDefinition ParseExaltation(JsonElement element) => new(
        Required(element, "id"), Required(element, "name"), Classes(element),
        element.TryGetProperty("isValuable", out var valuable) && valuable.GetBoolean());

    private static ClassSet Classes(JsonElement element)
    {
        if (!element.TryGetProperty("classes", out var classes) || classes.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Catalog definitions require classes.");
        }

        return ClassSet.Of(classes.EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray());
    }

    private static string Required(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : throw new FormatException($"Catalog packages require {property}.");
}
