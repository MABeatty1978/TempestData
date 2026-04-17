namespace TempestData.Models
{
    public class TempestStation
    {
        public string StationId { get; set; } = string.Empty;
        public int? DeviceId { get; set; }
        public string DeviceType { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public override string ToString() => string.IsNullOrWhiteSpace(Name) ? StationId : $"{Name} ({StationId})";
    }
}
