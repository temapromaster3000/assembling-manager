using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AssemblingManager.Core.Common
{
    public class NaturalStringComparer : IComparer<string>
    {
        private static readonly Regex NumberRegex = new Regex(@"(\d+)", RegexOptions.Compiled);

        public int Compare(string x, string y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x == null)
            {
                return -1;
            }

            if (y == null)
            {
                return 1;
            }

            string[] partsX = NumberRegex.Split(x);
            string[] partsY = NumberRegex.Split(y);

            for (int i = 0; i < Math.Min(partsX.Length, partsY.Length); i++)
            {
                string partX = partsX[i];
                string partY = partsY[i];

                bool numericX = IsNumeric(partX);
                bool numericY = IsNumeric(partY);

                if (!numericX && !numericY)
                {
                    int textCompare = string.Compare(partX, partY, StringComparison.OrdinalIgnoreCase);
                    if (textCompare != 0)
                    {
                        return textCompare;
                    }
                }
                else if (numericX && numericY)
                {
                    long valueX;
                    long valueY;
                    bool parsedX = long.TryParse(partX, out valueX);
                    bool parsedY = long.TryParse(partY, out valueY);

                    if (parsedX && parsedY)
                    {
                        if (valueX != valueY)
                        {
                            return valueX < valueY ? -1 : 1;
                        }

                        int lengthCompare = partX.Length.CompareTo(partY.Length);
                        if (lengthCompare != 0)
                        {
                            return lengthCompare;
                        }
                    }
                }
                else
                {
                    return numericX ? 1 : -1;
                }
            }

            return partsX.Length.CompareTo(partsY.Length);
        }

        private static bool IsNumeric(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            foreach (char c in text)
            {
                if (!char.IsDigit(c))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
