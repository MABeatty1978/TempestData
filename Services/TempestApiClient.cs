using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using TempestData.Models;

namespace TempestData.Services
{
    public class TempestApiClient
    {
        private readonly HttpClient _httpClient;
        private static readonly string[] DefaultObservationFieldOrder =
        {
            "timestamp",
            "wind_lull",
            "wind_avg",
            "wind_gust",
            "wind_direction",
            "wind_sample_interval",
            "pressure",
            "air_temperature",
            "relative_humidity",
            "illuminance",
            "uv",
            "solar_radiation",
            "rain_accumulation",
            "precipitation_type",
            "lightning_distance",
            "lightning_strike_count",
            "battery",
            "reporting_interval",
            "local_day_rain_accumulation",
            "nearcast_rain_accumulation",
            "precipitation_analysis_type"
        };

        public TempestApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<List<TempestStation>> GetStationsAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return new List<TempestStation>();
            }

            var uri = new UriBuilder("https://swd.weatherflow.com/swd/rest/stations")
            {
                Query = $"token={Uri.EscapeDataString(token)}"
            }.Uri;

            using var response = await _httpClient.GetAsync(uri).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var stations = new List<TempestStation>();

            if (document.RootElement.TryGetProperty("stations", out var stationsElement) && stationsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var station in stationsElement.EnumerateArray())
                {
                    var id = station.GetPropertyOrDefault("station_id")?.GetRawText() ?? station.GetPropertyOrDefault("station_id")?.GetString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(id) && station.TryGetProperty("station_id", out var numericId))
                    {
                        id = numericId.ToString();
                    }

                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    var name = station.GetPropertyOrDefault("name")?.GetString() ?? station.GetPropertyOrDefault("station_name")?.GetString() ?? string.Empty;
                    var deviceInfo = ParseStationDeviceInfo(station);
                    stations.Add(new TempestStation
                    {
                        StationId = id.Trim('"'),
                        Name = name,
                        DeviceId = deviceInfo.deviceId,
                        DeviceType = deviceInfo.deviceType
                    });
                }
            }

            return stations.OrderBy(s => s.Name).ThenBy(s => s.StationId).ToList();
        }

        public async Task<DataTable> GetObservationsAsync(string token, string deviceId, string bucketValue, string unitsTemp, string unitsWind, string unitsPressure, string unitsPrecip, string unitsDistance, string obsFields, DateTime? startUtc, DateTime? endUtc, int maxRows)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(deviceId))
            {
                return new DataTable();
            }

            var query = new List<string>
            {
                $"token={Uri.EscapeDataString(token)}"
            };

            if (startUtc.HasValue)
            {
                query.Add($"time_start={(long)startUtc.Value.Subtract(DateTime.UnixEpoch).TotalSeconds}");
            }

            if (endUtc.HasValue)
            {
                query.Add($"time_end={(long)endUtc.Value.Subtract(DateTime.UnixEpoch).TotalSeconds}");
            }

            query.Add($"limit={maxRows}");

            var builder = new UriBuilder($"https://swd.weatherflow.com/swd/rest/observations/device/{deviceId}")
            {
                Query = string.Join("&", query)
            };

            using var response = await _httpClient.GetAsync(builder.Uri).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {body}");
            }

            using var document = JsonDocument.Parse(body);
            var fieldList = string.IsNullOrWhiteSpace(obsFields) ? new List<string>() : obsFields.Split(',').Select(f => f.Trim()).ToList();
            return ParseObservations(document.RootElement, maxRows, fieldList);
        }

        private static (int? deviceId, string deviceType) ParseStationDeviceInfo(JsonElement station)
        {
            if (station.TryGetProperty("devices", out var devices) && devices.ValueKind == JsonValueKind.Array)
            {
                int? firstDeviceId = null;
                string firstDeviceType = string.Empty;
                foreach (var device in devices.EnumerateArray())
                {
                    if (!device.TryGetProperty("device_id", out var deviceIdProperty) || !deviceIdProperty.TryGetInt32(out var deviceId))
                    {
                        continue;
                    }

                    var deviceType = device.GetPropertyOrDefault("device_type")?.GetString() ?? string.Empty;
                    if (!firstDeviceId.HasValue)
                    {
                        firstDeviceId = deviceId;
                        firstDeviceType = deviceType;
                    }

                    if (string.Equals(deviceType, "ST", StringComparison.OrdinalIgnoreCase))
                    {
                        return (deviceId, deviceType);
                    }
                }

                return (firstDeviceId, firstDeviceType);
            }

            return (null, string.Empty);
        }

        private static DataTable ParseObservations(JsonElement root, int maxRows, List<string>? fieldNames = null)
        {
            fieldNames ??= new List<string>();
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryGetArrayProperty(root, "observations", out var observationArray))
                {
                    return ParseObservationArray(observationArray, maxRows, fieldNames);
                }

                if (TryGetArrayProperty(root, "obs", out var obsArray))
                {
                    return ParseObservationArray(obsArray, maxRows, fieldNames);
                }

                if (TryGetArrayProperty(root, "data", out var dataArray))
                {
                    return ParseObservationArray(dataArray, maxRows, fieldNames);
                }
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                return ParseObservationArray(root, maxRows, fieldNames);
            }

            return new DataTable("Observations");
        }

        private static DataTable ParseObservationArray(JsonElement array, int maxRows, List<string>? fieldNames = null)
        {
            fieldNames ??= new List<string>();
            if (!array.EnumerateArray().Any())
            {
                return new DataTable("Observations");
            }

            var firstItem = array.EnumerateArray().First();
            return firstItem.ValueKind switch
            {
                JsonValueKind.Object => ParseObjectArray(array, maxRows, fieldNames),
                JsonValueKind.Array => ParseArrayRowArray(array, maxRows, fieldNames),
                _ => new DataTable("Observations")
            };
        }

        private static DataTable ParseArrayRowArray(JsonElement array, int maxRows, List<string>? fieldNames = null)
        {
            fieldNames ??= new List<string>();
            var rows = new List<List<string>>();
            var maxColumns = 0;
            var count = 0;

            foreach (var item in array.EnumerateArray())
            {
                if (count >= maxRows)
                {
                    break;
                }

                if (item.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var values = new List<string>();
                foreach (var value in item.EnumerateArray())
                {
                    values.Add(value.ValueKind switch
                    {
                        JsonValueKind.String => value.GetString() ?? string.Empty,
                        JsonValueKind.Number => value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => value.ToString() ?? string.Empty
                    });
                }

                maxColumns = Math.Max(maxColumns, values.Count);
                rows.Add(values);
                count++;
            }

            var table = new DataTable("Observations");
            for (var i = 0; i < maxColumns; i++)
            {
                var columnName = i < DefaultObservationFieldOrder.Length
                    ? DefaultObservationFieldOrder[i]
                    : $"Value{i}";
                table.Columns.Add(columnName, typeof(string));
            }

            foreach (var rowValues in rows)
            {
                var newRow = table.NewRow();
                for (var i = 0; i < rowValues.Count; i++)
                {
                    newRow[i] = rowValues[i];
                }

                table.Rows.Add(newRow);
            }

            return table;
        }

        private static bool TryGetArrayProperty(JsonElement parent, string propertyName, out JsonElement arrayElement)
        {
            if (parent.TryGetProperty(propertyName, out arrayElement) && arrayElement.ValueKind == JsonValueKind.Array)
            {
                return true;
            }

            arrayElement = default;
            return false;
        }

        private static DataTable ParseObjectArray(JsonElement array, int maxRows, List<string>? fieldNames = null)
        {
            fieldNames ??= new List<string>();
            var rows = new List<Dictionary<string, string>>();
            var discoveredFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in item.EnumerateObject())
                {
                    var value = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                        JsonValueKind.Number => property.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => property.Value.ToString() ?? string.Empty
                    };

                    row[property.Name] = value;
                    discoveredFields.Add(property.Name);
                }

                rows.Add(row);
                if (rows.Count >= maxRows)
                {
                    break;
                }
            }

            var table = new DataTable("Observations");
            var fieldsToUse = fieldNames.Count > 0 ? fieldNames : discoveredFields.OrderBy(n => n).ToList();
            foreach (var field in fieldsToUse)
            {
                table.Columns.Add(field, typeof(string));
            }

            foreach (var row in rows)
            {
                var newRow = table.NewRow();
                foreach (var field in fieldsToUse)
                {
                    newRow[field] = row.TryGetValue(field, out var value) ? value : string.Empty;
                }

                table.Rows.Add(newRow);
            }

            return table;
        }
    }

    internal static class JsonElementExtensions
    {
        public static JsonElement? GetPropertyOrDefault(this JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var property))
            {
                return property;
            }

            return null;
        }
    }
}
