using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Xiaohei
{
    internal static class PetMenu
    {
        private static readonly ResourceDictionary Skin = (ResourceDictionary)XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
    <Style x:Key='Surface' TargetType='Border'>
        <Setter Property='Background' Value='#FCFDF9'/>
        <Setter Property='BorderBrush' Value='#9AB29C'/>
        <Setter Property='BorderThickness' Value='1'/>
        <Setter Property='CornerRadius' Value='14'/>
        <Setter Property='Padding' Value='5'/>
    </Style>
    <ControlTemplate x:Key='MenuSkin' TargetType='ContextMenu'>
        <Border x:Name='MenuSurface' Style='{StaticResource Surface}' MinWidth='148'>
            <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle'/>
        </Border>
    </ControlTemplate>
    <ControlTemplate x:Key='ItemSkin' TargetType='MenuItem'>
        <Grid Background='Transparent'>
            <Border x:Name='Row' CornerRadius='9' Padding='10,0,8,0' MinHeight='33'
                    BorderBrush='Transparent' BorderThickness='1'>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width='*'/>
                        <ColumnDefinition Width='24'/>
                    </Grid.ColumnDefinitions>
                    <ContentPresenter ContentSource='Header' RecognizesAccessKey='True'
                                      VerticalAlignment='Center'/>
                    <Path x:Name='Arrow' Grid.Column='1' Visibility='Collapsed'
                          HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,3,0'
                          Data='M 1,1 L 5,5 L 1,9' Stroke='#667E69' StrokeThickness='1.7'
                          StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>
                    <Border x:Name='SelectedMark' Grid.Column='1' Visibility='Collapsed'
                            HorizontalAlignment='Right' VerticalAlignment='Center' Width='17' Height='17'
                            CornerRadius='6' Background='#365D45'>
                        <Path Data='M 1,5 L 4,8 L 10,1' Stroke='White' StrokeThickness='1.6'
                              StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'
                              HorizontalAlignment='Center' VerticalAlignment='Center'/>
                    </Border>
                </Grid>
            </Border>
            <Popup x:Name='PART_Popup' AllowsTransparency='True' Focusable='False'
                   Placement='Custom' PopupAnimation='None' PlacementTarget='{Binding RelativeSource={RelativeSource TemplatedParent}}'
                   IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
                <Border x:Name='SubmenuSurface' Style='{StaticResource Surface}' MinWidth='118'>
                    <ItemsPresenter x:Name='ItemsPresenter' KeyboardNavigation.DirectionalNavigation='Cycle'/>
                </Border>
            </Popup>
        </Grid>
        <ControlTemplate.Triggers>
            <Trigger Property='HasItems' Value='True'>
                <Setter TargetName='Arrow' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
                <Setter TargetName='SelectedMark' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsHighlighted' Value='True'>
                <Setter TargetName='Row' Property='Background' Value='#E5EDE1'/>
            </Trigger>
            <Trigger Property='IsSubmenuOpen' Value='True'>
                <Setter TargetName='Row' Property='Background' Value='#E5EDE1'/>
            </Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'>
                <Setter TargetName='Row' Property='BorderBrush' Value='#809C84'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='Row' Property='Opacity' Value='.48'/>
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>
    <ControlTemplate x:Key='HintSkin' TargetType='ToolTip'>
        <Border Style='{StaticResource Surface}' CornerRadius='8' Padding='9,5'>
            <ContentPresenter/>
        </Border>
    </ControlTemplate>
</ResourceDictionary>");

        internal static ControlTemplate HintTemplate { get { return (ControlTemplate)Skin["HintSkin"]; } }

        internal static void ApplySurface(ContextMenu menu, Window pet)
        {
            menu.FontFamily = new FontFamily("Microsoft YaHei UI");
            menu.FontSize = 13;
            menu.Foreground = new SolidColorBrush(Color.FromRgb(35, 54, 44));
            menu.Background = Brushes.Transparent;
            menu.Template = (ControlTemplate)Skin["MenuSkin"];
            StyleItems(menu, menu, pet);
        }

        internal static void Apply(ContextMenu menu, Window pet)
        {
            ApplySurface(menu, pet);
            ContextMenuService.SetPlacement(pet, PlacementMode.Custom);
            ContextMenuService.SetPlacementTarget(pet, pet);
            menu.PlacementTarget = pet;
            menu.Placement = PlacementMode.Custom;
            menu.CustomPopupPlacementCallback = delegate(Size size, Size target, Point offset)
            {
                return Locate(size, pet, pet, null);
            };

            var petHint = new ToolTip { Placement = PlacementMode.Custom, PlacementTarget = pet,
                Content = new TextBlock { Text = (string)pet.ToolTip, TextWrapping = TextWrapping.Wrap, MaxWidth = 210 },
                FontFamily = menu.FontFamily, Foreground = menu.Foreground, FontSize = 12,
                Template = HintTemplate, CustomPopupPlacementCallback = menu.CustomPopupPlacementCallback };
            ToolTipService.SetPlacement(pet, PlacementMode.Custom);
            ToolTipService.SetPlacementTarget(pet, pet);
            pet.ToolTip = petHint;
            menu.Opened += delegate { ToolTipService.SetIsEnabled(pet, false); petHint.IsOpen = false; };
            menu.Closed += delegate { ToolTipService.SetIsEnabled(pet, true); };
        }

        private static void StyleItems(ItemsControl parent, ContextMenu root, Window pet)
        {
            foreach (object child in parent.Items)
            {
                var item = child as MenuItem;
                if (item == null) continue;
                item.Template = (ControlTemplate)Skin["ItemSkin"];
                if (item.HasItems)
                {
                    item.Loaded += delegate
                    {
                        var popup = (Popup)item.Template.FindName("PART_Popup", item);
                        popup.CustomPopupPlacementCallback = delegate(Size size, Size target, Point offset)
                        {
                            return Locate(size, pet, item, root);
                        };
                    };
                }
                StyleItems(item, root, pet);
            }
        }

        private static Rect ScreenBounds(FrameworkElement element)
        {
            return new Rect(element.PointToScreen(new Point()),
                element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight)));
        }

        private static double Clamp(double value, double low, double high)
        {
            return Math.Max(low, Math.Min(value, high));
        }

        private static bool Overlap(Rect a, Rect b)
        {
            return a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
        }

        // WPF computes the custom-popup size and the displacement from the target in device pixels.
        private static CustomPopupPlacement[] Locate(Size size, Window pet, FrameworkElement target, ContextMenu root)
        {
            var avoid = ScreenBounds(pet);
            var anchor = ScreenBounds(target);
            var beside = root == null ? avoid : ScreenBounds(root);
            var monitor = Forms.Screen.FromPoint(new System.Drawing.Point(
                (int)(beside.Left + beside.Width / 2), (int)(beside.Top + beside.Height / 2)));
            var bounds = monitor.WorkingArea;
            var area = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            var scale = PresentationSource.FromVisual(pet).CompositionTarget.TransformToDevice.M11;
            double gap = root == null ? Math.Ceiling(7 * scale) : 0;

            // Opening away from the cat leaves a continuous path from the size row into its submenu.
            bool rightFirst = root == null || (beside.Left < avoid.Right && beside.Right > avoid.Left)
                ? area.Right - beside.Right >= beside.Left - area.Left
                : beside.Left >= avoid.Right;
            double right = beside.Right + gap, left = beside.Left - gap - size.Width;
            double first = rightFirst ? right : left, second = rightFirst ? left : right;
            double top = Clamp(anchor.Top, area.Top, area.Bottom - size.Height);
            double x = Clamp(anchor.Left, area.Left, area.Right - size.Width);
            double below = beside.Bottom + gap, above = beside.Top - gap - size.Height;
            bool downFirst = area.Bottom - beside.Bottom >= beside.Top - area.Top;
            if (root != null)
            {
                // Meet the actual size row, including when both flyouts open
                // toward the left. A pointer never needs to cross a sibling row.
                double rowRight = anchor.Right, rowLeft = anchor.Left - size.Width;
                var paths = new[] {
                    new Point(rightFirst ? rowRight : rowLeft, top), new Point(rightFirst ? rowLeft : rowRight, top),
                    new Point(x, anchor.Bottom), new Point(x, anchor.Top - size.Height)
                };
                foreach (var path in paths)
                {
                    var popup = new Rect(path, size);
                    if (area.Contains(popup) && !Overlap(popup, avoid)) return Relative(path, anchor);
                }
            }
            var candidates = new[] {
                new Point(first, top), new Point(second, top),
                new Point(x, downFirst ? below : above), new Point(x, downFirst ? above : below),
                new Point(first, avoid.Bottom), new Point(first, avoid.Top - size.Height),
                new Point(second, avoid.Bottom), new Point(second, avoid.Top - size.Height)
            };
            foreach (var candidate in candidates)
            {
                var popup = new Rect(candidate, size);
                if (!area.Contains(popup) || Overlap(popup, avoid) || Overlap(popup, beside)) continue;
                return Relative(candidate, anchor);
            }
            // On a very small work area, the submenu may have to cover part of its parent.
            // It must still leave the pet exposed.
            var outsidePet = PetLayout.Beside(area, avoid, size, size, 0);
            return Relative(outsidePet.Location, anchor);
        }

        private static CustomPopupPlacement[] Relative(Point screen, Rect anchor)
        {
            return new[] { new CustomPopupPlacement(new Point(screen.X - anchor.X, screen.Y - anchor.Y), PopupPrimaryAxis.None) };
        }
    }
}
