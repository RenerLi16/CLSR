using UnityEngine;

// The forest consists of generated geometry, not hand-authored YAML. Unity's native
// binary representation avoids shipping giant hexadecimal vertex buffers in source.
[PreferBinarySerialization]
public sealed class CedarForestMeshPack : ScriptableObject { }
