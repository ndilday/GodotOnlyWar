using System.Collections.Generic;

namespace OnlyWar.Helpers.UI
{
    public enum TreeNodeKind
    {
        General,
        Fleet,
        Ship,
        Unit,
        Squad
    }

    /// <summary>
    /// A detached row in one of the simple id-keyed trees (fleet transfer, diplomacy). It carries
    /// only ids, text and an optional squad row, so a query can build a whole tree without handing
    /// a screen anything live.
    /// </summary>
    public class TreeNode
    {
        public int Id;
        public string Name;
        public IReadOnlyList<TreeNode> Children;
        public bool Selectable;
        public TreeNodeKind Kind;
        public SquadRowViewModel SquadRow;

        public TreeNode(
            int id,
            string name,
            IReadOnlyList<TreeNode> children,
            bool selectable = true,
            TreeNodeKind kind = TreeNodeKind.General,
            SquadRowViewModel squadRow = null)
        {
            Id = id;
            Name = name;
            Children = children;
            Selectable = selectable;
            Kind = kind;
            SquadRow = squadRow;
        }
    }
}
