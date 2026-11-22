using System;
using GLib;
using Lopital;

namespace HospitalFixes
{
    public static class HospitalFixesApi
    {
        public const int ApiVersion = 2;

        public const int ExternalWaitingPresentationDefault = 0;
        public const int ExternalWaitingPresentationPreservePose = 1;

        private const int ExternalWaitingApiVersion = 3;
        private const int ExternalClothingChangeApiVersion = 1;

        private static bool s_waitingFailureLogged;
        private static bool s_clothingFailureLogged;

        public static bool TrySetExternalWaitingState(
            Entity character,
            string sourceId,
            bool waiting)
        {
            try
            {
                return Patches.ExternalWaitingStateRuntime
                    .TrySetWaitingState(
                        character,
                        sourceId,
                        waiting);
            }
            catch (Exception exception)
            {
                if (!waiting)
                {
                    try
                    {
                        Patches.ExternalWaitingStateRuntime
                            .EmergencyClearWaitingState(
                                character,
                                sourceId);
                    }
                    catch
                    {
                        // Public API boundary is deliberately never-throw.
                    }
                }

                LogApiFailureOnce(
                    ref s_waitingFailureLogged,
                    "external waiting",
                    exception);

                return false;
            }
        }

        public static int GetExternalWaitingApiVersion()
        {
            return ExternalWaitingApiVersion;
        }

        public static bool TrySetExternalWaitingState(
            Entity character,
            string sourceId,
            bool waiting,
            int presentationMode)
        {
            try
            {
                return Patches.ExternalWaitingStateRuntime
                    .TrySetWaitingState(
                        character,
                        sourceId,
                        waiting,
                        presentationMode);
            }
            catch (Exception exception)
            {
                if (!waiting)
                {
                    try
                    {
                        Patches.ExternalWaitingStateRuntime
                            .EmergencyClearWaitingState(
                                character,
                                sourceId);
                    }
                    catch
                    {
                        // Public API boundary is deliberately never-throw.
                    }
                }

                LogApiFailureOnce(
                    ref s_waitingFailureLogged,
                    "external waiting",
                    exception);

                return false;
            }
        }

        public static bool TrySetExternalWaitingState(
            Entity character,
            string sourceId,
            bool waiting,
            TileObject[] interactionObjects)
        {
            try
            {
                return Patches.ExternalWaitingStateRuntime
                    .TrySetWaitingState(
                        character,
                        sourceId,
                        waiting,
                        interactionObjects);
            }
            catch (Exception exception)
            {
                if (!waiting)
                {
                    try
                    {
                        Patches.ExternalWaitingStateRuntime
                            .EmergencyClearWaitingState(
                                character,
                                sourceId);
                    }
                    catch
                    {
                        // Public API boundary is deliberately never-throw.
                    }
                }

                LogApiFailureOnce(
                    ref s_waitingFailureLogged,
                    "external waiting",
                    exception);

                return false;
            }
        }

        public static bool TrySetExternalWaitingState(
            Entity character,
            string sourceId,
            bool waiting,
            TileObject[] interactionObjects,
            TileObject unavailableEquipment,
            bool showEquipmentShortagePopup)
        {
            try
            {
                bool accepted =
                    Patches.ExternalWaitingStateRuntime
                        .TrySetWaitingState(
                            character,
                            sourceId,
                            waiting,
                            interactionObjects);

                if (accepted &&
                    waiting &&
                    showEquipmentShortagePopup &&
                    unavailableEquipment != null)
                {
                    Patches.MedicalFridgeContentionNotificationRuntime
                        .TryShow(unavailableEquipment);
                }

                return accepted;
            }
            catch (Exception exception)
            {
                if (!waiting)
                {
                    try
                    {
                        Patches.ExternalWaitingStateRuntime
                            .EmergencyClearWaitingState(
                                character,
                                sourceId);
                    }
                    catch
                    {
                        // Public API boundary is deliberately never-throw.
                    }
                }

                LogApiFailureOnce(
                    ref s_waitingFailureLogged,
                    "external waiting",
                    exception);

                return false;
            }
        }

        public static int GetExternalClothingChangeApiVersion()
        {
            return ExternalClothingChangeApiVersion;
        }

        public static bool TrySetExternalClothingChangeState(
            Entity character,
            string sourceId,
            bool changing)
        {
            try
            {
                return Patches.ExternalClothingChangeStateRuntime
                    .TrySetChangingState(
                        character,
                        sourceId,
                        changing);
            }
            catch (Exception exception)
            {
                if (!changing)
                {
                    try
                    {
                        Patches.ExternalClothingChangeStateRuntime
                            .EmergencyClearChangingState(
                                character,
                                sourceId);
                    }
                    catch
                    {
                        // Public API boundary is deliberately never-throw.
                    }
                }

                LogApiFailureOnce(
                    ref s_clothingFailureLogged,
                    "external clothing-change",
                    exception);

                return false;
            }
        }

        internal static void ResetFailureLog()
        {
            s_waitingFailureLogged = false;
            s_clothingFailureLogged = false;
        }

        private static void LogApiFailureOnce(
            ref bool failureLogged,
            string capability,
            Exception exception)
        {
            if (failureLogged ||
                Plugin.Log == null)
            {
                return;
            }

            try
            {
                failureLogged = true;
                Plugin.Log.LogWarning(
                    "HospitalFixesApi " +
                    capability +
                    " request failed safely; the producer workflow is unaffected: " +
                    exception.GetType().Name +
                    ": " +
                    exception.Message);
            }
            catch
            {
                // Public API boundary is deliberately never-throw.
            }
        }
    }
}
