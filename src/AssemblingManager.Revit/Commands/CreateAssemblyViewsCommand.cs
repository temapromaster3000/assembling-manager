using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AssemblingManager.Core.Models;
using AssemblingManager.Revit.Services;
using AssemblingManager.Revit.Views;

namespace AssemblingManager.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateAssemblyViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApplication = commandData.Application;
            UIDocument uiDocument = uiApplication.ActiveUIDocument;
            Document document = uiDocument.Document;

            int assemblyCount = new FilteredElementCollector(document)
                .OfClass(typeof(AssemblyInstance))
                .GetElementCount();

            MainWindow window = new MainWindow(assemblyCount);
            bool? dialogResult = window.ShowDialog();

            if (dialogResult != true)
            {
                return Result.Cancelled;
            }

            ViewCreationOptions options = window.Options;

            using (TransactionGroup transactionGroup = new TransactionGroup(document, "Assembling Manager"))
            {
                transactionGroup.Start();

                try
                {
                    using (Transaction transaction = new Transaction(document, "Create views and filters"))
                    {
                        FailureHandlingOptions failureOptions = transaction.GetFailureHandlingOptions();
                        failureOptions.SetFailuresPreprocessor(new FailurePreprocessor());
                        transaction.SetFailureHandlingOptions(failureOptions);

                        transaction.Start();

                        OrchestratorService orchestrator = new OrchestratorService();
                        orchestrator.GenerateViews(document, uiApplication.Application, options);

                        transaction.Commit();
                    }

                    transactionGroup.Assimilate();
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    transactionGroup.RollBack();
                    message = ex.Message;
                    return Result.Failed;
                }
            }
        }
    }
}
