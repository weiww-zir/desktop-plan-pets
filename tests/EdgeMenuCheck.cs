using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Xiaohei
{
    internal static class EdgeMenuCheck
    {
        static void Check(bool ok, string detail)
        {
            if (!ok) throw new InvalidOperationException(detail);
            Console.WriteLine("PASS " + detail);
        }

        static void GeometryChecks()
        {
            var work = new Rect(0, 0, 1920, 1040);
            var samples = new[] {
                new { Box = new Rect(-19, 200, 92, 90), X = 0.0, Y = 200.0 },
                new { Box = new Rect(1890, 200, 92, 90), X = 1828.0, Y = 200.0 },
                new { Box = new Rect(300, -13, 92, 90), X = 300.0, Y = 0.0 },
                new { Box = new Rect(300, 1000, 92, 90), X = 300.0, Y = 950.0 },
                new { Box = new Rect(300, 400, 92, 90), X = 300.0, Y = 400.0 }
            };
            foreach (var sample in samples)
            {
                var location = PetDock.Place(sample.Box, work);
                Check(location.X == sample.X && location.Y == sample.Y &&
                    work.Contains(new Rect(location, sample.Box.Size)), "full-pet placement " + location);
            }
            var secondary = new Rect(-1280, -100, 1280, 800);
            var size = new Size(92,90);
            var point = PetDock.Place(new Rect(-30,-150,size.Width,size.Height), secondary);
            Check(secondary.Contains(new Rect(point,size)) && point == new Point(-92,-100),
                "negative-coordinate monitor preserves complete pet");
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NativeRect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out NativeRect bounds);

        static Rect Bounds(Visual ownWindow)
        {
            var source = (HwndSource)PresentationSource.FromVisual(ownWindow);
            NativeRect r;
            if (source == null || !GetWindowRect(source.Handle, out r))
                throw new InvalidOperationException("Test window has no valid native rectangle");
            return new Rect(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top);
        }

        static void Separate(Rect pet, Rect panel, string scenario)
        {
            var overlap = Rect.Intersect(pet, panel);
            Check(overlap.IsEmpty || overlap.Width <= 0 || overlap.Height <= 0,
                scenario + "; pet " + pet + ", UI " + panel);
        }

        static void Settle()
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
            Flush();
        }

        static void Flush()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate {}));
        }

        static MenuItem Item(ItemsControl menu, string header)
        {
            return menu.Items.OfType<MenuItem>().Single(x => (string)x.Header == header);
        }

        static void OpenMenu(DesktopPet pet)
        {
            var menu = pet.ContextMenu;
            menu.IsOpen = true;
            Flush();
            menu.UpdateLayout();
        }

        static T Part<T>(Control control, string name) where T : FrameworkElement
        {
            control.ApplyTemplate();
            return (T)control.Template.FindName(name, control);
        }

        static void CheckPopupPalette(Border surface, string name)
        {
            var fill = surface == null ? null : surface.Background as SolidColorBrush;
            var edge = surface == null ? null : surface.BorderBrush as SolidColorBrush;
            Check(fill != null && fill.Color == Color.FromRgb(0xD6, 0xDF, 0xD4) &&
                edge != null && edge.Color == Color.FromRgb(0x8D, 0xA3, 0x8F) && surface.BorderThickness.Top > 0,
                name + " uses the shared popup fill and edge");
        }

        static byte Alpha(BitmapSource bitmap, int x, int y)
        {
            var sample = new byte[4];
            bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), sample, 4, 0);
            return sample[3];
        }

        static RenderTargetBitmap CapturePopup(HwndSource source, string file)
        {
            var root = (FrameworkElement)source.RootVisual;
            root.UpdateLayout();
            var image = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * 2),
                (int)Math.Ceiling(root.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
            image.Render(root);
            int right = image.PixelWidth - 1, bottom = image.PixelHeight - 1;
            Console.WriteLine("Popup {0}: root {1} {2}x{3}, alpha TL/TR/BL/BR/mid/center {4}/{5}/{6}/{7}/{8}/{9}",
                Path.GetFileName(file), root.GetType().Name, root.ActualWidth, root.ActualHeight, Alpha(image,0,0),
                Alpha(image,right,0), Alpha(image,0,bottom), Alpha(image,right,bottom),
                Alpha(image,right/2,8), Alpha(image,right/2,bottom/2));
            var diagnostic = new PngBitmapEncoder();
            diagnostic.Frames.Add(BitmapFrame.Create(image));
            using (var target = File.Create(file)) diagnostic.Save(target);
            Check(Alpha(image,0,0) == 0 && Alpha(image,right,0) == 0 &&
                Alpha(image,0,bottom) == 0 && Alpha(image,right,bottom) == 0 &&
                Alpha(image,right / 2,8) > 245, "rounded popup has four transparent corners and an opaque surface");
            return image;
        }

        static void Preview(DesktopPet pet, BitmapSource main, Rect menuRect, BitmapSource sub, Rect subRect, string path)
        {
            var fromPixels = PresentationSource.FromVisual(pet).CompositionTarget.TransformFromDevice;
            var petRect = Rect.Transform(Bounds(pet), fromPixels);
            menuRect = Rect.Transform(menuRect, fromPixels);
            subRect = Rect.Transform(subRect, fromPixels);
            var area = Rect.Union(Rect.Union(menuRect,subRect),petRect);
            area.Inflate(10,10);
            double width = area.Width, height = area.Height;
            var cat = new RenderTargetBitmap((int)Math.Ceiling(pet.ActualWidth*2),
                (int)Math.Ceiling(pet.ActualHeight*2),192,192,PixelFormats.Pbgra32);
            cat.Render((Visual)pet.Content);
            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
            {
                ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(219,229,220)), null, new Rect(0,0,width,height));
                ctx.PushTransform(new TranslateTransform(-area.Left,-area.Top));
                ctx.DrawImage(cat, petRect);
                ctx.DrawImage(main, menuRect);
                ctx.DrawImage(sub, subRect);
                ctx.Pop();
            }
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width*2), (int)Math.Ceiling(height*2),
                192, 192, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) png.Save(stream);
        }

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                GeometryChecks();
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var folder = Path.Combine(args[0], "edge-check-" + Guid.NewGuid().ToString("N"));
                var store = new Store(folder);
                // System-DPI-aware app: convert the rightmost monitor to its WPF coordinate space.
                var screen = Forms.Screen.AllScreens.OrderByDescending(x => x.Bounds.Right).First().WorkingArea;
                double scale;
                using (var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                    scale = 96.0 / graphics.DpiX;
                double edgeX = screen.Right * scale;
                store.PetLeft = edgeX - store.PetWidth / 2;
                store.PetTop = (screen.Top + screen.Height / 2) * scale - 40;
                store.Save();
                var liveStore = new Store(folder);
                var pet = new DesktopPet(liveStore) { ShowActivated = false };
                pet.Show();
                Flush();
                var workPx = new Rect(screen.Left, screen.Top, screen.Width, screen.Height);
                var work = new Rect(screen.Left*scale, screen.Top*scale, screen.Width*scale, screen.Height*scale);
                Check(Math.Abs(pet.Left + pet.Width - edgeX) < 1 && workPx.Contains(Bounds(pet)) &&
                    ((UIElement)pet.Content).Clip == null, "legacy half-parked pet restores fully visible");
                var petPeer = UIElementAutomationPeer.CreatePeerForElement(pet);
                Check(pet.ToolTip == null && ((FrameworkElement)pet.Content).ToolTip == null,
                    "hovering the pet has no visual help popup");
                Check(petPeer.GetHelpText().Contains("点击打开手帐") && petPeer.GetHelpText().Contains("按住拖动"),
                    "the pet's interaction instructions are available to screen readers");
                OpenMenu(pet);
                var menu = pet.ContextMenu;
                var sizes = Item(menu, "桌宠大小");
                sizes.IsSubmenuOpen = true;
                Flush();
                var active = Item(sizes, "小巧（92，默认）");
                var other = Item(sizes, "适中（120）");
                var main = (HwndSource)PresentationSource.FromVisual(menu);
                var sub = (HwndSource)PresentationSource.FromVisual(active);
                Check(main != null && sub != null && main.Handle != sub.Handle &&
                    main.Handle != new WindowInteropHelper(pet).Handle && sub.Handle != IntPtr.Zero &&
                    main.CompositionTarget.UsesPerPixelOpacity && sub.CompositionTarget.UsesPerPixelOpacity,
                    "menu and submenu open in separate transparent native popup windows");
                CheckPopupPalette(Part<Border>(menu, "MenuSurface"), "pet context menu");
                CheckPopupPalette(Part<Border>(sizes, "SubmenuSurface"), "size submenu");
                var peer = UIElementAutomationPeer.CreatePeerForElement(active);
                var toggle = peer.GetPattern(PatternInterface.Toggle) as IToggleProvider;
                Check(peer.GetName() == (string)active.Header && toggle != null &&
                    toggle.ToggleState == ToggleState.On && !other.IsChecked &&
                    Part<Border>(active,"SelectedMark").IsVisible && !Part<Border>(other,"SelectedMark").IsVisible,
                    "current size exposes a checkmark, accessible name and checked UI Automation state");
                var row = Part<Border>(sizes, "Row");
                Check(row.CornerRadius.TopLeft > 0 && row.Background != null &&
                    sizes.Template.Triggers.OfType<Trigger>().Any(x => x.Property == MenuItem.IsHighlightedProperty),
                    "submenu host and pointer-highlight state use the rounded row template");
                Settle();
                var parentFrame = CapturePopup(main, Path.Combine(args[0],"MenuDiagMain.png"));
                var childFrame = CapturePopup(sub, Path.Combine(args[0],"MenuDiagSub.png"));
                Separate(Bounds(pet),Bounds(menu),"restored edge, main menu");
                Separate(Bounds(pet),Bounds(active),"restored edge, size submenu");
                Preview(pet, parentFrame, Bounds(menu), childFrame, Bounds(active), Path.Combine(args[0], "MenuPreview.png"));
                other.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, other));
                menu.IsOpen = false;
                Flush();
                var saved = new Store(folder);
                Check(pet.Width == 120 && saved.PetWidth == 120 && workPx.Contains(Bounds(pet)) &&
                    Math.Abs(pet.Left-saved.PetLeft)<1 && ((UIElement)pet.Content).Clip == null,
                    "size menu action keeps the enlarged pet fully visible and saves position");
                OpenMenu(pet);
                sizes.IsSubmenuOpen = true;
                Flush();
                Check(other.IsChecked && !active.IsChecked, "reopened menu marks updated size");
                sizes.IsSubmenuOpen = false;
                Item(menu,"让小黑喵一声").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                menu.IsOpen = false;
                Settle();
                foreach (double y in new[] {0.0,0.5,1.0})
                foreach (double x in new[] {0.0,0.5,1.0})
                {
                    pet.Left = work.Left + Math.Floor((work.Width-pet.Width)*x);
                    pet.Top = work.Top + Math.Floor((work.Height-pet.Height)*y);
                    Flush();
                    var scenario = String.Format("monitor ({0},{1})", x, y);
                    Check(workPx.Contains(Bounds(pet)) && ((UIElement)pet.Content).Clip == null,
                        scenario + " shows complete pet");
                    OpenMenu(pet);
                    sizes.IsSubmenuOpen = true;
                    Settle();
                    Separate(Bounds(pet),Bounds(menu),scenario + " main menu");
                    Separate(Bounds(pet),Bounds(active),scenario + " size submenu");
                    sizes.IsSubmenuOpen = false;
                    menu.IsOpen = false;
                    Settle();
                    if (x == 0.5 && y == 0.5)
                    {
                        liveStore.PlannerWidth = Math.Floor(work.Width*0.85);
                        liveStore.PlannerHeight = Math.Floor(work.Height*0.82);
                    }
                    pet.ShowPlanner();
                    Settle();
                    var planner = pet.OwnedWindows.OfType<Planner>().Single();
                    Separate(Bounds(pet),Bounds(planner),scenario + " opened planner");
                    if (x == 0.5 && y == 0.5)
                    {
                        planner.Left = pet.Left;
                        planner.Top = pet.Top;
                        Settle();
                        Separate(Bounds(pet),Bounds(planner),"planner after moving onto pet");
                        planner.Width = work.Width * 0.85;
                        planner.Height = work.Height * 0.82;
                        Settle();
                        Separate(Bounds(pet),Bounds(planner),"planner after a large resize");
                        pet.Left = planner.Left+8;
                        pet.Top = planner.Top+8;
                        Settle();
                        Separate(Bounds(pet),Bounds(planner),"after moving pet onto the planner");
                    }
                    planner.Close();
                    Flush();
                }
                Item(menu,"退出小黑").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(!pet.IsVisible, "exit menu action closes test pet");
                app.Shutdown();
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
