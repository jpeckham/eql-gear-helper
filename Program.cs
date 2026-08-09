using System.Globalization;
using System.Net.Http.Headers;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http;
using System.Text;
using System.IO;

public static class GearLookupService
{
    private const string BaseApiUrl = "https://eqlegendstools.com/api/bis-gear";
    private const string WeaponsApiUrl = "https://eqlegendstools.com/api/weapons";
    private const string WikiApiUrl = "https://eqlwiki.com/api.php";
    private const string CacheDbPath = "Cache\\bis-cache.sqlite";
    private const int CacheTtlHours = 24;
    private const int MaxConcurrency = 4;
    private static readonly string[] Classes = new[]
    {
        "Bard", "Beastlord", "Berserker", "Cleric", "Druid", "Enchanter", "Magician", "Monk",
        "Necromancer", "Paladin", "Ranger", "Rogue", "Shadow Knight", "Shaman", "Warrior", "Wizard"
    };

    public static async Task<string> RunInventoryAnalysisAsync(string? requestedPath)
    {
        var result = await RunInventoryAnalysisResultAsync(requestedPath);
        return result.Output;
    }

    public static async Task<InventoryAnalysisResult> RunInventoryAnalysisResultAsync(string? requestedPath)
    {
        using var httpClient = CreateHttpClient();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var classRows = await LoadAllClassesAsync(Classes, httpClient, options);
        var sortedByClass = classRows.ToDictionary(
            entry => entry.Key,
            entry => SortRowsForRanking(entry.Key, entry.Value),
            StringComparer.OrdinalIgnoreCase);
        var slotSortedByClass = BuildSlotSortedLists(sortedByClass);

        var itemLookups = await AnalyzeInventoryDumpAsync(
            requestedPath,
            classRows,
            sortedByClass,
            slotSortedByClass,
            httpClient,
            options);

        var output = BuildInventoryAnalysisOutput(itemLookups);
        return new InventoryAnalysisResult(output, itemLookups);
    }

    public static async Task<string> SearchByNameAsync(string? query)
    {
        var trimmedQuery = query?.Trim();
        using var httpClient = CreateHttpClient();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var output = new StringBuilder();
        using var writer = new StringWriter(output);
        var previousOut = Console.Out;
        var previousError = Console.Error;

        Console.SetOut(writer);
        Console.SetError(writer);
        try
        {
            if (string.IsNullOrWhiteSpace(trimmedQuery))
            {
                return "Type an item name to search.";
            }

            var normalizedQuery = NormalizeQuery(trimmedQuery);
            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                Console.WriteLine("Try a clearer item name.");
                return output.ToString();
            }

            Console.WriteLine("Pulling EQ Legends BiS data from eqlegendstools.com...");
            var classRows = await LoadAllClassesAsync(Classes, httpClient, options);
            var sortedByClass = classRows.ToDictionary(
                entry => entry.Key,
                entry => SortRowsForRanking(entry.Key, entry.Value),
                StringComparer.OrdinalIgnoreCase);
            var slotSortedByClass = BuildSlotSortedLists(sortedByClass);

            var matches = FindItemMatches(trimmedQuery, normalizedQuery, classRows, sortedByClass, slotSortedByClass);
            if (matches.Count == 0)
            {
                Console.WriteLine($"No BiS entries matched '{trimmedQuery}'.");
                var allWeaponRows = await LoadAllWeaponRowsAsync(httpClient, options);
                var weaponMatches = FindWeaponMatchesAsync(normalizedQuery, allWeaponRows);

                if (weaponMatches.Count > 0)
                {
                    Console.WriteLine("Weapon API results:");
                    foreach (var weaponMatch in weaponMatches.OrderBy(match => match.DpsRank))
                    {
                        PrintWeaponMatchReport(weaponMatch);
                    }
                }
                else
                {
                    Console.WriteLine("No matching weapon rows found.");
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
                }

                return output.ToString();
            }

            foreach (var match in matches)
            {
                PrintMatchReport(match, slotSortedByClass);
            }
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }

        return output.ToString();
    }

    public static async Task<ItemLookupSearchResult> SearchByNameResultAsync(string? query)
    {
        var trimmedQuery = query?.Trim();
        var result = new ItemLookupSearchResult();

        if (string.IsNullOrWhiteSpace(trimmedQuery))
        {
            result.QueryFeedback = "Type an item name to search.";
            return result;
        }

        var normalizedQuery = NormalizeQuery(trimmedQuery);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            result.QueryFeedback = "Try a clearer item name.";
            return result;
        }

        using var httpClient = CreateHttpClient();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        result.QueryFeedback = "Pulling EQ Legends BiS data from eqlegendstools.com...";
        var classRows = await LoadAllClassesAsync(Classes, httpClient, options);
        var sortedByClass = classRows.ToDictionary(
            entry => entry.Key,
            entry => SortRowsForRanking(entry.Key, entry.Value),
            StringComparer.OrdinalIgnoreCase);
        var slotSortedByClass = BuildSlotSortedLists(sortedByClass);

        var matches = FindItemMatches(trimmedQuery, normalizedQuery, classRows, sortedByClass, slotSortedByClass);
        if (matches.Count == 0)
        {
            result.NoGearMatchesMessage = $"No BiS entries matched '{trimmedQuery}'.";
            var allWeaponRows = await LoadAllWeaponRowsAsync(httpClient, options);
            var weaponMatches = FindWeaponMatchesAsync(normalizedQuery, allWeaponRows);
            if (weaponMatches.Count > 0)
            {
                result.WeaponResultLines.Add("Weapon API results:");
                foreach (var weaponMatch in weaponMatches.OrderBy(match => match.DpsRank))
                {
                    result.WeaponResultLines.Add(BuildWeaponMatchText(weaponMatch));
                }
            }
            else
            {
                result.NoWeaponMatchesMessage = "No matching weapon rows found.";
                var wikiResult = await CheckEqWikiItemAsync(trimmedQuery, httpClient);
                if (wikiResult is not null)
                {
                    result.WikiResultLines.Add($"It is present in the EQ wiki:");
                    result.WikiResultLines.Add($"  Title: {wikiResult.CanonicalTitle}");
                    result.WikiResultLines.Add($"  URL: {wikiResult.PageUrl}");

                    if (!string.IsNullOrWhiteSpace(wikiResult.Slot))
                    {
                        result.WikiResultLines.Add($"  Slot: {wikiResult.Slot}");
                    }

                    if (!string.IsNullOrWhiteSpace(wikiResult.ClassList))
                    {
                        result.WikiResultLines.Add($"  Classes: {wikiResult.ClassList}");
                    }

                    if (!string.IsNullOrWhiteSpace(wikiResult.IntValue))
                    {
                        result.WikiResultLines.Add($"  INT: {wikiResult.IntValue}");
                    }
                }
                else
                {
                    result.WikiResultLines.Add("No matching EQ wiki page found either.");
                }
            }

            return result;
        }

        foreach (var match in matches)
        {
            result.GearMatches.Add(BuildItemLookupMatchSummary(match, slotSortedByClass));
        }

        return result;
    }

    public static string? GetDefaultInventoryFilePath()
    {
        return ResolveInventoryPath(null);
    }

    public static string GetDefaultInventoryDirectory()
    {
        var resolvedPath = GetDefaultInventoryFilePath();
        if (!string.IsNullOrWhiteSpace(resolvedPath))
        {
            var inferredDirectory = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrWhiteSpace(inferredDirectory) && Directory.Exists(inferredDirectory))
            {
                return inferredDirectory;
            }
        }

        var knownFolders = new[]
        {
            @"C:\Users\Public\Daybreak Game Company\Installed Games\EverQuest Legends\",
            @"C:\Users\Public\Daybreak Game Company\Installed Games\EverQuest\"
        };

        foreach (var folder in knownFolders)
        {
            if (Directory.Exists(folder))
            {
                return folder;
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EverQuest");
    }

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
    using var cacheConnection = await OpenCacheConnectionAsync();
    var cachedData = new Dictionary<string, List<GearRow>>(StringComparer.OrdinalIgnoreCase);
    var staleClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var classesNeedingRefresh = new List<string>();

    foreach (var className in classNames)
    {
        var cacheResult = await TryLoadCachedClassRowsAsync(cacheConnection, className, jsonOptions);
        if (cacheResult.Rows is null)
        {
            classesNeedingRefresh.Add(className);
            continue;
        }

        cachedData[className] = cacheResult.Rows;
        if (cacheResult.IsStale)
        {
            classesNeedingRefresh.Add(className);
            staleClasses.Add(className);
        }
    }

    if (classesNeedingRefresh.Count > 0)
    {
        Console.WriteLine($"Refreshing {classesNeedingRefresh.Count} classes from network cache miss/stale...");
    }

    using var throttler = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
    var tasks = classesNeedingRefresh.Select(async className =>
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
    var fetched = loaded.ToDictionary(entry => entry.ClassName, entry => entry.Rows, StringComparer.OrdinalIgnoreCase);

    foreach (var (className, rows) in fetched)
    {
        if (rows.Count > 0)
        {
            await CacheClassRowsAsync(cacheConnection, className, rows);
            cachedData[className] = rows;
            continue;
        }

        if (staleClasses.Contains(className) && cachedData.TryGetValue(className, out var staleRows))
        {
            cachedData[className] = staleRows;
        }
    }

    foreach (var className in classNames)
    {
        if (!cachedData.TryGetValue(className, out var rows))
        {
            cachedData[className] = new List<GearRow>();
        }
    }

    return cachedData;
}

static async Task<SqliteConnection> OpenCacheConnectionAsync()
{
    var cacheDirectory = Path.GetDirectoryName(CacheDbPath);
    if (!string.IsNullOrWhiteSpace(cacheDirectory) && !Directory.Exists(cacheDirectory))
    {
        Directory.CreateDirectory(cacheDirectory);
    }

    var connection = new SqliteConnection($"Data Source={CacheDbPath}");
    await connection.OpenAsync();

    using var createCommand = connection.CreateCommand();
    createCommand.CommandText = @"
        CREATE TABLE IF NOT EXISTS bis_class_cache (
            class_name TEXT PRIMARY KEY,
            payload TEXT NOT NULL,
            fetched_at_utc TEXT NOT NULL
        );";
    await createCommand.ExecuteNonQueryAsync();

    return connection;
}

static async Task<(List<GearRow>? Rows, bool IsStale)> TryLoadCachedClassRowsAsync(
    SqliteConnection connection,
    string className,
    JsonSerializerOptions jsonOptions)
{
    using var selectCommand = connection.CreateCommand();
    selectCommand.CommandText = @"SELECT payload, fetched_at_utc FROM bis_class_cache WHERE class_name = @className";
    selectCommand.Parameters.AddWithValue("@className", className);

    using var reader = await selectCommand.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return (null, true);
    }

    var payload = reader.GetString(0);
    var fetchedText = reader.GetString(1);
    if (!DateTime.TryParse(fetchedText, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var fetchedUtc))
    {
        return (null, true);
    }

    var rows = JsonSerializer.Deserialize<List<GearRow>>(payload, jsonOptions);
    if (rows is null)
    {
        return (null, true);
    }

    return (rows, DateTime.UtcNow - fetchedUtc > TimeSpan.FromHours(CacheTtlHours));
}

