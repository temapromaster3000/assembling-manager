using System;

namespace AssemblingManager.Core.Utils
{
    public static class PluginVersion
    {
        public static bool TryParse(string text, out int major, out int minor, out int patch)
        {
            major = 0;
            minor = 0;
            patch = 0;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim().TrimStart('v', 'V');
            int plusIndex = trimmed.IndexOf('+');
            if (plusIndex >= 0)
            {
                trimmed = trimmed.Substring(0, plusIndex);
            }

            string[] parts = trimmed.Split('.');
            if (parts.Length == 0 || parts.Length > 3)
            {
                return false;
            }

            for (int i = 0; i < parts.Length; i++)
            {
                int value;
                if (!int.TryParse(parts[i], out value) || value < 0)
                {
                    return false;
                }

                if (i == 0) major = value;
                else if (i == 1) minor = value;
                else patch = value;
            }

            return true;
        }

        public static int Compare(string left, string right)
        {
            bool leftParsed = TryParse(left, out int leftMajor, out int leftMinor, out int leftPatch);
            bool rightParsed = TryParse(right, out int rightMajor, out int rightMinor, out int rightPatch);

            if (!leftParsed || !rightParsed)
            {
                return string.CompareOrdinal(left, right);
            }

            int result = leftMajor.CompareTo(rightMajor);
            if (result != 0) return result;

            result = leftMinor.CompareTo(rightMinor);
            if (result != 0) return result;

            return leftPatch.CompareTo(rightPatch);
        }
    }
}
