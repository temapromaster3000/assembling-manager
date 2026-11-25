using System;

namespace AssemblingManager.Revit.Mcp
{
    internal sealed class McpToolException : Exception
    {
        public McpToolException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
