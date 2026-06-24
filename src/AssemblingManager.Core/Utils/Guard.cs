using System;

namespace AssemblingManager.Core.Utils
{
    public static class Guard
    {
        public static void NotNull(object value, string parameterName)
        {
            if (value == null)
                throw new ArgumentNullException(parameterName);
        }
    }
}