static async Task CacheClassRowsAsync(SqliteConnection connection, string className, List<GearRow> rows)
{
    if (rows.Count == 0)
    {
        return;
    }

    using var insertCommand = connection.CreateCommand();
    insertCommand.CommandText = @"
        INSERT INTO bis_class_cache (class_name, payload, fetched_at_utc)
        VALUES (@className, @payload, @fetchedAtUtc)
        ON CONFLICT(class_name) DO UPDATE SET
            payload = excluded.payload,
            fetched_at_utc = excluded.fetched_at_utc;";
    insertCommand.Parameters.AddWithValue("@className", className);
    insertCommand.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(rows));
    insertCommand.Parameters.AddWithValue("@fetchedAtUtc", DateTime.UtcNow.ToString("O"));
    await insertCommand.ExecuteNonQueryAsync();
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

static List<GearRow> SortRowsForRanking(string className, IReadOnlyList<GearRow> rows)
{
    return rows
        .Select(row => new
        {
            Row = row,
            Score = GetClassWeightedScore(row, className),
            HasPrimaryStatMatch = HasPrimaryClassStatMatch(row, className)
        })
        .OrderByDescending(entry => entry.HasPrimaryStatMatch)
        .ThenByDescending(entry => entry.Score)
        .ThenBy(entry => NormalizeQuery(entry.Row.Name))
        .Select(entry => entry.Row)
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
                .Select(row => new
                {
                    Row = row,
                    Score = GetClassWeightedScore(row, className),
                    HasPrimaryStatMatch = HasPrimaryClassStatMatch(row, className)
                })
                .OrderByDescending(entry => entry.HasPrimaryStatMatch)
                .ThenByDescending(entry => entry.Score)
                .ThenBy(entry => NormalizeQuery(entry.Row.Name))
                .Select(entry => entry.Row)
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

static int FindWeaponRank(
    IReadOnlyList<WeaponMatch> sortedRows,
    string normalizedName,
    StringComparer comparer)
{
    for (var index = 0; index < sortedRows.Count; index++)
    {
        if (comparer.Equals(sortedRows[index].NormalizedName, normalizedName))
        {
            return index + 1;
        }
    }

    return 0;
}

static void PrintMatchReport(
    MatchReport report,
    Dictionary<string, Dictionary<string, List<GearRow>>> slotSortedByClass)
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
            WriteRankPrefix($"    {className,-12} overall rank");
            WritePercentileValue(fit.OverallRank, fit.OverallTotal);
            Console.WriteLine();
            foreach (var slot in fit.SlotRanks)
            {
                WriteRankPrefix($"      Slot {slot.Slot,-10} rank");
                WritePercentileValue(slot.Rank, slot.Total);
                Console.WriteLine();
                PrintBetterSlotSuggestion(
                    className,
                    report.NormalizedName,
                    slot,
                    slotSortedByClass);
            }
        }
    }

    var qualityPercentile = CalculateCompositeItemPercentile(report);
    var quality = GetItemQualityLabel(qualityPercentile);
    Console.Write("  Quick read: ");
    WriteWithPercentileColor(quality, qualityPercentile, higherIsBetter: false);
}

static ItemLookupMatchSummary BuildItemLookupMatchSummary(
    MatchReport report,
    Dictionary<string, Dictionary<string, List<GearRow>>> slotSortedByClass,
    HashSet<string>? ownedNormalizedNames = null)
{
    var item = report.Item;
    var classSummaryLines = report.MatchesByClass
        .OrderBy(entry => entry.Key)
        .SelectMany(classEntry => classEntry.Value.Select(fit =>
        {
            var slotSummary = fit.SlotRanks.Count == 0
                ? "no slot ranks"
                : string.Join(", ", fit.SlotRanks.Select(slot => $"{slot.Slot} {slot.Rank}/{slot.Total}"));

            var overallText = fit.OverallTotal > 0 && fit.OverallRank > 0
                ? $"{fit.OverallRank}/{fit.OverallTotal}"
                : "n/a";

            return $"{classEntry.Key}: overall {overallText}; {slotSummary}";
        }))
        .ToList();

    var betterSlotHints = report.MatchesByClass
        .SelectMany(classEntry => classEntry.Value.SelectMany(fit =>
            fit.SlotRanks
                .Select(slot => new
                {
                    Slot = slot.Slot,
                    Hint = TryGetBetterSlotReplacement(
                        classEntry.Key,
                        report.NormalizedName,
                        slot,
                        slotSortedByClass,
                        ownedNormalizedNames)
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Hint))
                .Select(item => $"Better for {classEntry.Key} {item.Slot}: {item.Hint}")))
        .Where(hint => !string.IsNullOrWhiteSpace(hint))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    var classCompositeScores = report.MatchesByClass
        .OrderBy(entry => entry.Key)
        .Select(classEntry => BuildClassCompositeScore(
            report.Item,
            classEntry.Key,
            classEntry.Value,
            report.NormalizedName,
            slotSortedByClass,
            ownedNormalizedNames))
        .Where(summary => summary is not null)
        .Cast<ClassCompositeScore>()
        .ToList();

    var contextualizedScores = ApplyClassFitContext(classCompositeScores);
    var displayClassScores = BuildOtherClassCompositeScore(contextualizedScores);

    var qualityPercentile = CalculateCompositeItemPercentile(report);

    return new ItemLookupMatchSummary(
        item.Name,
        string.Join(", ", NormalizeSlots(item.Slots)),
        item.Source,
        (double)GetStatValue(item, "AC"),
        TopStats(item),
        qualityPercentile,
        GetItemQualityLabel(qualityPercentile),
        classSummaryLines,
        betterSlotHints,
        displayClassScores);
}

static List<ClassCompositeScore> BuildOtherClassCompositeScore(IReadOnlyList<ClassCompositeScore> classCompositeScores)
{
    var result = classCompositeScores.OrderBy(score => score.ClassName).ToList();
    if (result.Count < 3)
    {
        return result;
    }

    var directClassScores = result.Where(score => score.IsDirectClassFit).ToList();
    var scorePool = directClassScores.Count > 0 ? directClassScores : result;
    var topThree = scorePool
        .OrderByDescending(score => score.CompositeScore)
        .Take(3)
        .ToList();

    var bestForUnknownClass = topThree
        .OrderByDescending(score => score.CompositeScore)
        .First();

    var bestReplacementHint = topThree
        .Where(score => !string.IsNullOrWhiteSpace(score.BetterItem))
        .OrderByDescending(score => score.CompositeScore)
        .FirstOrDefault();

    var isCurrentBest = topThree.All(score => score.IsCurrentBestInInventory);
    var topClasses = string.Join(", ", topThree.Select(score => score.ClassName));
    var context = directClassScores.Count > 0
        ? $"Useful if your 3-class setup includes at least one of: {topClasses}"
        : $"Only class-matched by fallback stats; use as a low-confidence option only.";
    var otherScore = new ClassCompositeScore(
        "Any 3-class setup",
        100.0 - bestForUnknownClass.CompositeScore,
        bestReplacementHint?.BetterItem,
        isCurrentBest,
        context);

    result.Insert(0, otherScore);
    return result;
}

static ClassCompositeScore? BuildClassCompositeScore(
    GearRow item,
    string className,
    IReadOnlyList<ClassFit> fits,
    string normalizedName,
    Dictionary<string, Dictionary<string, List<GearRow>>> slotSortedByClass,
    HashSet<string>? ownedNormalizedNames = null)
{
    var (bestScore, bestSlotFit) = ResolveClassBestFit(fits);

    if (double.IsPositiveInfinity(bestScore))
    {
        return null;
    }

    var hasPrimaryClassFit = HasPrimaryClassStatMatch(item, className);
    string? betterItem = null;
    if (bestSlotFit is not null)
    {
        betterItem = TryGetBetterSlotReplacement(
            className,
            normalizedName,
            bestSlotFit,
            slotSortedByClass,
            ownedNormalizedNames);
    }

    var isCurrentBestInInventory = false;
    if (ownedNormalizedNames is not null)
    {
        isCurrentBestInInventory = !HasBetterOwnedSlotReplacement(className, normalizedName, fits, slotSortedByClass, ownedNormalizedNames);
    }

    if (isCurrentBestInInventory && !string.IsNullOrWhiteSpace(betterItem))
    {
        isCurrentBestInInventory = false;
    }

    return new ClassCompositeScore(className, bestScore, betterItem, isCurrentBestInInventory, isDirectClassFit: hasPrimaryClassFit);
}

