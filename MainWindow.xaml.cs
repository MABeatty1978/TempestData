using ClosedXML.Excel;
using Microsoft.Win32;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TempestData.Models;
using TempestData.Services;

namespace TempestData
{
    public partial class MainWindow : Window
    {
        private readonly TempestApiClient _apiClient;
        private readonly GitHubReleaseChecker _releaseChecker;
        private readonly List<ObservationField> _fields = new();
        private const string AllFieldName = "__all__";
        private const string NoneFieldName = "__none__";
        private const string SpacerFieldPrefix = "__spacer__";
        private TempestSettings _settings = new();
        private DataTable _observationTable = new();
        private DataTable _observationTableUtc = new();
        private bool _isInitializing;
        private bool _isFieldSelectionUpdating;
        private bool _isBusy;
        private bool _isCheckingUpdates;
        private HashSet<string> _standardSelectedFields = new(StringComparer.OrdinalIgnoreCase);
        private ContextMenu? _exportContextMenu;

        public MainWindow()
        {
            InitializeComponent();
            _apiClient = new TempestApiClient(new HttpClient());
            _releaseChecker = new GitHubReleaseChecker(new HttpClient());
            Loaded += MainWindow_Loaded;
            UpdateExportButtonState();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            try
            {
                PopulateComboBoxes();
                
                // Set default unit selections
                BucketComboBox.SelectedValue = "1";
                UnitsTempComboBox.SelectedValue = "f";
                UnitsWindComboBox.SelectedValue = "mph";
                UnitsPressureComboBox.SelectedValue = "inhg";
                UnitsPrecipComboBox.SelectedValue = "in";
                UnitsDistanceComboBox.SelectedValue = "km";
                
                BuildFieldList();
                FieldCheckList.ItemsSource = _fields;
                await LoadSettingsAsync().ConfigureAwait(false);

                var endUtc = DateTime.UtcNow;
                var startUtc = endUtc.AddHours(-24);
                await Dispatcher.InvokeAsync(() =>
                {
                    var tz = GetSelectedTimeZone();
                    var endLocal = TimeZoneInfo.ConvertTimeFromUtc(endUtc, tz);
                    var startLocal = TimeZoneInfo.ConvertTimeFromUtc(startUtc, tz);
                    EndDatePicker.SelectedDate = endLocal.Date;
                    EndTimeBox.Text = endLocal.ToString("HH:mm");
                    StartDatePicker.SelectedDate = startLocal.Date;
                    StartTimeBox.Text = startLocal.ToString("HH:mm");
                    UpdateTimeZoneLabels();
                });
            }
            finally
            {
                _isInitializing = false;
                UpdateExportButtonState();
            }
        }

        private void PopulateComboBoxes()
        {
            BucketComboBox.ItemsSource = new List<KeyValuePair<string, string>>
            {
                new("1", "1 minute"),
                new("5", "5 minutes"),
                new("30", "30 minutes"),
                new("180", "180 minutes")
            };
            BucketComboBox.DisplayMemberPath = "Value";
            BucketComboBox.SelectedValuePath = "Key";

            UnitsTempComboBox.ItemsSource = new List<KeyValuePair<string, string>>
            {
                new("c", "Celsius"),
                new("f", "Fahrenheit")
            };
            UnitsTempComboBox.DisplayMemberPath = "Value";
            UnitsTempComboBox.SelectedValuePath = "Key";

            UnitsWindComboBox.ItemsSource = new List<KeyValuePair<string, string>>
            {
                new("mps", "m/s"),
                new("mph", "mph"),
                new("kph", "kph"),
                new("kts", "kts"),
                new("bft", "Beaufort"),
                new("lfm", "LFM")
            };
            UnitsWindComboBox.DisplayMemberPath = "Value";
            UnitsWindComboBox.SelectedValuePath = "Key";

            UnitsPressureComboBox.ItemsSource = new List<KeyValuePair<string, string>>
            {
                new("mb", "Millibars"),
                new("inhg", "Inches Hg"),
                new("mmhg", "mm Hg"),
                new("hpa", "hPa")
            };
            UnitsPressureComboBox.DisplayMemberPath = "Value";
            UnitsPressureComboBox.SelectedValuePath = "Key";

            UnitsPrecipComboBox.ItemsSource = new List<KeyValuePair<string, string>>
            {
                new("mm", "Millimeters"),
                new("cm", "Centimeters"),
                new("in", "Inches")
            };
            UnitsPrecipComboBox.DisplayMemberPath = "Value";
            UnitsPrecipComboBox.SelectedValuePath = "Key";

            UnitsDistanceComboBox.ItemsSource = new List<KeyValuePair<string, string>>
            {
                new("km", "Kilometers"),
                new("mi", "Miles")
            };
            UnitsDistanceComboBox.DisplayMemberPath = "Value";
            UnitsDistanceComboBox.SelectedValuePath = "Key";

            var timeZones = TimeZoneInfo.GetSystemTimeZones()
                .Select(tz => new KeyValuePair<string, string>(tz.Id, tz.DisplayName))
                .OrderBy(tz => tz.Value)
                .ToList();
            TimeZoneComboBox.ItemsSource = timeZones;
            TimeZoneComboBox.DisplayMemberPath = "Value";
            TimeZoneComboBox.SelectedValuePath = "Key";
            TimeZoneComboBox.SelectedValue = TimeZoneInfo.Local.Id;
        }

