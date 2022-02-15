using Godot;
using System;

namespace Client.UI 
{
    public class UIResearch : Control
    {
#pragma warning disable CS0649 // Values are assigned in the editor
        //[Export] private readonly NodePath nodePathTextureRect;
        [Export] private readonly NodePath nodePathLabel;
        [Export] private readonly NodePath nodePathLabelPos;
#pragma warning restore CS0649 // Values are assigned in the editor

        public Control Column { get; set; }
        public Control Group { get; set; }

        public void Init(string name)
        {
            GetNode<Label>(nodePathLabel).Text = name;
            GetNode<Label>(nodePathLabelPos).Text = RectPosition.ToString();
        }
    }
}
