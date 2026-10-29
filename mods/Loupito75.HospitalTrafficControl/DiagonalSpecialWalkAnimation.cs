using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml;
using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class DiagonalSpecialWalkAnimationRuntime
    {
        // Generated XML assets are intentionally retained for future work,
        // but special-walk cardinal runtime support is disabled until their
        // visual quality is good enough in game.
        internal static readonly bool Enabled = false;

        internal const string WalkSlowAnimationId = "walk_slow";
        internal const string WalkSickAnimationId = "walk_sick";
        internal const string WalkLimpAnimationId = "walk_limp";
        internal const string WalkHoldPhoneAnimationId = "walk_hold_phone";
        internal const string WalkBraindeadAnimationId = "walk_braindead";
        internal const string WalkMaskAnimationId = "walk_mask";
        internal const string WalkCrutchAnimationId = "walk_crutch";

        private const string AnimationDirectory =
            "AnimatedModels\\Animations\\anim_walk";

        private static readonly string[] AnimationIds =
        {
            WalkSlowAnimationId,
            WalkSickAnimationId,
            WalkLimpAnimationId,
            WalkHoldPhoneAnimationId,
            WalkBraindeadAnimationId,
            WalkMaskAnimationId,
            WalkCrutchAnimationId
        };

        private static readonly Dictionary<string, Animation>
            FemaleN = new Dictionary<string, Animation>();
        private static readonly Dictionary<string, Animation>
            FemaleE = new Dictionary<string, Animation>();
        private static readonly Dictionary<string, Animation>
            FemaleS = new Dictionary<string, Animation>();
        private static readonly Dictionary<string, Animation>
            MaleN = new Dictionary<string, Animation>();
        private static readonly Dictionary<string, Animation>
            MaleE = new Dictionary<string, Animation>();
        private static readonly Dictionary<string, Animation>
            MaleS = new Dictionary<string, Animation>();
        private static readonly Dictionary<string, bool>
            ReadyAnimationIds = new Dictionary<string, bool>();

        private static bool s_loadAttempted;

        internal static bool IsReadyAnimation(string animationId)
        {
            if (!Enabled)
            {
                return false;
            }

            bool ready;
            return !string.IsNullOrEmpty(animationId) &&
                   ReadyAnimationIds.TryGetValue(animationId, out ready) &&
                   ready;
        }

        internal static bool IsSupportedAnimation(
            string animationId,
            string controllerPrefix)
        {
            if (!IsReadyAnimation(animationId))
            {
                return false;
            }

            // Project Hospital's male cardinal models N/E/S contain only the
            // body skeleton. The NE/SW male models additionally own the
            // left_prop/right_prop/big_prop slices used by phone and crutch.
            // Enabling these two male cardinal animations would therefore
            // knowingly make the prop disappear. Keep the generated XML
            // available for future model work, but do not route into them yet.
            if (controllerPrefix == "m_" &&
                (animationId == WalkHoldPhoneAnimationId ||
                 animationId == WalkCrutchAnimationId))
            {
                return false;
            }

            return controllerPrefix == "f_" ||
                   controllerPrefix == "m_";
        }

        internal static void Register(
            AnimatedModel8DirController controller,
            Dictionary<int, AnimModelInstanceData> instances)
        {
            if (!Enabled)
            {
                return;
            }

            if (controller == null ||
                controller.m_animatedModel8DirController == null ||
                instances == null)
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

            bool female = prefix == "f_";
            for (int i = 0; i < AnimationIds.Length; i++)
            {
                string animationId = AnimationIds[i];
                if (!IsSupportedAnimation(animationId, prefix))
                {
                    continue;
                }

                RegisterDirection(
                    instances,
                    Direction.N,
                    animationId,
                    female ? FemaleN : MaleN);
                RegisterDirection(
                    instances,
                    Direction.E,
                    animationId,
                    female ? FemaleE : MaleE);
                RegisterDirection(
                    instances,
                    Direction.S,
                    animationId,
                    female ? FemaleS : MaleS);
                RegisterDirection(
                    instances,
                    Direction.W,
                    animationId,
                    female ? FemaleE : MaleE);
            }
        }

        private static void RegisterDirection(
            Dictionary<int, AnimModelInstanceData> instances,
            Direction direction,
            string animationId,
            Dictionary<string, Animation> animations)
        {
            AnimModelInstanceData instance;
            Animation animation;
            if (!animations.TryGetValue(animationId, out animation) ||
                animation == null ||
                !instances.TryGetValue((int)direction, out instance) ||
                instance == null ||
                instance.m_animationController == null)
            {
                return;
            }

            instance.m_animationController.addAnimation(
                animationId,
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
                        "Diagonal special-walk animation directory is " +
                        "missing; special walks will remain orthogonal.");
                    return;
                }

                for (int i = 0; i < AnimationIds.Length; i++)
                {
                    string animationId = AnimationIds[i];

                    Animation femaleN = LoadAnimation(
                        Path.Combine(
                            root,
                            "f_n_htc_" + animationId + ".xml"),
                        animationId);
                    Animation femaleE = LoadAnimation(
                        Path.Combine(
                            root,
                            "f_e_htc_" + animationId + ".xml"),
                        animationId);
                    Animation femaleS = LoadAnimation(
                        Path.Combine(
                            root,
                            "f_s_htc_" + animationId + ".xml"),
                        animationId);
                    Animation maleN = LoadAnimation(
                        Path.Combine(
                            root,
                            "m_n_htc_" + animationId + ".xml"),
                        animationId);
                    Animation maleE = LoadAnimation(
                        Path.Combine(
                            root,
                            "m_e_htc_" + animationId + ".xml"),
                        animationId);
                    Animation maleS = LoadAnimation(
                        Path.Combine(
                            root,
                            "m_s_htc_" + animationId + ".xml"),
                        animationId);

                    bool ready =
                        femaleN != null &&
                        femaleE != null &&
                        femaleS != null &&
                        maleN != null &&
                        maleE != null &&
                        maleS != null;

                    ReadyAnimationIds[animationId] = ready;
                    if (!ready)
                    {
                        Plugin.Log?.LogWarning(
                            "One or more diagonal " +
                            animationId +
                            " XML files could not be loaded; this " +
                            "animation will remain orthogonal.");
                        continue;
                    }

                    FemaleN[animationId] = femaleN;
                    FemaleE[animationId] = femaleE;
                    FemaleS[animationId] = femaleS;
                    MaleN[animationId] = maleN;
                    MaleE[animationId] = maleE;
                    MaleS[animationId] = maleS;
                }
            }
            catch (Exception exception)
            {
                ReadyAnimationIds.Clear();
                FemaleN.Clear();
                FemaleE.Clear();
                FemaleS.Clear();
                MaleN.Clear();
                MaleE.Clear();
                MaleS.Clear();

                Plugin.Log?.LogWarning(
                    "Failed to load diagonal special-walk XML files; " +
                    "special walks will remain orthogonal. " +
                    exception.Message);
            }
        }

        private static Animation LoadAnimation(
            string path,
            string animationId)
        {
            if (string.IsNullOrEmpty(path) ||
                !File.Exists(path))
            {
                return null;
            }

            XmlDocument document = new XmlDocument();
            document.Load(path);

            XmlElement root = document.DocumentElement;
            if (root == null || root.Name != "Animation")
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
            animation.m_name = animationId;

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
}
