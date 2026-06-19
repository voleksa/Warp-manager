using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using WarpManager.Services;

namespace WarpManager.Converters;

public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is WarpStatus s ? s switch
        {
            WarpStatus.Connected        => Brushes.LimeGreen,
            WarpStatus.Connecting       => Brushes.Gold,
            WarpStatus.Disconnected     => Brushes.OrangeRed,
            WarpStatus.ServiceNotRunning=> Brushes.Gray,
            _                           => Brushes.Gray,
        } : Brushes.Gray;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
