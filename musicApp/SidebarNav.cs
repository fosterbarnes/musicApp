using System.Windows;

namespace musicApp
{
    public static class SidebarNav
    {
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.RegisterAttached(
                "IsActive",
                typeof(bool),
                typeof(SidebarNav),
                new FrameworkPropertyMetadata(false));

        public static bool GetIsActive(DependencyObject obj) =>
            (bool)obj.GetValue(IsActiveProperty);

        public static void SetIsActive(DependencyObject obj, bool value) =>
            obj.SetValue(IsActiveProperty, value);
    }
}
