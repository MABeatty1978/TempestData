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
            "timestamp",                               // 0
            "report_interval",                        // 1
            "wind_lull",                              // 2
            "wind_avg",                               // 3
            "wind_gust",                              // 4
            "wind_dir",                               // 5
            "station_pressure",                       // 6
            "sea_level_pressure",                     // 7
            "air_temp",                               // 8
            "rh",                                     // 9
            "illuminance",                            // 10
            "uv",                                     // 11
            "solar_radiation",                        // 12
            "precip_accumulation",                    // 13
            "local_day_precip_accumulation",          // 14
            "precip_type",                            // 15
            "strike_count",                           // 16
            "strike_distance",                        // 17
            "nc_precip_accumulation",                 // 18
            "nc_local_day_precip_accumulation"        // 19
        };

        private static readonly string[] DailyBucketObservationFieldOrder =
        {
            "timestamp",                              // 0
            "average_pressure",                       // 1
            "highest_pressure",                       // 2
            "lowest_pressure",                        // 3
            "average_temperature",                    // 4
            "highest_temperature",                    // 5
            "lowest_temperature",                     // 6
            "average_humidity",                       // 7
            "highest_humidity",                       // 8
            "lowest_humidity",                        // 9
            "average_illuminance",                    // 10
            "highest_illuminance",                    // 11
            "lowest_illuminance",                     // 12
            "average_uv",                             // 13
            "highest_uv",                             // 14
            "lowest_uv",                              // 15
            "average_solar_radiation",                // 16
            "highest_solar_radiation",                // 17
            "lowest_solar_radiation",                 // 18
            "average_wind_speed",                     // 19
            "wind_gust",                              // 20
            "wind_lull",                              // 21
            "average_wind_direction",                 // 22
            "wind_sample_interval",                   // 23
            "strike_count",                           // 24
            "average_strike_distance",                // 25
            "record_count",                           // 26
            "battery",                                // 27
            "local_day_rain_accumulation",            // 28
            "local_day_nearcast_rain_accumulation",   // 29
            "local_day_precipitation_minutes",        // 30
            "local_day_nearcast_precipitation_minutes",// 31
            "precipitation_type",                     // 32
            "precipitation_analysis_type"             // 33
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

            var normalizedBucket = NormalizeBucketForApi(bucketValue);
            if (!string.IsNullOrWhiteSpace(normalizedBucket))
            {
                query.Add($"bucket={Uri.EscapeDataString(normalizedBucket)}");
            }

            if (!string.IsNullOrWhiteSpace(unitsTemp))
            {
                query.Add($"units_temp={Uri.EscapeDataString(unitsTemp)}");
            }

            if (!string.IsNullOrWhiteSpace(unitsWind))
            {
                query.Add($"units_wind={Uri.EscapeDataString(unitsWind)}");
            }

            if (!string.IsNullOrWhiteSpace(unitsPressure))
            {
                query.Add($"units_pressure={Uri.EscapeDataString(unitsPressure)}");
            }

            if (!string.IsNullOrWhiteSpace(unitsPrecip))
            {
                query.Add($"units_precip={Uri.EscapeDataString(unitsPrecip)}");
            }

            if (!string.IsNullOrWhiteSpace(unitsDistance))
            {
                query.Add($"units_distance={Uri.EscapeDataString(unitsDistance)}");
            }

            // NOTE: obs_fields parameter may not be supported by station endpoint or may not filter results
            // Commenting out for now - we'll filter on client side instead
            // if (!string.IsNullOrWhiteSpace(obsFields))
            // {
            //     query.Add($"obs_fields={Uri.EscapeDataString(obsFields)}");
            // }

            query.Add($"limit={maxRows}");

            var builder = new UriBuilder($"https://swd.weatherflow.com/swd/rest/observations/stn/{deviceId}")
            {
                Query = string.Join("&", query)
            };

            using var response = await _httpClient.GetAsync(builder.Uri).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {body}");
            }

            // DEBUG: Log request and response details
            System.Diagnostics.Debug.WriteLine($"\n=== API Request ===");
            System.Diagnostics.Debug.WriteLine($"URL: {builder.Uri}");
            System.Diagnostics.Debug.WriteLine($"obsFields parameter: {obsFields ?? "(empty)"}");
            System.Diagnostics.Debug.WriteLine($"\n=== API Response ===");
            System.Diagnostics.Debug.WriteLine($"Body: {body}");

            using var document = JsonDocument.Parse(body);
            var fieldList = string.IsNullOrWhiteSpace(obsFields) ? new List<string>() : obsFields.Split(',').Select(f => f.Trim()).ToList();
            System.Diagnostics.Debug.WriteLine($"\n=== Parsed Fields ===");
            System.Diagnostics.Debug.WriteLine($"Requested fields ({fieldList.Count}): {string.Join(", ", fieldList)}");
            var defaultFieldOrder = IsDailyBucket(normalizedBucket) ? DailyBucketObservationFieldOrder : DefaultObservationFieldOrder;
            
            // Check if API provided field names
            var responseFieldNames = TryGetFieldNameArray(document.RootElement);
            System.Diagnostics.Debug.WriteLine($"API-provided field names ({responseFieldNames.Count}): {string.Join(", ", responseFieldNames.Take(10))}...");
            System.Diagnostics.Debug.WriteLine($"Default field order ({defaultFieldOrder.Length}): {string.Join(", ", defaultFieldOrder.Take(10))}...");
            
            var result = ParseObservations(document.RootElement, maxRows, fieldList, defaultFieldOrder);
            System.Diagnostics.Debug.WriteLine($"\n=== Parse Result ===");
            System.Diagnostics.Debug.WriteLine($"Columns ({result.Columns.Count}): {string.Join(", ", result.Columns.Cast<DataColumn>().Select(c => c.ColumnName))}");
            if (result.Rows.Count > 0)
            {
                var firstRow = result.Rows[0];
                var values = string.Join(" | ", result.Columns.Cast<DataColumn>().Select(c => $"{c.ColumnName}={firstRow[c.ColumnName]}"));
                System.Diagnostics.Debug.WriteLine($"First row: {values}");
            }
            return result;
        }

        private static string NormalizeBucketForApi(string? bucketValue)
        {
            if (string.IsNullOrWhiteSpace(bucketValue))
            {
                return "1";
            }

            return bucketValue.Trim().ToLowerInvariant() switch
            {
                "a" => "1",
                "b" => "5",
                "c" => "30",
                "d" => "180",
                "e" => "1440",
                "1" => "1",
                "5" => "5",
                "30" => "30",
                "180" => "180",
                "1440" => "1440",
                _ => "1"
            };
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

        private static DataTable ParseObservations(JsonElement root, int maxRows, List<string>? fieldNames = null, string[]? defaultFieldOrder = null)
        {
            fieldNames ??= new List<string>();
            defaultFieldOrder ??= DefaultObservationFieldOrder;
            var responseFieldNames = TryGetFieldNameArray(root);
            var effectiveFieldNames = responseFieldNames.Count > 0 ? responseFieldNames : fieldNames;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryGetArrayProperty(root, "observations", out var observationArray))
                {
                    return ParseObservationArray(observationArray, maxRows, effectiveFieldNames, defaultFieldOrder);
                }

                if (TryGetArrayProperty(root, "obs", out var obsArray))
                {
                    return ParseObservationArray(obsArray, maxRows, effectiveFieldNames, defaultFieldOrder);
                }

                if (TryGetArrayProperty(root, "data", out var dataArray))
                {
                    return ParseObservationArray(dataArray, maxRows, effectiveFieldNames, defaultFieldOrder);
                }
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                return ParseObservationArray(root, maxRows, effectiveFieldNames, defaultFieldOrder);
            }

            return new DataTable("Observations");
        }

        private static DataTable ParseObservationArray(JsonElement array, int maxRows, List<string>? fieldNames = null, string[]? defaultFieldOrder = null)
        {
            fieldNames ??= new List<string>();
            defaultFieldOrder ??= DefaultObservationFieldOrder;
            if (!array.EnumerateArray().Any())
            {
                return new DataTable("Observations");
            }

            var firstItem = array.EnumerateArray().First();
            return firstItem.ValueKind switch
            {
                JsonValueKind.Object => ParseObjectArray(array, maxRows, fieldNames),
                JsonValueKind.Array => ParseArrayRowArray(array, maxRows, fieldNames, defaultFieldOrder),
                _ => new DataTable("Observations")
            };
        }

        private static DataTable ParseArrayRowArray(JsonElement array, int maxRows, List<string>? fieldNames = null, string[]? defaultFieldOrder = null)
        {
            fieldNames ??= new List<string>();
            defaultFieldOrder ??= DefaultObservationFieldOrder;
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
            var useFieldNames = fieldNames.Count > 0 && fieldNames.Count == maxColumns;
            for (var i = 0; i < maxColumns; i++)
            {
                var requestedName = useFieldNames && i < fieldNames.Count ? fieldNames[i] : string.Empty;
                var columnName = !string.IsNullOrWhiteSpace(requestedName)
                    ? requestedName
                    : i < defaultFieldOrder.Length
                        ? defaultFieldOrder[i]
                        : $"Value{i}";

                // Prevent duplicate names when requested fields contain duplicates.
                if (table.Columns.Contains(columnName))
                {
                    var suffix = 2;
                    var baseName = columnName;
                    while (table.Columns.Contains($"{baseName}_{suffix}"))
                    {
                        suffix++;
                    }

                    columnName = $"{baseName}_{suffix}";
                }

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

        private static bool IsDailyBucket(string? bucketValue)
        {
            var value = bucketValue?.Trim();
            return string.Equals(value, "e", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "1440", StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> TryGetFieldNameArray(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new List<string>();
            }

            var candidateNames = new[]
            {
                "ob_fields",
                "obs_fields",
                "observation_fields",
                "fields",
                "field_names",
                "column_names"
            };

            foreach (var candidate in candidateNames)
            {
                if (!root.TryGetProperty(candidate, out var fieldElement) || fieldElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var names = new List<string>();
                foreach (var item in fieldElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        names.Clear();
                        break;
                    }

                    var name = item.GetString();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        names.Clear();
                        break;
                    }

                    names.Add(name.Trim());
                }

                if (names.Count > 0)
                {
                    return names;
                }
            }

            return new List<string>();
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
