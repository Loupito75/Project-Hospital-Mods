using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using GLib;
using UnityEngine;

namespace HospitalTrafficControl
{
    internal static class MaleNCardinalRuntime
    {
        internal const string RuntimeTextureFileName =
            "Loupito75_HTC_male_n_runtime.png";

        private const string RuntimeTextureManagerKey =
            "AnimatedModels/Textures/" + RuntimeTextureFileName;
        private const int CellPadding = 4;
        private const float IntegerOffsetTolerance = 0.001f;

        private static readonly Regex CellFilePattern =
            new Regex(
                "^(?<slice>.+)_x(?<x>\\d+)_y(?<y>\\d+)\\.png$",
                RegexOptions.IgnoreCase);

        private static bool s_attempted;
        private static AnimModel s_runtimeModel;
        private static Texture2D s_runtimeTexture;
        private static SliceRuntimeInfo[] s_sliceInfoByIndex;

        private static int s_referenceTextureWidth;
        private static int s_referenceTextureHeight;
        private static int s_runtimeTextureWidth;
        private static int s_runtimeTextureHeight;
        private static int s_fallbackOriginX;
        private static int s_fallbackOriginY;

        internal static bool TryGetModel(
            string legacyModelPath,
            string modernModelPath,
            out AnimModel model)
        {
            model = s_runtimeModel;
            if (model != null)
            {
                return true;
            }

            if (s_attempted ||
                string.IsNullOrEmpty(legacyModelPath) ||
                string.IsNullOrEmpty(modernModelPath))
            {
                return false;
            }

            s_attempted = true;

            StreamingAssetManager assets =
                StreamingAssetManager.GetInstance();
            if (assets == null ||
                assets.m_animatedModelTextures == null)
            {
                return false;
            }

            AnimModel legacyN =
                assets.GetAnimModel(legacyModelPath);
            AnimModel modernNE =
                assets.GetAnimModel(modernModelPath);
            if (legacyN == null ||
                modernNE == null ||
                modernNE.m_skeleton == null ||
                modernNE.m_slices == null)
            {
                Plugin.Log?.LogWarning(
                    "Male N runtime modernization could not find the " +
                    "loaded Vanilla N/NE models; using NE fallback.");
                return false;
            }

            Texture2D texture = null;
            Dictionary<string, Dictionary<int, CellInfo>>
                customCells = null;

            try
            {
                if (!TryBuildRuntimeTexture(
                        modernNE,
                        out texture,
                        out customCells))
                {
                    return false;
                }

                SliceRuntimeInfo[] sliceInfos;
                AnimModel runtimeModel =
                    BuildModernizedModel(
                        legacyN,
                        modernNE,
                        customCells,
                        out sliceInfos);

                if (runtimeModel == null ||
                    runtimeModel.m_slices == null ||
                    runtimeModel.m_slices.Count != modernNE.m_slices.Count)
                {
                    throw new InvalidOperationException(
                        "Modernized male N model is incomplete.");
                }

                Texture previous;
                if (assets.m_animatedModelTextures.TryGetValue(
                        RuntimeTextureManagerKey,
                        out previous) &&
                    previous != null &&
                    !ReferenceEquals(previous, texture))
                {
                    UnityEngine.Object.Destroy(previous);
                }

                assets.m_animatedModelTextures[
                    RuntimeTextureManagerKey] = texture;

                s_runtimeTexture = texture;
                s_runtimeModel = runtimeModel;
                s_sliceInfoByIndex = sliceInfos;
                texture = null;
                model = s_runtimeModel;

                Plugin.Log?.LogInfo(
                    "Male N runtime model ready: " +
                    s_runtimeModel.m_slices.Count +
                    " modern slices, " +
                    CountCustomCells(customCells) +
                    " separate N PNG cell(s), no generated disk atlas.");

                return true;
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning(
                    "Male N runtime modernization failed; " +
                    "using the complete NE visual fallback. " +
                    exception.GetType().FullName + ": " +
                    exception.Message);
                return false;
            }
            finally
            {
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
        }

        internal static void ApplyRuntimeSliceOverrides(
            AnimModelInstance instance)
        {
            if (instance == null ||
                s_runtimeModel == null ||
                s_sliceInfoByIndex == null ||
                instance.m_animModel == null ||
                !ReferenceEquals(
                    instance.m_animModel.m_slices,
                    s_runtimeModel.m_slices) ||
                instance.m_sliceGeometryList == null ||
                instance.m_animModelSkeletonInstance == null)
            {
                return;
            }

            for (int i = 0;
                 i < instance.m_sliceGeometryList.Count;
                 i++)
            {
                AnimModelSliceGeometry geometry =
                    instance.m_sliceGeometryList[i];
                if (geometry == null ||
                    geometry.m_slice == null)
                {
                    continue;
                }

                int sliceIndex =
                    geometry.m_slice.m_index;
                if (sliceIndex < 0 ||
                    sliceIndex >= s_sliceInfoByIndex.Length)
                {
                    continue;
                }

                SliceRuntimeInfo info =
                    s_sliceInfoByIndex[sliceIndex];
                if (info == null)
                {
                    continue;
                }

                AnimModelBoneTransform transform =
                    instance.m_animModelSkeletonInstance.getBoneState(
                        geometry.m_slice.m_parentBoneID);
                if (transform == null ||
                    transform.m_hidden)
                {
                    continue;
                }

                Vector2f variation =
                    Vector2f.ZERO_VECTOR;
                if (instance.m_sliceOffsetList != null &&
                    sliceIndex < instance.m_sliceOffsetList.Count)
                {
                    variation =
                        instance.m_sliceOffsetList[sliceIndex];
                }

                ApplySliceOverride(
                    geometry,
                    transform,
                    variation,
                    info);
            }
        }

        internal static void Reset()
        {
            StreamingAssetManager assets =
                StreamingAssetManager.GetInstance();
            if (assets != null &&
                assets.m_animatedModelTextures != null)
            {
                Texture registered;
                if (assets.m_animatedModelTextures.TryGetValue(
                        RuntimeTextureManagerKey,
                        out registered) &&
                    ReferenceEquals(
                        registered,
                        s_runtimeTexture))
                {
                    assets.m_animatedModelTextures.Remove(
                        RuntimeTextureManagerKey);
                }
            }

            if (s_runtimeTexture != null)
            {
                UnityEngine.Object.Destroy(
                    s_runtimeTexture);
            }

            s_runtimeTexture = null;
            s_runtimeModel = null;
            s_sliceInfoByIndex = null;
            s_referenceTextureWidth = 0;
            s_referenceTextureHeight = 0;
            s_runtimeTextureWidth = 0;
            s_runtimeTextureHeight = 0;
            s_fallbackOriginX = 0;
            s_fallbackOriginY = 0;
            s_attempted = false;
        }

        private static bool TryBuildRuntimeTexture(
            AnimModel modernNE,
            out Texture2D runtimeTexture,
            out Dictionary<string, Dictionary<int, CellInfo>>
                customCells)
        {
            runtimeTexture = null;
            customCells =
                new Dictionary<string, Dictionary<int, CellInfo>>(
                    StringComparer.OrdinalIgnoreCase);

            string pluginRoot =
                Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location);
            string sourceDirectory =
                string.IsNullOrEmpty(pluginRoot)
                    ? null
                    : Path.Combine(
                        pluginRoot,
                        Path.Combine(
                            "SourceAssets",
                            Path.Combine(
                                "AnimatedModels",
                                Path.Combine(
                                    "Textures",
                                    "male_n_cells"))));

            if (string.IsNullOrEmpty(sourceDirectory) ||
                !Directory.Exists(sourceDirectory))
            {
                Plugin.Log?.LogWarning(
                    "Male N source PNG directory is not installed; " +
                    "using NE fallback.");
                return false;
            }

            string vanillaTexturePath =
                ResolveVanillaTexturePath(
                    modernNE.m_textureFilename);
            if (string.IsNullOrEmpty(vanillaTexturePath))
            {
                Plugin.Log?.LogWarning(
                    "Vanilla male NE atlas was not found; " +
                    "using NE fallback.");
                return false;
            }

            Texture2D vanillaTexture = null;
            List<LoadedCell> loadedCells =
                new List<LoadedCell>();

            try
            {
                vanillaTexture =
                    LoadReadableTexture(
                        vanillaTexturePath);
                if (vanillaTexture == null ||
                    vanillaTexture.width <= 0 ||
                    vanillaTexture.height <= 0)
                {
                    return false;
                }

                s_referenceTextureWidth =
                    vanillaTexture.width;
                s_referenceTextureHeight =
                    vanillaTexture.height;

                string[] files =
                    Directory.GetFiles(
                        sourceDirectory,
                        "*.png",
                        SearchOption.TopDirectoryOnly);
                Array.Sort(
                    files,
                    StringComparer.OrdinalIgnoreCase);

                Dictionary<string, bool> seenKeys =
                    new Dictionary<string, bool>(
                        StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < files.Length; i++)
                {
                    string fileName =
                        Path.GetFileName(files[i]);
                    Match match =
                        CellFilePattern.Match(fileName);
                    if (!match.Success)
                    {
                        throw new InvalidOperationException(
                            "Unexpected male N source filename '" +
                            fileName +
                            "'.");
                    }

                    string sliceId =
                        match.Groups["slice"].Value;
                    int offsetX =
                        int.Parse(
                            match.Groups["x"].Value,
                            CultureInfo.InvariantCulture);
                    int offsetY =
                        int.Parse(
                            match.Groups["y"].Value,
                            CultureInfo.InvariantCulture);
                    string duplicateKey =
                        sliceId + "|" + offsetX + "|" + offsetY;
                    if (seenKeys.ContainsKey(
                            duplicateKey))
                    {
                        throw new InvalidOperationException(
                            "Duplicate male N cell '" +
                            duplicateKey +
                            "'.");
                    }

                    seenKeys[duplicateKey] = true;

                    Texture2D cellTexture =
                        LoadReadableTexture(files[i]);
                    if (cellTexture == null ||
                        cellTexture.width <= 0 ||
                        cellTexture.height <= 0)
                    {
                        throw new InvalidOperationException(
                            "Could not decode male N cell '" +
                            fileName +
                            "'.");
                    }

                    LoadedCell loaded =
                        new LoadedCell();
                    loaded.SliceId = sliceId;
                    loaded.OffsetX = offsetX;
                    loaded.OffsetY = offsetY;
                    loaded.Texture = cellTexture;
                    loadedCells.Add(loaded);
                }

                if (loadedCells.Count == 0)
                {
                    Plugin.Log?.LogWarning(
                        "No male N source PNG cells were found; " +
                        "using NE fallback.");
                    return false;
                }

                loadedCells.Sort(
                    LoadedCellComparer.Instance);

                int maxTextureSize =
                    SystemInfo.maxTextureSize;
                bool horizontal =
                    s_referenceTextureWidth * 2 <=
                        maxTextureSize &&
                    s_referenceTextureHeight <=
                        maxTextureSize;
                bool vertical =
                    s_referenceTextureWidth <=
                        maxTextureSize &&
                    s_referenceTextureHeight * 2 <=
                        maxTextureSize;

                if (!horizontal && !vertical)
                {
                    throw new InvalidOperationException(
                        "GPU texture limit is too small for the " +
                        "male N runtime composite.");
                }

                int customOriginX;
                int customOriginY;
                if (horizontal)
                {
                    s_runtimeTextureWidth =
                        s_referenceTextureWidth * 2;
                    s_runtimeTextureHeight =
                        s_referenceTextureHeight;
                    s_fallbackOriginX = 0;
                    s_fallbackOriginY = 0;
                    customOriginX =
                        s_referenceTextureWidth;
                    customOriginY = 0;
                }
                else
                {
                    s_runtimeTextureWidth =
                        s_referenceTextureWidth;
                    s_runtimeTextureHeight =
                        s_referenceTextureHeight * 2;
                    s_fallbackOriginX = 0;
                    s_fallbackOriginY = 0;
                    customOriginX = 0;
                    customOriginY =
                        s_referenceTextureHeight;
                }

                runtimeTexture =
                    new Texture2D(
                        s_runtimeTextureWidth,
                        s_runtimeTextureHeight,
                        TextureFormat.ARGB32,
                        true);
                runtimeTexture.name =
                    "Loupito75 HTC male N runtime composite";
                runtimeTexture.wrapMode =
                    TextureWrapMode.Clamp;
                runtimeTexture.filterMode =
                    FilterMode.Trilinear;

                runtimeTexture.SetPixels32(
                    new Color32[
                        s_runtimeTextureWidth *
                        s_runtimeTextureHeight]);

                runtimeTexture.SetPixels32(
                    s_fallbackOriginX,
                    s_fallbackOriginY,
                    s_referenceTextureWidth,
                    s_referenceTextureHeight,
                    vanillaTexture.GetPixels32());

                int cursorX = 0;
                int cursorY = 0;
                int shelfHeight = 0;

                for (int i = 0;
                     i < loadedCells.Count;
                     i++)
                {
                    LoadedCell loaded =
                        loadedCells[i];
                    int width =
                        loaded.Texture.width;
                    int height =
                        loaded.Texture.height;

                    if (width >
                            s_referenceTextureWidth ||
                        height >
                            s_referenceTextureHeight)
                    {
                        throw new InvalidOperationException(
                            "Male N cell '" +
                            loaded.SliceId +
                            "' is larger than the Vanilla " +
                            "NE atlas region.");
                    }

                    if (cursorX > 0 &&
                        cursorX + width >
                            s_referenceTextureWidth)
                    {
                        cursorY +=
                            shelfHeight +
                            CellPadding;
                        cursorX = 0;
                        shelfHeight = 0;
                    }

                    if (cursorY + height >
                        s_referenceTextureHeight)
                    {
                        throw new InvalidOperationException(
                            "Separate male N PNG cells do not fit " +
                            "in one runtime packing region.");
                    }

                    int targetX =
                        customOriginX + cursorX;
                    int targetY =
                        customOriginY + cursorY;

                    runtimeTexture.SetPixels32(
                        targetX,
                        targetY,
                        width,
                        height,
                        loaded.Texture.GetPixels32());

                    CellInfo cell =
                        new CellInfo();
                    cell.Width = width;
                    cell.Height = height;
                    cell.UvTopLeft =
                        new Vector2f(
                            (float)targetX /
                                s_runtimeTextureWidth,
                            (float)(targetY + height) /
                                s_runtimeTextureHeight);
                    cell.UvBottomRight =
                        new Vector2f(
                            (float)(targetX + width) /
                                s_runtimeTextureWidth,
                            (float)targetY /
                                s_runtimeTextureHeight);

                    Dictionary<int, CellInfo>
                        cellsForSlice;
                    if (!customCells.TryGetValue(
                            loaded.SliceId,
                            out cellsForSlice))
                    {
                        cellsForSlice =
                            new Dictionary<int, CellInfo>();
                        customCells[
                            loaded.SliceId] =
                            cellsForSlice;
                    }

                    cellsForSlice[
                        MakeOffsetKey(
                            loaded.OffsetX,
                            loaded.OffsetY)] = cell;

                    cursorX +=
                        width +
                        CellPadding;
                    if (height > shelfHeight)
                    {
                        shelfHeight = height;
                    }
                }

                runtimeTexture.Apply(
                    false,
                    false);

                if (SettingsManager.Instance != null &&
                    SettingsManager.Instance.m_viewSettings != null &&
                    SettingsManager.Instance.m_viewSettings
                        .m_lowresTextures.m_value)
                {
                    TextureScale.Bilinear(
                        runtimeTexture,
                        runtimeTexture.width / 2,
                        runtimeTexture.height / 2);
                }

                runtimeTexture.Apply(
                    true,
                    true);

                return true;
            }
            finally
            {
                if (vanillaTexture != null)
                {
                    UnityEngine.Object.Destroy(
                        vanillaTexture);
                }

                for (int i = 0;
                     i < loadedCells.Count;
                     i++)
                {
                    if (loadedCells[i].Texture != null)
                    {
                        UnityEngine.Object.Destroy(
                            loadedCells[i].Texture);
                    }
                }
            }
        }

