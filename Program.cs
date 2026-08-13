using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Net;

const string BaseApiUrl = "https://eqlegendstools.com/api/bis-gear";
const string WikiApiUrl = "https://eqlwiki.com/api.php";
const int MaxConcurrency = 4;

var classes = new[]
{
    "Bard", "Beastlord", "Berserker", "Cleric", "Druid", "Enchanter", "Magician", "Monk",
    "Necromancer", "Paladin", "Ranger", "Rogue", "Shadow Knight", "Shaman", "Warrior", "Wizard"
};

using var httpClient = CreateHttpClient();
var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

Console.WriteLine("Pulling EQ Legends BiS data from eqlegendstools.com...");
var classRows = await LoadAllClassesAsync(classes, httpClient, options);
var sortedByClass = classRows.ToDictionary(
    entry => entry.Key,
    entry => SortRowsForRanking(entry.Value),
    StringComparer.OrdinalIgnoreCase
);
var slotSortedByClass = BuildSlotSortedLists(sortedByClass);

Console.WriteLine("Data loaded. Enter an item name to search (empty line to exit).");

while (true)
{
    Console.WriteLine();
    Console.Write("Item name: ");
    var query = Console.ReadLine();
    if (query is null)
    {
        break;
    }

    var trimmedQuery = query.Trim();
    if (string.IsNullOrWhiteSpace(trimmedQuery))
    {
        break;
    }

    var normalizedQuery = NormalizeQuery(trimmedQuery);
    if (string.IsNullOrEmpty(normalizedQuery))
    {
        Console.WriteLine("Try a clearer item name.");
        continue;
    }

    var matches = FindItemMatches(trimmedQuery, normalizedQuery, classRows, sortedByClass, slotSortedByClass);
    if (matches.Count == 0)
    {
        Console.WriteLine($"No BiS entries matched '{trimmedQuery}'.");
        var wikiResult = await CheckEqWikiItemAsync(trimmedQuery, httpClient);
        if (wikiResult is not null)
        {
            Console.WriteLine("It is present in the EQ wiki:");
            Console.WriteLine($"  Title: {wikiResult.CanonicalTitle}");
            Console.WriteLine($"  URL: {wikiResult.PageUrl}");

            if (!string.IsNullOrWhiteSpace(wikiResult.Slot))
            {
                Console.WriteLine($"  Slot: {wikiResult.Slot}");
            }

            if (!string.IsNullOrWhiteSpace(wikiResult.ClassList))
            {
                Console.WriteLine($"  Classes: {wikiResult.ClassList}");
            }

            if (!string.IsNullOrWhiteSpace(wikiResult.IntValue))
            {
                Console.WriteLine($"  INT: {wikiResult.IntValue}");
            }
        }
        else
        {
            Console.WriteLine("No matching EQ wiki page found either.");
        }

        continue;
    }

    foreach (var match in matches)
    {
        PrintMatchReport(match);
    }
}

return;

static HttpClient CreateHttpClient()
{
    var client = new HttpClient
    {
        DefaultRequestVersion = HttpVersion.Version20,
        Timeout = TimeSpan.FromSeconds(30)
    };

    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    client.DefaultRequestHeaders.Referrer = new Uri("https://eqlegendstools.com/bis-gear/");
    client.DefaultRequestHeaders.Add("Origin", "https://eqlegendstools.com");
    client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

    return client;
}

static async Task<Dictionary<string, List<GearRow>>> LoadAllClassesAsync(
    IReadOnlyList<string> classNames,
    HttpClient httpClient,
    JsonSerializerOptions jsonOptions)
{
    using var throttler = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
    var tasks = classNames.Select(async className =>
    {
        await throttler.WaitAsync();
        try
        {
            var rows = await LoadClassRowsAsync(className, httpClient, jsonOptions);
            return (ClassName: className, Rows: rows);
        }
        finally
        {
            throttler.Release();
        }
    }).ToArray();

    var loaded = await Task.WhenAll(tasks);
    return loaded.ToDictionary(entry => entry.ClassName, entry => entry.Rows, StringComparer.OrdinalIgnoreCase);
}

static async Task<List<GearRow>> LoadClassRowsAsync(
    string className,
    HttpClient httpClient,
    JsonSerializerOptions jsonOptions)
{
    var requestUrl = $"{BaseApiUrl}?classes={Uri.EscapeDataString(className)}";
    using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
    using var response = await httpClient.SendAsync(request);
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"Failed loading {className}: {(int)response.StatusCode} {response.ReasonPhrase}");
        return new List<GearRow>();
    }

    var payload = await response.Content.ReadAsStringAsync();
    var parsed = JsonSerializer.Deserialize<ApiResponse>(payload, jsonOptions);
    if (parsed?.Rows is null)
    {
        Console.Error.WriteLine($"Unexpected payload for {className}.");
        return new List<GearRow>();
    }

    return parsed.Rows
        .Where(r => !string.IsNullOrWhiteSpace(r.Name))
        .DistinctBy(r => NormalizeQuery(r.Name))
        .ToList();
}

