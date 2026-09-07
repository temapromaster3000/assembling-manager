using System;
using System.Globalization;
using System.Windows.Data;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Views
{
    public class ConflictActionIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ConflictAction action)
            {
                return (int)action;
            }

            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index && index >= 0 && index <= (int)ConflictAction.Replace)
            {
                return (ConflictAction)index;
            }

            return ConflictAction.Keep;
        }
    }
}
