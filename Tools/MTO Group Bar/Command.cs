using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

using System.Collections.ObjectModel;
using RincoMTO.Tools.MtoGroupBar.Models;
using RincoMTO.Tools.MtoGroupBar.UI;

namespace RincoMTO.Tools.MtoGroupBar
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        // Keep a static reference to the window so we don't open multiples
        public static MtoGroupBarWindow CurrentWindow { get; set; }
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = uidoc.ActiveView;

            try
            {
                // 1. Collect Rebars
                var rebars = new FilteredElementCollector(doc, activeView.Id)
                    .OfCategory(BuiltInCategory.OST_DetailComponents)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.SuperComponent == null)
                    .Where(fi => fi.Symbol != null && fi.Symbol.Family != null)
                    .Where(fi => fi.Symbol.Family.Name.Contains("Reo__Reinforcement_DistributionAdjustable[Rinco]"))
                    .ToList();

                // 2. Collect Laps
                var laps = new FilteredElementCollector(doc, activeView.Id)
                    .OfCategory(BuiltInCategory.OST_DetailComponents)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.SuperComponent == null)
                    .Where(fi => fi.Symbol != null && fi.Symbol.Family != null)
                    .Where(fi => fi.Symbol.Family.Name.Contains("RINCO_LAPSIGHN") || fi.Name.Contains("RINCO_LAPSIGHN"))
                    .ToList();

                if (rebars.Count == 0)
                {
                    TaskDialog.Show("MTO Group Bar", "KhÃ´ng tÃ¬m tháº¥y Rebar Adjustable trong View hiá»‡n táº¡i.");
                    return Result.Succeeded;
                }

                // 3. Build Adjacency List for Rebars
                Dictionary<ElementId, List<ElementId>> adj = new Dictionary<ElementId, List<ElementId>>();
                foreach (var rebar in rebars)
                {
                    adj[rebar.Id] = new List<ElementId>();
                }

                foreach (var lap in laps)
                {
                    List<FamilyInstance> intersectedRebars = new List<FamilyInstance>();

                    foreach (var rebar in rebars)
                    {
                        if (Intersect(lap, rebar, activeView))
                        {
                            intersectedRebars.Add(rebar);
                        }
                    }

                    // Connect all rebars that intersect this lap
                    for (int i = 0; i < intersectedRebars.Count; i++)
                    {
                        for (int j = i + 1; j < intersectedRebars.Count; j++)
                        {
                            ElementId id1 = intersectedRebars[i].Id;
                            ElementId id2 = intersectedRebars[j].Id;

                            if (!adj[id1].Contains(id2)) adj[id1].Add(id2);
                            if (!adj[id2].Contains(id1)) adj[id2].Add(id1);
                        }
                    }
                }

                // 3.5. Connect Rebars that directly overlap (without lap sign)
                for (int i = 0; i < rebars.Count; i++)
                {
                    for (int j = i + 1; j < rebars.Count; j++)
                    {
                        if (IntersectRebars(rebars[i], rebars[j], activeView))
                        {
                            ElementId id1 = rebars[i].Id;
                            ElementId id2 = rebars[j].Id;

                            if (!adj[id1].Contains(id2)) adj[id1].Add(id2);
                            if (!adj[id2].Contains(id1)) adj[id2].Add(id1);
                        }
                    }
                }

                // 4. Extract Pairs (Edges) and Update Parameter
                using (Transaction tx = new Transaction(doc, "MTO Group Bar"))
                {
                    tx.Start();

                    int pairIndex = 1;
                    ObservableCollection<MtoGroupItem> groupItems = new ObservableCollection<MtoGroupItem>();
                    HashSet<string> processedEdges = new HashSet<string>();

                    foreach (var kvp in adj)
                    {
                        ElementId id1 = kvp.Key;
                        foreach (var id2 in kvp.Value)
                        {
                            // Ensure unique pair by comparing string representation to avoid version issues with .Value
                            string s1 = id1.ToString();
                            string s2 = id2.ToString();
                            string edgeKey = string.Compare(s1, s2) < 0 ? $"{s1}_{s2}" : $"{s2}_{s1}";

                            if (!processedEdges.Contains(edgeKey))
                            {
                                processedEdges.Add(edgeKey);

                                List<ElementId> pair = new List<ElementId> { id1, id2 };
                                string pairName = "Pair " + pairIndex;
                                bool hasError = false;
                                string remarks = "OK";
                                List<double> arrowLengths = new List<double>();

                                foreach (var id in pair)
                                {
                                    Element elem = doc.GetElement(id);
                                    Parameter param = elem.LookupParameter("Blank Text");
                                    if (param != null && !param.IsReadOnly)
                                    {
                                        param.Set(pairName);
                                    }

                                    Parameter pArrow = elem.LookupParameter("Arrow 1 Length");
                                    if (pArrow != null)
                                    {
                                        arrowLengths.Add(pArrow.AsDouble());
                                    }
                                }

                                if (arrowLengths.Count == 2)
                                {
                                    if (Math.Abs(arrowLengths[0] - arrowLengths[1]) > 0.001)
                                    {
                                        hasError = true;
                                        remarks = "KhÃ¡c Arrow 1 Length";
                                    }
                                }
                                
                                groupItems.Add(new MtoGroupItem 
                                {
                                    GroupName = pairName,
                                    Count = 2,
                                    ElementIds = pair,
                                    Remarks = remarks,
                                    HasError = hasError
                                });

                                pairIndex++;
                            }
                        }
                    }

                    // Clear Blank Text for independent rebars
                    foreach (var rebar in rebars)
                    {
                        if (adj[rebar.Id].Count == 0)
                        {
                            Element elem = doc.GetElement(rebar.Id);
                            Parameter param = elem.LookupParameter("Blank Text");
                            if (param != null && !param.IsReadOnly)
                            {
                                param.Set("");
                            }
                        }
                    }

                    tx.Commit();

                    if (groupItems.Count > 0)
                    {
                        if (CurrentWindow != null && CurrentWindow.IsLoaded)
                        {
                            CurrentWindow.Focus();
                        }
                        else
                        {
                            MtoGroupBarEventHandler handler = new MtoGroupBarEventHandler();
                            ExternalEvent exEvent = ExternalEvent.Create(handler);

                            CurrentWindow = new MtoGroupBarWindow(groupItems, exEvent, handler);
                            CurrentWindow.Show();
                        }
                    }
                    else
                    {
                        TaskDialog.Show("MTO Group Bar", "KhÃ´ng tÃ¬m tháº¥y nhÃ³m thÃ©p ná»‘i nÃ o.");
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private bool Intersect(FamilyInstance lap, FamilyInstance rebar, View activeView)
        {
            BoundingBoxXYZ bb1 = lap.get_BoundingBox(activeView);
            BoundingBoxXYZ bb2 = rebar.get_BoundingBox(activeView);
            
            if (bb1 == null || bb2 == null) return false;
            
            // Tolerance to handle slight inaccuracies
            double tol = 0.05; 
            
            bool overlapX = bb1.Min.X <= bb2.Max.X + tol && bb1.Max.X >= bb2.Min.X - tol;
            bool overlapY = bb1.Min.Y <= bb2.Max.Y + tol && bb1.Max.Y >= bb2.Min.Y - tol;
            bool overlapZ = bb1.Min.Z <= bb2.Max.Z + tol && bb1.Max.Z >= bb2.Min.Z - tol;

            if (!(overlapX && overlapY && overlapZ)) return false;

            // Check if they are parallel using their Location (Rotation or Curve Direction)
            double rotLap = GetRotation(lap);
            double rotRebar = GetRotation(rebar);

            // Normalize to [0, PI)
            rotLap = rotLap % Math.PI;
            if (rotLap < 0) rotLap += Math.PI;
            
            rotRebar = rotRebar % Math.PI;
            if (rotRebar < 0) rotRebar += Math.PI;

            double rotDiff = Math.Abs(rotLap - rotRebar);
            double angTol = 0.1; // Roughly 5.7 degrees tolerance

            // If the difference is not close to 0 or PI, they are not parallel
            if (rotDiff > angTol && Math.Abs(rotDiff - Math.PI) > angTol)
            {
                return false;
            }

            return true;
        }

        private bool IntersectRebars(FamilyInstance r1, FamilyInstance r2, View activeView)
        {
            BoundingBoxXYZ bb1 = GetPhysicalBoundingBox(r1, activeView);
            BoundingBoxXYZ bb2 = GetPhysicalBoundingBox(r2, activeView);
            
            if (bb1 == null || bb2 == null) return false;
            
            double tol = 0.05; 
            
            bool overlapX = bb1.Min.X <= bb2.Max.X + tol && bb1.Max.X >= bb2.Min.X - tol;
            bool overlapY = bb1.Min.Y <= bb2.Max.Y + tol && bb1.Max.Y >= bb2.Min.Y - tol;
            bool overlapZ = bb1.Min.Z <= bb2.Max.Z + tol && bb1.Max.Z >= bb2.Min.Z - tol;

            if (!(overlapX && overlapY && overlapZ)) return false;

            double rot1 = GetRotation(r1);
            double rot2 = GetRotation(r2);

            rot1 = rot1 % Math.PI;
            if (rot1 < 0) rot1 += Math.PI;
            
            rot2 = rot2 % Math.PI;
            if (rot2 < 0) rot2 += Math.PI;

            double rotDiff = Math.Abs(rot1 - rot2);
            double angTol = 0.1;

            if (rotDiff > angTol && Math.Abs(rotDiff - Math.PI) > angTol)
            {
                return false;
            }

            return true;
        }

        private BoundingBoxXYZ GetPhysicalBoundingBox(FamilyInstance fi, View view)
        {
            Curve mainCurve = GetMainCurve(fi, view);
            if (mainCurve != null)
            {
                XYZ p1 = mainCurve.GetEndPoint(0);
                XYZ p2 = mainCurve.GetEndPoint(1);
                
                XYZ min = new XYZ(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y), Math.Min(p1.Z, p2.Z));
                XYZ max = new XYZ(Math.Max(p1.X, p2.X), Math.Max(p1.Y, p2.Y), Math.Max(p1.Z, p2.Z));
                
                return new BoundingBoxXYZ { Min = min, Max = max };
            }
            return fi.get_BoundingBox(view);
        }

        private Curve GetMainCurve(FamilyInstance fi, View view)
        {
            if (fi.Location is LocationCurve lc && lc.Curve != null)
            {
                return lc.Curve;
            }

            var geom = fi.get_Geometry(new Options { View = view, ComputeReferences = false });
            if (geom == null) return null;
            
            Line longestLine = null;
            double maxLen = 0;

            foreach (var obj in geom)
            {
                if (obj is Line l)
                {
                    if (l.Length > maxLen) { maxLen = l.Length; longestLine = l; }
                }
                else if (obj is GeometryInstance gi)
                {
                    var instGeom = gi.GetInstanceGeometry();
                    foreach (var instObj in instGeom)
                    {
                        if (instObj is Line il)
                        {
                            if (il.Length > maxLen) { maxLen = il.Length; longestLine = il; }
                        }
                    }
                }
            }
            return longestLine;
        }

        private double GetRotation(FamilyInstance fi)
        {
            if (fi.Location is LocationPoint lp)
            {
                return lp.Rotation;
            }
            else if (fi.Location is LocationCurve lc && lc.Curve is Line line)
            {
                XYZ dir = line.Direction;
                return Math.Atan2(dir.Y, dir.X);
            }
            return 0;
        }
    }
}
