using UnityEngine;

/// <summary>
/// A survivor figure placed at a Cedar Valley site for the telescope sighting. Figures are
/// authored inactive (Tools > Cedar Valley > Telescope > Build Lookout And Sites) and only the
/// observed site's figures are switched on, for the length of one sighting, so the planning map
/// never shows them. Animation is procedural: walking figures follow a closed path through the
/// site clearing, standing and seated figures shift, breathe and look around.
/// Presentation only: nothing here reads or changes game state.
/// </summary>
public sealed class SurvivorFigure : MonoBehaviour
{
    public enum Pose { Standing, Walking, Seated }

    public Pose pose = Pose.Standing;
    public Transform pelvis, torso, head, armL, armR, legL, legR;
    [Tooltip("Walking: closed path in world space, ground height included.")]
    public Vector3[] path = new Vector3[0];
    public float walkSpeed = 1.05f;
    [Tooltip("Desynchronises idle motion between figures.")]
    public float phase;

    private float pathLength, travelled;
    private Vector3 pelvisRest;
    private Quaternion torsoRest, headRest, armLRest, armRRest, legLRest, legRRest;
    private bool cached;

    private void Cache()
    {
        if (cached) return;
        cached = true;
        if (pelvis != null) pelvisRest = pelvis.localPosition;
        torsoRest = torso != null ? torso.localRotation : Quaternion.identity;
        headRest = head != null ? head.localRotation : Quaternion.identity;
        armLRest = armL != null ? armL.localRotation : Quaternion.identity;
        armRRest = armR != null ? armR.localRotation : Quaternion.identity;
        legLRest = legL != null ? legL.localRotation : Quaternion.identity;
        legRRest = legR != null ? legR.localRotation : Quaternion.identity;
        pathLength = 0f;
        for (int i = 0; i < path.Length; i++) pathLength += Vector3.Distance(path[i], path[(i + 1) % path.Length]);
    }

    private void OnEnable()
    {
        Cache();
        // every sighting starts the walk at the same point of the loop
        travelled = pathLength * Mathf.Repeat(phase, 1f);
        Tick(0f);
    }

    private void Update() => Tick(Time.deltaTime);

    private void Tick(float dt)
    {
        float t = Time.time + phase * 7.3f;
        switch (pose)
        {
            case Pose.Walking: Walk(dt); break;
            case Pose.Standing: Stand(t); break;
            case Pose.Seated: Sit(t); break;
        }
    }

    private void Walk(float dt)
    {
        if (path.Length < 2 || pathLength <= 0f) return;
        travelled = Mathf.Repeat(travelled + walkSpeed * dt, pathLength);
        float d = travelled;
        for (int i = 0; i < path.Length; i++)
        {
            Vector3 a = path[i], b = path[(i + 1) % path.Length];
            float seg = Vector3.Distance(a, b);
            if (d > seg) { d -= seg; continue; }
            Vector3 p = Vector3.Lerp(a, b, seg > 0f ? d / seg : 0f);
            transform.position = p;
            Vector3 heading = b - a;
            heading.y = 0f;
            if (heading.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(heading),
                    dt <= 0f ? 1f : 1f - Mathf.Exp(-6f * dt));
            break;
        }
        // stride: about 0.7 m per step
        float cycle = travelled / 1.4f * Mathf.PI * 2f;
        float swing = Mathf.Sin(cycle);
        Rotate(legL, legLRest, swing * 27f);
        Rotate(legR, legRRest, -swing * 27f);
        Rotate(armL, armLRest, -swing * 22f);
        Rotate(armR, armRRest, swing * 22f);
        if (pelvis != null) pelvis.localPosition = pelvisRest + Vector3.up * (Mathf.Abs(Mathf.Cos(cycle)) * 0.035f - 0.02f);
        if (torso != null) torso.localRotation = torsoRest * Quaternion.Euler(6f, Mathf.Sin(cycle) * 4f, 0f);
        if (head != null) head.localRotation = headRest * Quaternion.Euler(0f, Mathf.Sin(cycle * 0.11f) * 18f, 0f);
    }

    private void Stand(float t)
    {
        float shift = Mathf.Sin(t * 0.7f);
        if (torso != null) torso.localRotation = torsoRest * Quaternion.Euler(Mathf.Sin(t * 1.9f) * 1.2f, 0f, shift * 2.5f);
        if (head != null) head.localRotation = headRest * Quaternion.Euler(Mathf.Sin(t * 0.5f) * 6f, Mathf.Sin(t * 0.33f) * 35f, 0f);
        // now and then rubs hands / stamps against the cold: small arm and leg movement
        float stamp = Mathf.Max(0f, Mathf.Sin(t * 0.9f));
        Rotate(armL, armLRest, -stamp * 14f);
        Rotate(armR, armRRest, -stamp * 14f);
        Rotate(legL, legLRest, stamp * stamp * 9f);
        Rotate(legR, legRRest, 0f);
    }

    private void Sit(float t)
    {
        if (torso != null) torso.localRotation = torsoRest * Quaternion.Euler(Mathf.Sin(t * 1.6f) * 1.5f, 0f, 0f);
        if (head != null) head.localRotation = headRest * Quaternion.Euler(Mathf.Sin(t * 0.4f) * 5f, Mathf.Sin(t * 0.27f) * 28f, 0f);
        Rotate(armL, armLRest, Mathf.Sin(t * 0.8f) * 3f);
        Rotate(armR, armRRest, -Mathf.Sin(t * 0.8f) * 3f);
    }

    private static void Rotate(Transform limb, Quaternion rest, float pitch)
    {
        if (limb != null) limb.localRotation = rest * Quaternion.Euler(pitch, 0f, 0f);
    }
}
