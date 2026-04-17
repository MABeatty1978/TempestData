using System.ComponentModel;

namespace TempestData.Models
{
    public class ObservationField : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
