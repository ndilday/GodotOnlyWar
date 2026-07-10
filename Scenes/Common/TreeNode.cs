using System.Collections.Generic;

public enum TreeNodeKind
{
    General,
    Fleet,
    Ship,
    Unit,
    Squad
}

public class TreeNode
{
    public int Id;
    public string Name;
    public IReadOnlyList<TreeNode> Children;
    public bool Selectable;
    public TreeNodeKind Kind;

    public TreeNode(int id, string name, IReadOnlyList<TreeNode> children, bool selectable = true, TreeNodeKind kind = TreeNodeKind.General)
    {
        Id = id;
        Name = name;
        Children = children;
        Selectable = selectable;
        Kind = kind;
    }
}