        private void BuildFieldList()
        {
            RebuildFieldListForSelectedBucket();
        }

        private void RebuildFieldListForSelectedBucket()
        {
            if (_fields.Count > 0)
            {
                _standardSelectedFields = GetSelectedRealFieldNames();
            }

            var knownFields = BuildStandardKnownFields();

            _fields.Clear();
            _fields.AddRange(knownFields);

            // Place All/None in the last row, columns 3 and 4 (1-based).
            const int columns = 4;
            var remainder = _fields.Count % columns;
            var spacerCount = (2 - remainder + columns) % columns;
            for (var i = 0; i < spacerCount; i++)
            {
                _fields.Add(new ObservationField
                {
                    Name = $"{SpacerFieldPrefix}{i}",
                    DisplayName = string.Empty,
                    IsSelected = false
                });
            }

            var allField = new ObservationField { Name = AllFieldName, DisplayName = "All", IsSelected = false };
            var noneField = new ObservationField { Name = NoneFieldName, DisplayName = "None", IsSelected = false };
            _fields.Add(allField);
            _fields.Add(noneField);

            var preferred = _standardSelectedFields;
            foreach (var field in _fields)
            {
                if (string.Equals(field.Name, AllFieldName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(field.Name, NoneFieldName, StringComparison.OrdinalIgnoreCase) ||
                    field.Name.StartsWith(SpacerFieldPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    field.IsSelected = false;
                    continue;
                }

                field.IsSelected = preferred.Count == 0 || preferred.Contains(field.Name);
            }

            SyncControlFieldStates();
            FieldCheckList.ItemsSource = _fields;
            FieldCheckList.Items.Refresh();
        }

        private static List<ObservationField> BuildStandardKnownFields()
        {
            return new List<ObservationField>
            {
                new() { Name = "timestamp", DisplayName = "Timestamp" },
                new() { Name = "report_interval", DisplayName = "Report Interval" },
                new() { Name = "wind_lull", DisplayName = "Wind Lull" },
                new() { Name = "wind_avg", DisplayName = "Wind Average" },
                new() { Name = "wind_gust", DisplayName = "Wind Gust" },
                new() { Name = "wind_dir", DisplayName = "Wind Direction" },
                new() { Name = "station_pressure", DisplayName = "Station Pressure" },
                new() { Name = "sea_level_pressure", DisplayName = "Sea Level Pressure" },
                new() { Name = "air_temp", DisplayName = "Air Temperature" },
                new() { Name = "rh", DisplayName = "Relative Humidity" },
                new() { Name = "illuminance", DisplayName = "Illuminance" },
                new() { Name = "uv", DisplayName = "UV" },
                new() { Name = "solar_radiation", DisplayName = "Solar Radiation" },
                new() { Name = "precip_accumulation", DisplayName = "Precipitation Accumulation" },
                new() { Name = "precip_type", DisplayName = "Precipitation Type" },
                new() { Name = "strike_count", DisplayName = "Lightning Strike Count" },
                new() { Name = "strike_distance", DisplayName = "Lightning Average Distance" },
                new() { Name = "nc_precip_accumulation", DisplayName = "Nearcast Precip Accumulation" },
                    new() { Name = "local_day_precip_accumulation", DisplayName = "Local Day Precip Accumulation" },
                    new() { Name = "nc_local_day_precip_accumulation", DisplayName = "Local Day Nearcast Precip Accumulation" }
            };
        }

        private HashSet<string> GetSelectedRealFieldNames()
        {
            return _fields
                .Where(f => f.IsSelected &&
                            !string.Equals(f.Name, AllFieldName, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(f.Name, NoneFieldName, StringComparison.OrdinalIgnoreCase) &&
                            !f.Name.StartsWith(SpacerFieldPrefix, StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private async Task LoadSettingsAsync()
        {
            _isInitializing = true;
            try
            {
                _settings = await SettingsStore.LoadAsync();
                SetStatus($"Settings loaded. Station: {_settings.SelectedStationId ?? "none"}");
                await Dispatcher.InvokeAsync(() =>
                {
                    ApiKeyBox.Text = _settings.ApiKey;
                    BucketComboBox.SelectedValue = NormalizeBucketValue(_settings.Bucket);
                    UnitsTempComboBox.SelectedValue = _settings.UnitsTemp;
                    UnitsWindComboBox.SelectedValue = _settings.UnitsWind;
                    UnitsPressureComboBox.SelectedValue = _settings.UnitsPressure;
                    UnitsPrecipComboBox.SelectedValue = _settings.UnitsPrecip;
                    UnitsDistanceComboBox.SelectedValue = _settings.UnitsDistance;
                    PageSizeBox.Text = _settings.PageSize.ToString();
                    var tz = GetSelectedTimeZone();
                    if (_settings.StartTimeUtc.HasValue)
                    {
                        var startLocal = TimeZoneInfo.ConvertTimeFromUtc(_settings.StartTimeUtc.Value, tz);
                        StartDatePicker.SelectedDate = startLocal.Date;
                        StartTimeBox.Text = startLocal.ToString("HH:mm");
                    }
                    else
                    {
                        StartDatePicker.SelectedDate = null;
                        StartTimeBox.Text = "00:00";
                    }
                    if (_settings.EndTimeUtc.HasValue)
                    {
                        var endLocal = TimeZoneInfo.ConvertTimeFromUtc(_settings.EndTimeUtc.Value, tz);
                        EndDatePicker.SelectedDate = endLocal.Date;
                        EndTimeBox.Text = endLocal.ToString("HH:mm");
                    }
                    else
                    {
                        EndDatePicker.SelectedDate = null;
                        EndTimeBox.Text = "23:59";
                    }
                    UpdateTimeZoneLabels();

                    _standardSelectedFields = _settings.SelectedFields.ToHashSet(StringComparer.OrdinalIgnoreCase);

                    RebuildFieldListForSelectedBucket();
                });

                if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
                {
                    await LoadStationsAsync();
                }

                SetStatus("Settings loaded.");
            }
            catch (Exception ex)
            {
                SetStatus($"Could not load settings: {ex.Message}");
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                _settings.ApiKey = ApiKeyBox.Text.Trim();
                
                // Save the currently selected station ID
                var selectedStationId = StationComboBox.SelectedValue?.ToString();
                _settings.SelectedStationId = !string.IsNullOrWhiteSpace(selectedStationId) ? selectedStationId : null;
                
                _settings.Bucket = NormalizeBucketValue(BucketComboBox.SelectedValue?.ToString());
                _settings.UnitsTemp = UnitsTempComboBox.SelectedValue?.ToString() ?? "f";
                _settings.UnitsWind = UnitsWindComboBox.SelectedValue?.ToString() ?? "mph";
                _settings.UnitsPressure = UnitsPressureComboBox.SelectedValue?.ToString() ?? "inhg";
                _settings.UnitsPrecip = UnitsPrecipComboBox.SelectedValue?.ToString() ?? "in";
                _settings.UnitsDistance = UnitsDistanceComboBox.SelectedValue?.ToString() ?? "km";
                _settings.PageSize = int.TryParse(PageSizeBox.Text, out var pageSize) && pageSize > 0 ? pageSize : 250;
                _settings.StartTimeUtc = ParseDateTime(StartDatePicker, StartTimeBox);
                _settings.EndTimeUtc = ParseDateTime(EndDatePicker, EndTimeBox);
                _settings.SelectedFields = _fields
                    .Where(f => f.IsSelected &&
                                !string.Equals(f.Name, AllFieldName, StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(f.Name, NoneFieldName, StringComparison.OrdinalIgnoreCase) &&
                                !f.Name.StartsWith(SpacerFieldPrefix, StringComparison.OrdinalIgnoreCase))
                    .Select(f => f.Name)
                    .ToList();

                await SettingsStore.SaveAsync(_settings).ConfigureAwait(false);
                SetStatus($"Settings saved. Station: {_settings.SelectedStationId ?? "none"}");
            }
            catch (Exception ex)
            {
                SetStatus($"Could not save settings: {ex.Message}");
            }
        }

        private DateTime? ParseDateTime(DatePicker datePicker, TextBox timeBox)
        {
            var tz = GetSelectedTimeZone();
            return ParseDateTimeInZone(datePicker, timeBox, tz);
        }

        private static DateTime? ParseDateTimeInZone(DatePicker datePicker, TextBox timeBox, TimeZoneInfo timeZone)
        {
            if (!datePicker.SelectedDate.HasValue)
            {
                return null;
            }

            if (!TimeSpan.TryParseExact(timeBox.Text.Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out var time))
            {
                var unspecified = datePicker.SelectedDate.Value.Date;
                return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(unspecified, DateTimeKind.Unspecified), timeZone);
            }

            var localResult = DateTime.SpecifyKind(datePicker.SelectedDate.Value.Date.Add(time), DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(localResult, timeZone);
        }

        private TimeZoneInfo GetSelectedTimeZone()
        {
            var id = TimeZoneComboBox.SelectedValue?.ToString();
            if (string.IsNullOrWhiteSpace(id))
            {
                return TimeZoneInfo.Local;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch
            {
                return TimeZoneInfo.Local;
            }
        }

        private static string GetTimeZoneAbbreviation(TimeZoneInfo tz)
        {
            // Use the standard or daylight abbreviation from the display name, e.g. "(UTC-05:00) Eastern Standard Time" -> "EST"
            var name = tz.IsDaylightSavingTime(DateTime.Now) ? tz.DaylightName : tz.StandardName;
            // Build abbreviation from initials of each word, max 5 chars
            var abbr = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => char.IsLetter(w[0]))
                .Select(w => w[0]));
            return abbr.Length > 0 ? abbr : name;
        }

        private async void RefreshStationsButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadStationsAsync().ConfigureAwait(false);
        }

        private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveSettingsAsync().ConfigureAwait(false);
        }

        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("Loading observations...");
            await LoadObservationsAsync();
        }

        private async void ExportCsvButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportToCsvAsync().ConfigureAwait(false);
        }

        private async void ExportJsonButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportToJsonAsync().ConfigureAwait(false);
        }

        private async void ExportExcelButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportToExcelAsync().ConfigureAwait(false);
        }

