using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPorters
{
    internal static class PorterSampleCartAnimationRuntime
    {
        internal const string AnimationId =
            "hpo_walk_push_sample_cart";

        private const string AnimationDirectory =
            "AnimatedModels\\Animations\\anim_walk";

        private static readonly FieldInfo InstanceDataField =
            AccessTools.Field(
                typeof(AnimatedModel8DirController),
                "m_animModelInstanceData");

        private static bool s_loadAttempted;
        private static bool s_ready;
        private static Animation s_femaleNe;
        private static Animation s_femaleSw;
        private static Animation s_maleNe;
        private static Animation s_maleSw;

        internal static bool IsReady
        {
            get { return s_ready; }
        }

        internal static void Register(
            AnimatedModel8DirController controller)
        {
            if (controller == null ||
                controller.m_animatedModel8DirController == null)
            {
                return;
            }

            string prefix =
                controller.m_animatedModel8DirController
                    .AnimationFilenamePrefix;
            if (prefix != "f_" && prefix != "m_")
            {
                return;
            }

            EnsureLoaded();
            if (!s_ready || InstanceDataField == null)
            {
                return;
            }

            Dictionary<int, AnimModelInstanceData> instances =
                InstanceDataField.GetValue(controller)
                    as Dictionary<int, AnimModelInstanceData>;
            if (instances == null)
            {
                return;
            }

            Animation ne = prefix == "f_"
                ? s_femaleNe
                : s_maleNe;
            Animation sw = prefix == "f_"
                ? s_femaleSw
                : s_maleSw;

            RegisterDirection(
                instances,
                Direction.NE,
                ne);
            RegisterDirection(
                instances,
                Direction.NW,
                ne);
            RegisterDirection(
                instances,
                Direction.SW,
                sw);
            RegisterDirection(
                instances,
                Direction.SE,
                sw);
        }

        private static void RegisterDirection(
            Dictionary<int, AnimModelInstanceData> instances,
            Direction direction,
            Animation animation)
        {
            AnimModelInstanceData instance;
            if (animation == null ||
                !instances.TryGetValue(
                    (int)direction,
                    out instance) ||
                instance == null ||
                instance.m_animationController == null)
            {
                return;
            }

            instance.m_animationController.addAnimation(
                AnimationId,
                animation,
                direction);
        }

        private static void EnsureLoaded()
        {
            if (s_loadAttempted)
            {
                return;
            }

            s_loadAttempted = true;
            try
            {
                string pluginRoot =
                    Path.GetDirectoryName(
                        Assembly.GetExecutingAssembly().Location);
                string root =
                    string.IsNullOrEmpty(pluginRoot)
                        ? null
                        : Path.Combine(
                            pluginRoot,
                            AnimationDirectory);
                if (string.IsNullOrEmpty(root) ||
                    !Directory.Exists(root))
                {
                    Plugin.Log?.LogWarning(
                        "Sample Cart animation directory is missing; " +
                        "Vanilla walk_push_cart will be used.");
                    return;
                }

                s_femaleNe = LoadAnimation(
                    Path.Combine(
                        root,
                        "f_ne_hpo_walk_push_sample_cart.xml"));
                s_femaleSw = LoadAnimation(
                    Path.Combine(
                        root,
                        "f_sw_hpo_walk_push_sample_cart.xml"));
                s_maleNe = LoadAnimation(
                    Path.Combine(
                        root,
                        "m_ne_hpo_walk_push_sample_cart.xml"));
                s_maleSw = LoadAnimation(
                    Path.Combine(
                        root,
                        "m_sw_hpo_walk_push_sample_cart.xml"));

                s_ready =
                    s_femaleNe != null &&
                    s_femaleSw != null &&
                    s_maleNe != null &&
                    s_maleSw != null;

                if (!s_ready)
                {
                    Plugin.Log?.LogWarning(
                        "One or more Sample Cart animations could not " +
                        "be loaded; Vanilla walk_push_cart will be used.");
                }
            }
            catch (Exception exception)
            {
                s_ready = false;
                Plugin.Log?.LogWarning(
                    "Failed to load HPO Sample Cart animations; " +
                    "Vanilla walk_push_cart will be used. " +
                    exception.Message);
            }
        }

        private static Animation LoadAnimation(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                !File.Exists(path))
            {
                return null;
            }

            XmlDocument document = new XmlDocument();
            document.Load(path);

            XmlElement root =
                document.DocumentElement;
            if (root == null ||
                root.Name != "Animation")
            {
                return null;
            }

            float length;
            if (!float.TryParse(
                    root.GetAttribute("Length"),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out length))
            {
                return null;
            }

            Animation animation = new Animation(length);
            animation.m_name = AnimationId;

            XmlNodeList keyFrames =
                root.SelectNodes("KeyFrame");
            if (keyFrames != null)
            {
                foreach (XmlNode keyFrameNode in keyFrames)
                {
                    using (XmlNodeReader reader =
                        new XmlNodeReader(keyFrameNode))
                    {
                        if (reader.Read())
                        {
                            animation.addKeyframe(
                                new KeyFrame(reader));
                        }
                    }
                }
            }

            XmlNodeList events =
                root.SelectNodes("Event");
            if (events != null)
            {
                foreach (XmlNode eventNode in events)
                {
                    using (XmlNodeReader reader =
                        new XmlNodeReader(eventNode))
                    {
                        if (reader.Read())
                        {
                            animation.m_events.Add(
                                new AnimationEvent(reader));
                        }
                    }
                }
            }

            return animation.m_keyFrames.Count > 1
                ? animation
                : null;
        }
    }

    [HarmonyPatch(
        typeof(AnimatedModel8DirController),
        MethodType.Constructor,
        new Type[]
        {
            typeof(GameDBAnimatedModel8DirController),
            typeof(Face),
            typeof(Clothes),
            typeof(GameDBBodyType)
        })]
    internal static class PorterSampleCartAnimationRegistrationPatch
    {
        private static void Postfix(
            AnimatedModel8DirController __instance)
        {
            PorterSampleCartAnimationRuntime.Register(
                __instance);
        }
    }
}
