using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalModUpdateChecker
{
    [HarmonyPatch(
        typeof(TitleScreenController),
        nameof(TitleScreenController.Start))]
    internal static class TitleScreenStartPatch
    {
        private static void Postfix(
            TitleScreenController __instance)
        {
            TitleScreenUpdatePanel.Attach(__instance);
        }
    }

    [HarmonyPatch(
        typeof(TitleScreenMenuController),
        nameof(TitleScreenMenuController.Start))]
    internal static class TitleScreenMenuStartPatch
    {
        private static void Postfix(
            TitleScreenMenuController __instance)
        {
            if (__instance == null)
            {
                return;
            }

            AddBringToFrontHandler(
                __instance.m_newGameButton,
                __instance.m_newGameScreen);

            AddBringToFrontHandler(
                __instance.m_sandboxButton,
                __instance.m_sandboxScreen);

            AddBringToFrontHandler(
                __instance.m_campaignButton,
                __instance.m_campaignScreen);

            AddBringToFrontHandler(
                __instance.m_loadButton,
                __instance.m_loadScreen);

            AddBringToFrontHandler(
                __instance.m_optionsButton,
                __instance.m_optionsScreen);

            AddBringToFrontHandler(
                __instance.m_creditsButton,
                __instance.m_creditsScreen);

            AddBringToFrontHandler(
                __instance.m_testworkshopUploadButton,
                __instance.m_testworkshopUploadPanel);
        }

        private static void AddBringToFrontHandler(
            GameObject buttonObject,
            GameObject screen)
        {
            if (buttonObject == null ||
                screen == null)
            {
                return;
            }

            Button button =
                buttonObject.GetComponent<Button>();

            if (button == null)
            {
                return;
            }

            button.onClick.AddListener(delegate
            {
                TitleScreenUpdatePanel.BringNativeScreenToFront(
                    screen);
            });
        }
    }
}
