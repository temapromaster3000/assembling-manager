using System;
using System.Collections.Generic;
using System.Linq;
using AssemblingManager.Core.Models;

namespace AssemblingManager.Revit.Mcp
{
    /// <summary>
    /// Одноразовые предпросмотры изменений. Хранятся в памяти процесса Revit
    /// и живут ограниченное время. Каждый previewId можно применить только один раз.
    /// </summary>
    internal sealed class McpPreviewStore
    {
        public const int LifetimeMinutes = 10;
        private const int MaxEntries = 20;

        private readonly object _sync = new object();
        private readonly Dictionary<string, McpPreviewEntry> _entries =
            new Dictionary<string, McpPreviewEntry>(StringComparer.Ordinal);

        public McpPreviewEntry Create(
            DocumentIdentity document,
            int segmentId,
            string segmentName,
            string catalogHash,
            string unitTypeId,
            string operation,
            double nominalDiameter,
            IEnumerable<SortamentSizeRow> rows)
        {
            DateTime now = DateTime.UtcNow;
            McpPreviewEntry entry = new McpPreviewEntry
            {
                PreviewId = Guid.NewGuid().ToString("N"),
                CreatedUtc = now,
                ExpiresUtc = now.AddMinutes(LifetimeMinutes),
                DocumentPath = document.Path,
                DocumentTitle = document.Title,
                SegmentId = segmentId,
                SegmentName = segmentName,
                CatalogHash = catalogHash,
                UnitTypeId = unitTypeId,
                Operation = operation,
                NominalDiameter = nominalDiameter,
                Rows = rows.Select(row => row.Clone()).ToList()
            };

            lock (_sync)
            {
                CleanupExpired(now);

                while (_entries.Count >= MaxEntries)
                {
                    string oldestKey = _entries
                        .OrderBy(pair => pair.Value.CreatedUtc)
                        .Select(pair => pair.Key)
                        .FirstOrDefault();
                    if (oldestKey == null)
                    {
                        break;
                    }

                    _entries.Remove(oldestKey);
                }

                _entries[entry.PreviewId] = entry;
            }

            return entry;
        }

        public bool TryTake(string previewId, out McpPreviewEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(previewId))
            {
                return false;
            }

            lock (_sync)
            {
                if (!_entries.TryGetValue(previewId, out entry))
                {
                    return false;
                }

                _entries.Remove(previewId);
                return true;
            }
        }

        private void CleanupExpired(DateTime now)
        {
            List<string> expired = _entries
                .Where(pair => pair.Value.ExpiresUtc <= now)
                .Select(pair => pair.Key)
                .ToList();

            foreach (string key in expired)
            {
                _entries.Remove(key);
            }
        }
    }

    internal sealed class McpPreviewEntry
    {
        public string PreviewId { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public string DocumentPath { get; set; }
        public string DocumentTitle { get; set; }
        public int SegmentId { get; set; }
        public string SegmentName { get; set; }
        public string CatalogHash { get; set; }
        public string UnitTypeId { get; set; }
        public string Operation { get; set; }
        public double NominalDiameter { get; set; }
        public List<SortamentSizeRow> Rows { get; set; }
    }

    internal sealed class DocumentIdentity
    {
        public DocumentIdentity(string path, string title)
        {
            Path = path ?? string.Empty;
            Title = title ?? string.Empty;
        }

        public string Path { get; }
        public string Title { get; }

        public bool Matches(Autodesk.Revit.DB.Document document)
        {
            if (document == null)
            {
                return false;
            }

            return string.Equals(Path, document.PathName ?? string.Empty, StringComparison.Ordinal) &&
                   string.Equals(Title, document.Title ?? string.Empty, StringComparison.Ordinal);
        }
    }
}
