using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using RincoMTO.Tools.MTOQuery.Models;

namespace RincoMTO.Tools.MTOQuery.ViewModels
{
    public class MtoQueryViewModel
    {
        public ObservableCollection<MtoQueryItem> Items { get; set; }

        public MtoQueryViewModel(Document doc, View activeView)
        {
            Items = new ObservableCollection<MtoQueryItem>();
            LoadData(doc, activeView);
        }

        private void LoadData(Document doc, View activeView)
        {
            // Get all detail items in the current view
            var detailItems = new FilteredElementCollector(doc, activeView.Id)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(fi => fi.Symbol != null && fi.Symbol.Family != null)
                .Where(fi => 
                    fi.Symbol.Family.Name.Contains("Reo__Reinforcement_DistributionAdjustable[Rinco]") || 
                    fi.Symbol.Family.Name.Contains("Reo__ZBar[Rinco]"))
                .ToList();

            // Get all tags in the current view
            var tags = new FilteredElementCollector(doc, activeView.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            // Group the detail items
            var groupedItems = detailItems.GroupBy(fi =>
            {
                string familyName = fi.Symbol.Family.Name;
                string typeName = fi.Symbol.Name;
                
                // Find tag text for this element
                string tagText = "";
#if REVIT2022_OR_GREATER
                var elementTags = tags.Where(t => t.GetTaggedLocalElementIds().Contains(fi.Id)).ToList();
#else
                var elementTags = tags.Where(t => t.TaggedLocalElementId == fi.Id).ToList();
#endif
                if (elementTags.Any())
                {
                    tagText = string.Join(", ", elementTags.Select(t => t.TagText).Where(text => !string.IsNullOrEmpty(text)));
                }

                // If no tag found, try to look for common text parameters just in case
                if (string.IsNullOrEmpty(tagText))
                {
                    var markParam = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                    if (markParam != null && markParam.HasValue)
                    {
                        tagText = markParam.AsString();
                    }
                }

                return new { familyName, typeName, tagText };
            });

            // Populate the ObservableCollection
            foreach (var group in groupedItems)
            {
                Items.Add(new MtoQueryItem
                {
                    FamilyName = group.Key.familyName,
                    TypeName = group.Key.typeName,
                    TagText = group.Key.tagText,
                    Quantity = group.Count()
                });
            }
        }
    }
}
