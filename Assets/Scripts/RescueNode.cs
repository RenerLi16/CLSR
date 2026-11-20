using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Collider), typeof(RescueNodeLabel))]
public class RescueNode : MonoBehaviour
{
    [Header("Node Information")]
    public string nodeId = "A";
    public int depth = 1;
    public int difficulty = 1;
    public int survivorCount = 0;
    public bool explored = false;
    public bool reached = false;
    public bool survivorsRescued = false;

    [Header("Interaction Colors")]
    public Color hoverColor = new Color(0.5f, 0.84f, 0.74f);
    public Color selectedColor = new Color(0.094f, 0.702f, 0.541f);
    public Color exploredColor = new Color(0.07f, 0.52f, 0.4f);
    public Color blockedColor = new Color(0.45f, 0.45f, 0.45f);

    private Renderer nodeRenderer;
    private Color initialColor;
    private Color normalColor;
    private bool selected;
    private bool mouseOver;

    private void Awake()
    {
        if (GetComponent<RescueNodeLabel>() == null)
        {
            gameObject.AddComponent<RescueNodeLabel>();
        }

        nodeRenderer = GetComponentInChildren<Renderer>();

        if (nodeRenderer != null)
        {
            initialColor = nodeRenderer.material.color;
            normalColor = initialColor;
        }
    }

    private void OnMouseEnter()
    {
        mouseOver = true;

        if (!selected && !ReviewStudyFlow.IsActive)
        {
            SetColor(hoverColor);
        }
    }

    private void OnMouseExit()
    {
        mouseOver = false;
        SetColor(selected ? selectedColor : normalColor);
    }

    private void OnMouseDown()
    {
        // The review UI selects on release so a drag can start over a node.
        if (ReviewStudyFlow.IsActive)
        {
            return;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        if (NodeSelectionManager.Instance != null)
        {
            NodeSelectionManager.Instance.SelectNode(this);
        }

        Debug.Log("Selected node: " + nodeId);
    }

    public void LoadDefinition(RescueNodeDefinition definition)
    {
        nodeId = definition.nodeId;
        depth = definition.depth;
        difficulty = definition.difficulty;
        survivorCount = definition.survivorCount;
        explored = false;
        reached = false;
        survivorsRescued = false;
    }
    
    public void SetSelected(bool value)
    {
        selected = value;

        if (selected)
        {
            SetColor(selectedColor);
        }
        else
        {
            SetColor(mouseOver && !ReviewStudyFlow.IsActive ? hoverColor : normalColor);
        }
    }

    public void RevealEncounter(bool passed)
    {
        explored = true;
        reached = reached || passed;
        normalColor = reached ? exploredColor : blockedColor;

        if (!selected)
        {
            SetColor(mouseOver && !ReviewStudyFlow.IsActive ? hoverColor : normalColor);
        }
    }

    public void MarkSurvivorsRescued()
    {
        survivorsRescued = true;
    }

    public void ResetDiscovery()
    {
        explored = false;
        reached = false;
        survivorsRescued = false;
        selected = false;
        mouseOver = false;
        normalColor = initialColor;
        SetColor(normalColor);
    }

    private void SetColor(Color newColor)
    {
        if (nodeRenderer != null)
        {
            nodeRenderer.material.color = newColor;
        }
    }
}
