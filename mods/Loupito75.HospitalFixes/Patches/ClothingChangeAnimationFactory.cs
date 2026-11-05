using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFixes.Patches
{
    internal static class ClothingChangeAnimationFactory
    {
        // Keep Project Hospital's native posture token first. Vanilla inspects
        // animation IDs for "stand" in character-state recovery, so the HFX
        // namespace starts at the first safe position after stand_/sit_.
        internal const string AnimationId =
            "stand_L75_HFX_CLOTHING_CHANGE";

        internal const string SeatedAnimationId =
            "sit_L75_HFX_CLOTHING_CHANGE";

        internal const float AnimationLength = 3.00f;

        private const string StandingAnimationDirectory =
            "AnimatedModels\\Animations\\anim_stand_treatments";

        private const string SeatedAnimationDirectory =
            "AnimatedModels\\Animations\\anim_sit";

        private static readonly Direction[] StandingDirections =
            new Direction[]
            {
                Direction.SE,
                Direction.SW,
                Direction.NE,
                Direction.NW
            };

        private static readonly FieldInfo InstanceDataField =
            AccessTools.Field(
                typeof(AnimatedModel8DirController),
                "m_animModelInstanceData");

        private static readonly Dictionary<string, Animation>
            AnimationCache =
                new Dictionary<string, Animation>(
                    StringComparer.Ordinal);

        private static readonly HashSet<string>
            FailedAnimationFiles =
                new HashSet<string>(
                    StringComparer.Ordinal);

        internal static bool TryRegister(
            AnimatedModel8DirController controller)
        {
            return TryRegisterAnimation(
                controller,
                AnimationId,
                seated: false);
        }

        internal static bool TryRegisterSeated(
            AnimatedModel8DirController controller)
        {
            return TryRegisterAnimation(
                controller,
                SeatedAnimationId,
                seated: true);
        }

        private static bool TryRegisterAnimation(
            AnimatedModel8DirController controller,
            string animationId,
            bool seated)
        {
            if (controller == null ||
                controller.m_animatedModel8DirController == null ||
                InstanceDataField == null)
            {
                return false;
            }

            Dictionary<int, AnimModelInstanceData> instanceData =
                InstanceDataField.GetValue(controller)
                    as Dictionary<int, AnimModelInstanceData>;

            if (instanceData == null)
            {
                return false;
            }

            string prefix =
                controller.m_animatedModel8DirController
                    .AnimationFilenamePrefix;

            if (prefix != "m_" &&
                prefix != "f_")
            {
                return false;
            }

            int registered = 0;

            foreach (Direction direction in StandingDirections)
            {
                AnimModelInstanceData data;
                if (!instanceData.TryGetValue(
                        (int)direction,
                        out data) ||
                    data == null ||
                    data.m_animationController == null)
                {
                    continue;
                }

                if (!data.m_animationController.hasAnimation(
                        animationId))
                {
                    Animation animation =
                        GetOrLoadAnimation(
                            prefix,
                            GetSourceDirection(direction),
                            seated);

                    if (animation == null)
                    {
                        continue;
                    }

                    data.m_animationController.addAnimation(
                        animationId,
                        animation,
                        direction);
                }

                if (data.m_animationController.hasAnimation(
                        animationId))
                {
                    registered++;
                }
            }

            return registered == StandingDirections.Length;
        }

        private static string GetSourceDirection(
            Direction direction)
        {
            return direction == Direction.NE ||
                direction == Direction.NW
                    ? "ne"
                    : "sw";
        }

        private static Animation GetOrLoadAnimation(
            string prefix,
            string sourceDirection,
            bool seated)
        {
            if (string.IsNullOrEmpty(prefix) ||
                string.IsNullOrEmpty(sourceDirection))
            {
                return null;
            }

            string fileName =
                prefix +
                sourceDirection +
                (seated
                    ? "_sit_L75_HFX_CLOTHING_CHANGE.xml"
                    : "_stand_L75_HFX_CLOTHING_CHANGE.xml");

            Animation cached;
            if (AnimationCache.TryGetValue(
                    fileName,
                    out cached))
            {
                return cached;
            }

            string path =
                GetAnimationPath(
                    fileName,
                    seated);

            if (string.IsNullOrEmpty(path) ||
                !File.Exists(path))
            {
                LogLoadFailureOnce(
                    fileName,
                    "Missing animation asset.");
                return null;
            }

            try
            {
                Animation animation =
                    LoadAnimationXml(path);

                if (animation == null ||
                    animation.m_keyFrames == null ||
                    animation.m_keyFrames.Count == 0)
                {
                    LogLoadFailureOnce(
                        fileName,
                        "Animation asset contains no keyframes.");
                    return null;
                }

                AnimationCache[fileName] =
                    animation;
                return animation;
            }
            catch (Exception exception)
            {
                LogLoadFailureOnce(
                    fileName,
                    "Animation XML load failed: " +
                    exception.Message);
                return null;
            }
        }

        private static string GetAnimationPath(
            string fileName,
            bool seated)
        {
            string assemblyPath =
                Assembly.GetExecutingAssembly().Location;
            string pluginDirectory =
                Path.GetDirectoryName(assemblyPath);

            if (string.IsNullOrEmpty(pluginDirectory))
            {
                return null;
            }

            return Path.Combine(
                pluginDirectory,
                Path.Combine(
                    seated
                        ? SeatedAnimationDirectory
                        : StandingAnimationDirectory,
                    fileName));
        }

        private static Animation LoadAnimationXml(
            string path)
        {
            string xml =
                File.ReadAllText(path);

            XmlReader reader =
                XmlReader.Create(
                    new StringReader(xml));

            try
            {
                if (!reader.ReadToDescendant(
                        "Animation"))
                {
                    throw new FormatException(
                        "Animation root element not found.");
                }

                string lengthText =
                    reader.GetAttribute("Length");

                if (string.IsNullOrEmpty(lengthText))
                {
                    throw new FormatException(
                        "Animation Length is missing.");
                }

                float length =
                    float.Parse(
                        lengthText,
                        CultureInfo.InvariantCulture);

                Animation animation =
                    new Animation(length);

                animation.m_name =
                    reader.GetAttribute("Name") ??
                    string.Empty;

                if (reader.ReadToDescendant(
                        "KeyFrame"))
                {
                    do
                    {
                        XmlReader subtree =
                            reader.ReadSubtree();

                        try
                        {
                            subtree.Read();
                            animation.addKeyframe(
                                new KeyFrame(reader));
                        }
                        finally
                        {
                            subtree.Close();
                        }
                    }
                    while (reader.ReadToNextSibling(
                        "KeyFrame"));
                }

                return animation;
            }
            finally
            {
                reader.Close();
            }
        }

        private static void LogLoadFailureOnce(
            string fileName,
            string message)
        {
            if (!FailedAnimationFiles.Add(
                    fileName))
            {
                return;
            }

            Plugin.Log.LogError(
                "[ClothingChange] " +
                message +
                " " +
                fileName);
        }
    }
}
