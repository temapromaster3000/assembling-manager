using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AssemblingManager.Core.Common;

namespace AssemblingManager.Revit.Services
{
    public enum ElevationCheckRequest
    {
        None,
        Check,
        Navigate
    }

    /// <summary>
    /// Выполняет обращения к Revit API из немодального окна: проверку листов и переход к листу.
    /// Запросы складываются в очередь, чтобы быстрые клики не теряли действия.
    /// </summary>
    public class ElevationCheckEventHandler : IExternalEventHandler
    {
        private readonly object _sync = new object();
        private readonly Queue<RequestItem> _requests = new Queue<RequestItem>();
        private readonly ElevationCheckService _service = new ElevationCheckService();

        public Document Document { get; }

        public Action<ElevationCheckResult> CheckCompleted { get; set; }
        public Action<string> CheckFailed { get; set; }
        public Action<string> Navigated { get; set; }
        public Action<string> NavigationFailed { get; set; }

        public ElevationCheckEventHandler(Document document)
        {
            Document = document;
        }

        public void RequestCheck(IEnumerable<ElementId> sheetIds)
        {
            lock (_sync)
            {
                _requests.Enqueue(new RequestItem
                {
                    Kind = ElevationCheckRequest.Check,
                    SheetIds = sheetIds == null
                        ? new List<ElementId>()
                        : new List<ElementId>(sheetIds)
                });
            }
        }

        public void RequestNavigate(ElementId sheetId)
        {
            lock (_sync)
            {
                _requests.Enqueue(new RequestItem
                {
                    Kind = ElevationCheckRequest.Navigate,
                    SheetId = sheetId
                });
            }
        }

        public void ClearRequests()
        {
            lock (_sync)
            {
                _requests.Clear();
            }
        }

        public void Execute(UIApplication app)
        {
            while (true)
            {
                RequestItem request;
                lock (_sync)
                {
                    if (_requests.Count == 0)
                    {
                        return;
                    }

                    request = _requests.Dequeue();
                }

                try
                {
                    if (request.Kind == ElevationCheckRequest.Check)
                    {
                        RunCheck(request.SheetIds);
                    }
                    else if (request.Kind == ElevationCheckRequest.Navigate)
                    {
                        RunNavigate(app, request.SheetId);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"ElevationCheckEventHandler: {ex}");

                    if (request.Kind == ElevationCheckRequest.Check)
                    {
                        CheckFailed?.Invoke(ex.Message);
                    }
                    else if (request.Kind == ElevationCheckRequest.Navigate)
                    {
                        NavigationFailed?.Invoke(ex.Message);
                    }
                }
            }
        }

        private void RunCheck(List<ElementId> sheetIds)
        {
            if (Document == null || !Document.IsValidObject)
            {
                CheckFailed?.Invoke("Документ уже закрыт.");
                return;
            }

            ElevationCheckResult result = _service.Check(Document, sheetIds);
            CheckCompleted?.Invoke(result);
        }

        private void RunNavigate(UIApplication app, ElementId sheetId)
        {
            UIDocument uiDocument = app?.ActiveUIDocument;

            if (uiDocument == null || uiDocument.Document == null || !uiDocument.Document.IsValidObject)
            {
                NavigationFailed?.Invoke("Нет активного документа Revit.");
                return;
            }

            if (Document == null || !Document.IsValidObject)
            {
                NavigationFailed?.Invoke("Документ уже закрыт.");
                return;
            }

            if (!SameDocument(uiDocument.Document, Document))
            {
                NavigationFailed?.Invoke($"Активен другой документ. Переключитесь на «{Document.Title}» и повторите.");
                return;
            }

            ViewSheet sheet = Document.GetElement(sheetId) as ViewSheet;
            if (sheet == null)
            {
                NavigationFailed?.Invoke("Лист не найден — возможно, он был удалён.");
                return;
            }

            if (uiDocument.ActiveView != null && uiDocument.ActiveView.Id == sheet.Id)
            {
                Navigated?.Invoke($"Лист № {FormatSheetNumber(sheet)} уже открыт.");
                return;
            }

            uiDocument.RequestViewChange(sheet);
            Navigated?.Invoke($"Открываю лист № {FormatSheetNumber(sheet)} — «{SheetService.GetSheetName(sheet)}».");
        }

        public string GetName()
        {
            return "Assembling Manager — Проверка отметок";
        }

        public static bool SameDocument(Document first, Document second)
        {
            if (ReferenceEquals(first, second))
            {
                return true;
            }

            if (first == null || second == null)
            {
                return false;
            }

            return string.Equals(first.Title, second.Title, StringComparison.Ordinal)
                   && string.Equals(
                       first.PathName ?? string.Empty,
                       second.PathName ?? string.Empty,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatSheetNumber(ViewSheet sheet)
        {
            string number = (sheet.SheetNumber ?? string.Empty).Trim();
            return string.IsNullOrEmpty(number) ? "(без номера)" : number;
        }

        private class RequestItem
        {
            public ElevationCheckRequest Kind { get; set; }
            public List<ElementId> SheetIds { get; set; }
            public ElementId SheetId { get; set; }
        }
    }
}
