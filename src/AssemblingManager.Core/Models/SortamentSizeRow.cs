using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AssemblingManager.Core.Models
{
    public class SortamentSizeRow : INotifyPropertyChanged
    {
        private double _nominalDiameter;
        private double _outerDiameter;
        private double _wallThickness;
        private double _innerDiameter;
        private bool _isExisting;
        private bool _isUsedByPipes;
        private bool _usedInSizeLists = true;
        private bool _usedInSizing = true;

        public double NominalDiameter
        {
            get => _nominalDiameter;
            set => Set(ref _nominalDiameter, value);
        }

        public double OuterDiameter
        {
            get => _outerDiameter;
            set
            {
                if (Set(ref _outerDiameter, value))
                {
                    RecalculateInnerDiameter();
                }
            }
        }

        public double WallThickness
        {
            get => _wallThickness;
            set
            {
                if (Set(ref _wallThickness, value))
                {
                    RecalculateInnerDiameter();
                }
            }
        }

        public double InnerDiameter
        {
            get => _innerDiameter;
            private set => Set(ref _innerDiameter, value);
        }

        public bool IsExisting
        {
            get => _isExisting;
            set => Set(ref _isExisting, value);
        }

        public bool IsUsedByPipes
        {
            get => _isUsedByPipes;
            set => Set(ref _isUsedByPipes, value);
        }

        public bool UsedInSizeLists
        {
            get => _usedInSizeLists;
            set => Set(ref _usedInSizeLists, value);
        }

        public bool UsedInSizing
        {
            get => _usedInSizing;
            set => Set(ref _usedInSizing, value);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public SortamentSizeRow Clone()
        {
            return new SortamentSizeRow
            {
                NominalDiameter = NominalDiameter,
                OuterDiameter = OuterDiameter,
                WallThickness = WallThickness,
                IsExisting = IsExisting,
                IsUsedByPipes = IsUsedByPipes,
                UsedInSizeLists = UsedInSizeLists,
                UsedInSizing = UsedInSizing
            };
        }

        private void RecalculateInnerDiameter()
        {
            InnerDiameter = OuterDiameter - 2.0 * WallThickness;
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
