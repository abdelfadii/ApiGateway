using Microsoft.AspNetCore.Mvc;

namespace SirmarocGateway.Controllers;

[ApiController]
[Route("api/flights")]
public class FlightsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public FlightsController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string departure, [FromQuery] string arrival, [FromQuery] string date)
    {
        var apiKey = _configuration["SerpApi:ApiKey"] ?? _configuration["SERPAPI_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return StatusCode(500, new { message = "SERPAPI_API_KEY is not configured on gateway." });
        }

        if (string.IsNullOrWhiteSpace(departure) || string.IsNullOrWhiteSpace(arrival))
        {
            return BadRequest(new { message = "departure and arrival are required." });
        }

        var outboundDate = date;
        if (!DateOnly.TryParse(date, out _))
        {
            outboundDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        }

        var query = new Dictionary<string, string?>
        {
            ["engine"] = "google_flights",
            ["api_key"] = apiKey,
            ["departure_id"] = departure,
            ["arrival_id"] = arrival,
            ["outbound_date"] = outboundDate,
            ["type"] = "2",
            ["hl"] = "fr",
            ["gl"] = "ma",
            ["currency"] = "MAD"
        };

        var queryString = string.Join("&", query
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));

        var url = $"https://serpapi.com/search.json?{queryString}";

        var client = _httpClientFactory.CreateClient();
        using var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return StatusCode((int)response.StatusCode, payload);
        }

        return Content(payload, "application/json");
    }
}
