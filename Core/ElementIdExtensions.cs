using System;
using Autodesk.Revit.DB;

namespace Autodesk.Revit.DB
{
    public static class ElementIdExtensions
    {
        public static long GetIdValue(this ElementId id)
        {
#if REVIT2024_OR_GREATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }
    }
}
