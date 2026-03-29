using System;
using System.Globalization;
using System.Windows.Data;

namespace musicApp.Views;

public sealed class FlyoutTrackSelectedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3 || values[0] is not musicApp.Song song || values[1] is not AlbumsView view)
            return false;
        return view.IsFlyoutTrackSelected(song);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