        private static AnimModel BuildModernizedModel(
            AnimModel legacyN,
            AnimModel modernNE,
            Dictionary<string, Dictionary<int, CellInfo>>
                customCells,
            out SliceRuntimeInfo[] sliceInfos)
        {
            AnimModel model =
                new AnimModel(
                    modernNE.m_skeleton);
            model.m_name =
                "Loupito75 HTC Male N Modernized";
            model.m_filename =
                legacyN.m_filename +
                "#L75_HTC_MODERN_N";
            model.m_skeleton =
                modernNE.m_skeleton;
            model.m_skeletonFilename =
                modernNE.m_skeletonFilename;
            model.m_textureFilename =
                RuntimeTextureFileName;

            List<SliceRuntimeInfo> infos =
                new List<SliceRuntimeInfo>();

            foreach (KeyValuePair<string, AnimModelSlice>
                     pair in modernNE.m_slices)
            {
                AnimModelSlice modernSlice =
                    pair.Value;
                AnimModelSlice runtimeSlice =
                    CreateModernizedSlice(
                        legacyN,
                        modernSlice);

                model.addSlice(
                    runtimeSlice);

                SliceRuntimeInfo info =
                    new SliceRuntimeInfo();
                info.RuntimeSlice =
                    runtimeSlice;
                info.FallbackSlice =
                    modernSlice;
                ComputeAnchor(
                    runtimeSlice,
                    out info.AnchorX,
                    out info.AnchorTop);

                Dictionary<int, CellInfo>
                    cellsForSlice;
                if (customCells.TryGetValue(
                        runtimeSlice.m_ID,
                        out cellsForSlice))
                {
                    info.CustomCells =
                        cellsForSlice;
                }

                infos.Add(info);
            }

            sliceInfos =
                new SliceRuntimeInfo[
                    model.m_slices.Count];
            for (int i = 0;
                 i < infos.Count;
                 i++)
            {
                SliceRuntimeInfo info =
                    infos[i];
                sliceInfos[
                    info.RuntimeSlice.m_index] =
                    info;
            }

            return model;
        }

