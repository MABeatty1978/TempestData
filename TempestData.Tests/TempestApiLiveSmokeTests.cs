using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using TempestData.Services;
using Xunit;

namespace TempestData.Tests;

public class TempestApiLiveSmokeTests
{
    [Theory]
    [InlineData("1", 6)]
    [InlineData("5", 24)]
    [InlineData("30", 24 * 7)]
    [InlineData("180", 24 * 30)]
    [InlineData("1440", 24 * 120)]
    public async Task LiveApi_DifferentResolutionWindows_ReturnsMappedAndCompleteRows(string bucket, int lookbackHours)
    {
        var token = Environment.GetEnvironmentVariable("TEMPEST_API_TOKEN");
        var deviceId = Environment.GetEnvironmentVariable("TEMPEST_DEVICE_ID");
        var pageSizeEnv = Environment.GetEnvironmentVariable("TEMPEST_PAGE_SIZE");

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(deviceId))
        {
            return;
        }

        var pageSize = int.TryParse(pageSizeEnv, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPageSize) && parsedPageSize > 0
            ? parsedPageSize
            : 250;

        var endUtc = DateTime.UtcNow;
        var startUtc = endUtc.AddHours(-lookbackHours);
        var requestedFields = "timestamp,air_temperature,wind_avg,pressure,relative_humidity";

        using var httpClient = new HttpClient();
        var client = new TempestApiClient(httpClient);

        DataTable table = await client.GetObservationsAsync(
            token: token,
            deviceId: deviceId,
            bucketValue: bucket,
            unitsTemp: "f",
            unitsWind: "mph",
            unitsPressure: "inhg",
            unitsPrecip: "in",
            unitsDistance: "mi",
            obsFields: requestedFields,
            startUtc: startUtc,
            endUtc: endUtc,
            maxRows: pageSize);

        Assert.True(table.Columns.Contains("timestamp"));
        Assert.True(table.Columns.Contains("air_temperature"));
        Assert.True(table.Columns.Contains("wind_avg"));
        Assert.True(table.Columns.Contains("pressure"));
        Assert.True(table.Columns.Contains("relative_humidity"));
        Assert.True(table.Rows.Count <= pageSize, $"Expected <= {pageSize} rows, got {table.Rows.Count}.");

        foreach (DataRow row in table.Rows)
        {
            var timestamp = row["timestamp"]?.ToString();
            Assert.False(string.IsNullOrWhiteSpace(timestamp));

            var isUnix = long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            var isIso = DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _);
            Assert.True(isUnix || isIso, $"Invalid timestamp value: '{timestamp}'");

            var selectedValues = new[]
            {
                row["timestamp"]?.ToString(),
                row["air_temperature"]?.ToString(),
                row["wind_avg"]?.ToString(),
                row["pressure"]?.ToString(),
                row["relative_humidity"]?.ToString()
            };

            Assert.True(selectedValues.Any(v => !string.IsNullOrWhiteSpace(v)), "Row appears empty across selected fields.");
            Assert.Equal(table.Columns.Count, row.ItemArray.Length);
        }
    }
}