static List<ClassCompositeScore> ApplyClassFitContext(IReadOnlyList<ClassCompositeScore> classCompositeScores)
{
    var primaryClassNames = classCompositeScores
        .Where(score => score.IsDirectClassFit)
        .Select(score => score.ClassName)
        .OrderBy(className => className)
        .ToList();

    var directClassHint = primaryClassNames.Count > 0
        ? string.Join(", ", primaryClassNames)
        : string.Empty;

    var contextualized = new List<ClassCompositeScore>(classCompositeScores.Count);
    foreach (var score in classCompositeScores.OrderBy(score => score.ClassName))
    {
        string? contextLabel = null;
        if (score.IsDirectClassFit)
        {
            contextLabel = "Valid for this class.";
        }
        else if (!string.IsNullOrWhiteSpace(directClassHint))
        {
            contextLabel = $"Conditional: valid only if your 3-class setup includes one of: {directClassHint}";
        }
        else
        {
            contextLabel = "Conditional: low-confidence class fit.";
        }

        contextualized.Add(new ClassCompositeScore(
            score.ClassName,
            score.QualityPercentile,
            score.BetterItem,
            score.IsCurrentBestInInventory,
            contextLabel,
            isDirectClassFit: score.IsDirectClassFit));
    }

    return contextualized;
}

static bool HasPrimaryClassStatMatch(GearRow item, string className)
{
    if (item.Stats is null || item.Stats.Count == 0)
    {
        return false;
    }

    if (!RankingProfiles.ClassStatProfiles.TryGetValue(className, out var classAxisWeights))
    {
        return false;
    }

    var hasStrongPrimaryStat = item.Stats.Any(stat =>
    {
        if (!classAxisWeights.DpsWeights.TryGetValue(stat.Key, out var dpsWeight))
        {
            dpsWeight = 0;
        }

        if (!classAxisWeights.SustainWeights.TryGetValue(stat.Key, out var sustainWeight))
        {
            sustainWeight = 0;
        }

        return (dpsWeight >= 1.0 || sustainWeight >= 1.0) && ParseStatValue(stat.Value) > 0;
    });
    if (hasStrongPrimaryStat)
    {
        return true;
    }

    if (IsCasterClass(className))
    {
        foreach (var statKey in item.Stats.Keys)
        {
            var normalized = NormalizeStatToken(statKey);
            if (IsCasterRegenerationStat(normalized))
            {
                return true;
            }
        }
    }

    return false;
}

static (double BestScore, SlotFit? BestSlotFit) ResolveClassBestFit(IReadOnlyList<ClassFit> fits)
{
    var bestSlotScore = double.PositiveInfinity;
    SlotFit? bestSlotFit = null;
    var bestOverallScore = double.PositiveInfinity;

    foreach (var fit in fits)
    {
        if (fit.OverallTotal > 0 && fit.OverallRank > 0)
        {
            var overallPercentile = (double)fit.OverallRank / fit.OverallTotal * 100.0;
            if (overallPercentile < bestOverallScore)
            {
                bestOverallScore = overallPercentile;
            }
        }

        foreach (var slot in fit.SlotRanks)
        {
            if (slot.Total <= 0 || slot.Rank <= 0)
            {
                continue;
            }

            var slotPercentile = (double)slot.Rank / slot.Total * 100.0;
            if (slotPercentile < bestSlotScore)
            {
                bestSlotScore = slotPercentile;
                bestSlotFit = slot;
            }
        }
    }

    if (bestSlotScore < double.PositiveInfinity)
    {
        return (bestSlotScore, bestSlotFit);
    }

    return (bestOverallScore, null);
}

static bool HasBetterOwnedSlotReplacement(
    string className,
    string normalizedName,
    IReadOnlyList<ClassFit> fits,
    Dictionary<string, Dictionary<string, List<GearRow>>> slotSortedByClass,
    IReadOnlySet<string> ownedNormalizedNames)
{
    if (!slotSortedByClass.TryGetValue(className, out var slotMap) || slotMap.Count == 0 || ownedNormalizedNames.Count == 0)
    {
        return false;
    }

    foreach (var fit in fits)
    {
        foreach (var slot in fit.SlotRanks)
        {
            var betterReplacement = TryGetBetterSlotReplacement(
                className,
                normalizedName,
                slot,
                slotSortedByClass,
                ownedNormalizedNames,
                out var wasCurrentItemLocated);

            if (betterReplacement is not null)
            {
                if (IsOwnedReplacement(betterReplacement, ownedNormalizedNames))
                {
                    return true;
                }
            }
            else if (!wasCurrentItemLocated)
            {
                return true;
            }
        }
    }

    return false;
}

static bool IsOwnedReplacement(string replacementText, IReadOnlySet<string> ownedNormalizedNames)
{
    var replacementName = ExtractReplacementName(replacementText);
    return IsOwnedItemInSet(replacementName, ownedNormalizedNames);
}

static string? TryGetBetterWeaponReplacement(
    string normalizedWeaponName,
    string slotKey,
    Dictionary<string, List<WeaponMatch>> weaponRankingsBySlot,
    IReadOnlySet<string> ownedNormalizedNames,
    out bool currentWeaponLocatedInRanking)
{
    if (!weaponRankingsBySlot.TryGetValue(slotKey, out var slotRows) || slotRows.Count == 0)
    {
        currentWeaponLocatedInRanking = false;
        return null;
    }

    var currentIndex = FindWeaponRank(slotRows, normalizedWeaponName, StringComparer.OrdinalIgnoreCase);
    if (currentIndex <= 1)
    {
        currentWeaponLocatedInRanking = currentIndex > 0;
        return null;
    }

    currentWeaponLocatedInRanking = true;
    for (var index = currentIndex - 2; index >= 0; index--)
    {
        var candidate = slotRows[index];
        if (IsQuestRewardWeapon(candidate))
        {
            continue;
        }

        if (!IsOwnedItemInSet(candidate.NormalizedName, ownedNormalizedNames))
        {
            continue;
        }

        var source = GetItemSourceSummaryFromWeapon(candidate);
        var sourceText = string.IsNullOrWhiteSpace(source) ? "source unknown" : source;
        return $"{candidate.Row.WeaponName} (Source: {sourceText})";
    }

    return null;
}

static string? ExtractReplacementName(string replacementText)
{
    if (string.IsNullOrWhiteSpace(replacementText))
    {
        return null;
    }

    var marker = " (Source:";
    var markerIndex = replacementText.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    if (markerIndex < 0)
    {
        return replacementText.Trim();
    }

    return replacementText[..markerIndex].Trim();
}

static HashSet<string> BuildOwnedItemNameSet(IReadOnlyList<InventoryDumpEntry> entries)
{
    var ownedNormalizedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in entries)
    {
        var normalizedName = NormalizeQuery(entry.Name);
        if (!string.IsNullOrWhiteSpace(normalizedName))
        {
            ownedNormalizedNames.Add(normalizedName);
        }
    }

    return ownedNormalizedNames;
}

static double CalculateCompositeItemPercentile(MatchReport report)
{
    var bestClassPercentiles = report.MatchesByClass
        .Values
        .Select(fits => ResolveClassBestFit(fits).BestScore)
        .Where(score => score < double.PositiveInfinity)
        .ToList();

    return bestClassPercentiles.Count > 0
        ? bestClassPercentiles.Min()
        : 100;
}

static string GetItemQualityLabel(double qualityPercentile)
{
    return qualityPercentile <= 5 ? "Top-tier candidate" :
           qualityPercentile <= 25 ? "Strong BiS-adjacent option" :
           qualityPercentile <= 50 ? "Viable slot-specific option" :
           "Probably niche for current BiS lists";
}

static string BuildWeaponMatchText(WeaponMatch match)
{
    var row = match.Row;
    var output = new StringBuilder();
    output.AppendLine($"  Weapon: {row.WeaponName}");
    output.AppendLine($"    Slot: {row.SlotDisplay ?? row.Slot}");

    if (!string.IsNullOrWhiteSpace(row.SourceType) || !string.IsNullOrWhiteSpace(row.Source))
    {
        output.AppendLine($"    Source: {row.SourceType ?? row.Source}");
    }

    if (!string.IsNullOrWhiteSpace(row.DropsFrom))
    {
        output.AppendLine($"    Drops from: {row.DropsFrom}");
    }

    if (!string.IsNullOrWhiteSpace(row.QuestReward))
    {
        output.AppendLine($"    Quest reward: {row.QuestReward}");
    }

    if (!string.IsNullOrWhiteSpace(row.OneHanded) || !string.IsNullOrWhiteSpace(row.OffhandUsable))
    {
        output.AppendLine($"    Weapon type: {row.OneHanded} | Offhand: {row.OffhandUsable}");
    }

    if (row.Dps.HasValue)
    {
        output.AppendLine($"    DPS rank: {match.DpsRank,4}/{match.DpsTotal,4}");
        output.AppendLine($"    DPS: {row.Dps:0.0000}");
    }

    if (!string.IsNullOrWhiteSpace(row.ZamUrl))
    {
        output.AppendLine($"    Allakhazam: {row.ZamUrl}");
    }

    output.AppendLine($"    Classes: {string.Join(", ", match.ClassNames.OrderBy(c => c).ToList())}");

    return output.ToString().TrimEnd();
}

