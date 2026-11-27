using System;

namespace CLSR.Presentation
{
    /// <summary>
    /// Engine-free optics and timing for the Cedar Valley telescope sighting, kept here so the
    /// focus behaviour can be unit-tested offline. Presentation only: nothing here reads or
    /// changes simulation state.
    ///
    /// Focus is a distance along the camera's viewing axis. Blur follows the thin-lens circle of
    /// confusion, c = A * |1/s - 1/d| * f, with s the focus distance, d the view-axis depth of a
    /// surface, f the focal length in pixels and A an effective aperture. A is fixed for the
    /// whole sighting and chosen from the site's distance, so a given depth offset from the
    /// focal plane blurs by about the same number of pixels at every site and resolution.
    /// </summary>
    public static class TelescopeOptics
    {
        /// <summary>Length of the viewing window once the zoom has settled.</summary>
        public const float ViewSeconds = 10f;

        /// <summary>Screen height the pixel constants below are defined against.</summary>
        public const float ReferenceScreenHeight = 1080f;

        /// <summary>Blur diameter, in reference pixels, per metre of depth away from the focal plane.</summary>
        public const float BlurPixelsPerMetre = 2.4f;

        /// <summary>Largest blur diameter, in reference pixels.</summary>
        public const float MaxBlurPixels = 28f;

        /// <summary>Focus travel either side of the site's aim point (metres along the view axis).</summary>
        public const float DefaultFocusRange = 16f;

        /// <summary>Wheel turns for the whole focus travel (the visible half of the rim covers about 55%).</summary>
        public const float TurnsForFullRange = 0.9f;

        // ---------------------------------------------------------------- transition timing
        public const float FlySeconds = 1.7f;
        public const float AimSeconds = 0.85f;
        public const float ZoomSeconds = 1.9f;
        public const float SettleSeconds = 0.25f;
        public const float ReturnSeconds = 0.8f;
        public static float ArrivalSeconds => FlySeconds + AimSeconds + ZoomSeconds + SettleSeconds;

        /// <summary>Focal length in pixels for a vertical field of view.</summary>
        public static float FocalPixels(float screenHeightPixels, float verticalFovDegrees) =>
            screenHeightPixels / (2f * (float)Math.Tan(verticalFovDegrees * 0.5 * Math.PI / 180.0));

        /// <summary>Vertical field of view that shows a frame of the given height at a distance.</summary>
        public static float FovForFrame(float frameHeightMetres, float distanceMetres) =>
            (float)(2.0 * Math.Atan(frameHeightMetres * 0.5 / Math.Max(0.01, distanceMetres)) * 180.0 / Math.PI);

        /// <summary>
        /// Effective aperture for a sighting at <paramref name="siteDistance"/> framed
        /// <paramref name="frameHeightMetres"/> high: a surface one metre off the focal plane near
        /// the site blurs by about <see cref="BlurPixelsPerMetre"/> reference pixels.
        /// From c ~= A * delta / s^2 * f and f ~= H * s / frame.
        /// </summary>
        public static float Aperture(float siteDistance, float frameHeightMetres) =>
            BlurPixelsPerMetre * siteDistance * frameHeightMetres / ReferenceScreenHeight;

        /// <summary>
        /// Signed blur diameter in pixels: positive behind the focal plane, negative in front,
        /// zero on it; magnitude grows continuously with distance from the plane and is capped.
        /// </summary>
        public static float SignedBlurPixels(float depth, float focus, float aperture, float focalPixels, float maxPixels)
        {
            if (depth <= 0f || focus <= 0f) return 0f;
            float c = aperture * (1f / focus - 1f / depth) * focalPixels;
            return Math.Max(-maxPixels, Math.Min(maxPixels, c));
        }

        public static float MaxBlurFor(float screenHeightPixels) =>
            MaxBlurPixels * screenHeightPixels / ReferenceScreenHeight;

        /// <summary>Wheel rotation (radians) for a focus distance; the dial shows this as ridge travel.</summary>
        public static float WheelAngle(float focus, float near, float far)
        {
            float span = Math.Max(0.01f, far - near);
            return (focus - near) / span * TurnsForFullRange * 2f * (float)Math.PI;
        }

        /// <summary>
        /// Focus change for a drag of the dial rim (pixels; positive = up, toward far). The rim
        /// surface moves with the pointer: a drag of d pixels turns a rim of projected radius R
        /// by d / R radians.
        /// </summary>
        public static float FocusForDrag(float dragPixels, float rimRadiusPixels, float near, float far) =>
            dragPixels / Math.Max(1f, rimRadiusPixels) / (TurnsForFullRange * 2f * (float)Math.PI) * (far - near);

        /// <summary>Focus change for mouse wheel notches.</summary>
        public static float FocusForScroll(float notches, float near, float far) => notches * (far - near) * 0.03f;

        public static float Clamp(float focus, float near, float far) => Math.Max(near, Math.Min(far, focus));

        /// <summary>Critically damped approach used for the dial (frame-rate independent).</summary>
        public static float Approach(float current, float target, float deltaSeconds, float rate = 14f) =>
            target + (current - target) * (float)Math.Exp(-rate * Math.Max(0f, deltaSeconds));

        public static float SmoothStep(float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return t * t * (3f - 2f * t);
        }

        public static float SmootherStep(float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return t * t * t * (t * (6f * t - 15f) + 10f);
        }
    }
}