        private static AnimModelSlice CreateModernizedSlice(
            AnimModel legacyN,
            AnimModelSlice modernSlice)
        {
            AnimModelSlice legacySlice;
            if (legacyN.m_slices.TryGetValue(
                    modernSlice.m_ID,
                    out legacySlice))
            {
                return CloneSlice(
                    legacySlice,
                    modernSlice.m_ID,
                    legacySlice.m_parentBoneID,
                    legacySlice.m_zOffset);
            }

            if (string.Equals(
                    modernSlice.m_ID,
                    "glasses",
                    StringComparison.OrdinalIgnoreCase) &&
                legacyN.m_slices.TryGetValue(
                    "glasses001",
                    out legacySlice))
            {
                return CloneSlice(
                    legacySlice,
                    modernSlice.m_ID,
                    modernSlice.m_parentBoneID,
                    legacySlice.m_zOffset);
            }

            if (string.Equals(
                    modernSlice.m_ID,
                    "left_calf_pants",
                    StringComparison.OrdinalIgnoreCase) &&
                legacyN.m_slices.TryGetValue(
                    "left_calf",
                    out legacySlice))
            {
                return CloneSlice(
                    legacySlice,
                    modernSlice.m_ID,
                    modernSlice.m_parentBoneID,
                    modernSlice.m_zOffset);
            }

            if (string.Equals(
                    modernSlice.m_ID,
                    "right_calf_pants",
                    StringComparison.OrdinalIgnoreCase) &&
                legacyN.m_slices.TryGetValue(
                    "right_calf",
                    out legacySlice))
            {
                return CloneSlice(
                    legacySlice,
                    modernSlice.m_ID,
                    modernSlice.m_parentBoneID,
                    modernSlice.m_zOffset);
            }

            return CloneSlice(
                modernSlice,
                modernSlice.m_ID,
                modernSlice.m_parentBoneID,
                modernSlice.m_zOffset);
        }

