namespace AssemblingManager.Core.Models
{
    public class ParameterValidationResult
    {
        public bool IsValid { get; set; }
        public bool IsTypeBinding { get; set; }
        public bool AllCategoriesBound { get; set; }
        public System.Collections.Generic.List<string> MissingCategories { get; set; }
    }
}
