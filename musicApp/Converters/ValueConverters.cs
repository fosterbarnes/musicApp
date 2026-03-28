using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;
using musicApp.Helpers;

namespace musicApp.Converters
{
    /// <summary>
    /// Converts slider value, maximum, and width to a percentage-based width for visual feedback
    /// </summary>
    public class SliderValueToWidthMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values.Length >= 3 && values[0] is double value && values[1] is double maximum && values[2] is double width)
            {
                if (maximum > 0)
                {
                    return (value / maximum) * width;
                }
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a single slider value to a percentage-based width
    /// </summary>
    public class SliderValueToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is double sliderValue && parameter is double maxWidth)
            {
                return (sliderValue / 100.0) * maxWidth;
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts DateTime to formatted date string
    /// </summary>
    public class DateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dateTime)
            {
                if (dateTime == DateTime.MinValue)
                    return "";
                
                // Use short date format
                return dateTime.ToString("M/d/yyyy", culture);
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts nullable DateTime to formatted date string
    /// </summary>
    public class NullableDateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dateTime)
            {
                return dateTime.ToString("M/d/yyyy", culture);
            }
            
            var nullableDate = value as DateTime?;
            if (nullableDate.HasValue)
            {
                return nullableDate.Value.ToString("M/d/yyyy", culture);
            }
            
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts file size (long) to human-readable string (KB, MB, GB)
    /// </summary>
    public class FileSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long fileSize)
            {
                if (fileSize == 0)
                    return "";

                string[] sizes = { "B", "KB", "MB", "GB", "TB" };
                double len = fileSize;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }

                return $"{len:0.##} {sizes[order]}";
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts integer to string, showing empty string for zero
    /// </summary>
    public class IntConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue)
            {
                return intValue == 0 ? "" : intValue.ToString(culture);
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts bool to Visibility: true -> Visible, false -> Collapsed.
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is System.Windows.Visibility v && v == System.Windows.Visibility.Visible;
        }
    }

    /// <summary>
    /// Converts bool (IsPinned) to PackIconKind: true -> Star, false -> StarOutline.
    /// </summary>
    public class PinnedToStarIconKindConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? PackIconKind.Star : PackIconKind.StarOutline;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts Song or AlbumSearchItem to album art BitmapImage for search popup thumbnails.
    /// </summary>
    public class AlbumArtThumbnailConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Song song)
                return AlbumArtThumbnailHelper.LoadForTrack(song);
            if (value is AlbumSearchItem album && album.Songs.Count > 0)
                return AlbumArtThumbnailHelper.LoadForTrack(album.Songs[0]);
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class AlternatingRowBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int trackNumber)
            {
                bool invert = parameter is string p &&
                    p.Equals("invert", StringComparison.OrdinalIgnoreCase);
                bool isEven = trackNumber % 2 == 0;
                if (invert)
                    isEven = !isEven;

                return isEven
                    ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1C1C1C"))
                    : new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#151515"));
            }
            return System.Windows.Data.Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Returns true when two string values are equal (case-insensitive).
    /// Useful for comparing a row's data (e.g. Song.FilePath) to a selected value.
    /// </summary>
    public class StringEqualsMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return false;

            var a = values[0]?.ToString();
            var b = values[1]?.ToString();

            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;

            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class HalfWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double width && width > 0)
                return width / 2.0;
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class CrossfadeStartSecondsLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not double d)
                return "";

            var n = (int)Math.Round(d);
            var unit = n == 1 ? "second" : "seconds";
            return $"{n.ToString(culture)} {unit} before the next song";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ListViewItemQueueOrderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ListViewItem item && parameter is ListView lv)
            {
                int i = lv.ItemContainerGenerator.IndexFromContainer(item);
                if (i >= 0)
                    return (i + 1).ToString(culture);
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 