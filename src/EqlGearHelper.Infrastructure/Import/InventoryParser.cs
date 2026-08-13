using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EqlGearHelper.Application;

namespace EqlGearHelper.Infrastructure.Import;

public sealed partial class InventoryParser : IInventoryParser
{
    private static readonly string[] UnavailableStorage = ["Dragon Hoard", "Item Storage", "Exaltation Storage"];

    public InventorySnapshotDraft Parse(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var header = reader.ReadLine();
        if (!string.Equals(header, "Location\tName\tID\tCount\tSlots", StringComparison.Ordinal))
        {
            throw new FormatException("Inventory data must begin with the expected tab-separated header.");
        }

        var rows = new List<RawInventoryRow>();
        var items = new List<InventoryItemDraft>();
        var sockets = new List<InventorySocketDraft>();
        var itemIdsByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        var canonicalPathsByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        var pathOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var rowNumber = 1;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            rowNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var columns = line.Split('\t');
            if (columns.Length != 5)
            {
                if (IsSectionHeader(columns)) continue;
                throw new FormatException($"Inventory row {rowNumber} must have five columns.");
            }
            var sourcePath = Require(columns[0], rowNumber, "location");
            var path = CanonicalPath(sourcePath, pathOccurrences);
            var name = Require(columns[1], rowNumber, "name");
            var itemId = Require(columns[2], rowNumber, "ID");
            if (!int.TryParse(columns[3], out var count) || count < 0) throw new FormatException($"Inventory row {rowNumber} has an invalid count.");
            if (!int.TryParse(columns[4], out var slots) || slots < 0) throw new FormatException($"Inventory row {rowNumber} has an invalid slot count.");

            var isEmpty = string.Equals(name, "Empty", StringComparison.OrdinalIgnoreCase) || itemId == "0";
            var isExaltation = name.Contains("(Exaltation)", StringComparison.OrdinalIgnoreCase);
            var status = isEmpty ? MappingStatus.Empty : isExaltation ? MappingStatus.ExaltationCandidate : MappingStatus.Unknown;
            rows.Add(new RawInventoryRow(rowNumber, path, name, itemId, count, slots, status, line));

            var parentPath = ParentPath(sourcePath);
            if (!isEmpty && !isExaltation)
            {
                itemIdsByPath[sourcePath] = itemId;
                canonicalPathsByPath[sourcePath] = path;
                items.Add(new InventoryItemDraft(DeterministicGuid(path), path, name, itemId, count, UpgradeLevel(name), status));
            }

            if (parentPath is not null && (isEmpty || isExaltation))
            {
                itemIdsByPath.TryGetValue(parentPath, out var hostItemId);
                canonicalPathsByPath.TryGetValue(parentPath, out var canonicalParentPath);
                sockets.Add(new InventorySocketDraft(path, canonicalParentPath ?? parentPath, hostItemId ?? string.Empty, itemId, name, isExaltation,
                    isExaltation && !string.Equals(hostItemId, itemId, StringComparison.Ordinal), status));
            }
        }

        return new InventorySnapshotDraft(rows, items, sockets,
            UnavailableStorage.Select(name => new InventoryStorage(name, StorageAvailability.Unavailable)).ToArray());
    }

    private static string Require(string value, int rowNumber, string column) =>
        !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new FormatException($"Inventory row {rowNumber} has no {column}.");

    private static bool IsSectionHeader(string[] columns) =>
        columns.Length is 3 or 4 &&
        string.Equals(columns[1], "Name", StringComparison.Ordinal) &&
        string.Equals(columns[2], "ID", StringComparison.Ordinal);

    private static string CanonicalPath(string sourcePath, Dictionary<string, int> occurrences)
    {
        var occurrence = occurrences.TryGetValue(sourcePath, out var existing) ? existing + 1 : 1;
        occurrences[sourcePath] = occurrence;
        return occurrence == 1 ? sourcePath : $"{sourcePath} [{occurrence}]";
    }


    private static string? ParentPath(string path)
    {
        var index = path.LastIndexOf("-Slot", StringComparison.OrdinalIgnoreCase);
        return index > 0 ? path[..index] : null;
    }

    private static int UpgradeLevel(string name)
    {
        var match = UpgradeSuffix().Match(name);
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private static Guid DeterministicGuid(string path) => new(MD5.HashData(Encoding.UTF8.GetBytes(path)));

    [GeneratedRegex(@"\s\+(\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex UpgradeSuffix();
}
