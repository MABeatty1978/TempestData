using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using Xunit;

namespace TempestData.Tests;

public class ObservationDisplayPipelineTests
{
    [Fact]
    public void DisplayPipeline_SelectedFieldsAndTimezoneConversion_ProducesExpectedGridColumnsAndValues()
    {
        RunOnStaThread(() =>
        {
            var window = new TempestData.MainWindow();

            var source = new DataTable("Observations");
            source.Columns.Add("timestamp", typeof(string));
            source.Columns.Add("air_temperature", typeof(string));
            source.Columns.Add("wind_avg", typeof(string));
            source.Columns.Add("pressure", typeof(string));

            source.Rows.Add("1710000000", "72.5", "5.4", "30.12");
            source.Rows.Add("1710000060", "72.4", "5.8", "30.11");

            var selectedFields = new List<string> { "timestamp", "wind_avg", "air_temperature" };
            var filtered = InvokeFilterSelectedFields(source, selectedFields);

            SetPrivateField(window, "_observationTable", filtered.Copy());
            SetPrivateField(window, "_observationTableUtc", filtered.Copy());

            var timeZoneComboBox = GetNamedField<ComboBox>(window, "TimeZoneComboBox");
            timeZoneComboBox.SelectedValue = TimeZoneInfo.Utc.Id;

            InvokeInstanceMethod(window, "ApplySelectedTimeZoneToObservationTable");

            var grid = GetNamedField<DataGrid>(window, "ObservationDataGrid");
            var view = Assert.IsType<DataView>(grid.ItemsSource);

            Assert.NotNull(view.Table);
            Assert.Equal(2, view.Table.Rows.Count);
            Assert.Equal(3, view.Table.Columns.Count);
            Assert.Equal("timestamp", view.Table.Columns[0].ColumnName);
            Assert.Equal("air_temperature", view.Table.Columns[1].ColumnName);
            Assert.Equal("wind_avg", view.Table.Columns[2].ColumnName);

            var expectedTs0 = DateTimeOffset.FromUnixTimeSeconds(1710000000).UtcDateTime
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var expectedTs1 = DateTimeOffset.FromUnixTimeSeconds(1710000060).UtcDateTime
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            Assert.Equal(expectedTs0, view[0]["timestamp"]?.ToString());
            Assert.Equal(expectedTs1, view[1]["timestamp"]?.ToString());
            Assert.Equal("5.4", view[0]["wind_avg"]?.ToString());
            Assert.Equal("72.5", view[0]["air_temperature"]?.ToString());
        });
    }

    [Fact]
    public void DisplayPipeline_MissingSelectedFields_DoesNotDropAvailableColumns()
    {
        RunOnStaThread(() =>
        {
            var source = new DataTable("Observations");
            source.Columns.Add("timestamp", typeof(string));
            source.Columns.Add("pressure", typeof(string));
            source.Rows.Add("1710000000", "30.12");

            var selectedFields = new List<string> { "timestamp", "air_temperature" };
            var filtered = InvokeFilterSelectedFields(source, selectedFields);

            // The filter keeps only selected fields that actually exist in source.
            Assert.True(filtered.Columns.Contains("timestamp"));
            Assert.False(filtered.Columns.Contains("air_temperature"));
            Assert.Single(filtered.Columns.Cast<DataColumn>());
            Assert.Equal("1710000000", filtered.Rows[0]["timestamp"]?.ToString());
        });
    }

    private static DataTable InvokeFilterSelectedFields(DataTable source, IList<string> selectedFields)
    {
        var method = typeof(TempestData.MainWindow).GetMethod(
            "FilterObservationTableToSelectedFields",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var result = method!.Invoke(null, new object[] { source, selectedFields });
        return Assert.IsType<DataTable>(result);
    }

    private static void InvokeInstanceMethod(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        method!.Invoke(instance, null);
    }

    private static T GetNamedField<T>(object instance, string name) where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(field);
        var value = field!.GetValue(instance);
        return Assert.IsType<T>(value);
    }

    private static void SetPrivateField(object instance, string name, object value)
    {
        var field = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (captured != null)
        {
            ExceptionDispatchInfo.Capture(captured).Throw();
        }
    }
}