static List<GearRow> SortRowsForRanking(IReadOnlyList<GearRow> rows)
{
    return rows
        .OrderByDescending(row => GetStatValue(row, "AC"))
        .ThenByDescending(row => TotalStatScore(row))
        .ThenBy(row => NormalizeQuery(row.Name))
        .ToList();
}

static Dictionary<string, Dictionary<string, List<GearRow>>> BuildSlotSortedLists(
    Dictionary<string, List<GearRow>> sortedByClass)
{
    var output = new Dictionary<string, Dictionary<string, List<GearRow>>>(StringComparer.OrdinalIgnoreCase);
    foreach (var (className, rows) in sortedByClass)
    {
        var slotGroups = new Dictionary<string, List<GearRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            foreach (var slot in NormalizeSlots(row.Slots))
            {
                if (!slotGroups.TryGetValue(slot, out var list))
                {
                    list = new List<GearRow>();
                    slotGroups[slot] = list;
                }

                list.Add(row);
            }
        }

        foreach (var slot in slotGroups.Keys.ToList())
        {
            slotGroups[slot] = slotGroups[slot]
                .OrderByDescending(row => GetStatValue(row, "AC"))
                .ThenByDescending(row => TotalStatScore(row))
                .ThenBy(row => NormalizeQuery(row.Name))
                .ToList();
        }

        output[className] = slotGroups;
    }

    return output;
}

static List<MatchReport> FindItemMatches(
    string originalQuery,
    string normalizedQuery,
    Dictionary<string, List<GearRow>> classRows,
    Dictionary<string, List<GearRow>> sortedByClass,
    Dictionary<string, Dictionary<string, List<GearRow>>> slotSortedByClass)
{
    var collected = new Dictionary<string, MatchReport>(StringComparer.OrdinalIgnoreCase);

    foreach (var (className, rows) in classRows)
    {
        var sortedRows = sortedByClass[className];
        foreach (var row in rows)
        {
            var normalizedName = NormalizeQuery(row.Name);
            if (!normalizedName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!collected.TryGetValue(normalizedName, out var report))
            {
                report = new MatchReport(row, normalizedName);
                collected[normalizedName] = report;
            }

            if (!report.MatchesByClass.TryGetValue(className, out var classFits))
            {
                classFits = new List<ClassFit>();
                report.MatchesByClass[className] = classFits;
            }

            var fit = EvaluateFitForClass(row, className, sortedRows, slotSortedByClass);
            classFits.Add(fit);
        }
    }

    var queryExact = NormalizeQuery(originalQuery);
    return collected.Values
        .OrderBy(r => r.BestMatchDistance(queryExact))
        .ThenBy(r => NormalizeQuery(r.Item.Name))
        .ToList();
}

static ClassFit EvaluateFitForClass(
    GearRow item,
    string className,
    List<GearRow> sortedRows,
    Dictionary<string, Dictionary<string, List<GearRow>>> slotSortedByClass)
{
    var matchName = NormalizeQuery(item.Name);
    var overallRank = FindRank(sortedRows, matchName, StringComparer.OrdinalIgnoreCase);
    var overallTotal = sortedRows.Count;

    var slotRanks = new List<SlotFit>();
    var slotMap = slotSortedByClass[className];
    foreach (var slot in NormalizeSlots(item.Slots))
    {
        if (!slotMap.TryGetValue(slot, out var slotRows) || slotRows.Count == 0)
        {
            continue;
        }

        var slotRank = FindRank(slotRows, matchName, StringComparer.OrdinalIgnoreCase);
        slotRanks.Add(new SlotFit(slot, slotRank, slotRows.Count));
    }

    return new ClassFit(className, overallRank, overallTotal, slotRanks);
}

static int FindRank(IReadOnlyList<GearRow> sortedRows, string normalizedName, StringComparer comparer)
{
    for (var index = 0; index < sortedRows.Count; index++)
    {
        if (comparer.Equals(NormalizeQuery(sortedRows[index].Name), normalizedName))
        {
            return index + 1;
        }
    }

    return 0;
}

