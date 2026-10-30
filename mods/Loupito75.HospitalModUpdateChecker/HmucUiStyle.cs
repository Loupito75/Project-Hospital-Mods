using UnityEngine;

namespace HospitalModUpdateChecker
{
    // Shared visual metrics for HMUC title-screen UI. Keep these values
    // centralized so dashboard, config editor, log viewer and tooltips do not
    // slowly drift into different visual systems.
    internal static class HmucUiStyle
    {
        internal const int TitleFontSize = 18;
        internal const int SectionFontSize = 17;
        internal const int RowPrimaryFontSize = 17;
        internal const int RowSecondaryFontSize = 16;
        internal const int BodyFontSize = 16;
        internal const int ButtonFontSize = 16;
        internal const int TooltipFontSize = 14;

        internal const float TooltipPaddingHorizontal = 10f;
        internal const float TooltipPaddingVertical = 7f;
        internal const float TooltipMinWidth = 220f;
        internal const float TooltipMaxWidth = 490f;
        internal const float TooltipMinHeight = 42f;
        internal const float TooltipMaxHeight = 270f;
        internal const float TooltipPointerGap = 18f;
        internal const float TooltipEdgeMargin = 8f;

        internal static Color TooltipColor()
        {
            return new Color(
                .3019608f,
                .3019608f,
                .3019608f,
                .9411765f);
        }

        internal static float TooltipWidth(
            float preferredWidth)
        {
            return Mathf.Clamp(
                preferredWidth +
                    TooltipPaddingHorizontal * 2f +
                    8f,
                TooltipMinWidth,
                TooltipMaxWidth);
        }

        internal static float TooltipHeight(
            float preferredHeight)
        {
            return Mathf.Clamp(
                preferredHeight +
                    TooltipPaddingVertical * 2f +
                    6f,
                TooltipMinHeight,
                TooltipMaxHeight);
        }
    }
}