        private static AnimModelSlice CloneSlice(
            AnimModelSlice source,
            string id,
            string boneId,
            int zPriority)
        {
            StringBuilder xml =
                new StringBuilder();
            xml.Append(
                "<AnimModelSlice BoneID=\"")
                .Append(EscapeXml(boneId))
                .Append(
                    "\" DefaultRotation=\"")
                .Append(
                    FormatFloat(source.m_rotation))
                .Append(
                    "\" ID=\"")
                .Append(EscapeXml(id))
                .Append(
                    "\" ZPriority=\"")
                .Append(
                    zPriority.ToString(
                        CultureInfo.InvariantCulture))
                .Append("\">");

            AppendVector(
                xml,
                "Pivot",
                source.m_pivot);
            AppendVector(
                xml,
                "TextureTopLeft",
                source.m_textureCoordsTopLeft);
            AppendVector(
                xml,
                "TextureBottomRight",
                source.m_textureCoordsBottomRight);
            AppendVector(
                xml,
                "TextureStep",
                source.m_textureStep);
            AppendVector(
                xml,
                "DefaultOffset",
                source.m_offset);
            xml.Append(
                "</AnimModelSlice>");

            using (StringReader stringReader =
                   new StringReader(
                       xml.ToString()))
            using (XmlReader reader =
                   XmlReader.Create(
                       stringReader))
            {
                if (!reader.ReadToDescendant(
                        "AnimModelSlice"))
                {
                    throw new InvalidOperationException(
                        "Could not clone male N slice '" +
                        id +
                        "'.");
                }

                return new AnimModelSlice(
                    reader);
            }
        }