static void PrintMatchReport(MatchReport report)
{
    var item = report.Item;
    Console.WriteLine();
    Console.WriteLine($"Item: {item.Name}");
    Console.WriteLine($"  Slots: {string.Join(", ", NormalizeSlots(item.Slots))}");
    if (!string.IsNullOrWhiteSpace(item.Source))
    {
        Console.WriteLine($"  Source: {item.Source}");
    }

    Console.WriteLine($"  AC: {GetStatValue(item, "AC"):0}");
    var primaryStats = TopStats(item);
    if (primaryStats.Count > 0)
    {
        Console.WriteLine($"  Notable stats: {string.Join(", ", primaryStats)}");
    }

    Console.WriteLine("  Fit by class:");
    foreach (var (className, fits) in report.MatchesByClass.OrderBy(entry => entry.Key))
    {
        foreach (var fit in fits)
        {
            var overallPct = PercentileText(fit.OverallRank, fit.OverallTotal);
            Console.WriteLine($"    {className,-12} overall rank {fit.OverallRank,4}/{fit.OverallTotal,4} ({overallPct})");
            foreach (var slot in fit.SlotRanks)
            {
                var slotPct = PercentileText(slot.Rank, slot.Total);
                Console.WriteLine($"      Slot {slot.Slot,-10} rank {slot.Rank,4}/{slot.Total,4} ({slotPct})");
            }
        }
    }

    var bestPercentile = report.MatchesByClass.Values
        .SelectMany(values => values)
        .Where(fit => fit.OverallTotal > 0 && fit.OverallRank > 0)
        .Select(fit => (double)fit.OverallRank / fit.OverallTotal * 100)
        .DefaultIfEmpty(100)
        .Min();

    var quality = bestPercentile <= 5 ? "Top-tier candidate" :
                  bestPercentile <= 25 ? "Strong BiS-adjacent option" :
                  "Probably niche for current BiS lists";
    Console.WriteLine($"  Quick read: {quality}");
}

static string PercentileText(int rank, int total)
{
    if (total <= 0 || rank <= 0)
    {
        return "n/a";
    }

    var pct = (double)rank / total * 100.0;
    return $"{pct:0.0}%";
}

static List<string> TopStats(GearRow item)
{
    return item.Stats?
        .Select(stat => new { stat.Key, Value = ParseStatValue(stat.Value) })
        .Where(stat => stat.Value > 0 && stat.Key != "AC")
        .OrderByDescending(stat => stat.Value)
        .ThenBy(stat => stat.Key)
        .Take(5)
        .Select(stat => $"{stat.Key} {stat.Value:0}")
        .ToList() ?? new List<string>();
}

static double TotalStatScore(GearRow row)
{
    if (row.Stats is null)
    {
        return 0;
    }

    return row.Stats.Sum(stat => ParseStatValue(stat.Value));
}

static double GetStatValue(GearRow row, string key)
{
    if (row.Stats is null || !row.Stats.TryGetValue(key, out var element))
    {
        return 0;
    }

    return ParseStatValue(element);
}

static double ParseStatValue(JsonElement element)
{
    return element.ValueKind switch
    {
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.String when double.TryParse(
            element.GetString(),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var value) => value,
        _ => 0
    };
}

static async Task<WikiItemMatch?> CheckEqWikiItemAsync(string itemName, HttpClient httpClient)
{
    var encodedTitle = Uri.EscapeDataString(itemName.Replace(' ', '_'));
    var queryUrl = $"{WikiApiUrl}?action=query&format=json&formatversion=2&titles={encodedTitle}";
    using var queryResponse = await httpClient.GetAsync(queryUrl);
    if (!queryResponse.IsSuccessStatusCode)
    {
        return null;
    }

    using var queryDoc = JsonDocument.Parse(await queryResponse.Content.ReadAsStringAsync());
    if (!queryDoc.RootElement.TryGetProperty("query", out var queryElement) ||
        !queryElement.TryGetProperty("pages", out var pagesElement) ||
        pagesElement.ValueKind != JsonValueKind.Array ||
        pagesElement.GetArrayLength() == 0)
    {
        return null;
    }

    var pageElement = pagesElement[0];
    if (pageElement.TryGetProperty("missing", out _))
    {
        return null;
    }

    if (!pageElement.TryGetProperty("title", out var titleElement) ||
        !pageElement.TryGetProperty("pageid", out var pageIdElement))
    {
        return null;
    }

    var canonicalTitle = titleElement.GetString() ?? itemName;
    var pageId = pageIdElement.GetInt32();
    var parseUrl = $"{WikiApiUrl}?action=parse&pageid={pageId}&prop=wikitext&format=json";
    var pageUrl = $"https://eqlwiki.com/{Uri.EscapeDataString(canonicalTitle.Replace(' ', '_'))}";

    using var parseResponse = await httpClient.GetAsync(parseUrl);
    if (!parseResponse.IsSuccessStatusCode)
    {
        return new WikiItemMatch(canonicalTitle, pageUrl);
    }

    using var parseDoc = JsonDocument.Parse(await parseResponse.Content.ReadAsStringAsync());
    if (!parseDoc.RootElement.TryGetProperty("parse", out var parseElement) ||
        !parseElement.TryGetProperty("wikitext", out var wikitextElement) ||
        !wikitextElement.TryGetProperty("*", out var contentElement))
    {
        return new WikiItemMatch(canonicalTitle, pageUrl);
    }

    var wikiText = contentElement.GetString() ?? string.Empty;
    var classList = ParseWikiField(wikiText, "Class");
    var slot = ParseWikiField(wikiText, "Slot");
    var intValue = ParseWikiNumericField(wikiText, "INT");

    return new WikiItemMatch(canonicalTitle, pageUrl, classList, slot, intValue);
}