        private async void ExportPdfButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportToPdfAsync().ConfigureAwait(false);
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            _exportContextMenu ??= BuildExportContextMenu();
            _exportContextMenu.PlacementTarget = button;
            _exportContextMenu.IsOpen = true;
        }

        private ContextMenu BuildExportContextMenu()
        {
            var menu = new ContextMenu();

            var csvItem = new MenuItem { Header = "CSV (.csv)" };
            csvItem.Click += async (_, _) => await ExportToCsvAsync().ConfigureAwait(false);

            var jsonItem = new MenuItem { Header = "JSON (.json)" };
            jsonItem.Click += async (_, _) => await ExportToJsonAsync().ConfigureAwait(false);

            var excelItem = new MenuItem { Header = "Excel (.xlsx)" };
            excelItem.Click += async (_, _) => await ExportToExcelAsync().ConfigureAwait(false);

            var pdfItem = new MenuItem { Header = "PDF (.pdf)" };
            pdfItem.Click += async (_, _) => await ExportToPdfAsync().ConfigureAwait(false);

            menu.Items.Add(csvItem);
            menu.Items.Add(jsonItem);
            menu.Items.Add(excelItem);
            menu.Items.Add(pdfItem);

            return menu;
        }

        private void KoFiButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://ko-fi.com/michaelbeatty9142002",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                SetStatus($"Could not open Ko-fi link: {ex.Message}");
            }
        }

        private async void StationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            var selectedStationId = StationComboBox.SelectedValue?.ToString();
            if (!string.IsNullOrWhiteSpace(selectedStationId))
            {
                _settings.SelectedStationId = selectedStationId;
                await SaveSettingsAsync().ConfigureAwait(false);
            }
        }

        private async void UnitSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            await SaveSettingsAsync().ConfigureAwait(false);
        }

        private async void BucketComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RebuildFieldListForSelectedBucket();

            if (_isInitializing)
            {
                return;
            }

            await SaveSettingsAsync().ConfigureAwait(false);
        }

        private void TimeZoneComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateTimeZoneLabels();

            if (_isInitializing || _observationTableUtc.Rows.Count == 0)
            {
                return;
            }

            Dispatcher.Invoke(ApplySelectedTimeZoneToObservationTable);
        }

        private void UpdateTimeZoneLabels()
        {
            var tz = GetSelectedTimeZone();
            var abbr = GetTimeZoneAbbreviation(tz);
            StartLabel.Text = $"Start {abbr}";
            EndLabel.Text = $"End {abbr}";
        }

        private void FieldCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isFieldSelectionUpdating)
            {
                return;
            }

            if (sender is not CheckBox checkBox || checkBox.DataContext is not ObservationField changedField)
            {
                return;
            }

            _isFieldSelectionUpdating = true;
            try
            {
                if (string.Equals(changedField.Name, AllFieldName, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var field in _fields)
                    {
                        if (field.Name.StartsWith(SpacerFieldPrefix, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(field.Name, NoneFieldName, StringComparison.OrdinalIgnoreCase))
                        {
                            field.IsSelected = false;
                        }
                        else
                        {
                            field.IsSelected = true;
                        }
                    }
                }
                else if (string.Equals(changedField.Name, NoneFieldName, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var field in _fields)
                    {
                        if (string.Equals(field.Name, NoneFieldName, StringComparison.OrdinalIgnoreCase))
                        {
                            field.IsSelected = true;
                        }
                        else if (field.Name.StartsWith(SpacerFieldPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            field.IsSelected = false;
                        }
                        else
                        {
                            field.IsSelected = false;
                        }
                    }
                }
                else
                {
                    // Any real-field change should immediately reflect in All/None controls.
                    SyncControlFieldStates();
                }

                FieldCheckList.Items.Refresh();
            }
            finally
            {
                _isFieldSelectionUpdating = false;
            }
        }

        private void SyncControlFieldStates()
        {
            var allField = _fields.FirstOrDefault(f => string.Equals(f.Name, AllFieldName, StringComparison.OrdinalIgnoreCase));
            var noneField = _fields.FirstOrDefault(f => string.Equals(f.Name, NoneFieldName, StringComparison.OrdinalIgnoreCase));
            var realFields = _fields.Where(f =>
                !string.Equals(f.Name, AllFieldName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(f.Name, NoneFieldName, StringComparison.OrdinalIgnoreCase) &&
                !f.Name.StartsWith(SpacerFieldPrefix, StringComparison.OrdinalIgnoreCase)).ToList();

            var allRealSelected = realFields.Count > 0 && realFields.All(f => f.IsSelected);
            var anyRealSelected = realFields.Any(f => f.IsSelected);

            if (allField != null)
            {
                allField.IsSelected = allRealSelected;
            }

            if (noneField != null)
            {
                noneField.IsSelected = !anyRealSelected;
            }
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void ApiKeyHelpMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                this,
                "To create a Tempest API token:\n\n" +
                "1. Go to https://tempestwx.com/.\n" +
                "2. Log in.\n" +
                "3. Click on Settings.\n" +
                "4. Scroll down to Data Authorizations.\n" +
                "5. Click Create Token.",
                "API Key Help",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void HelpAboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var version = GetCurrentAppVersion();
            MessageBox.Show(this, $"Tempest Weather Station Viewer\nVersion: {version}\n\nUse File > Exit to close the app.\nUse Station > Refresh Station to reload your station list.", "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void CheckUpdatesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_isCheckingUpdates)
            {
                return;
            }

            _isCheckingUpdates = true;
            try
            {
                SetStatus("Checking for updates...");
                var currentVersion = GetCurrentAppVersion();
                var result = await _releaseChecker.CheckForUpdatesAsync(currentVersion);

                if (!result.IsSuccess)
                {
                    SetStatus("Update check failed.");
                    MessageBox.Show(this, result.ErrorMessage ?? "Unable to check for updates right now.", "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (result.IsPrerelease)
                {
                    SetStatus("Latest GitHub release is marked as prerelease and was ignored.");
                    MessageBox.Show(this, "The latest GitHub release is marked as prerelease and was skipped.\n\nStable updates only are enabled.", "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (!result.IsUpdateAvailable)
                {
                    SetStatus($"You are up to date (v{result.CurrentVersion}).");
                    MessageBox.Show(this, $"You are up to date.\n\nInstalled version: v{result.CurrentVersion}\nLatest version: v{result.LatestVersion}", "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                SetStatus($"Update available: v{result.LatestVersion}");
                var message =
                    $"A new version is available.\n\nInstalled version: v{result.CurrentVersion}\nLatest version: v{result.LatestVersion}\n\nOpen the release page to download it?";
                var choice = MessageBox.Show(this, message, "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (choice == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = result.ReleaseUrl,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                SetStatus("Update check failed.");
                MessageBox.Show(this, $"Unable to check for updates.\n\n{ex.Message}", "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _isCheckingUpdates = false;
            }
        }

        private static string GetCurrentAppVersion()
        {
            var assembly = typeof(MainWindow).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                var plusIndex = informational.IndexOf('+');
                return plusIndex > 0 ? informational[..plusIndex] : informational;
            }

            var version = assembly.GetName().Version;
            if (version == null)
            {
                return "unknown";
            }

            var build = version.Build >= 0 ? version.Build : 0;
            return $"{version.Major}.{version.Minor}.{build}";
        }

        private async Task LoadStationsAsync()
        {
            try
            {
                SetBusy(true, "Discovering stations...");
                var apiKey = _settings.ApiKey?.Trim() ?? string.Empty;
                var stations = await _apiClient.GetStationsAsync(apiKey).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    StationComboBox.ItemsSource = stations;
                    StationComboBox.DisplayMemberPath = "Name";
                    StationComboBox.SelectedValuePath = "StationId";
                    
                    if (!string.IsNullOrWhiteSpace(_settings.SelectedStationId))
                    {
                        var matchingStation = stations.FirstOrDefault(s =>
                            string.Equals(s.StationId, _settings.SelectedStationId, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(s.DeviceId?.ToString(), _settings.SelectedStationId, StringComparison.OrdinalIgnoreCase));
                        if (matchingStation != null)
                        {
                            StationComboBox.SelectedItem = matchingStation;
                        }
                    }

                    if (StationComboBox.SelectedItem == null && stations.Count > 0)
                    {
                        StationComboBox.SelectedItem = stations[0];
                    }

                    var selectedStation = StationComboBox.SelectedItem as TempestStation ?? stations.FirstOrDefault();
                    if (selectedStation != null)
                    {
                        SetStatus($"Discovered {stations.Count} station(s). Selected '{selectedStation.Name}' ({selectedStation.StationId}).");
                    }
                    else
                    {
                        SetStatus("No stations found for that API token.");
                    }
                });
            }
            catch (Exception ex)
            {
                SetStatus($"Station discovery failed: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task LoadObservationsAsync()
        {
            try
            {
                var apiKey = ApiKeyBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    SetStatus("Enter your Tempest API token before loading observations.");
                    return;
                }

                var stationId = StationComboBox.SelectedValue?.ToString();
                if (string.IsNullOrWhiteSpace(stationId) && StationComboBox.SelectedItem is TempestStation selectedStation)
                {
                    stationId = selectedStation.StationId;
                }

                if (string.IsNullOrWhiteSpace(stationId))
                {
                    var stations = StationComboBox.ItemsSource as IList<TempestStation>;
                    if (stations?.Count > 0)
                    {
                        stationId = stations[0].StationId;
                        await Dispatcher.InvokeAsync(() => StationComboBox.SelectedItem = stations[0]);
                    }
                }

                if (string.IsNullOrWhiteSpace(stationId))
                {
                    var count = (StationComboBox.ItemsSource as IList<TempestStation>)?.Count ?? 0;
                    SetStatus(count > 0
                        ? "A station is listed but it has no station ID. Refresh stations again or select another station."
                        : "No station is selected. Refresh stations to populate the list first.");
                    return;
                }

                var fields = _fields.Where(f => f.IsSelected).Select(f => f.Name).ToList();
                fields = fields
                    .Where(name =>
                        !string.Equals(name, AllFieldName, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(name, NoneFieldName, StringComparison.OrdinalIgnoreCase) &&
                        !name.StartsWith(SpacerFieldPrefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var obsFields = string.Join(",", fields);

                var startDate = ParseDateTime(StartDatePicker, StartTimeBox);
                var endDate = ParseDateTime(EndDatePicker, EndTimeBox);
                var selectedBucket = NormalizeBucketValue(BucketComboBox.SelectedValue?.ToString());

                if (TryBuildBucketRangeWarning(selectedBucket, startDate, endDate, out var rangeWarning))
                {
                    var proceed = MessageBox.Show(
                        this,
                        rangeWarning,
                        "Requested Range Warning",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (proceed != MessageBoxResult.Yes)
                    {
                        SetStatus("Load cancelled by user.");
                        return;
                    }
                }

                SetBusy(true, "Loading observations...");
                await SaveSettingsAsync();

                _observationTable = await _apiClient.GetObservationsAsync(
                    apiKey,
                    stationId,
                    selectedBucket,
                    UnitsTempComboBox.SelectedValue?.ToString() ?? "c",
                    UnitsWindComboBox.SelectedValue?.ToString() ?? "mps",
                    UnitsPressureComboBox.SelectedValue?.ToString() ?? "mb",
                    UnitsPrecipComboBox.SelectedValue?.ToString() ?? "mm",
                    UnitsDistanceComboBox.SelectedValue?.ToString() ?? "km",
                    obsFields,
                    startDate,
                    endDate,
                    _settings.PageSize)
                    .ConfigureAwait(false);

                // DEBUG: Log what was returned
                var columnNames = string.Join(", ", _observationTable.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
                var debugInfo = $"Requested fields: {obsFields}\nReturned columns: {columnNames}";
                if (_observationTable.Rows.Count > 0)
                {
                    var firstRow = _observationTable.Rows[0];
                    var values = string.Join(" | ", _observationTable.Columns.Cast<DataColumn>().Select(c => $"{c.ColumnName}={firstRow[c.ColumnName]}"));
                    debugInfo += $"\nFirst row: {values}";
                }
                System.Diagnostics.Debug.WriteLine("\n=== DEBUG INFO ===\n" + debugInfo);

                _observationTable = FilterObservationTableToSelectedFields(_observationTable, fields);
                _observationTable = NormalizeNumericColumns(_observationTable);

                _observationTableUtc = _observationTable.Copy();

                await Dispatcher.InvokeAsync(() =>
                {
                    ApplySelectedTimeZoneToObservationTable();
                });

                if (_observationTable.Rows.Count == 0)
                {
                    SetStatus($"No observations were returned for station {stationId}. Verify the station, API token, and selected date range ({startDate:yyyy-MM-dd HH:mm} to {endDate:yyyy-MM-dd HH:mm} UTC).");
                }
                else
                {
                    SetStatus($"Loaded {_observationTable.Rows.Count} observation row(s) for station {stationId}.\nRemember: exports include only visible fields.");
                }

                UpdateExportButtonState();
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to load observations: {ex.Message}");
                UpdateExportButtonState();
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task ExportToCsvAsync()
        {
            if (_observationTable.Rows.Count == 0)
            {
                SetStatus("No observation data is loaded to export.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = "csv",
                FileName = "TempestObservations.csv"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            await Task.Run(() => WriteCsv(dialog.FileName, _observationTable)).ConfigureAwait(false);
            SetStatus($"CSV exported to {dialog.FileName}");
        }

        private async Task ExportToJsonAsync()
        {
            if (_observationTable.Rows.Count == 0)
            {
                SetStatus("No observation data is loaded to export.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json",
                FileName = "TempestObservations.json"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var rows = _observationTable.Rows.Cast<DataRow>()
                .Select(r => _observationTable.Columns.Cast<DataColumn>()
                    .ToDictionary(c => c.ColumnName, c => r[c]?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                .ToList();

            var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(dialog.FileName, json).ConfigureAwait(false);
            SetStatus($"JSON exported to {dialog.FileName}");
        }

        private async Task ExportToExcelAsync()
        {
            if (_observationTable.Rows.Count == 0)
            {
                SetStatus("No observation data is loaded to export.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                DefaultExt = "xlsx",
                FileName = "TempestObservations.xlsx"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            await Task.Run(() => WriteExcel(dialog.FileName, _observationTable)).ConfigureAwait(false);
            SetStatus($"Excel exported to {dialog.FileName}");
        }

        private async Task ExportToPdfAsync()
        {
            if (_observationTable.Rows.Count == 0)
            {
                SetStatus("No observation data is loaded to export.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
                DefaultExt = "pdf",
                FileName = "TempestObservations.pdf"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            await Task.Run(() => WritePdf(dialog.FileName, _observationTable)).ConfigureAwait(false);
            SetStatus($"PDF exported to {dialog.FileName}");
        }

        private static void WriteCsv(string path, DataTable table)
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            writer.WriteLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(c => EscapeCsv(c.ColumnName))));
            foreach (DataRow row in table.Rows)
            {
                writer.WriteLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(c => EscapeCsv(row[c]?.ToString() ?? string.Empty))));
            }
        }

        private static string EscapeCsv(string input)
        {
            if (input.Contains(',') || input.Contains('"') || input.Contains('\n') || input.Contains('\r'))
            {
                return '"' + input.Replace("\"", "\"\"") + '"';
            }
            return input;
        }

        private static void WriteExcel(string path, DataTable table)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Observations");
            for (var col = 0; col < table.Columns.Count; col++)
            {
                worksheet.Cell(1, col + 1).Value = table.Columns[col].ColumnName;
            }

            for (var row = 0; row < table.Rows.Count; row++)
            {
                for (var col = 0; col < table.Columns.Count; col++)
                {
                    worksheet.Cell(row + 2, col + 1).Value = table.Rows[row][col]?.ToString();
                }
            }

            workbook.SaveAs(path);
        }

        private static void WritePdf(string path, DataTable table)
        {
            using var document = new PdfDocument();
            var page = document.AddPage();
            var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Verdana", 9);
            var lineHeight = font.GetHeight();
            var margin = 40.0;
            var x = margin;
            var y = margin;
            var columnWidths = new List<double>();

            foreach (DataColumn col in table.Columns)
            {
                columnWidths.Add(100);
            }

            for (var colIndex = 0; colIndex < table.Columns.Count; colIndex++)
            {
                gfx.DrawString(table.Columns[colIndex].ColumnName, font, XBrushes.Black, new XRect(x, y, columnWidths[colIndex], lineHeight), XStringFormats.TopLeft);
                x += columnWidths[colIndex];
            }

            y += lineHeight + 6;
            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                x = margin;
                if (y + lineHeight + 6 > page.Height - margin)
                {
                    page = document.AddPage();
                    gfx = XGraphics.FromPdfPage(page);
                    y = margin;
                }

                for (var colIndex = 0; colIndex < table.Columns.Count; colIndex++)
                {
                    gfx.DrawString(table.Rows[rowIndex][colIndex]?.ToString() ?? string.Empty, font, XBrushes.Black, new XRect(x, y, columnWidths[colIndex], lineHeight), XStringFormats.TopLeft);
                    x += columnWidths[colIndex];
                }

                y += lineHeight + 4;
            }

            document.Save(path);
        }

        private void ApplySelectedTimeZoneToObservationTable()
        {
            if (_observationTableUtc.Rows.Count == 0)
            {
                ObservationDataGrid.ItemsSource = _observationTable.DefaultView;
                return;
            }

            var timeZoneId = TimeZoneComboBox.SelectedValue?.ToString();
            TimeZoneInfo timeZone;
            try
            {
                timeZone = !string.IsNullOrWhiteSpace(timeZoneId)
                    ? TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)
                    : TimeZoneInfo.Local;
            }
            catch (TimeZoneNotFoundException)
            {
                timeZone = TimeZoneInfo.Local;
            }
            catch (InvalidTimeZoneException)
            {
                timeZone = TimeZoneInfo.Local;
            }

            var convertedTable = _observationTableUtc.Copy();
            var timestampColumn = convertedTable.Columns
                .Cast<DataColumn>()
                .FirstOrDefault(c => string.Equals(c.ColumnName, "timestamp", StringComparison.OrdinalIgnoreCase));

            if (timestampColumn != null)
            {
                foreach (DataRow row in convertedTable.Rows)
                {
                    var raw = row[timestampColumn]?.ToString();
                    if (!TryParseUtcTimestamp(raw, out var utcTimestamp))
                    {
                        continue;
                    }

                    var converted = TimeZoneInfo.ConvertTimeFromUtc(utcTimestamp, timeZone);
                    row[timestampColumn] = converted.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                }
            }

            _observationTable = convertedTable;
            ObservationDataGrid.ItemsSource = _observationTable.DefaultView;
        }

        private static bool TryParseUtcTimestamp(string? value, out DateTime utcTimestamp)
        {
            utcTimestamp = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixValue))
            {
                var abs = Math.Abs(unixValue);
                utcTimestamp = abs > 9999999999
                    ? DateTimeOffset.FromUnixTimeMilliseconds(unixValue).UtcDateTime
                    : DateTimeOffset.FromUnixTimeSeconds(unixValue).UtcDateTime;
                return true;
            }

            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
            {
                utcTimestamp = dto.UtcDateTime;
                return true;
            }

            return false;
        }

        private static string NormalizeBucketValue(string? bucketValue)
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
                "1" => "1",
                "5" => "5",
                "30" => "30",
                "180" => "180",
                _ => "1"
            };
        }

        private static bool TryGetBucketDefinition(string bucketValue, out int stepMinutes, out int? maxRangeDays, out string label)
        {
            stepMinutes = 1;
            maxRangeDays = 1;
            label = "1 minute";

            switch (NormalizeBucketValue(bucketValue))
            {
                case "1":
                    stepMinutes = 1;
                    maxRangeDays = 1;
                    label = "1 minute";
                    return true;
                case "5":
                    stepMinutes = 5;
                    maxRangeDays = 5;
                    label = "5 minutes";
                    return true;
                case "30":
                    stepMinutes = 30;
                    maxRangeDays = 30;
                    label = "30 minutes";
                    return true;
                case "180":
                    stepMinutes = 180;
                    maxRangeDays = 180;
                    label = "180 minutes";
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryBuildBucketRangeWarning(string bucketValue, DateTime? startUtc, DateTime? endUtc, out string warningMessage)
        {
            warningMessage = string.Empty;
            if (!startUtc.HasValue || !endUtc.HasValue)
            {
                return false;
            }

            if (!TryGetBucketDefinition(bucketValue, out _, out var maxRangeDays, out var label) || !maxRangeDays.HasValue)
            {
                return false;
            }

            var start = startUtc.Value;
            var end = endUtc.Value;

            // No observations can exist in the future; cap the range at current UTC time.
            var nowUtc = DateTime.UtcNow;
            if (end > nowUtc)
            {
                end = nowUtc;
            }

            if (start > nowUtc)
            {
                return false;
            }

            if (end < start)
            {
                (start, end) = (end, start);
            }

            var requestedSpan = end - start;
            if (requestedSpan <= TimeSpan.FromDays(maxRangeDays.Value))
            {
                return false;
            }

            warningMessage =
                $"The selected resolution ({label}) supports a maximum range of {maxRangeDays.Value} day(s).\n\n" +
                $"Requested range: {requestedSpan.TotalDays:F1} day(s).\n\n" +
                "The API may reject or adjust this request. Continue anyway?";

            return true;
        }

        private static DataTable FilterObservationTableToSelectedFields(DataTable source, IList<string> selectedFields)
        {
            if (source.Columns.Count == 0 || selectedFields.Count == 0)
            {
                return source;
            }

            var selectedSet = new HashSet<string>(selectedFields, StringComparer.OrdinalIgnoreCase);
            var orderedSelectedColumns = source.Columns
                .Cast<DataColumn>()
                .Where(c => selectedSet.Contains(c.ColumnName))
                .Select(c => c.ColumnName)
                .ToList();

            if (orderedSelectedColumns.Count == 0)
            {
                return source;
            }

            var filtered = new DataTable(source.TableName);
            foreach (var columnName in orderedSelectedColumns)
            {
                filtered.Columns.Add(columnName, source.Columns[columnName]?.DataType ?? typeof(string));
            }

            foreach (DataRow sourceRow in source.Rows)
            {
                var newRow = filtered.NewRow();
                foreach (var columnName in orderedSelectedColumns)
                {
                    newRow[columnName] = sourceRow[columnName];
                }

                filtered.Rows.Add(newRow);
            }

            return filtered;
        }

        private static DataTable NormalizeNumericColumns(DataTable source)
        {
            if (source.Columns.Count == 0)
            {
                return source;
            }

            var numericColumnMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

            foreach (DataColumn column in source.Columns)
            {
                // Keep timestamp as text because it is later transformed into a local time string.
                if (string.Equals(column.ColumnName, "timestamp", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var hasValue = false;
                var allNumeric = true;
                var allInteger = true;

                foreach (DataRow row in source.Rows)
                {
                    var raw = row[column]?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        continue;
                    }

                    hasValue = true;

                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    {
                        allNumeric = false;
                        break;
                    }

                    if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    {
                        allInteger = false;
                    }
                }

                if (!hasValue || !allNumeric)
                {
                    continue;
                }

                numericColumnMap[column.ColumnName] = allInteger ? typeof(long) : typeof(double);
            }

            if (numericColumnMap.Count == 0)
            {
                return source;
            }

            var normalized = new DataTable(source.TableName);
            foreach (DataColumn sourceColumn in source.Columns)
            {
                var type = numericColumnMap.TryGetValue(sourceColumn.ColumnName, out var numericType)
                    ? numericType
                    : typeof(string);
                normalized.Columns.Add(sourceColumn.ColumnName, type);
            }

            foreach (DataRow sourceRow in source.Rows)
            {
                var newRow = normalized.NewRow();
                foreach (DataColumn column in source.Columns)
                {
                    var raw = sourceRow[column]?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        newRow[column.ColumnName] = DBNull.Value;
                        continue;
                    }

                    if (numericColumnMap.TryGetValue(column.ColumnName, out var numericType))
                    {
                        if (numericType == typeof(long) && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
                        {
                            newRow[column.ColumnName] = longValue;
                            continue;
                        }

                        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
                        {
                            newRow[column.ColumnName] = doubleValue;
                            continue;
                        }
                    }

                    newRow[column.ColumnName] = raw;
                }

                normalized.Rows.Add(newRow);
            }

            return normalized;
        }

        private void SetBusy(bool isBusy, string? message = null)
        {
            _isBusy = isBusy;
            Dispatcher.Invoke(() =>
            {
                LoadButton.IsEnabled = !isBusy;
                SaveSettingsButton.IsEnabled = !isBusy;
                if (message != null)
                {
                    StatusTextBlock.Text = message;
                }

                UpdateExportButtonState();
            });
        }

        private void UpdateExportButtonState()
        {
            Dispatcher.Invoke(() =>
            {
                ExportButton.IsEnabled = !_isBusy && _observationTable.Rows.Count > 0;
            });
        }

        private void SetStatus(string text)
        {
            Dispatcher.Invoke(() => StatusTextBlock.Text = text);
        }
    }
}
