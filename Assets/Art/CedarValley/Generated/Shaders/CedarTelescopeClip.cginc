// Cedar Valley telescope sightline. While a sighting is running the camp telescope looks across
// a few hundred metres of forest; vegetation and props nearer than the site's clearing would
// fill the frame. TelescopeSighting sets _TelescopeClipDepth to an eye depth just short of the
// site, and Winter Lit / Winter Foliage surfaces in front of it are skipped. Terrain uses other
// shaders and stays solid, so ground occlusion is real. 0 (the default) turns this off.
#ifndef CEDAR_TELESCOPE_CLIP_INCLUDED
#define CEDAR_TELESCOPE_CLIP_INCLUDED

float _TelescopeClipDepth;

inline void TelescopeSightlineClip(float3 wpos)
{
    // Perspective views only: orthographic passes (directional shadow maps) keep every caster.
    if (_TelescopeClipDepth > 0.0 && UNITY_MATRIX_P[3][3] < 0.5)
        clip(-mul(UNITY_MATRIX_V, float4(wpos, 1.0)).z - _TelescopeClipDepth);
}

#endif
