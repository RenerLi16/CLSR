# Cedar Valley forest LOD prototypes

These visual-only models are integrated into Cedar Valley's forest. Units
are meters, Y is up, and origins are at the intended ground level. The
roughly 21 by 14 m sample patch has no ground slab; its roots share a flat
datum for the sample.

| Asset | Height | Triangles | Design |
| --- | ---: | ---: | --- |
| `Ridge_Spruce_Mid.obj` | 10.6 m | 160 | Tall, narrow, cool slate-green spruce |
| `Woodland_Pine_Mid.obj` | 8.5 m | 148 | Full, slightly warmer pine crown |
| `Wind_Bent_Sapling_Mid.obj` | 4.7 m | 92 | Leaning, uneven, pale gray-green sapling |
| `Woodland_Patch_Far.obj` | 11.55 m | 184 | Single combined mesh, 12 trees |

All models use one opaque material, `Cedar_Conifer_Atlas`, and the same small
sRGB RGB atlas. Use imported normals; no mesh colliders, lightmap UV generation,
or textures per tree. If Unity does not bind the OBJ material automatically,
create one shared material with `Cedar_Conifer_Atlas.png` as its base color
texture and remap the importers' single material slot to it. A neutral tint
preserves the tree variation. Use Clamp wrapping and avoid low-resolution
texture overrides that blur neighboring colors together.

`Woodland_Patch_Layout.json` contains the sample’s twelve authored local
placements at Y = 0. Use it for a matched near/far test; check the OBJ import
orientation before recreating positions. For live slopes, place each tree at
the sampled terrain height and combine the geometry from the same placements
for both detail tiers. Do not stamp the flat sample across roads or riverbanks.

The sample is 184 triangles versus 1,600 triangles for twelve matching mid
stand-ins (four of each). That is a mesh comparison, not a measured frame-rate
gain, nor a comparison to the scene's existing procedural batches. The mid
models are references/stand-ins, not a blanket replacement for the existing
cheap forest meshes.

## In the scene

- Overview shows one simplified canopy batch per visible forest chunk. It
  does not render the detailed per-tree shadow decals. Outer woodland is
  divided into cullable batches rather than one valley-wide mesh.
- Mid zoom keeps the same placements, using the imported silhouettes along
  trails and lighter crown meshes in the interior. Close zoom keeps the mid
  meshes and adds bough, snow, and trail-edge ground detail.
- The tree tint and placement are seeded. Thresholds use pixels per meter,
  with a small buffer between zooming in and out.
- Map cylinders, storage markers, and the map text keep their apparent
  size. The old marker bounds remain available for camera framing and the
  capsule follows the visible size for picking. Site and camp cylinders
  have a minimum display width scaled with the UI. Terrain and physical
  trails zoom normally. The existing screen-space hover plaques and dual
  route strokes retain their behavior.

To regenerate only these visuals, open and save Cedar Valley, then use
**Tools → Cedar Valley → Rebuild Forest and Map Overlays**. This does not
rebuild terrain, move the nodes, or alter the scenario.

## Local WebGL check

Same Chrome session and camera views at 1280×720. Triangles and draw calls
count the whole view, including UI, from the WebGL calls. Frame intervals
are browser measurements, not GPU timing.

| View | Triangles before → after | Draw calls before → after |
| --- | ---: | ---: |
| Overview | 922,650 → 880,978 | 463 → 441 |
| Middle | 1,059,478 → 607,826 | 152 → 163 |
| Close | 377,710 → 292,092 | 69 → 89 |

Average frame interval stayed about 16.7 ms before and after. The geometry
load was lower, but this run did not establish an FPS gain. Both 1280×720
and 960×540 reached all three tiers; raycasts hit all 16 on-screen sites
at overview and the sampled site at close zoom. The sample cylinder kept
the same apparent width at each zoom within each resolution.
