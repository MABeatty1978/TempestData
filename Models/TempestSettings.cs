using System;
using System.Collections.Generic;

namespace TempestData.Models
{
    public class TempestSettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public string? SelectedStationId { get; set; }
        public string Bucket { get; set; } = "1";
        public string UnitsTemp { get; set; } = "f";
        public string UnitsWind { get; set; } = "mph";
        public string UnitsPressure { get; set; } = "inhg";
        public string UnitsPrecip { get; set; } = "in";
        public string UnitsDistance { get; set; } = "km";
        public List<string> SelectedFields { get; set; } = new List<string>();
        public int PageSize { get; set; } = 250;
        public DateTime? StartTimeUtc { get; set; }
        public DateTime? EndTimeUtc { get; set; }
    }
}
