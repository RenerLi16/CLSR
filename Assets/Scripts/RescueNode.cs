using UnityEngine;

[RequireComponent(typeof(Collider))]
public class RescueNode : MonoBehaviour
{
    [Header("Node Information")]
    public string nodeId = "A";
    public int depth = 1;
    public int difficulty = 1;
    public int survivorCount = 0;
    public bool explored = false;

    [Header("Interaction Colors")]
    public Color hoverColor = new Color(1f, 0.8f, 0.3f);
    public Color selectedColor = new Color(0.3f, 0.9f, 1f);

    private Renderer nodeRenderer;
    private Color normalColor;
    private bool selected;

    private void Awake()
    {
        nodeRenderer = GetComponentInChildren<Renderer>();

        if (nodeRenderer != null)
        {
            normalColor = nodeRenderer.material.color;
        }
    }

    private void OnMouseEnter()
    {
        if (!selected)
        {
            SetColor(hoverColor);
        }
    }

    private void OnMouseExit()
    {
        SetColor(selected ? selectedColor : normalColor);
    }

    private void OnMouseDown()
    {
        selected = !selected;
        SetColor(selected ? selectedColor : hoverColor);

        Debug.Log(
            "Selected node: " + nodeId +
            " | Depth: " + depth +
            " | Difficulty: " + difficulty +
            " | Survivors: " + survivorCount
        );
    }

    private void SetColor(Color newColor)
    {
        if (nodeRenderer != null)
        {
            nodeRenderer.material.color = newColor;
        }
    }
}