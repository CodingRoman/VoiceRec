using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace VoiceRec.Converters;

public class BoolToRecordIconConverter : IValueConverter
{
    public static readonly BoolToRecordIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isRecording)
        {
            return isRecording ? "⏹" : "⏺";
        }
        return "⏺";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToRecordTextConverter : IValueConverter
{
    public static readonly BoolToRecordTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isRecording)
        {
            return isRecording ? "Stop" : "Record";
        }
        return "Record";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToColorConverter : IValueConverter
{
    public static readonly BoolToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isRecording)
        {
            // Red when recording, Green when not
            return isRecording ? Color.Parse("#FF4444") : Color.Parse("#44AA44");
        }
        return Color.Parse("#44AA44");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }
        return false;
    }
}
