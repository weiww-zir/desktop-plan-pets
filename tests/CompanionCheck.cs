using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Xiaohei
{
    internal static class CompanionCheck
    {
        private static void Require(bool ok, string description)
        {
            if (!ok) throw new InvalidOperationException(description);
            Console.WriteLine("PASS " + description);
        }

        private static void PointerGestures()
        {
            var pointer = new PetPointer();
            var monitor = new Rect(-1920, 120, 1920, 1080);
            var size = new Size(92, 90);
            double now = 1;
            Func<int, bool, bool> sample = (x, pressed) => {
                now += .035;
                return pointer.Sample(new Point(-1000 + x, 600), monitor, new Point(x, 30),
                    size, now, 0, 0, pressed, 3);
            };
            Require(!sample(20, false) && !sample(21, false), "a stationary pointer and a one-pixel nudge are quiet");
            sample(30, false); sample(42, false); sample(55, false);
            Require(!pointer.Petting, "passing once across the cat does not pet it");
            sample(40, false); sample(26, false);
            Require(!pointer.Petting, "a single reversal is still a pass");
            sample(43, false);
            Require(pointer.Petting, "two deliberate reversals on the head trigger petting");
            Require(Math.Abs(pointer.MouseX) < .02 && pointer.GazeY < 0, "desktop direction and local gaze use their own coordinates on a left-hand display");
            sample(25, true); sample(52, true); sample(23, true);
            Require(!pointer.Petting, "dragging does not pet the cat");
            sample(78, false); sample(88, false); sample(79, false);
            Require(!pointer.Petting, "crossing the empty area outside the head is quiet");
        }

        private static void Wait(int milliseconds)
        {
            var loop = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += delegate { timer.Stop(); loop.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(loop);
        }

        private static T Field<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T) yield return (T)child;
                foreach (var item in Descendants<T>(child)) yield return item;
            }
        }

        private static PetArt.TypingKeyboard VisibleKeyboard(DependencyObject root)
        {
            return Descendants<PetArt.TypingKeyboard>(root).FirstOrDefault(d => d.IsVisible);
        }

        private sealed class EyeFrame
        {
            internal Rect[] Pupils;
            internal int White;
            internal bool HasBrow;

            internal EyeFrame(FrameworkElement scene, FrameworkElement ear)
            {
                scene.Measure(new Size(480, 455));
                scene.Arrange(new Rect(0, 0, 480, 455));
                scene.UpdateLayout();
                var paths = Descendants<Path>(scene).ToList();
                var pupils = paths.Where(p => p.Stroke is SolidColorBrush &&
                    ((SolidColorBrush)p.Stroke).Color == Color.FromRgb(61, 42, 32)).OrderBy(p => p.Data.Bounds.Left).ToArray();
                if (pupils.Length != 2) throw new InvalidOperationException("Expected two seated pupils, found " + pupils.Length);
                var face = (Visual)VisualTreeHelper.GetParent(pupils[0]);
                while (!face.IsAncestorOf(ear)) face = (Visual)VisualTreeHelper.GetParent(face);
                Pupils = pupils.Select(p => p.TransformToAncestor(face).TransformBounds(p.Data.Bounds)).ToArray();
                // Look for separate strokes over the eyes, excluding the rims, ears and the closed lids.
                HasBrow = paths.Any(p => p.Fill == null && p.Stroke != null && p.Data != null && !p.Data.Bounds.IsEmpty && p.Data.Bounds.Top > 130 &&
                    p.Data.Bounds.Bottom < 180 && p.Data.Bounds.Left > 210 && p.Data.Bounds.Right < 523);
                // Sample the left eye in face coordinates so head tilts and breathing cannot mimic an eyelid.
                var brush = new VisualBrush(face) { ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(203, 120, 166, 212), Stretch = Stretch.Fill };
                var eye = new DrawingVisual();
                using (var dc = eye.RenderOpen()) dc.DrawRectangle(brush, null, new Rect(0, 0, 166, 212));
                var frame = new RenderTargetBitmap(166, 212, 96, 96, PixelFormats.Pbgra32);
                frame.Render(eye);
                var rgba = new byte[166 * 212 * 4];
                frame.CopyPixels(rgba, 166 * 4, 0);
                for (int i = 0; i < rgba.Length; i += 4)
                    if (rgba[i + 2] > 211 && rgba[i + 1] > 204 && rgba[i] > 181) White++;
            }

            internal bool SamePupilSize(EyeFrame other)
            {
                return Pupils.Zip(other.Pupils, (a, b) => Math.Abs(a.Width - b.Width) < .3 &&
                    Math.Abs(a.Height - b.Height) < .3).All(same => same);
            }
        }

        private static void EyeExpressions()
        {
            var motion = new PetArt.CompanionMotion();
            var ear = new FrameworkElement();
            var scene = PetArt.Draw(false, PetPose.Rest, ear, null, motion);
            var seated = new EyeFrame(scene, ear);
            for (int i = 0; i < 18; i++) motion.Tick(1.0 / 60, .88, -.35, 1);
            var tracking = new EyeFrame(scene, ear);
            Require(seated.White > 5000 && tracking.SamePupilSize(seated) &&
                tracking.Pupils.Zip(seated.Pupils, (a, b) => a.Left > b.Left + 2).All(moved => moved),
                "fast pointer tracking moves both pupils without stretching them (" + seated.Pupils[0] + " to " +
                tracking.Pupils[0] + ", neutral white pixels " + seated.White + ")");
            for (int i = 0; i < 8; i++) { motion.Pulse(1); motion.Tick(.033, .88, -.35, 0); }
            var clicking = new EyeFrame(scene, ear);
            Require(!clicking.HasBrow && clicking.SamePupilSize(seated), "repeated mouse clicks add no eyebrows or pupil deformation");
            Require(clicking.White < tracking.White * .97, "mouse clicks change the visible eye opening: " + tracking.White + " to " + clicking.White);
            motion.PrivateInput();
            motion.Tick(1.0 / 60, .88, -.35, 0);
            var closing = new EyeFrame(scene, ear);
            for (int i = 0; i < 14; i++) motion.Tick(.025, .88, -.35, 0);
            var shut = new EyeFrame(scene, ear);
            Require(shut.White < seated.White * .25, "the eyelids hide the eye white in private mode: " + shut.White + "/" + seated.White);
            Require(closing.White > shut.White + (clicking.White - shut.White) * .25,
                "the first frame of closure retains visible eye white before the lids finish closing");
            foreach (var pose in new[] { PetPose.Typing, PetPose.Rest, PetPose.Tilt })
            {
                ear = new FrameworkElement();
                scene = PetArt.Draw(false, pose, ear, null, motion);
                var first = new EyeFrame(scene, ear);
                Require(Math.Abs(first.White - shut.White) < seated.White * .035 &&
                    first.Pupils.Zip(shut.Pupils, (a, b) => (a.TopLeft - b.TopLeft).Length < .7).All(stable => stable),
                    "first frame keeps eye closure and gaze in " + pose);
            }
        }

        private static void CompanionWithoutKeyboard(Window host, PetActor pet)
        {
            var motion = Field<PetArt.CompanionMotion>(pet, "companion");
            var pointer = Field<PetPointer>(pet, "pointer");
            pet.PointerActivity(8, 0, 0, 0);
            Wait(40);
            pet.PointerActivity(1900, 0, 0, 0);
            Wait(100);
            Require(VisibleKeyboard(pet) == null && (!SystemParameters.ClientAreaAnimation ||
                Field<double>(motion, "peekAge") < 1), "a fast pointer pass still animates the cat without revealing the keyboard");
            // Move the isolated test window under the pointer, keeping the system cursor untouched.
            foreach (double x in new[] { .24, .42, .60, .40, .24, .52 })
            {
                var cursor = System.Windows.Forms.Cursor.Position;
                var actual = pet.PointFromScreen(new Point(cursor.X, cursor.Y));
                host.Left += actual.X - pet.ActualWidth * x;
                host.Top += actual.Y - pet.ActualHeight * .36;
                Wait(35);
                pet.PointerActivity(70, 0, 0, 0);
            }
            Wait(130);
            Require(pointer.Petting && VisibleKeyboard(pet) == null && (!SystemParameters.ClientAreaAnimation ||
                Field<Canvas[]>(motion, "particles").Any(p => p.Opacity > .05)), "petting the bare head produces hearts without a keyboard");
            pet.Leave();
            var idle = Field<DispatcherTimer>(pet, "idle");
            var normal = idle.Interval;
            idle.Interval = TimeSpan.FromMilliseconds(45);
            Wait(150);
            idle.Interval = normal;
            Require(pet.Pose == PetPose.Nap && VisibleKeyboard(pet) == null && (!SystemParameters.ClientAreaAnimation ||
                Field<DispatcherTimer>(pet, "frames").IsEnabled), "the bare cat can nap and run its sleeping animation");
            pet.Touch();
        }

        [STAThread]
        private static int Main()
        {
            try
            {
                PointerGestures();
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                EyeExpressions();
                var pet = new PetActor();
                var host = new Window { Width = 120, Height = 125, Content = pet, Left = 80, Top = 80,
                    WindowStartupLocation = WindowStartupLocation.Manual, WindowStyle = WindowStyle.None,
                    ShowActivated = false, ShowInTaskbar = false };
                host.Show();
                Wait(60);
                var frames = Field<DispatcherTimer>(pet, "frames");
                Require(pet.Pose == PetPose.Rest && VisibleKeyboard(pet) == null, "the seated cat starts without a keyboard");
                pet.PointerActivity(60, 0, 0, 0);
                Require(VisibleKeyboard(pet) == null, "moving the pointer does not reveal a keyboard");
                pet.PointerActivity(0, 0, 1, 0);
                Require(VisibleKeyboard(pet) == null, "holding the mouse button does not reveal a keyboard");
                pet.PointerActivity(0, 0, 8, 0);
                Require(VisibleKeyboard(pet) == null, "releasing the mouse button does not reveal a keyboard");
                pet.PointerActivity(0, 0, 0, -120);
                Require(pet.Pose != PetPose.Typing && VisibleKeyboard(pet) == null, "scrolling alone does not open a keyboard");
                pet.Enter();
                Wait(520);
                Require(pet.Pose == PetPose.Tilt && VisibleKeyboard(pet) == null, "hovering tilts the cat without adding a keyboard");
                var body = Field<PetArt.CompanionMotion>(pet, "companion");
                double mouseHeat = Field<double>(body, "heat");
                pet.TapKeyboard("Enter", false, true);
                var desk = VisibleKeyboard(pet);
                Require(desk != null && desk.IsKeyHeld && desk.StrikeSide == 1, "physical key-down brings out the keyboard and the right paw holds Enter");
                Require(Math.Abs(mouseHeat - Field<double>(body, "heat")) < .00001, "a keystroke does not trigger the whole-body mouse reaction");
                var groom = Field<Canvas>(body, "lifted");
                bool groomed = false;
                for (int i = 0; i < 195; i++)
                {
                    body.Tick(.033, 0, 0, 0);
                    groomed |= groom.Opacity > .01;
                }
                Require(!groomed && desk.IsKeyHeld, "idle grooming never takes a paw away from a held key");
                pet.PointerActivity(0, 0, 1, 0);
                pet.PointerActivity(0, 0, 0, 120);
                Wait(900);
                Require(pet.Pose == PetPose.Typing && desk.IsKeyHeld && VisibleKeyboard(pet) == desk, "a held key survives the normal pause while using the mouse");
                pet.ReleaseKeyboard("A");
                Require(desk.IsKeyHeld, "releasing another key does not lift the paw");
                pet.ReleaseKeyboard("Enter");
                Wait(290);
                pet.PointerActivity(90, 0, 0, 0);
                Wait(350);
                pet.PointerActivity(0, 0, 0, -120);
                Require(VisibleKeyboard(pet) == desk && !desk.IsKeyHeld, "key-up leaves the keyboard visible during its brief pause");
                Wait(330);
                Require(pet.Pose == PetPose.Rest && VisibleKeyboard(pet) == null && Field<int>(pet, "heldButtons") != 0,
                    "pointer motion, scrolling and holding a mouse button do not extend the keyboard deadline");
                pet.PointerActivity(0, 0, 8, 0);
                pet.PointerActivity(0, 0, 0, -120);
                Require(VisibleKeyboard(pet) == null, "a late mouse release and scroll do not reopen the keyboard");
                CompanionWithoutKeyboard(host, pet);
                pet.PointerActivity(0, 0, 1, 0);
                // Touch woke the nap through its normal stretch. Let that independent action finish first.
                Wait(1240);
                pet.TapKeyboard("Ctrl", false, true);
                desk = VisibleKeyboard(pet);
                Require(desk != null && desk.IsKeyHeld, "a fresh physical key opens the keyboard again");
                pet.ResetTransientInput();
                Require(pet.Pose == PetPose.Rest && VisibleKeyboard(pet) == null && Field<int>(pet, "heldButtons") == 0,
                    "a device or session change clears a missing key and mouse release");
                pet.SetSessionActive(false);
                pet.TapKeyboard("B");
                Require(pet.Pose == PetPose.Rest && !frames.IsEnabled && !Field<DispatcherTimer>(pet, "tailTimer").IsEnabled,
                    "the locked session stops rendering and ignores input");
                pet.SetSessionActive(true);
                pet.TapKeyboard("C");
                Require(pet.Pose == PetPose.Typing, "the unlocked session accepts fresh input");
                host.Close();
                Wait(45);
                Require(!frames.IsEnabled, "closing the pet stops the body animation clock");
                app.Shutdown();
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
