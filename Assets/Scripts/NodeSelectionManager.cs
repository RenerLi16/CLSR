using UnityEngine;

public class NodeSelectionManager : MonoBehaviour
{
    public static NodeSelectionManager Instance { get; private set; }

    public RescueNode SelectedNode { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    public void SelectNode(RescueNode newNode)
    {
        if (SelectedNode == newNode)
        {
            SelectedNode.SetSelected(false);
            SelectedNode = null;
            return;
        }

        if (SelectedNode != null)
        {
            SelectedNode.SetSelected(false);
        }

        SelectedNode = newNode;
        SelectedNode.SetSelected(true);
    }
}