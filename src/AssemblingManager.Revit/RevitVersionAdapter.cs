using Autodesk.Revit.ApplicationServices;

namespace AssemblingManager.Revit
{
    public static class RevitVersionAdapter
    {
        public static string GetVersion(Application application)
        {
            return application.VersionNumber;
        }
    }
}