        private static void ApplySliceOverride(
            AnimModelSliceGeometry geometry,
            AnimModelBoneTransform transform,
            Vector2f variation,
            SliceRuntimeInfo info)
        {
            float totalX =
                variation.m_x +
                transform.m_textureOffset.m_x;
            float totalY =
                variation.m_y +
                transform.m_textureOffset.m_y;

            int offsetX =
                (int)Math.Round(totalX);
            int offsetY =
                (int)Math.Round(totalY);

            CellInfo customCell = null;
            if (info.CustomCells != null &&
                Math.Abs(totalX - offsetX) <=
                    IntegerOffsetTolerance &&
                Math.Abs(totalY - offsetY) <=
                    IntegerOffsetTolerance)
            {
                info.CustomCells.TryGetValue(
                    MakeOffsetKey(
                        offsetX,
                        offsetY),
                    out customCell);
            }

            if (customCell != null)
            {
                geometry.m_texCoordsTopLeft =
                    customCell.UvTopLeft;
                geometry.m_texCoordsBottomRight =
                    customCell.UvBottomRight;

                float width =
                    (float)customCell.Width /
                    s_referenceTextureWidth;
                float height =
                    (float)customCell.Height /
                    s_referenceTextureHeight;
                float left =
                    -info.AnchorX *
                    width;
                float right =
                    (1f - info.AnchorX) *
                    width;
                float top =
                    info.AnchorTop *
                    height;
                float bottom =
                    -(1f - info.AnchorTop) *
                    height;

                geometry.m_topLeft =
                    geometry.m_matrix.transform(
                        new Vector2f(
                            left,
                            top));
                geometry.m_topRight =
                    geometry.m_matrix.transform(
                        new Vector2f(
                            right,
                            top));
                geometry.m_bottomLeft =
                    geometry.m_matrix.transform(
                        new Vector2f(
                            left,
                            bottom));
                geometry.m_bottomRight =
                    geometry.m_matrix.transform(
                        new Vector2f(
                            right,
                            bottom));
                return;
            }

            AnimModelSlice fallback =
                info.FallbackSlice;
            if (fallback == null)
            {
                return;
            }

            float u1 =
                fallback.m_textureCoordsTopLeft.m_x +
                totalX *
                fallback.m_textureStep.m_x;
            float v1 =
                fallback.m_textureCoordsTopLeft.m_y +
                totalY *
                fallback.m_textureStep.m_y;
            float u2 =
                fallback.m_textureCoordsBottomRight.m_x +
                totalX *
                fallback.m_textureStep.m_x;
            float v2 =
                fallback.m_textureCoordsBottomRight.m_y +
                totalY *
                fallback.m_textureStep.m_y;

            geometry.m_texCoordsTopLeft =
                MapFallbackUv(
                    u1,
                    v1);
            geometry.m_texCoordsBottomRight =
                MapFallbackUv(
                    u2,
                    v2);
        }

