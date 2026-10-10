using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    public static class HospitalTrafficControlApi
    {
        public static bool IsBathroomAllowedForCharacter(
            Entity character,
            TileObject bathroom)
        {
            if (bathroom == null ||
                bathroom.m_state == null ||
                !bathroom.HasTag("wc"))
            {
                LogBathroomCheck(character, bathroom, false, false);
                return false;
            }

            bool isPrivate =
                PrivateBathroomManager.IsPrivateHospitalBathroom(bathroom);
            bool allowed =
                !isPrivate ||
                PrivateBathroomManager.IsPrivateHospitalBathroomForPatient(
                    bathroom,
                    character);

            LogBathroomCheck(character, bathroom, isPrivate, allowed);
            return allowed;
        }

        public static TileObject FindAllowedBathroomForCharacter(
            Entity character,
            GameDBProcedure procedure,
            AccessRights accessRights)
        {
            TileObject result =
                PrivateBathroomManager.FindAllowedBladderReplacement(
                    character,
                    procedure,
                    accessRights);

            if (TrafficControlConfig.PathfindingDebug)
            {
                Plugin.Log?.LogInfo(
                    "[PathDebug] WC_API find entity='" +
                    GetCharacterName(character) +
                    "' access=" + accessRights +
                    "(" + (int)accessRights + ")" +
                    " result=" + DescribeBathroom(result) + ".");
            }

            return result;
        }

        private static void LogBathroomCheck(
            Entity character,
            TileObject bathroom,
            bool isPrivate,
            bool allowed)
        {
            if (!TrafficControlConfig.PathfindingDebug)
            {
                return;
            }

            Plugin.Log?.LogInfo(
                "[PathDebug] WC_API check entity='" +
                GetCharacterName(character) +
                "' private=" + isPrivate +
                " allowed=" + allowed +
                " wc=" + DescribeBathroom(bathroom) + ".");
        }

        private static string GetCharacterName(Entity character)
        {
            return character == null
                ? "<null>"
                : (character.Name ?? string.Empty).Trim();
        }

        private static string DescribeBathroom(TileObject bathroom)
        {
            if (bathroom == null || bathroom.m_state == null)
            {
                return "<none>";
            }

            return "F" + bathroom.GetFloorIndex() +
                   ":" + bathroom.m_state.m_position;
        }
    }
}
