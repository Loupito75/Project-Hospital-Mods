using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    [HarmonyPatch(
        typeof(MapScriptInterface),
        nameof(MapScriptInterface.FindClosestFreeObjectWithTags),
        new System.Type[]
        {
            typeof(Vector2i),
            typeof(int),
            typeof(Department),
            typeof(string[]),
            typeof(AccessRights),
            typeof(GameDBRoomType)
        })]
    internal static class VisitorSeatAccessSelectionPatch
    {
        private static void Prefix(string[] tags, ref AccessRights accessRights)
        {
            if (accessRights != AccessRights.STAFF ||
                tags == null ||
                tags.Length != 1 ||
                tags[0] != "ui_visitor_seat")
            {
                return;
            }

            // BehaviorVisitor.GetAccessRights() is PATIENT_PROCEDURE. Vanilla searches
            // lounge seats with STAFF and can therefore reserve a seat the visitor
            // cannot legally reach without the later STAFF fallback.
            accessRights = AccessRights.PATIENT_PROCEDURE;
        }
    }
}