        private static Vector2f MapFallbackUv(
            float u,
            float v)
        {
            return new Vector2f(
                (s_fallbackOriginX +
                 u * s_referenceTextureWidth) /
                    s_runtimeTextureWidth,
                (s_fallbackOriginY +
                 v * s_referenceTextureHeight) /
                    s_runtimeTextureHeight);
        }

        private static void ComputeAnchor(
            AnimModelSlice slice,
            out float anchorX,
            out float anchorTop)
        {
            float left =
                Math.Min(
                    slice.m_textureCoordsTopLeft.m_x,
                    slice.m_textureCoordsBottomRight.m_x);
            float right =
                Math.Max(
                    slice.m_textureCoordsTopLeft.m_x,
                    slice.m_textureCoordsBottomRight.m_x);
            float bottom =
                Math.Min(
                    slice.m_textureCoordsTopLeft.m_y,
                    slice.m_textureCoordsBottomRight.m_y);
            float top =
                Math.Max(
                    slice.m_textureCoordsTopLeft.m_y,
                    slice.m_textureCoordsBottomRight.m_y);
            float width =
                right - left;
            float height =
                top - bottom;

            anchorX =
                width > Vector2f.EPSILON
                    ? (slice.m_pivot.m_x - left) /
                        width
                    : 0.5f;
            anchorTop =
                height > Vector2f.EPSILON
                    ? (top - slice.m_pivot.m_y) /
                        height
                    : 0.5f;
        }

