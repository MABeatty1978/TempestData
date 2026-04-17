using System;
using System.Data;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TempestData.Services;
using Xunit;

namespace TempestData.Tests;

public class TempestApiClientTests
{
    [Fact]
    public async Task GetObservationsAsync_IncludesBucketUnitsObsAndTimeRangeInRequest()
    {
        Uri? capturedUri = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"obs\":[[1710000000,0.1]]}", Encoding.UTF8, "application/json")
            };
        });

        var client = new TempestApiClient(new HttpClient(handler));
        var startUtc = DateTime.UnixEpoch.AddHours(1);
        var endUtc = DateTime.UnixEpoch.AddHours(2);

        DataTable result = await client.GetObservationsAsync(
            token: "abc123",
            deviceId: "789",
            bucketValue: "30",
            unitsTemp: "f",
            unitsWind: "mph",
            unitsPressure: "inhg",
            unitsPrecip: "in",
            unitsDistance: "mi",
            obsFields: "timestamp,air_temperature,wind_avg",
            startUtc: startUtc,
            endUtc: endUtc,
            maxRows: 500);

        Assert.NotNull(capturedUri);
        string query = capturedUri!.Query;

        Assert.Contains("token=abc123", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("time_start=3600", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("time_end=7200", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bucket=30", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("units_temp=f", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("units_wind=mph", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("units_pressure=inhg", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("units_precip=in", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("units_distance=mi", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("obs_fields=timestamp%2Cair_temperature%2Cwind_avg", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("limit=500", query, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(1, result.Rows.Count);
        Assert.True(result.Columns.Contains("timestamp"));
    }

    [Fact]
    public async Task GetObservationsAsync_ArrayRows_MapValuesToExpectedDefaultColumns()
    {
        const string payload = """
        {
          "obs": [
            [1710000000, 1.1, 2.2, 3.3, 180, 3, 1012.5, 22.4, 56, 1000, 3.1, 450, 0.0, 0, 12, 0, 2.67, 60, 0.0, 0.0, 0],
            [1710000060, 1.2, 2.3, 3.4, 181, 3, 1012.6, 22.5, 57, 1001, 3.2, 451, 0.1, 0, 13, 1, 2.66, 60, 0.1, 0.0, 0]
          ]
        }
        """;

        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });

        var client = new TempestApiClient(new HttpClient(handler));

        DataTable table = await client.GetObservationsAsync(
            token: "t",
            deviceId: "d",
            bucketValue: "1",
            unitsTemp: "c",
            unitsWind: "mps",
            unitsPressure: "mb",
            unitsPrecip: "mm",
            unitsDistance: "km",
            obsFields: string.Empty,
            startUtc: null,
            endUtc: null,
            maxRows: 100);

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(21, table.Columns.Count);

        Assert.Equal("1710000000", table.Rows[0]["timestamp"]?.ToString());
        Assert.Equal("2.2", table.Rows[0]["wind_avg"]?.ToString());
        Assert.Equal("1012.5", table.Rows[0]["pressure"]?.ToString());
        Assert.Equal("22.4", table.Rows[0]["air_temperature"]?.ToString());
        Assert.Equal("56", table.Rows[0]["relative_humidity"]?.ToString());
        Assert.Equal("2.67", table.Rows[0]["battery"]?.ToString());
        Assert.Equal("0.0", table.Rows[0]["nearcast_rain_accumulation"]?.ToString());
    }

    [Fact]
    public async Task GetObservationsAsync_ArrayRows_WithExtraValues_AddsValueColumnsAndPreservesCompleteness()
    {
        const string payload = "{\"obs\":[[1710000000,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22]]}";
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });

        var client = new TempestApiClient(new HttpClient(handler));
        DataTable table = await client.GetObservationsAsync(
            token: "t",
            deviceId: "d",
            bucketValue: "1",
            unitsTemp: "c",
            unitsWind: "mps",
            unitsPressure: "mb",
            unitsPrecip: "mm",
            unitsDistance: "km",
            obsFields: string.Empty,
            startUtc: null,
            endUtc: null,
            maxRows: 100);

        Assert.Equal(23, table.Columns.Count);
        Assert.True(table.Columns.Contains("local_day_nearcast_rain_accumulation"));
        Assert.True(table.Columns.Contains("Value22"));
        Assert.Equal("20", table.Rows[0]["local_day_nearcast_rain_accumulation"]?.ToString());
        Assert.Equal("22", table.Rows[0]["Value22"]?.ToString());
    }

    [Fact]
    public async Task GetObservationsAsync_ArrayRows_UsesRequestedFieldOrderForColumnNames()
    {
        const string payload = "{\"obs\":[[1710000000,989.66,74.2,52,2.1,3.4,5.9]]}";
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });

        var client = new TempestApiClient(new HttpClient(handler));
        DataTable table = await client.GetObservationsAsync(
            token: "t",
            deviceId: "d",
            bucketValue: "e",
            unitsTemp: "f",
            unitsWind: "mph",
            unitsPressure: "mb",
            unitsPrecip: "in",
            unitsDistance: "mi",
            obsFields: "timestamp,pressure,air_temperature,relative_humidity,wind_lull,wind_avg,wind_gust",
            startUtc: null,
            endUtc: null,
            maxRows: 100);

        Assert.Equal(1, table.Rows.Count);
        Assert.Equal("1710000000", table.Rows[0]["timestamp"]?.ToString());
        Assert.Equal("989.66", table.Rows[0]["pressure"]?.ToString());
        Assert.Equal("2.1", table.Rows[0]["wind_lull"]?.ToString());
        Assert.Equal("3.4", table.Rows[0]["wind_avg"]?.ToString());
        Assert.Equal("5.9", table.Rows[0]["wind_gust"]?.ToString());
    }

    [Fact]
    public async Task GetObservationsAsync_ArrayRows_PrefersApiFieldMetadataWhenProvided()
    {
        const string payload = "{\"obs_fields\":[\"timestamp\",\"pressure\",\"air_temperature\",\"wind_lull\",\"wind_avg\",\"wind_gust\"],\"obs\":[[1710000000,989.66,74.2,2.1,3.4,5.9]]}";
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });

        var client = new TempestApiClient(new HttpClient(handler));
        DataTable table = await client.GetObservationsAsync(
            token: "t",
            deviceId: "d",
            bucketValue: "e",
            unitsTemp: "f",
            unitsWind: "mph",
            unitsPressure: "mb",
            unitsPrecip: "in",
            unitsDistance: "mi",
            obsFields: "timestamp,wind_lull,wind_avg,wind_gust,pressure,air_temperature",
            startUtc: null,
            endUtc: null,
            maxRows: 100);

        Assert.Equal("989.66", table.Rows[0]["pressure"]?.ToString());
        Assert.Equal("2.1", table.Rows[0]["wind_lull"]?.ToString());
        Assert.Equal("3.4", table.Rows[0]["wind_avg"]?.ToString());
        Assert.Equal("5.9", table.Rows[0]["wind_gust"]?.ToString());
    }

    [Fact]
    public async Task GetObservationsAsync_DailyBucket_UsesDailyFieldOrderWhenMetadataMissing()
    {
        // Bucket=1440 (daily) documented order: average_pressure=idx1, average_wind_speed=idx19, wind_gust=idx20, wind_lull=idx21.
        const string payload = "{\"obs\":[[1710000000,989.66,1000,970,74.2,80.1,67.0,52,57,45,100,120,80,2,3,1,300,400,200,3.4,5.9,2.1,180,5,0,12,24,2.6,0.3,0.3,0,0,0,0]]}";
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });

        var client = new TempestApiClient(new HttpClient(handler));
        DataTable table = await client.GetObservationsAsync(
            token: "t",
            deviceId: "d",
            bucketValue: "1440",
            unitsTemp: "f",
            unitsWind: "mph",
            unitsPressure: "mb",
            unitsPrecip: "in",
            unitsDistance: "mi",
            obsFields: "",  // Not specifying obsFields to test default field order mapping
            startUtc: null,
            endUtc: null,
            maxRows: 100);

        Assert.Equal("989.66", table.Rows[0]["average_pressure"]?.ToString());
        Assert.Equal("2.1", table.Rows[0]["wind_lull"]?.ToString());
        Assert.Equal("3.4", table.Rows[0]["average_wind_speed"]?.ToString());
        Assert.Equal("5.9", table.Rows[0]["wind_gust"]?.ToString());
    }

    [Fact]
    public async Task GetObservationsAsync_ObjectRows_UsesSelectedFieldListAndFillsMissingAsEmpty()
    {
        const string payload = "{\"observations\":[{\"timestamp\":1710000000,\"air_temperature\":21.5},{\"timestamp\":1710000060}]}";
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });

        var client = new TempestApiClient(new HttpClient(handler));
        DataTable table = await client.GetObservationsAsync(
            token: "t",
            deviceId: "d",
            bucketValue: "5",
            unitsTemp: "c",
            unitsWind: "mps",
            unitsPressure: "mb",
            unitsPrecip: "mm",
            unitsDistance: "km",
            obsFields: "timestamp,air_temperature,wind_avg",
            startUtc: null,
            endUtc: null,
            maxRows: 100);

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(3, table.Columns.Count);
        Assert.True(table.Columns.Contains("timestamp"));
        Assert.True(table.Columns.Contains("air_temperature"));
        Assert.True(table.Columns.Contains("wind_avg"));

        Assert.Equal("1710000000", table.Rows[0]["timestamp"]?.ToString());
        Assert.Equal("21.5", table.Rows[0]["air_temperature"]?.ToString());
        Assert.Equal(string.Empty, table.Rows[0]["wind_avg"]?.ToString());

        Assert.Equal("1710000060", table.Rows[1]["timestamp"]?.ToString());
        Assert.Equal(string.Empty, table.Rows[1]["air_temperature"]?.ToString());
        Assert.Equal(string.Empty, table.Rows[1]["wind_avg"]?.ToString());
    }

    [Theory]
    [InlineData("1", 6)]
    [InlineData("5", 24)]
    [InlineData("30", 168)]
    [InlineData("180", 720)]
    [InlineData("1440", 4320)]
    public async Task GetObservationsAsync_DifferentResolutionsAndTimeframes_AlwaysReturnsRowsMappedToTimestamp(string bucket, int durationHours)
    {
        var payload = "{\"obs\":[[1710000000,2.5],[1710000600,2.6]]}";
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });

        var client = new TempestApiClient(new HttpClient(handler));
        var start = DateTime.UnixEpoch;
        var end = start.AddHours(durationHours);

        DataTable table = await client.GetObservationsAsync(
            token: "t",
            deviceId: "d",
            bucketValue: bucket,
            unitsTemp: "c",
            unitsWind: "mps",
            unitsPressure: "mb",
            unitsPrecip: "mm",
            unitsDistance: "km",
            obsFields: "timestamp,wind_lull",
            startUtc: start,
            endUtc: end,
            maxRows: 1000);

        Assert.Equal(2, table.Rows.Count);
        Assert.True(table.Columns.Contains("timestamp"));
        Assert.True(table.Columns.Contains("wind_lull"));
        Assert.Equal("1710000000", table.Rows[0]["timestamp"]?.ToString());
        Assert.Equal("2.5", table.Rows[0]["wind_lull"]?.ToString());
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
