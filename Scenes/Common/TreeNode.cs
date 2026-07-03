using System.Collections.Generic;

public class TreeNode
{
    public int Id;
    public string Name;
    public IReadOnlyList<TreeNode> Children;
    public bool Selectable;

    public TreeNode(int id, string name, IReadOnlyList<TreeNode> children, bool selectable = true)
    {
        Id = id;
        Name = name;
        Children = children;
        Selectable = selectable;
    }
}
