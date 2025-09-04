using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace OpcDaScheduler.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private UserControl? _currentView;
        public UserControl? CurrentView
        {
            get => _currentView;
            set
            {
                if (!ReferenceEquals(_currentView, value))
                {
                    _currentView = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _currentTitle = "OPC-клиент";
        public string CurrentTitle
        {
            get => _currentTitle;
            set
            {
                var v = value ?? string.Empty;
                if (_currentTitle != v)
                {
                    _currentTitle = v;
                    OnPropertyChanged();
                }
            }
        }

        private string _currentSubtitle = "Подключение и выбор тегов";
        public string CurrentSubtitle
        {
            get => _currentSubtitle;
            set
            {
                var v = value ?? string.Empty;
                if (_currentSubtitle != v)
                {
                    _currentSubtitle = v;
                    OnPropertyChanged();
                }
            }
        }

        // Подсветка выбранной кнопки
        private bool _isOpc;
        public bool IsOpcSelected
        {
            get => _isOpc;
            set
            {
                if (_isOpc != value)
                {
                    _isOpc = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isHourly;
        public bool IsHourlySelected
        {
            get => _isHourly;
            set
            {
                if (_isHourly != value)
                {
                    _isHourly = value;
                    OnPropertyChanged();
                }
            }
        }

        public void Navigate(UserControl view, string title, string subtitle)
        {
            CurrentView = view;
            CurrentTitle = title;
            CurrentSubtitle = subtitle;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