static string NormalizeQuery(string value)
{
    var lowered = (value ?? "").ToLowerInvariant();
    var cleaned = Regex.Replace(lowered, @"[^a-z0-9 ]+", " ");
    return Regex.Replace(cleaned, @"\s+", " ", RegexOptions.Compiled).Trim();
}

static IReadOnlyList<string> NormalizeSlots(JsonElement? slots)
{
    if (slots is null)
    {
        return Array.Empty<string>();
    }

    return slots.Value.ValueKind switch
    {
        JsonValueKind.Array => slots.Value
            .EnumerateArray()
            .Select(slot => slot.GetString())
            .Where(slot => !string.IsNullOrWhiteSpace(slot))
            .Select(slot => slot!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(),

        JsonValueKind.String => new[]
        {
            slots.Value.GetString()!
        }
        .Where(slot => !string.IsNullOrWhiteSpace(slot))
        .Select(slot => slot.Trim().ToUpperInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList(),

        _ => Array.Empty<string>()
    };
}

static string? ParseWikiField(string wikiText, string fieldName)
{
    var match = Regex.Match(
        wikiText,
        $@"(?i){Regex.Escape(fieldName)}\s*:\s*([^\r\n<]+)",
        RegexOptions.CultureInvariant);

    return match.Success ? match.Groups[1].Value.Trim() : null;
}

static string? ParseWikiNumericField(string wikiText, string fieldName)
{
    var match = Regex.Match(
        wikiText,
        $@"(?i){Regex.Escape(fieldName)}\s*:\s*([+-]?\d+)",
        RegexOptions.CultureInvariant);

    return match.Success ? match.Groups[1].Value : null;
}

public sealed class ApiResponse
{
    [JsonPropertyName("rows")]
    public List<GearRow>? Rows { get; set; }
}

public sealed class GearRow
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("slots")]
    public JsonElement? Slots { get; set; }

    [JsonPropertyName("classes")]
    public JsonElement? Classes { get; set; }

    [JsonPropertyName("stats")]
    public Dictionary<string, JsonElement>? Stats { get; set; }

    [JsonPropertyName("special")]
    public JsonElement? Special { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("sourceUrl")]
    public string? SourceUrl { get; set; }

    [JsonPropertyName("flags")]
    public string? Flags { get; set; }

    [JsonPropertyName("minLevel")]
    public int? MinLevel { get; set; }
}

public sealed class MatchReport
{
    public MatchReport(GearRow item, string normalizedName)
    {
        Item = item;
        NormalizedName = normalizedName;
    }

    public GearRow Item { get; }
    public string NormalizedName { get; }
    public Dictionary<string, List<ClassFit>> MatchesByClass { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int BestMatchDistance(string query)
    {
        if (NormalizedName == query)
        {
            return 0;
        }

        if (NormalizedName.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
            NormalizedName.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }
}

public sealed class ClassFit(string className, int overallRank, int overallTotal, List<SlotFit> slotRanks)
{
    public string ClassName { get; } = className;
    public int OverallRank { get; } = overallRank;
    public int OverallTotal { get; } = overallTotal;
    public List<SlotFit> SlotRanks { get; } = slotRanks.OrderBy(slot => slot.Rank).ToList();
}

public sealed class WikiItemMatch(
    string canonicalTitle,
    string pageUrl,
    string? classList = null,
    string? slot = null,
    string? intValue = null)
{
    public string CanonicalTitle { get; } = canonicalTitle;
    public string PageUrl { get; } = pageUrl;
    public string? ClassList { get; } = classList;
    public string? Slot { get; } = slot;
    public string? IntValue { get; } = intValue;
}

public sealed class SlotFit(string slot, int rank, int total)
{
    public string Slot { get; } = slot;
    public int Rank { get; } = rank;
    public int Total { get; } = total;
}
