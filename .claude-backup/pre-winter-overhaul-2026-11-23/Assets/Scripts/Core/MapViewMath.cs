using System;

namespace CLSR.Presentation
{
    /// <summary>
    /// Pure camera math for the map view (screen &lt;-&gt; camera-local world
    /// units). Kept engine-free so zoom anchoring can be unit-tested. Zooming
    /// only changes presentation; it never touches simulation state.
    /// </summary>
    public static class MapViewMath
    {
        public const float MinZoomLevel = -2f;
        public const int StandardMaxZoomLevel = 4;
        public const float MagnificationPerStep = 0.15f;
        public const float CloseZoomMultiplier = 1.25f;

        /// <summary>
        /// Continuous version of the original stepped zoom: integer levels give
        /// exactly the magnifications the buttons always produced.
        /// </summary>
        public static float Magnification(float level)
        {
            float regular = Math.Min(level, StandardMaxZoomLevel);
            float magnification = 1f + regular * MagnificationPerStep;
            if (level > StandardMaxZoomLevel)
                magnification *= (float)Math.Pow(CloseZoomMultiplier, level - StandardMaxZoomLevel);
            return magnification;
        }

        public static float ClampLevel(float level, float maxLevel) =>
            Math.Max(MinZoomLevel, Math.Min(maxLevel, level));

        /// <summary>
        /// Camera-local offset (one axis) of a viewport position for an
        /// orthographic camera whose centre is at 0. <paramref name="normalized"/>
        /// is 0..1 across the full screen; use aspect = 1 for the vertical axis.
        /// </summary>
        public static float ScreenToLocal(float normalized, float orthographicSize, float aspect) =>
            (normalized - 0.5f) * 2f * orthographicSize * aspect;

        /// <summary>
        /// Pan offset (one axis) that keeps a camera-local anchor under the
        /// given viewport position after the camera is re-fitted at
        /// <paramref name="orthographicSize"/>. Matches GraphCameraFraming, where the
        /// camera shift is mapCentre - boardTarget - pan.
        /// </summary>
        public static float AnchoredPan(float mapCenter, float anchorLocal, float normalized,
            float boardCenterNormalized, float orthographicSize, float aspect) =>
            mapCenter - anchorLocal + (normalized - boardCenterNormalized) * 2f * orthographicSize * aspect;

        /// <summary>Camera shift (one axis) for a fitted map; the inverse used by tests.</summary>
        public static float CameraShift(float mapCenter, float boardCenterNormalized, float orthographicSize,
            float aspect, float pan) =>
            mapCenter - (boardCenterNormalized - 0.5f) * 2f * orthographicSize * aspect - pan;

        /// <summary>Frame-rate independent approach toward a target value.</summary>
        public static float Smooth(float current, float target, float deltaSeconds, float sharpness = 14f)
        {
            if (deltaSeconds <= 0f) return current;
            float next = target + (current - target) * (float)Math.Exp(-sharpness * deltaSeconds);
            return Math.Abs(next - target) < 0.0005f ? target : next;
        }
    }
}
