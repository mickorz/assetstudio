using System.Collections.Generic;
using System.Windows.Forms;

namespace AssetStudio.GUI
{
    public class ContainerTreeNode : TreeNode
    {
        public new string FullPath;
        public List<AssetItem> Assets = new List<AssetItem>();

        public ContainerTreeNode(string name, string fullPath)
        {
            Text = name;
            FullPath = fullPath;
        }
    }
}