static async Task<List<ItemLookupMatchSummary>> AnalyzeInventoryDumpAsync(
    string? requestedPath,
    Dictionary<string, List<GearRow>> classRows,
    Dictionary<string, List<GearRow>> sortedByClass,
    Dictionary<string, Dictionary<string, List<GearRow>>> slotSortedByClass,
    HttpClient httpClient,
    JsonSerializerOptions jsonOptions)
{
    var inventoryPath = ResolveInventoryPath(requestedPath);
    if (string.IsNullOrWhiteSpace(inventoryPath) || !File.Exists(inventoryPath))
    {
        return new List<ItemLookupMatchSummary>();
    }

    var entries = ParseInventoryDump(inventoryPath);
    if (entries.Count == 0)
    {
        return new List<ItemLookupMatchSummary>();
    }

    var ownedNormalizedNames = BuildOwnedItemNameSet(entries);

    var allWeaponRows = await LoadAllWeaponRowsAsync(httpClient, jsonOptions);
    var weaponRankingBySlot = BuildWeaponRankingBySlot(allWeaponRows);
    var summaries = new List<ItemLookupMatchSummary>(entries.Count);
    foreach (var entry in entries)
    {
        var summary = BuildInventoryItemSummary(
            entry,
            classRows,
            sortedByClass,
            slotSortedByClass,
            allWeaponRows,
            weaponRankingBySlot,
            ownedNormalizedNames);
        if (summary is not null)
        {
            summaries.Add(summary);
        }
    }

    return summaries
        .OrderByDescending(summary => summary.CompositeScore)
        .ThenBy(summary => summary.ItemName, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static ItemLookupMatchSummary? BuildInventoryItemSummary(
    InventoryDumpEntry entry,
    Dictionary<string, List<GearRow>> classRows,
    Dictionary<string, List<GearRow>> sortedByClass,
    Dictionary<string, Dictionary<string, List<GearRow>>> slotSortedByClass,
    IReadOnlyList<WeaponRowResponse> allWeaponRows,
    Dictionary<string, List<WeaponMatch>> weaponRankingBySlot,
    HashSet<string> ownedNormalizedNames)
{
    var normalizedName = NormalizeQuery(entry.Name);
    if (string.IsNullOrWhiteSpace(normalizedName))
    {
        return null;
    }

    var report = FindBestInventoryItemMatch(
        entry.Name,
        normalizedName,
        classRows,
        sortedByClass,
        slotSortedByClass);

    if (report is not null && HasGearSlotMatch(report))
    {
        return BuildInventoryGearSummary(entry, report, slotSortedByClass, ownedNormalizedNames);
    }

    var weaponCandidates = FindWeaponMatchesAsync(normalizedName, allWeaponRows);
    var weaponMatch = weaponCandidates
        .Where(match => match.DpsTotal > 0)
        .OrderBy(match => match.DpsRank)
        .ThenBy(match => match.Row.Dps ?? 0)
        .FirstOrDefault(match => !IsQuestRewardWeapon(match))
        ?? weaponCandidates
            .Where(match => match.DpsTotal > 0)
            .OrderBy(match => match.DpsRank)
            .ThenBy(match => match.Row.Dps ?? 0)
            .FirstOrDefault();

    if (weaponMatch is not null)
    {
        return BuildInventoryWeaponSummary(entry, weaponMatch, weaponRankingBySlot, ownedNormalizedNames);
    }

    if (IsLikelyEquippedSlotLocation(entry.Location))
    {
        return BuildInventoryEquippableUnmatchedSummary(entry);
    }

    return null;
}

static MatchReport? FindBestInventoryItemMatch(
    string originalName,
    string normalizedName,
    Dictionary<string, List<GearRow>> classRows,
    Dictionary<string, List<GearRow>> sortedByClass,
    Dictionary<string, Dictionary<string, List<GearRow>>> slotSortedByClass)
{
    var primaryMatches = FindItemMatches(originalName, normalizedName, classRows, sortedByClass, slotSortedByClass);
    var directMatch = primaryMatches
        .FirstOrDefault(match => string.Equals(match.NormalizedName, normalizedName, StringComparison.OrdinalIgnoreCase));

    if (directMatch is not null)
    {
        return directMatch;
    }

    if (primaryMatches.Count > 0)
    {
        var looseMatch = primaryMatches
            .OrderBy(match => match.BestMatchDistance(normalizedName))
            .ThenBy(match => match.Item.Name)
            .FirstOrDefault();

        if (looseMatch is not null)
        {
            return looseMatch;
        }
    }

    var fallbackMatches = FindItemMatchesByFuzzyContains(
        normalizedName,
        classRows,
        sortedByClass,
        slotSortedByClass);

    if (fallbackMatches.Count == 0)
    {
        return null;
    }

    return fallbackMatches
        .OrderBy(match => LevenshteinDistance(match.NormalizedName, normalizedName))
        .ThenBy(match => match.Item.Name)
        .FirstOrDefault();
}

static List<MatchReport> FindItemMatchesByFuzzyContains(
    string normalizedName,
    Dictionary<string, List<GearRow>> classRows,
    Dictionary<string, List<GearRow>> sortedByClass,
    Dictionary<string, Dictionary<string, List<GearRow>>> slotSortedByClass)
{
    if (string.IsNullOrWhiteSpace(normalizedName))
    {
        return new List<MatchReport>();
    }

    var collected = new Dictionary<string, MatchReport>(StringComparer.OrdinalIgnoreCase);
    foreach (var (className, rows) in classRows)
    {
        var sortedRows = sortedByClass[className];
        foreach (var row in rows)
        {
            var rowNormalized = NormalizeQuery(row.Name);
            if (string.IsNullOrWhiteSpace(rowNormalized))
            {
                continue;
            }

            if (!rowNormalized.Contains(normalizedName, StringComparison.OrdinalIgnoreCase) &&
                !normalizedName.Contains(rowNormalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!collected.TryGetValue(rowNormalized, out var report))
            {
                report = new MatchReport(row, rowNormalized);
                collected[rowNormalized] = report;
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

    return collected.Values
        .OrderBy(report => LevenshteinDistance(report.NormalizedName, normalizedName))
        .ThenBy(report => NormalizeQuery(report.Item.Name))
        .ToList();
}

static int LevenshteinDistance(string source, string target)
{
    if (string.IsNullOrEmpty(source))
    {
        return target?.Length ?? 0;
    }

    if (string.IsNullOrEmpty(target))
    {
        return source.Length;
    }

    var sourceSpan = source.AsSpan();
    var targetSpan = target.AsSpan();
    var dp = new int[target.Length + 1];
    var prev = new int[target.Length + 1];

    for (var j = 0; j <= target.Length; j++)
    {
        prev[j] = j;
    }

    for (var i = 1; i <= source.Length; i++)
    {
        dp[0] = i;
        for (var j = 1; j <= target.Length; j++)
        {
            var cost = sourceSpan[i - 1] == targetSpan[j - 1] ? 0 : 1;
            dp[j] = Math.Min(
                Math.Min(dp[j - 1] + 1, prev[j] + 1),
                prev[j - 1] + cost);
        }

        Array.Copy(dp, prev, target.Length + 1);
    }

    return prev[target.Length];
}

static bool HasGearSlotMatch(MatchReport report)
{
    var declaredSlots = NormalizeSlots(report.Item.Slots);
    if (declaredSlots.Any())
    {
        return true;
    }

    return report.MatchesByClass
        .Values
        .Any(classFits => classFits
            .Any(fit => fit.SlotRanks.Count > 0));
}

static bool IsLikelyEquippedSlotLocation(string location)
{
    if (string.IsNullOrWhiteSpace(location))
    {
        return false;
    }

    var normalized = Regex.Replace(location.Trim().ToLowerInvariant(), @"\s+", " ", RegexOptions.Compiled).Trim();
    if (string.Equals(normalized, "any slot", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (normalized.StartsWith("keyring", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (normalized.Contains('-', StringComparison.OrdinalIgnoreCase))
    {
        normalized = normalized.Split('-')[0].Trim();
    }

    if (normalized.StartsWith("left ", StringComparison.OrdinalIgnoreCase) ||
        normalized.StartsWith("right ", StringComparison.OrdinalIgnoreCase))
    {
        normalized = normalized[(normalized.IndexOf(' ') + 1)..];
    }

    var directMatch = normalized switch
    {
        "head" => true,
        "face" => true,
        "neck" => true,
        "shoulder" => true,
        "shoulders" => true,
        "arm" => true,
        "arms" => true,
        "back" => true,
        "wrist" => true,
        "wrists" => true,
        "hand" => true,
        "hands" => true,
        "finger" => true,
        "fingers" => true,
        "chest" => true,
        "leg" => true,
        "legs" => true,
        "feet" => true,
        "waist" => true,
        "ear" => true,
        "ammo" => true,
        "range" => true,
        "primary" => true,
        "secondary" => true,
        "offhand" => true,
        _ => false
    };

    if (directMatch)
    {
        return true;
    }

    return normalized.Contains(" head", StringComparison.OrdinalIgnoreCase) ||
           normalized.Contains(" face", StringComparison.OrdinalIgnoreCase) ||
           normalized.Contains(" neck", StringComparison.OrdinalIgnoreCase) ||
           normalized.Contains(" shoulder", StringComparison.OrdinalIgnoreCase) ||
           normalized.Contains(" arm", StringComparison.OrdinalIgnoreCase) ||
           normalized.Contains(" back", StringComparison.OrdinalIgnoreCase) ||
           normalized.Contains(" wrist", StringComparison.OrdinalIgnoreCase) ||
           normalized.Contains(" hand", StringComparison.OrdinalIgnoreCase) ||
           normalized.Contains(" finger", StringComparison.OrdinalIgnoreCase) ||
           normalized.Contains(" chest", StringComparison.OrdinalIgnoreCase) ||
           normalized.Contains(" leg", StringComparison.OrdinalIgnoreCase) ||
           normalized.Contains(" foot", StringComparison.OrdinalIgnoreCase) ||
           normalized.Contains(" waist", StringComparison.OrdinalIgnoreCase) ||
           normalized.Contains(" ear", StringComparison.OrdinalIgnoreCase) ||
           normalized.Contains(" ammo", StringComparison.OrdinalIgnoreCase) ||
           normalized.Contains(" range", StringComparison.OrdinalIgnoreCase) ||
           normalized.Contains(" primary", StringComparison.OrdinalIgnoreCase) ||
           normalized.Contains(" secondary", StringComparison.OrdinalIgnoreCase) ||
           normalized.Contains(" offhand", StringComparison.OrdinalIgnoreCase);
}

static ItemLookupMatchSummary BuildInventoryGearSummary(
    InventoryDumpEntry entry,
    MatchReport report,
    Dictionary<string, Dictionary<string, List<GearRow>>> slotSortedByClass,
    HashSet<string> ownedNormalizedNames)
{
    var baseSummary = BuildItemLookupMatchSummary(report, slotSortedByClass, ownedNormalizedNames);
    var summaryLines = BuildInventoryContextLines(entry);
    summaryLines.AddRange(baseSummary.ClassSummaryLines);

    return new ItemLookupMatchSummary(
        BuildInventoryDisplayName(entry),
        baseSummary.Slots,
        baseSummary.Source,
        baseSummary.Ac,
        baseSummary.NotableStats,
        baseSummary.QualityPercentile,
        baseSummary.QualityLabel,
        summaryLines,
        baseSummary.BetterSlotHints,
        baseSummary.ClassCompositeScores);
}

static ItemLookupMatchSummary BuildInventoryWeaponSummary(
    InventoryDumpEntry entry,
    WeaponMatch weaponMatch,
    Dictionary<string, List<WeaponMatch>> weaponRankingsBySlot,
    HashSet<string> ownedNormalizedNames)
{
    var row = weaponMatch.Row;
    var slot = row.SlotDisplay ?? row.Slot ?? "Unknown";
    var percentile = weaponMatch.DpsTotal > 0 && weaponMatch.DpsRank > 0
        ? (double)weaponMatch.DpsRank / weaponMatch.DpsTotal * 100.0
        : 100.0;

    var classSummaryLines = BuildInventoryContextLines(entry);
    classSummaryLines.Add($"Weapon: {row.WeaponName}");
    classSummaryLines.Add($"Slot: {slot}");
    classSummaryLines.Add($"Weapon rank: {weaponMatch.DpsRank}/{weaponMatch.DpsTotal}");
    var source = GetItemSourceSummaryFromWeapon(weaponMatch);
    if (!string.IsNullOrWhiteSpace(source))
    {
        classSummaryLines.Add($"Source: {source}");
    }

    if (row.ClassNames is not null && row.ClassNames.Count > 0)
    {
        classSummaryLines.Add($"Classes: {string.Join(", ", row.ClassNames.OrderBy(className => className))}");
    }

    if (row.Dps.HasValue)
    {
        classSummaryLines.Add($"DPS: {row.Dps:0.0000}");
    }

    var notableStats = new List<string>();
    if (row.Dps.HasValue)
    {
        notableStats.Add($"DPS {row.Dps:0.0000}");
    }

    var betterWeapon = TryGetBetterWeaponReplacement(
        weaponMatch.NormalizedName,
        weaponMatch.SlotKey,
        weaponRankingsBySlot,
        ownedNormalizedNames,
        out var isCurrentWeaponLocatedInRanking);
    var isCurrentBestInInventory = isCurrentWeaponLocatedInRanking && string.IsNullOrWhiteSpace(betterWeapon);

    var classCompositeScores = new List<ClassCompositeScore>
    {
        new ClassCompositeScore(
            $"Weapon {slot}",
            percentile,
            betterWeapon,
            isCurrentBestInInventory,
            "Weapon slot comparison")
    };

    return new ItemLookupMatchSummary(
        BuildInventoryDisplayName(entry),
        slot,
        source,
        0,
        notableStats,
        percentile,
        GetItemQualityLabel(percentile),
        classSummaryLines,
        Array.Empty<string>(),
        classCompositeScores);
}

static ItemLookupMatchSummary BuildInventoryEquippableUnmatchedSummary(InventoryDumpEntry entry)
{
    var context = BuildInventoryContextLines(entry);
    context.Add("No matching BiS or weapon entry.");
    return new ItemLookupMatchSummary(
        BuildInventoryDisplayName(entry),
        "N/A",
        "No match",
        0,
        Array.Empty<string>(),
        100,
        GetItemQualityLabel(100),
        context,
        Array.Empty<string>(),
        Array.Empty<ClassCompositeScore>());
}

static List<string> BuildInventoryContextLines(InventoryDumpEntry entry)
{
    var lines = new List<string>();
    if (entry.Count > 1)
    {
        lines.Add($"Count: {entry.Count}");
    }

    if (!string.IsNullOrWhiteSpace(entry.Location))
    {
        lines.Add($"Location: {entry.Location}");
    }

    if (entry.IsKeyRing)
    {
        lines.Add("Stored in KeyRing");
    }

    return lines;
}

static string BuildInventoryDisplayName(InventoryDumpEntry entry)
{
    var display = entry.Count > 1 ? $"{entry.Name} (x{entry.Count})" : entry.Name;
    if (!string.IsNullOrWhiteSpace(entry.Location))
    {
        display += $" [{entry.Location}]";
    }

    return display;
}

static string BuildInventoryAnalysisOutput(IReadOnlyList<ItemLookupMatchSummary> itemLookups)
{
    var output = new StringBuilder();
    if (itemLookups.Count == 0)
    {
        output.AppendLine("No inventory entries were parsed from the selected file.");
        return output.ToString().TrimEnd();
    }

    output.AppendLine($"Parsed {itemLookups.Count} inventory item(s).");
    foreach (var item in itemLookups)
    {
        output.AppendLine($"- {item.ItemName} | {item.CompositeScore:0.0}% | {item.QualityLabel}");
    }

    return output.ToString().TrimEnd();
}

static bool ShouldConsiderStorageDiscard(string name)
{
    if (string.IsNullOrWhiteSpace(name))
    {
        return false;
    }

    if (name.Contains("Quest", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (name.Contains("Epic", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    return true;
}

static bool IsQuestRewardWeapon(WeaponMatch match)
{
    return !string.IsNullOrWhiteSpace(match.Row.QuestReward);
}

static string? GetItemSourceSummaryFromWeapon(WeaponMatch match)
{
    if (!string.IsNullOrWhiteSpace(match.Row.DropsFrom))
    {
        return match.Row.DropsFrom;
    }

    if (!string.IsNullOrWhiteSpace(match.Row.SourceType))
    {
        return match.Row.SourceType;
    }

    if (!string.IsNullOrWhiteSpace(match.Row.Source))
    {
        return match.Row.Source;
    }

    return null;
}

static bool HasOnlyQuestRewardGearMatches(IReadOnlyList<MatchReport> matches)
{
    return matches.Count > 0 && matches.All(match => IsQuestReward(match.Item));
}

static string? FindBestGearReplacementHint(
    IReadOnlyList<MatchReport> itemReports,
    Dictionary<string, Dictionary<string, List<GearRow>>> slotSortedByClass)
{
    foreach (var report in itemReports.OrderBy(report => report.MatchesByClass.Count))
    {
        foreach (var slotEntry in report.MatchesByClass.SelectMany(classEntry =>
                     classEntry.Value.SelectMany(classFit => classFit.SlotRanks.Select(slot => new
                     {
                         Class = classEntry.Key,
                         Slot = slot
                     }))))
        {
            if (TryGetBetterSlotReplacement(slotEntry.Class, report.NormalizedName, slotEntry.Slot, slotSortedByClass) is { } betterMatch)
            {
                return $"Better option for {slotEntry.Class} {slotEntry.Slot.Slot}: {betterMatch}";
            }
        }
    }

    return null;
}

static string? TryGetBetterSlotReplacement(
    string className,
    string currentNormalizedName,
    SlotFit slot,
    Dictionary<string, Dictionary<string, List<GearRow>>> slotSortedByClass,
    IReadOnlySet<string>? ownedNormalizedNames = null)
{
    return TryGetBetterSlotReplacement(
        className,
        currentNormalizedName,
        slot,
        slotSortedByClass,
        ownedNormalizedNames,
        out _);
}

static string? TryGetBetterSlotReplacement(
    string className,
    string currentNormalizedName,
    SlotFit slot,
    Dictionary<string, Dictionary<string, List<GearRow>>> slotSortedByClass,
    IReadOnlySet<string>? ownedNormalizedNames,
    out bool currentItemLocatedInSlotRanking)
{
    if (slot.Rank <= 1)
    {
        currentItemLocatedInSlotRanking = true;
        return null;
    }

    if (!slotSortedByClass.TryGetValue(className, out var slotMap))
    {
        currentItemLocatedInSlotRanking = false;
        return null;
    }

    if (!slotMap.TryGetValue(slot.Slot, out var slotRows) || slotRows.Count == 0)
    {
        currentItemLocatedInSlotRanking = false;
        return null;
    }

    var currentIndex = FindRank(slotRows, currentNormalizedName, StringComparer.OrdinalIgnoreCase) - 1;
    if (currentIndex <= 0)
    {
        currentItemLocatedInSlotRanking = false;
        return null;
    }

    currentItemLocatedInSlotRanking = true;

    for (var index = currentIndex - 1; index >= 0; index--)
    {
        var candidate = slotRows[index];
        if (IsQuestReward(candidate))
        {
            continue;
        }

        if (ownedNormalizedNames is not null &&
            !IsOwnedItemInSet(NormalizeQuery(candidate.Name), ownedNormalizedNames))
        {
            continue;
        }

        var source = GetItemSourceSummary(candidate);
        var sourceText = string.IsNullOrWhiteSpace(source) ? "source unknown" : source;
        return $"{candidate.Name} (Source: {sourceText})";
    }

    return null;
}

static bool IsOwnedItemInSet(string normalizedName, IReadOnlySet<string> ownedNormalizedNames)
{
    if (string.IsNullOrWhiteSpace(normalizedName))
    {
        return false;
    }

    if (ownedNormalizedNames.Contains(normalizedName))
    {
        return true;
    }

    foreach (var owned in ownedNormalizedNames)
    {
        if (string.IsNullOrWhiteSpace(owned))
        {
            continue;
        }

        if (normalizedName.Contains(owned, StringComparison.OrdinalIgnoreCase) ||
            owned.Contains(normalizedName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (LevenshteinDistance(normalizedName, owned) <= 2)
        {
            return true;
        }
    }

    return false;
}

static bool IsLikelyStorageContainer(string name, int itemId)
{
    if (itemId == 17005)
    {
        return true;
    }

    return Regex.IsMatch(name, @"(?i)\b(backpack|bag|quiver|bandolier)\b");
}

static bool IsEquippedInInventory(IReadOnlyCollection<string> locations)
{
    HashSet<string> equippedRootLocations = new(StringComparer.OrdinalIgnoreCase)
    {
        "Any Slot", "Ear", "Head", "Face", "Neck", "Shoulders", "Arms", "Back", "Wrist", "Range",
        "Hands", "Primary", "Secondary", "Fingers", "Chest", "Legs", "Feet", "Waist", "Ammo", "Range"
    };

    return locations.Any(location =>
        equippedRootLocations.Contains(location.Trim()) &&
        !location.Contains('-') &&
        !location.StartsWith("KeyRing", StringComparison.OrdinalIgnoreCase));
}

static string RemoveExaltationSuffix(string name)
{
    return Regex.Replace(name, @"\s*\(Exaltation\)", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
}

static double CalculateBestItemPercentile(MatchReport report)
{
    var bestSlotPercentile = report.MatchesByClass.Values
        .SelectMany(values => values)
        .SelectMany(fit => fit.SlotRanks)
        .Where(slot => slot.Total > 0 && slot.Rank > 0)
        .Select(slot => (double)slot.Rank / slot.Total * 100)
        .DefaultIfEmpty(100)
        .Min();

    return bestSlotPercentile;
}

static double CalculateBestItemPercentileFromReports(IReadOnlyList<MatchReport> itemReports)
{
    return itemReports
        .Select(CalculateBestItemPercentile)
        .DefaultIfEmpty(100)
        .Min();
}

static string ResolveInventoryPath(string? requestedPath)
{
    if (!string.IsNullOrWhiteSpace(requestedPath))
    {
        return requestedPath.Trim();
    }

    var knownFolders = new[]
    {
        @"C:\Users\Public\Daybreak Game Company\Installed Games\EverQuest Legends\",
        @"C:\Users\Public\Daybreak Game Company\Installed Games\EverQuest\"
    };

    foreach (var folder in knownFolders)
    {
        if (!Directory.Exists(folder))
        {
            continue;
        }

        var matches = Directory.GetFiles(folder, "*inventory.txt", SearchOption.TopDirectoryOnly)
            .Where(file => Regex.IsMatch(Path.GetFileName(file), @"(^|[-_])inventory\.txt$", RegexOptions.IgnoreCase));
        var newest = matches
            .Select(file => new FileInfo(file))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        if (newest is not null)
        {
            return newest.FullName;
        }
    }

    var gameInstallGuess = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EverQuest");
    if (Directory.Exists(gameInstallGuess))
    {
        var fallbackMatch = Directory.GetFiles(gameInstallGuess, "*inventory.txt", SearchOption.TopDirectoryOnly)
            .Where(file => Regex.IsMatch(Path.GetFileName(file), @"(^|[-_])inventory\.txt$", RegexOptions.IgnoreCase))
            .OrderByDescending(file => new FileInfo(file).LastWriteTimeUtc)
            .FirstOrDefault();
        if (fallbackMatch is not null)
        {
            return fallbackMatch;
        }
    }

    return string.Empty;
}

static List<InventoryDumpEntry> ParseInventoryDump(string path)
{
    var output = new List<InventoryDumpEntry>();
    var lines = File.ReadLines(path);
    var parsingEquipmentSection = false;
    foreach (var rawLine in lines)
    {
        if (string.IsNullOrWhiteSpace(rawLine))
        {
            continue;
        }

        if (rawLine.StartsWith("Location\tName\tID\tCount", StringComparison.OrdinalIgnoreCase))
        {
            parsingEquipmentSection = false;
            continue;
        }

        if (rawLine.StartsWith("KeyRing\tName\tID", StringComparison.OrdinalIgnoreCase))
        {
            parsingEquipmentSection = true;
            continue;
        }

        var parts = rawLine.Split('\t');
        if (parts.Length < 3)
        {
            continue;
        }

        var location = parts[0].Trim();
        var name = parts[1].Trim();
        var idText = parts[2].Trim();
        if (string.Equals(name, "Empty", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (!int.TryParse(idText, out var id))
        {
            id = 0;
        }

        if (id == 17005 || string.Equals(name, "Backpack", StringComparison.OrdinalIgnoreCase) || name.Contains("Backpack", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var count = 1;
        if (!parsingEquipmentSection && parts.Length >= 4)
        {
            int.TryParse(parts[3].Trim(), out count);
            if (count < 1)
            {
                count = 1;
            }
        }

        output.Add(new InventoryDumpEntry(
            location,
            RemoveExaltationSuffix(name),
            id,
            count,
            parsingEquipmentSection || location.StartsWith("KeyRing", StringComparison.OrdinalIgnoreCase)));
    }

    return output;
}

static void PrintBetterSlotSuggestion(
    string className,
    string currentNormalizedName,
    SlotFit slot,
    Dictionary<string, Dictionary<string, List<GearRow>>> slotSortedByClass)
{
    if (slot.Rank <= 1)
    {
        return;
    }

    if (!slotSortedByClass.TryGetValue(className, out var slotMap))
    {
        return;
    }

    if (!slotMap.TryGetValue(slot.Slot, out var slotRows) || slotRows.Count == 0)
    {
        return;
    }

    var currentIndex = FindRank(slotRows, currentNormalizedName, StringComparer.OrdinalIgnoreCase) - 1;
    if (currentIndex <= 0)
    {
        return;
    }

    var betterCandidateIndex = -1;
    for (var index = currentIndex - 1; index >= 0; index--)
    {
        if (!IsQuestReward(slotRows[index]))
        {
            betterCandidateIndex = index;
            break;
        }
    }

    if (betterCandidateIndex < 0)
    {
        return;
    }

    var betterItem = slotRows[betterCandidateIndex];
    WriteRankPrefix("        Better slot option");
    Console.Write(": ");
    Console.Write(betterItem.Name);
    Console.Write(" (");
    WritePercentileValue(betterCandidateIndex + 1, slotRows.Count);
    Console.Write(")");
    var source = GetItemSourceSummary(betterItem);
    if (!string.IsNullOrWhiteSpace(source))
    {
        Console.Write($" | Source: {source}");
    }
    Console.WriteLine();
}

static bool IsQuestReward(GearRow row)
{
    if (string.IsNullOrWhiteSpace(row.Source) && string.IsNullOrWhiteSpace(row.SourceUrl))
    {
        return false;
    }

    var sourceText = row.Source ?? string.Empty;
    if (sourceText.Contains("Reward from Quest", StringComparison.OrdinalIgnoreCase) ||
        sourceText.Contains("Quest Reward", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    var sourceUrl = row.SourceUrl ?? string.Empty;
    return sourceUrl.Contains("quest", StringComparison.OrdinalIgnoreCase);
}

static string? GetItemSourceSummary(GearRow row)
{
    if (!string.IsNullOrWhiteSpace(row.Source))
    {
        return row.Source.Trim();
    }

    if (!string.IsNullOrWhiteSpace(row.SourceUrl))
    {
        return row.SourceUrl.Trim();
    }

    return null;
}

static void WritePercentileValue(int rank, int total, bool higherIsBetter = false)
{
    if (total <= 0 || rank <= 0)
    {
        Console.Write("n/a");
        return;
    }

    var pct = (double)rank / total * 100.0;
    var invertedPct = 100.0 - pct;
    Console.Write($"{rank,4}/{total,4} ");
    WritePercentileBar(invertedPct, pct, higherIsBetter);
    Console.Write(" ");
    WriteWithPercentileColor($"{invertedPct:0.0}%", pct, higherIsBetter);
}

static void WriteRankPrefix(string prefix, int width = 34)
{
    Console.Write(prefix.PadRight(width));
}

static void WritePercentileBar(double invertedPercentile, double rankPercentile, bool higherIsBetter)
{
    var clamped = Math.Clamp(invertedPercentile, 0, 100);
    const int barWidth = 14;
    var filled = (int)Math.Round(clamped / 100.0 * barWidth, 0, MidpointRounding.AwayFromZero);
    var fullChars = Math.Clamp(filled, 0, barWidth);
    var bar = "|" + new string('#', fullChars) + new string('-', barWidth - fullChars) + "|";

    WriteWithPercentileColor(bar, rankPercentile, higherIsBetter);
}

static void WriteWithPercentileColor(string text, double percentile, bool higherIsBetter)
{
    var prior = Console.ForegroundColor;
    Console.ForegroundColor = PercentileColor(percentile, higherIsBetter);
    Console.Write(text);
    Console.ForegroundColor = prior;
}

static ConsoleColor PercentileColor(double percentile, bool higherIsBetter)
{
    if (higherIsBetter)
    {
        if (percentile >= 90.0)
        {
            return ConsoleColor.Green;
        }

        if (percentile >= 50.0)
        {
            return ConsoleColor.Yellow;
        }

        return ConsoleColor.Red;
    }

    if (percentile <= 25.0)
    {
        return ConsoleColor.Green;
    }

    if (percentile <= 50.0)
    {
        return ConsoleColor.Yellow;
    }

    return ConsoleColor.Red;
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

static double GetClassWeightedScore(GearRow row, string className)
{
    const double dpsAxisWeight = 0.60;
    const double sustainAxisWeight = 0.40;

    if (row.Stats is null)
    {
        return 0;
    }

    if (!RankingProfiles.ClassStatProfiles.TryGetValue(className, out var classAxisWeights))
    {
        return GetClassAxisWeightedScore(row, className, isSustainAxis: false) * dpsAxisWeight +
               GetClassAxisWeightedScore(row, className, isSustainAxis: true) * sustainAxisWeight;
    }

    var dpsScore = row.Stats.Sum(stat =>
    {
        var key = stat.Key;
        var value = ParseStatValue(stat.Value);
        if (!classAxisWeights.DpsWeights.TryGetValue(key, out var dpsWeight))
        {
            dpsWeight = GetFallbackStatWeight(className, key, isSustainAxis: false);
        }

        return value * dpsWeight;
    });

    var sustainScore = row.Stats.Sum(stat =>
    {
        var key = stat.Key;
        var value = ParseStatValue(stat.Value);
        if (!classAxisWeights.SustainWeights.TryGetValue(key, out var sustainWeight))
        {
            sustainWeight = GetFallbackStatWeight(className, key, isSustainAxis: true);
        }

        return value * sustainWeight;
    });

    return dpsScore * dpsAxisWeight + sustainScore * sustainAxisWeight;
}

static double GetClassAxisWeightedScore(
    GearRow row,
    string className,
    bool isSustainAxis)
{
    if (row.Stats is null)
    {
        return 0;
    }

    if (!RankingProfiles.ClassStatProfiles.TryGetValue(className, out var classAxisWeights))
    {
        return row.Stats.Sum(stat => ParseStatValue(stat.Value) * GetFallbackStatWeight(className, stat.Key, isSustainAxis));
    }

    var weights = isSustainAxis ? classAxisWeights.SustainWeights : classAxisWeights.DpsWeights;
    return row.Stats.Sum(stat =>
    {
        if (!weights.TryGetValue(stat.Key, out var weight))
        {
            return ParseStatValue(stat.Value) * GetFallbackStatWeight(className, stat.Key, isSustainAxis);
        }

        return ParseStatValue(stat.Value) * weight;
    });
}

static double GetFallbackStatWeight(string className, string statKey, bool isSustainAxis)
{
    var normalized = NormalizeStatToken(statKey);
    if (IsCasterClass(className))
    {
        if (IsCasterRegenerationStat(normalized))
        {
            return isSustainAxis ? 2.0 : 0.5;
        }

        if (normalized.Contains("MANA", StringComparison.Ordinal))
        {
            return isSustainAxis ? 1.4 : 1.3;
        }

        if (normalized.Contains("INT", StringComparison.Ordinal) || normalized.Contains("WIS", StringComparison.Ordinal))
        {
            return isSustainAxis ? 0.2 : 1.0;
        }

        if (ContainsAny(normalized, "HP", "HITPOINTS", "STA", "STAMINA", "END", "ENDURANCE"))
        {
            return isSustainAxis ? 1.2 : 0.2;
        }

        if (isSustainAxis)
        {
            if (ContainsAny(normalized, "AC", "ACCURACY", "EVASION"))
            {
                return 0.8;
            }
        }

        return 0.0;
    }

    if (isSustainAxis)
    {
        if (ContainsAny(normalized, "AC", "HP", "HITPOINTS", "STA", "STAMINA", "END", "ENDURANCE"))
        {
            return 1.0;
        }

        return 0.0;
    }

    return normalized.Contains("STR", StringComparison.Ordinal) ||
        normalized.Contains("AGI", StringComparison.Ordinal) ||
        normalized.Contains("DEX", StringComparison.Ordinal) ? 1.0 : 0.0;
}

static bool IsCasterRegenerationStat(string normalizedStatKey)
{
    return ContainsAny(normalizedStatKey, "REGEN");
}

static bool IsCasterClass(string className)
{
    return className.Equals("Enchanter", StringComparison.OrdinalIgnoreCase) ||
           className.Equals("Magician", StringComparison.OrdinalIgnoreCase) ||
           className.Equals("Necromancer", StringComparison.OrdinalIgnoreCase) ||
           className.Equals("Wizard", StringComparison.OrdinalIgnoreCase) ||
           className.Equals("Cleric", StringComparison.OrdinalIgnoreCase) ||
           className.Equals("Druid", StringComparison.OrdinalIgnoreCase) ||
           className.Equals("Shaman", StringComparison.OrdinalIgnoreCase);
}

static bool ContainsAny(string value, params string[] fragments)
{
    foreach (var fragment in fragments)
    {
        if (value.Contains(fragment, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

static string NormalizeStatToken(string statKey)
{
    return Regex.Replace((statKey ?? string.Empty).ToUpperInvariant(), @"[^A-Z0-9]", "", RegexOptions.Compiled);
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
    foreach (var candidate in BuildWikiTitleCandidates(itemName))
    {
        var exactMatch = await TryLoadWikiPageByTitleAsync(candidate, httpClient);
        if (exactMatch is not null)
        {
            return exactMatch;
        }
    }

    return null;
}

static async Task<List<WeaponRowResponse>> LoadAllWeaponRowsAsync(
    HttpClient httpClient,
    JsonSerializerOptions jsonOptions)
{
    var requestUrl = $"{WeaponsApiUrl}?limit=5000";
    using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
    using var response = await httpClient.SendAsync(request);
    if (!response.IsSuccessStatusCode)
    {
        return new List<WeaponRowResponse>();
    }

    var payload = await response.Content.ReadAsStringAsync();
    var parsed = JsonSerializer.Deserialize<WeaponApiResponse>(payload, jsonOptions);
    if (parsed?.Rows is null)
    {
        return new List<WeaponRowResponse>();
    }

    return parsed.Rows
        .Where(row => !string.IsNullOrWhiteSpace(row.WeaponName))
        .ToList();
}

static List<WeaponMatch> FindWeaponMatchesAsync(
    string normalizedQuery,
    IReadOnlyList<WeaponRowResponse> allWeaponRows)
{
    if (allWeaponRows.Count == 0)
    {
        return new List<WeaponMatch>();
    }

    var rankedBySlot = BuildWeaponRankingBySlot(allWeaponRows);
    var aggregated = new Dictionary<string, WeaponMatch>(StringComparer.OrdinalIgnoreCase);
    foreach (var row in allWeaponRows)
    {
        if (string.IsNullOrWhiteSpace(row.WeaponName))
        {
            continue;
        }

        var normalizedName = NormalizeQuery(row.WeaponName);
        if (!WeaponNameMatches(normalizedQuery, normalizedName))
        {
            continue;
        }

        var slotKey = NormalizeWeaponSlot(row.SlotDisplay ?? row.Slot);
        var key = $"{normalizedName}|{slotKey}";
        if (!aggregated.TryGetValue(key, out var match))
        {
            match = new WeaponMatch(row, normalizedName, slotKey);
            aggregated[key] = match;
        }

        match.AddClasses(row.ClassNames);
        var rowDps = row.Dps ?? 0;
        var currentDps = match.Row.Dps ?? 0;
        if (rowDps > currentDps)
        {
            match.Row = row;
        }
    }

    var ordered = aggregated.Values
        .OrderBy(match => match.BestMatchDistance(normalizedQuery))
        .ThenByDescending(match => match.Row.Dps)
        .ThenBy(match => match.NormalizedName)
        .ToList();

    foreach (var match in ordered)
    {
        if (!rankedBySlot.TryGetValue(match.SlotKey, out var slotRanking))
        {
            continue;
        }

        var slotRank = slotRanking.FindIndex(candidate =>
            StringComparer.OrdinalIgnoreCase.Equals(candidate.NormalizedName, match.NormalizedName));
        if (slotRank >= 0)
        {
            match.DpsRank = slotRank + 1;
            match.DpsTotal = slotRanking.Count;
        }
        else
        {
            match.DpsRank = 1;
            match.DpsTotal = slotRanking.Count;
        }
    }

    return ordered;
}

static bool WeaponNameMatches(string query, string candidate)
{
    if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(candidate))
    {
        return false;
    }

    if (string.Equals(query, candidate, StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (candidate.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        query.Contains(candidate, StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    var queryTokens = GetNameTokens(query);
    var candidateTokens = new HashSet<string>(GetNameTokens(candidate), StringComparer.OrdinalIgnoreCase);
    return queryTokens.Count > 0 && queryTokens.All(candidateTokens.Contains);
}

static IReadOnlyList<string> GetNameTokens(string normalizedValue)
{
    return normalizedValue
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(token => token.Length > 1)
        .ToList();
}

static void PrintWeaponMatchReport(WeaponMatch match)
{
    var row = match.Row;
    Console.WriteLine($"  Weapon: {row.WeaponName}");
    Console.WriteLine($"    Slot: {row.SlotDisplay ?? row.Slot}");

    if (!string.IsNullOrWhiteSpace(row.SourceType) || !string.IsNullOrWhiteSpace(row.Source))
    {
        Console.WriteLine($"    Source: {row.SourceType ?? row.Source}");
    }

    if (!string.IsNullOrWhiteSpace(row.DropsFrom))
    {
        Console.WriteLine($"    Drops from: {row.DropsFrom}");
    }

    if (!string.IsNullOrWhiteSpace(row.QuestReward))
    {
        Console.WriteLine($"    Quest reward: {row.QuestReward}");
    }

    if (!string.IsNullOrWhiteSpace(row.OneHanded) || !string.IsNullOrWhiteSpace(row.OffhandUsable))
    {
        Console.WriteLine($"    Weapon type: {row.OneHanded} | Offhand: {row.OffhandUsable}");
    }

    if (row.Dps.HasValue)
    {
        WriteRankPrefix("    DPS rank");
        WritePercentileValue(match.DpsRank, match.DpsTotal);
        Console.WriteLine();
        Console.WriteLine($"    DPS: {row.Dps:0.0000}");
    }

    if (!string.IsNullOrWhiteSpace(row.ZamUrl))
    {
        Console.WriteLine($"    Allakhazam: {row.ZamUrl}");
    }

    Console.WriteLine($"    Classes: {string.Join(", ", match.ClassNames.OrderBy(c => c).ToList())}");
}

static Dictionary<string, List<WeaponMatch>> BuildWeaponRankingBySlot(IReadOnlyList<WeaponRowResponse> rows)
{
    var slotMap = new Dictionary<string, Dictionary<string, WeaponMatch>>(StringComparer.OrdinalIgnoreCase);
    foreach (var row in rows)
    {
        if (string.IsNullOrWhiteSpace(row.WeaponName))
        {
            continue;
        }

        var normalizedName = NormalizeQuery(row.WeaponName);
        var slotKey = NormalizeWeaponSlot(row.SlotDisplay ?? row.Slot);
        if (!slotMap.TryGetValue(slotKey, out var byName))
        {
            byName = new Dictionary<string, WeaponMatch>(StringComparer.OrdinalIgnoreCase);
            slotMap[slotKey] = byName;
        }

        var key = $"{normalizedName}|{slotKey}";
        if (!byName.TryGetValue(key, out var match))
        {
            match = new WeaponMatch(row, normalizedName, slotKey);
            byName[key] = match;
            continue;
        }

        match.AddClasses(row.ClassNames);
        var rowDps = row.Dps ?? 0;
        var currentDps = match.Row.Dps ?? 0;
        if (rowDps > currentDps)
        {
            match.Row = row;
        }
    }

    return slotMap.ToDictionary(
        slot => slot.Key,
        slot => slot.Value.Values
            .OrderByDescending(match => match.Row.Dps ?? 0)
            .ThenBy(match => match.NormalizedName)
            .ToList(),
        StringComparer.OrdinalIgnoreCase);
}

static string NormalizeWeaponSlot(string? slot)
{
    if (string.IsNullOrWhiteSpace(slot))
    {
        return "UNKNOWN";
    }

    return Regex.Replace(slot.Trim().ToUpperInvariant(), @"\s+", " ", RegexOptions.Compiled).Trim();
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

static IEnumerable<string> BuildWikiTitleCandidates(string itemName)
{
    var trimmed = (itemName ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(trimmed))
    {
        yield break;
    }

    var normalizedSpaces = Regex.Replace(trimmed, @"\s+", " ").Trim();
    var smartTitleCase = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalizedSpaces.ToLowerInvariant());
    var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        normalizedSpaces,
        smartTitleCase,
        normalizedSpaces.Replace('\'', '’'),
        smartTitleCase.Replace('\'', '’'),
        normalizedSpaces.Replace('’', '\''),
        smartTitleCase.Replace('’', '\'')
    };

    foreach (var candidate in candidates)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            yield return candidate;
        }
    }
}

static async Task<WikiItemMatch?> TryLoadWikiPageByTitleAsync(string title, HttpClient httpClient)
{
    var encodedTitle = Uri.EscapeDataString(title.Replace(' ', '_'));
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

    var canonicalTitle = titleElement.GetString() ?? title;
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

}

public sealed class ApiResponse
{
    [JsonPropertyName("rows")]
    public List<GearRow>? Rows { get; set; }
}

public sealed class ItemLookupSearchResult
{
    public string? QueryFeedback { get; set; }
    public string? NoGearMatchesMessage { get; set; }
    public string? NoWeaponMatchesMessage { get; set; }
    public List<string> WeaponResultLines { get; } = new();
    public List<string> WikiResultLines { get; } = new();
    public List<ItemLookupMatchSummary> GearMatches { get; } = new();
}

public sealed class ItemLookupMatchSummary(
    string itemName,
    string slots,
    string? source,
    double ac,
    IReadOnlyList<string> notableStats,
    double qualityPercentile,
    string qualityLabel,
    IReadOnlyList<string> classSummaryLines,
    IReadOnlyList<string> betterSlotHints,
    IReadOnlyList<ClassCompositeScore> classCompositeScores)
{
    public string ItemName { get; } = itemName;
    public string Slots { get; } = slots;
    public string? Source { get; } = source;
    public double Ac { get; } = ac;
    public IReadOnlyList<string> NotableStats { get; } = notableStats;
    public double QualityPercentile { get; } = qualityPercentile;
    public string QualityLabel { get; } = qualityLabel;
    public IReadOnlyList<string> ClassSummaryLines { get; } = classSummaryLines;
    public IReadOnlyList<string> BetterSlotHints { get; } = betterSlotHints;
    public IReadOnlyList<ClassCompositeScore> ClassCompositeScores { get; } = classCompositeScores;
    public double CompositeScore => 100.0 - QualityPercentile;
}

public sealed class ClassCompositeScore(
    string className,
    double qualityPercentile,
    string? betterItem,
    bool isCurrentBestInInventory = false,
    string? contextLabel = null,
    bool isDirectClassFit = false)
{
    public string ClassName { get; } = className;
    public double QualityPercentile { get; } = qualityPercentile;
    public double CompositeScore => 100.0 - QualityPercentile;
    public string? BetterItem { get; } = betterItem;
    public bool IsCurrentBestInInventory { get; } = isCurrentBestInInventory;
    public string? ContextLabel { get; } = contextLabel;
    public bool IsDirectClassFit { get; } = isDirectClassFit;
}

public sealed class InventoryDumpEntry(string location, string name, int itemId, int count, bool isKeyRing)
{
    public string Location { get; } = location;
    public string Name { get; } = name;
    public int ItemId { get; } = itemId;
    public int Count { get; } = count;
    public bool IsKeyRing { get; } = isKeyRing;
}

public sealed class InventoryDisposalCandidate(
    string itemName,
    int count,
    List<string> locations,
    double dispositionPercentile,
    string reason,
    string? betterOption = null)
{
    public string ItemName { get; } = itemName;
    public int Count { get; } = count;
    public List<string> Locations { get; } = locations;
    public double DispositionPercentile { get; } = dispositionPercentile;
    public double DispositionPriority => DispositionPercentile;
    public string Reason { get; } = reason;
    public string? BetterOption { get; } = betterOption;
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

public sealed class WeaponApiResponse
{
    [JsonPropertyName("rows")]
    public List<WeaponRowResponse>? Rows { get; set; }
}

public sealed class WeaponRowResponse
{
    [JsonPropertyName("className")]
    public string? ClassName { get; set; }

    [JsonPropertyName("slot")]
    public string? Slot { get; set; }

    [JsonPropertyName("weaponName")]
    public string WeaponName { get; set; } = "";

    [JsonPropertyName("zamUrl")]
    public string? ZamUrl { get; set; }

    [JsonPropertyName("dps")]
    public double? Dps { get; set; }

    [JsonPropertyName("dmg")]
    public double? Dmg { get; set; }

    [JsonPropertyName("dly")]
    public double? Dly { get; set; }

    [JsonPropertyName("oneHanded")]
    public string? OneHanded { get; set; }

    [JsonPropertyName("offhandUsable")]
    public string? OffhandUsable { get; set; }

    [JsonPropertyName("backstabDmg")]
    public double? BackstabDmg { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("sourceType")]
    public string? SourceType { get; set; }

    [JsonPropertyName("dropsFrom")]
    public string? DropsFrom { get; set; }

    [JsonPropertyName("questReward")]
    public string? QuestReward { get; set; }

    [JsonPropertyName("rowId")]
    public int? RowId { get; set; }

    [JsonPropertyName("minLevel")]
    public int? MinLevel { get; set; }

    [JsonPropertyName("classNames")]
    public List<string>? ClassNames { get; set; }

    [JsonPropertyName("slotDisplay")]
    public string? SlotDisplay { get; set; }
}

public sealed class WeaponMatch
{
    public WeaponMatch(WeaponRowResponse row, string normalizedName, string slotKey)
    {
        Row = row;
        NormalizedName = normalizedName;
        SlotKey = slotKey;
        AddClasses(row.ClassNames);
        if (!string.IsNullOrWhiteSpace(row.ClassName))
        {
            _classes.Add(row.ClassName!);
        }
    }

    public WeaponRowResponse Row { get; set; }
    public string NormalizedName { get; }
    public string SlotKey { get; }
    public HashSet<string> _classes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<string> ClassNames => _classes;
    public int DpsRank { get; set; }
    public int DpsTotal { get; set; }

    public int BestMatchDistance(string query)
    {
        if (NormalizedName == query)
        {
            return 0;
        }

        if (NormalizedName.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

    public void AddClasses(IEnumerable<string>? classes)
    {
        if (classes is null)
        {
            return;
        }

        foreach (var className in classes)
        {
            if (!string.IsNullOrWhiteSpace(className))
            {
                _classes.Add(className.Trim());
            }
        }
    }
}
