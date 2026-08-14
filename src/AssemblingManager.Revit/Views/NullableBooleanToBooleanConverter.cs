using System;
using System.Windows.Data;

namespace AssemblingManager.Revit.Views
{
    [ValueConversion(typeof(bool?), typeof(bool))]
    public class NullableBooleanToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            bool? nullableValue = value as bool?;
            if (!nullableValue.HasValue)
            {
                return true;
            }

            return nullableValue.Value == true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue;
            }

            return false;
        }
    }
}