        private static string ResolveVanillaTexturePath(
            string textureFile)
        {
            if (string.IsNullOrEmpty(textureFile))
            {
                return null;
            }

            string animated =
                Path.Combine(
                    Application.streamingAssetsPath,
                    Path.Combine(
                        "AnimatedModels",
                        Path.Combine(
                            "Textures",
                            textureFile)));
            if (File.Exists(animated))
            {
                return animated;
            }

            string generic =
                Path.Combine(
                    Application.streamingAssetsPath,
                    Path.Combine(
                        "Textures",
                        textureFile));
            return File.Exists(generic)
                ? generic
                : null;
        }

        private static Texture2D LoadReadableTexture(
            string path)
        {
            Texture2D texture =
                new Texture2D(
                    2,
                    2,
                    TextureFormat.ARGB32,
                    false);
            WWW request = null;

            try
            {
                request =
                    new WWW(
                        new Uri(path).AbsoluteUri);
                request.LoadImageIntoTexture(
                    texture);

                if (!string.IsNullOrEmpty(
                        request.error) ||
                    texture.width <= 0 ||
                    texture.height <= 0)
                {
                    throw new InvalidOperationException(
                        "Could not decode texture '" +
                        Path.GetFileName(path) +
                        "'.");
                }

                return texture;
            }
            catch
            {
                UnityEngine.Object.Destroy(
                    texture);
                throw;
            }
            finally
            {
                if (request != null)
                {
                    request.Dispose();
                }
            }
        }

        private static void AppendVector(
            StringBuilder xml,
            string name,
            Vector2f value)
        {
            xml.Append("<")
                .Append(name)
                .Append(" X=\"")
                .Append(
                    FormatFloat(value.m_x))
                .Append("\" Y=\"")
                .Append(
                    FormatFloat(value.m_y))
                .Append("\"/>");
        }

        private static string FormatFloat(
            float value)
        {
            return value.ToString(
                "R",
                CultureInfo.InvariantCulture);
        }

        private static string EscapeXml(
            string value)
        {
            return (value ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("\"", "&quot;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        private static int MakeOffsetKey(
            int x,
            int y)
        {
            return ((x & 0xFFFF) << 16) |
                   (y & 0xFFFF);
        }

        private static int CountCustomCells(
            Dictionary<string, Dictionary<int, CellInfo>>
                customCells)
        {
            int count = 0;
            foreach (KeyValuePair<string, Dictionary<int, CellInfo>>
                     pair in customCells)
            {
                count +=
                    pair.Value.Count;
            }

            return count;
        }

        private sealed class SliceRuntimeInfo
        {
            internal AnimModelSlice RuntimeSlice;
            internal AnimModelSlice FallbackSlice;
            internal Dictionary<int, CellInfo> CustomCells;
            internal float AnchorX;
            internal float AnchorTop;
        }

        private sealed class CellInfo
        {
            internal int Width;
            internal int Height;
            internal Vector2f UvTopLeft;
            internal Vector2f UvBottomRight;
        }

        private sealed class LoadedCell
        {
            internal string SliceId;
            internal int OffsetX;
            internal int OffsetY;
            internal Texture2D Texture;
        }

        private sealed class LoadedCellComparer :
            IComparer<LoadedCell>
        {
            internal static readonly LoadedCellComparer Instance =
                new LoadedCellComparer();

            public int Compare(
                LoadedCell left,
                LoadedCell right)
            {
                int byHeight =
                    right.Texture.height.CompareTo(
                        left.Texture.height);
                if (byHeight != 0)
                {
                    return byHeight;
                }

                int byWidth =
                    right.Texture.width.CompareTo(
                        left.Texture.width);
                if (byWidth != 0)
                {
                    return byWidth;
                }

                int bySlice =
                    string.Compare(
                        left.SliceId,
                        right.SliceId,
                        StringComparison.OrdinalIgnoreCase);
                if (bySlice != 0)
                {
                    return bySlice;
                }

                int byX =
                    left.OffsetX.CompareTo(
                        right.OffsetX);
                return byX != 0
                    ? byX
                    : left.OffsetY.CompareTo(
                        right.OffsetY);
            }
        }
    }
}
