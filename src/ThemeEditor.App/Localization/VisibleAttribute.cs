// --------------------------------------------------
// 3DS Theme Editor - VisibleAttribute.cs
// --------------------------------------------------

using System;

namespace ThemeEditor.App.Localization
{
    [AttributeUsage(AttributeTargets.All)]
    public class VisibleAttribute : Attribute
    {
        public bool Visible { get; }

        public VisibleAttribute(bool visible)
        {
            Visible = visible;
        }
    }
}
