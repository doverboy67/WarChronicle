using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using WarChronicle.Web.Domain;

namespace WarChronicle.Web.Services;

/// <summary>
/// Loads the platform-neutral wc_data.json produced by the Python compiler.
/// Rules execution stays outside this catalog. This service only exposes
/// compiled game data to browser-side systems.
/// </summary>
public sealed class WcDataCatalog
{
    private readonly HttpClient _http;
    private readonly ILogger<WcDataCatalog> _logger;
    private JsonDocument? _document;

    public WcDataCatalog(HttpClient http, ILogger<WcDataCatalog> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_document is not null)
            return;

        await using var stream = await _http.GetStreamAsync("Data/wc_data.json?v=0523", cancellationToken);
        _document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        _logger.LogInformation("Loaded War Chronicle data from static Pages content.");
    }

    public int CardCount => CountObject("cards");
    public int ExploreCardCount => CountObject("explore_cards");
    public int ArrivalCardCount => CountObject("arrival_cards");
    public int AdvancementCount => CountObject("advancements");

    public string? GetCardTitle(string cardId)
    {
        if (_document is null)
            return null;

        if (!_document.RootElement.GetProperty("cards").TryGetProperty(cardId, out var card))
            return null;

        return card.TryGetProperty("title", out var title) ? title.GetString() : null;
    }

    public IReadOnlyList<ExploreCardState> GetExploreCards(bool includeOptional = false)
        => GetExploreCards(includeOptional
            ? new[] { "Base", "SitA", "SiaSL" }
            : new[] { "Base" });

    public IReadOnlyList<ExploreCardState> GetExploreCards(IEnumerable<string> enabledSets)
    {
        if (_document is null || !_document.RootElement.TryGetProperty("explore_cards", out var cards))
            return [];

        var enabled = new HashSet<string>(enabledSets ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase)
        {
            "Base"
        };

        var result = new List<ExploreCardState>();
        foreach (var property in cards.EnumerateObject())
        {
            var card = property.Value;
            var num = card.GetProperty("card_num").GetInt32();
            var set = num switch
            {
                <= 30 => "Base",
                31 => "SitA",
                32 => "SiaSL",
                _ => "Optional"
            };
            if (!enabled.Contains(set))
                continue;

            var terrain = card.GetProperty("terrain_keywords")
                .EnumerateArray()
                .Select(v => v.GetString() ?? string.Empty)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();

            var text = card.GetProperty("text");
            result.Add(new(
                property.Name,
                num,
                card.GetProperty("title").GetString() ?? $"Explore {num}",
                terrain,
                text.TryGetProperty("flavor", out var flavor) ? flavor.GetString() ?? string.Empty : string.Empty,
                text.TryGetProperty("effect", out var effect) ? effect.GetString() ?? string.Empty : string.Empty));
        }

        return result.OrderBy(c => c.CardNum).ToArray();
    }

    public IReadOnlyList<ArrivalCardState> GetArrivalCards()
    {
        if (_document is null || !_document.RootElement.TryGetProperty("arrival_cards", out var cards))
            return [];

        var result = new List<ArrivalCardState>();
        foreach (var property in cards.EnumerateObject())
        {
            var card = property.Value;
            var num = card.GetProperty("card_num").GetInt32();
            result.Add(new(
                property.Name,
                num,
                CleanArrivalTitle(card.TryGetProperty("title", out var title) ? title.GetString() : null, num),
                card.TryGetProperty("tribe", out var tribe) ? tribe.GetString() ?? string.Empty : string.Empty,
                card.TryGetProperty("tribute", out var tribute) ? tribute.GetString() ?? string.Empty : string.Empty,
                PlainText(card.TryGetProperty("setup", out var setup) ? setup.GetString() ?? string.Empty : string.Empty),
                card.TryGetProperty("effect", out var effect) ? effect.GetString() ?? string.Empty : string.Empty,
                PlainText(card.TryGetProperty("host", out var host) ? host.GetString() ?? string.Empty : string.Empty)));
        }

        return result.OrderBy(c => c.CardNum).ToArray();
    }


    public IReadOnlyList<CampCardState> GetCampCards(bool includeOptional = false)
        => GetCampCards(includeOptional
            ? new[] { "Base", "SitA", "SiaSL" }
            : new[] { "Base" });

    public IReadOnlyList<CampCardState> GetCampCards(IEnumerable<string> enabledSets)
    {
        if (_document is null || !_document.RootElement.TryGetProperty("cards", out var cards))
            return [];

        var enabled = new HashSet<string>(enabledSets ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase)
        {
            "Base"
        };

        var result = new List<CampCardState>();
        foreach (var property in cards.EnumerateObject())
        {
            var card = property.Value;
            var type = card.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? string.Empty : string.Empty;
            if (type is not ("Camp" or "Scene" or "Echo"))
                continue;

            var set = card.TryGetProperty("set", out var setProp) ? setProp.GetString() ?? string.Empty : string.Empty;
            if (!enabled.Contains(set))
                continue;

            int? cardNum = card.TryGetProperty("card_num", out var cardNumProp) && cardNumProp.ValueKind == JsonValueKind.Number
                ? cardNumProp.GetInt32() : null;
            int? echoNum = card.TryGetProperty("echo_num", out var echoNumProp) && echoNumProp.ValueKind == JsonValueKind.Number
                ? echoNumProp.GetInt32() : null;
            var echoPool = card.TryGetProperty("echo_pool", out var echoPoolProp) && echoPoolProp.ValueKind == JsonValueKind.String
                ? echoPoolProp.GetString() : null;
            var keyword = card.TryGetProperty("keyword", out var keywordProp) && keywordProp.ValueKind == JsonValueKind.String
                ? keywordProp.GetString() : null;
            var text = card.GetProperty("text");
            var cost = card.TryGetProperty("cost", out var costProp) ? costProp : default;

            result.Add(new(
                property.Name,
                type,
                set,
                cardNum,
                echoPool,
                echoNum,
                card.TryGetProperty("title", out var title) ? title.GetString() ?? property.Name : property.Name,
                keyword,
                card.TryGetProperty("location", out var location) ? location.GetString() ?? "Any" : "Any",
                card.TryGetProperty("time", out var time) && time.ValueKind == JsonValueKind.Number ? time.GetInt32() : 0,
                cost.ValueKind == JsonValueKind.Object && cost.TryGetProperty("type", out var costType) && costType.ValueKind == JsonValueKind.String ? costType.GetString() : null,
                cost.ValueKind == JsonValueKind.Object && cost.TryGetProperty("value", out var costValue) && costValue.ValueKind == JsonValueKind.String ? costValue.GetString() : null,
                card.TryGetProperty("after_play", out var afterPlay) ? afterPlay.GetString() ?? string.Empty : string.Empty,
                text.TryGetProperty("flavor", out var flavor) ? PlainText(flavor.GetString() ?? string.Empty) : string.Empty,
                text.TryGetProperty("effect", out var effect) ? effect.GetString() ?? string.Empty : string.Empty));
        }

        return result
            .OrderBy(c => c.CardType == "Camp" ? 0 : c.CardType == "Scene" ? 1 : 2)
            .ThenBy(c => c.CardNum ?? int.MaxValue)
            .ThenBy(c => c.EchoPool)
            .ThenBy(c => c.EchoNum ?? int.MaxValue)
            .ToArray();
    }

    public CampCardState? GetCampCard(string id, bool includeOptional = true)
        => GetCampCards(includeOptional).FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<AdvancementOfferState> GetBaseAdvancements()
    {
        if (_document is null || !_document.RootElement.TryGetProperty("advancements", out var cards))
            return [];

        var result = new List<AdvancementOfferState>();
        foreach (var property in cards.EnumerateObject())
        {
            var card = property.Value;
            if (!card.TryGetProperty("card_num", out var number) || number.ValueKind != JsonValueKind.Number)
                continue;

            var num = number.GetInt32();
            if (num is < 1 or > 15)
                continue;

            var rawEffect = card.TryGetProperty("effect", out var effect) ? effect.GetString() ?? string.Empty : string.Empty;
            result.Add(new(
                property.Name,
                card.GetProperty("title").GetString() ?? $"Advancement {num}",
                FormatCost(card.TryGetProperty("cost", out var cost) ? cost.GetString() : null),
                PlainText(rawEffect)));
        }

        return result.OrderBy(a => AdvancementSortKey(a.Id)).ToArray();
    }

    public IReadOnlyList<AdvancementOfferState> GetMiddelalderenAdvancements()
    {
        if (_document is null || !_document.RootElement.TryGetProperty("advancements", out var cards))
            return [];

        var result = new List<AdvancementOfferState>();
        foreach (var property in cards.EnumerateObject())
        {
            var card = property.Value;
            if (!property.Name.StartsWith("ADV-M", StringComparison.OrdinalIgnoreCase))
                continue;

            var rawEffect = card.TryGetProperty("effect", out var effect) ? effect.GetString() ?? string.Empty : string.Empty;
            result.Add(new(
                property.Name,
                card.TryGetProperty("title", out var title) ? title.GetString() ?? property.Name : property.Name,
                string.Empty,
                PlainText(rawEffect)));
        }

        return result.OrderBy(a => AdvancementSortKey(a.Id)).ToArray();
    }

    public AdvancementOfferState? GetAdvancement(string id)
    {
        var baseAdvancement = GetBaseAdvancements().FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        return baseAdvancement ?? GetMiddelalderenAdvancements().FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the first compiled flow registered to a source/trigger pair.
    /// A cloned JsonElement keeps the rules definition independent of the
    /// catalog document's enumeration lifetime while still remaining read-only.
    /// </summary>
    public JsonElement? GetFlowForSource(string sourceId, string trigger = "OnResolve")
    {
        if (_document is null)
            return null;

        var root = _document.RootElement;
        if (!root.TryGetProperty("source_index", out var index)
            || !index.TryGetProperty(sourceId, out var source)
            || !source.TryGetProperty("triggers", out var triggers)
            || !triggers.TryGetProperty(trigger, out var flowIds)
            || flowIds.ValueKind != JsonValueKind.Array)
            return null;

        var first = flowIds.EnumerateArray().FirstOrDefault();
        if (first.ValueKind != JsonValueKind.String)
            return null;

        var flowId = first.GetString();
        if (string.IsNullOrWhiteSpace(flowId)
            || !root.TryGetProperty("flows", out var flows)
            || !flows.TryGetProperty(flowId, out var flow))
            return null;

        return flow.Clone();
    }

    private int CountObject(string propertyName)
    {
        if (_document is null || !_document.RootElement.TryGetProperty(propertyName, out var obj))
            return 0;
        return obj.EnumerateObject().Count();
    }

    private static int AdvancementSortKey(string id)
    {
        var match = Regex.Match(id, @"(\d+)$");
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : int.MaxValue;
    }

    private static string FormatCost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return Regex.Replace(value, @"(?<=\d)R\b", "RT");
    }

    private static string PlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var withBreaks = Regex.Replace(html, @"(?i)<br\s*/?>|</li>|</p>", " ");
        var stripped = Regex.Replace(withBreaks, "<[^>]+>", string.Empty);
        return WebUtility.HtmlDecode(Regex.Replace(stripped, @"\s+", " ").Trim());
    }

    private static string CleanArrivalTitle(string? html, int cardNum)
    {
        var title = PlainText(html ?? string.Empty);
        title = Regex.Replace(title, @"\s*\([^)]*\)\s*$", string.Empty).Trim();
        return string.IsNullOrWhiteSpace(title) ? $"Arrival {cardNum}" : title;
    }
}
