using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Xiaohei
{
    internal static class TypingCheck
    {
        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
            Console.WriteLine("PASS " + message);
        }

        static T Field<T>(PetActor actor, string name)
        {
            return (T)typeof(PetActor).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(actor);
        }

        static void Until(Func<bool> reached, string description, int timeout = 2400)
        {
            var time = Stopwatch.StartNew();
            while (!reached())
            {
                if (time.ElapsedMilliseconds > timeout) throw new Exception("Timeout: " + description);
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate {}));
                Thread.Sleep(3);
            }
        }

        static void Wait(int ms)
        {
            var time = Stopwatch.StartNew();
            Until(() => time.ElapsedMilliseconds >= ms, "dispatcher delay");
        }

        static void Play()
        {
            var actor = new PetActor();
            var window = new Window { Width = 190, Height = 200, Content = actor, ShowActivated = false,
                ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -9000, Top = -9000 };
            var finish = Field<DispatcherTimer>(actor, "finish");
            var idle = Field<DispatcherTimer>(actor, "idle");
            var hover = Field<DispatcherTimer>(actor, "hover");
            var tail = Field<DispatcherTimer>(actor, "tailTimer");
            actor.TapKeyboard("C");
            Check(actor.Pose == PetPose.Rest && !finish.IsEnabled, "unloaded actor ignores keyboard input");
            window.Show();
            Until(() => actor.IsLoaded && actor.IsVisible, "actor load");
            actor.ShowPendingCount(3);
            actor.TapKeyboard("C");
            var keyboard = Field<PetArt.TypingKeyboard>(actor, "typingKeyboard");
            Check(actor.Pose == PetPose.Typing && keyboard != null && Caption(keyboard).Text == "C",
                "first keystroke immediately shows C");
            window.UpdateLayout();
            var scene = (FrameworkElement)Field<Decorator>(actor, "drawing").Child;
            var liveTails = new TailState(scene);
            foreach (string key in new[] { "A", "T", "P", "A", "P", "P", "4", ";", "Space", "F12" })
            {
                actor.TapKeyboard(key);
                Wait(24);
                Check(keyboard == Field<PetArt.TypingKeyboard>(actor, "typingKeyboard") && Caption(keyboard).Text == key,
                    "rapid keystroke shows the latest key in the same drawing: " + key);
                if (!liveTails.Unchanged()) throw new Exception("Actual typing actor moved its tail for " + key);
            }
            Check(liveTails.Unchanged(), "fast typing through the actor drives the paws without a tail beat");
            actor.TapKeyboard();
            Check(string.IsNullOrEmpty(Caption(keyboard).Text), "anonymous keystroke clears the old key");
            actor.TapKeyboard("C");
            actor.TapKeyboard("UnknownKey");
            Check(string.IsNullOrEmpty(Caption(keyboard).Text), "unrecognized key does not leave or invent a label");
            actor.TapKeyboard("T");
            Wait(360);
            actor.TapKeyboard("T");
            var pause = Stopwatch.StartNew();
            Wait(520);
            Check(actor.Pose == PetPose.Typing && Caption(keyboard).Text == "T", "rebound retains the last key until the renewed pause ends");
            Until(() => actor.Pose == PetPose.Rest, "return to seated rest");
            Check(pause.ElapsedMilliseconds >= 775 && pause.ElapsedMilliseconds < 1450 &&
                !finish.IsEnabled && !Descendants<PetArt.TypingKeyboard>(actor).Any(k => k.IsVisible),
                "stopping typing removes the keyboard from the pet after about 800 ms");

            var normalIdle = idle.Interval;
            idle.Interval = TimeSpan.FromMilliseconds(35);
            Until(() => actor.Pose == PetPose.Nap, "nap");
            idle.Interval = normalIdle;
            actor.TapKeyboard();
            Check(actor.Pose == PetPose.Typing, "typing wakes the sleeping cat");
            actor.Celebrate(true);
            actor.TapKeyboard();
            Check(actor.Pose == PetPose.Wave, "completion interrupts typing and keeps the wave");
            finish.Interval = TimeSpan.FromMilliseconds(40);
            Until(() => actor.Pose == PetPose.Hop, "celebration hop");
            actor.TapKeyboard();
            Check(actor.Pose == PetPose.Hop, "keyboard activity does not interrupt the follow-up hop");
            finish.Interval = TimeSpan.FromMilliseconds(40);
            Until(() => actor.Pose == PetPose.Rest, "celebration finish");
            actor.WatchSpirits();
            var normalTail = tail.Interval;
            tail.Interval = TimeSpan.FromMilliseconds(45);
            Wait(110);
            tail.Interval = normalTail;
            actor.TapKeyboard();
            Check(actor.Pose == PetPose.SpiritOrbit, "typing and the scheduled tail wait for the butterflies");
            finish.Interval = TimeSpan.FromMilliseconds(40);
            Until(() => actor.Pose == PetPose.TailKittens, "pending tail after butterflies");
            actor.TapKeyboard("A");
            Check(actor.Pose == PetPose.Typing, "keystroke immediately interrupts the scheduled kittens");
            tail.Interval = TimeSpan.FromMilliseconds(35);
            Wait(100);
            tail.Interval = normalTail;
            Check(actor.Pose == PetPose.Typing, "an automatic tail tick leaves active typing visible");
            actor.SplitTail();
            actor.TapKeyboard("C");
            Check(actor.Pose == PetPose.TailKittens, "typing honours a manually requested tail animation");
            finish.Interval = TimeSpan.FromMilliseconds(40);
            Until(() => actor.Pose == PetPose.Rest, "kittens finish");
            actor.TapKeyboard();
            window.Content = null;
            Until(() => !actor.IsLoaded, "unload during typing");
            actor.TapKeyboard();
            Check(!finish.IsEnabled && !idle.IsEnabled && !hover.IsEnabled && !tail.IsEnabled, "unloaded actor stops all its timers");
            window.Content = actor;
            Until(() => actor.IsLoaded && actor.Pose == PetPose.Rest, "reload without stale typing");
            window.Close();
        }

        static BitmapSource Capture(FrameworkElement scene, int width, int height)
        {
            scene.Measure(new Size(width, height));
            scene.Arrange(new Rect(0, 0, width, height));
            scene.UpdateLayout();
            var frame = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            frame.Render(scene);
            return frame;
        }

        static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T) yield return (T)child;
                foreach (var element in Descendants<T>(child)) yield return element;
            }
        }

        static TextBlock Caption(PetArt.TypingKeyboard keyboard)
        {
            return Descendants<Viewbox>(keyboard).Select(v => v.Child).OfType<TextBlock>().Single();
        }

        static int Differences(BitmapSource first, BitmapSource second, Rect area)
        {
            int width = first.PixelWidth;
            var a = new byte[width * 4 * first.PixelHeight];
            var b = new byte[a.Length];
            first.CopyPixels(a, width * 4, 0);
            second.CopyPixels(b, width * 4, 0);
            int count = 0;
            for (int y = Math.Max(0, (int)area.Top); y < Math.Min(first.PixelHeight, (int)Math.Ceiling(area.Bottom)); y++)
                for (int x = Math.Max(0, (int)area.Left); x < Math.Min(width, (int)Math.Ceiling(area.Right)); x++)
                {
                    int p = (y * width + x) * 4;
                    int change = 0;
                    for (int c = 0; c < 4; c++) change += Math.Abs(a[p + c] - b[p + c]);
                    if (change > 40) count++;
                }
            return count;
        }

        static int FurPixels(BitmapSource image, Rect area)
        {
            var crop = new CroppedBitmap(image, new Int32Rect((int)area.X, (int)area.Y,
                (int)area.Width, (int)area.Height));
            int stride = crop.PixelWidth * 4;
            var pixels = new byte[stride * crop.PixelHeight];
            crop.CopyPixels(pixels, stride, 0);
            int count = 0;
            for (int i = 0; i < pixels.Length; i += 4)
                if (pixels[i+3] == 255 && Math.Max(pixels[i], Math.Max(pixels[i+1], pixels[i+2])) < 40) count++;
            return count;
        }

        // Compare the two real fur paths independently from a paw or mouse passing in front of them.
        sealed class TailState
        {
            readonly FrameworkElement scene;
            readonly System.Windows.Shapes.Path[] paths;
            readonly string[] outlines;
            readonly Rect[] positions;

            internal TailState(FrameworkElement scene)
            {
                this.scene = scene;
                paths = Descendants<System.Windows.Shapes.Path>(scene).Where(p => {
                    if (p.Data == null || p.Fill == null) return false;
                    var b = p.Data.Bounds;
                    return (b.Left > 260 && b.Left < 270 && b.Right == 320) ||
                        (b.Left > 399 && b.Left < 415 && b.Top > 375 && b.Top < 395 && b.Right > 615 && b.Right < 630);
                }).ToArray();
                if (paths.Length != 2) throw new Exception("Expected the two seated tail silhouettes; found " + paths.Length);
                outlines = paths.Select(p => p.Data.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                positions = paths.Select(Position).ToArray();
            }

            Rect Position(System.Windows.Shapes.Path p) { return p.TransformToAncestor(scene).TransformBounds(p.Data.Bounds); }

            internal bool Matches(TailState other)
            {
                return outlines.SequenceEqual(other.outlines) && positions.Zip(other.positions, SamePosition).All(b => b);
            }

            static bool SamePosition(Rect a, Rect b)
            {
                return (a.TopLeft - b.TopLeft).Length < .01 && (a.BottomRight - b.BottomRight).Length < .01;
            }

            internal bool Unchanged()
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    var p = paths[i];
                    if (!scene.IsAncestorOf(p) || p.Data.ToString(System.Globalization.CultureInfo.InvariantCulture) != outlines[i]) return false;
                    if (!SamePosition(Position(p), positions[i])) return false;
                    for (DependencyObject e = p; e != scene; e = VisualTreeHelper.GetParent(e))
                    {
                        var element = e as UIElement;
                        if (element != null && (element.Visibility != Visibility.Visible || element.Opacity < .999)) return false;
                    }
                }
                return true;
            }
        }

        static void AnimatedRebound()
        {
            const int width = 240, height = 228;
            var keyboard = new PetArt.TypingKeyboard(true);
            var scene = PetArt.Draw(false, PetPose.Typing, null, keyboard);
            var host = new Window { Content = scene, Width = width, Height = height, WindowStyle = WindowStyle.None,
                ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -9000, Top = -9000 };
            host.Show();
            var raised = Capture(scene, width, height);
            Rect a, p;
            if (!PetArt.TypingKeyboard.TryGetKeyBounds("A", out a) || !PetArt.TypingKeyboard.TryGetKeyBounds("P", out p))
                throw new Exception("Missing A or P from the drawn keyboard");
            var transform = keyboard.TransformToAncestor(scene);
            a = transform.TransformBounds(a);
            p = transform.TransformBounds(p);
            keyboard.Press("A", true);
            keyboard.Hold("A");
            int restingFur = FurPixels(raised, a);
            Until(() => FurPixels(Capture(scene, width, height), a) > restingFur + 2,
                "animated A paw touches the cap before striking P");
            Wait(80);
            var leftArm = transform.TransformBounds(new Rect(259, 385, 92, 36));
            keyboard.Press("P", false);
            Until(delegate {
                var overlap = Capture(scene, width, height);
                return Differences(raised, overlap, leftArm) >= 4 && FurPixels(overlap, p) > FurPixels(raised, p) + 2;
            }, "the right paw contacts P while the left forearm is still returning from A");
            Until(delegate {
                var released = Capture(scene, width, height);
                return Differences(raised, released, a) == 0 && Differences(raised, released, p) == 0;
            }, "both A and P rebound on their own");
            Check(Caption(keyboard).Text == "P", "animated rapid cross-hand press rebounds while P stays readable");
            var tails = new TailState(scene);
            Rect enter, space;
            if (!PetArt.TypingKeyboard.TryGetKeyBounds("Enter", out enter) || !PetArt.TypingKeyboard.TryGetKeyBounds("Space", out space))
                throw new Exception("Missing Enter or Space key");
            enter = transform.TransformBounds(enter);
            space = transform.TransformBounds(space);
            keyboard.Press("Enter", true);
            keyboard.Hold("Enter");
            Wait(320);
            var held = Capture(scene, width, height);
            Check(keyboard.IsKeyHeld && keyboard.StrikeSide == 1 && FurPixels(held, enter) > FurPixels(raised, enter) + 2 &&
                tails.Unchanged(), "the right paw stays on a held Enter without moving either tail");
            keyboard.Release("A");
            Check(keyboard.IsKeyHeld, "unrelated key-up keeps Enter held");
            keyboard.Release("Enter");
            Until(() => Differences(raised, Capture(scene, width, height), enter) == 0, "paw released Enter");
            Check(!keyboard.IsKeyHeld && Caption(keyboard).Text == "Enter", "Enter release relaxes the paw and preserves the last label");
            keyboard.Press("Space", true);
            keyboard.Hold("Space");
            keyboard.Release("Space");
            Until(() => FurPixels(Capture(scene, width, height), space) > FurPixels(raised, space) + 2, "sub-frame Space tap reaches its cap");
            Check(tails.Unchanged(), "sub-frame tap keeps both seated tails fixed");
            Until(() => Differences(raised, Capture(scene, width, height), space) == 0, "short Space tap returns the paw home");
            keyboard.Press("Space", true);
            keyboard.Hold("Space");
            Wait(130);
            Check(FurPixels(Capture(scene, width, height), space) > FurPixels(raised, space) + 2, "a paw holds the space bar");
            keyboard.Press("Enter", true);
            keyboard.Hold("Enter");
            keyboard.Release("Space");
            Wait(145);
            var switched = Capture(scene, width, height);
            Check(keyboard.IsKeyHeld && Caption(keyboard).Text == "Enter" &&
                FurPixels(switched, enter) > FurPixels(raised, enter) + 2 && tails.Unchanged(), "Space to Enter moves paw contact to the newly held key");
            keyboard.Release("Enter");
            Until(() => Differences(raised, Capture(scene, width, height), enter) == 0, "newly held Enter returns home on release");
            foreach (var key in new[] { "A", "P", "Space", "Enter", "Esc", "Bksp", "U", "C", "G", "J", "Z", "P" })
            {
                keyboard.Press(key, false);
                Wait(45);
                if (!tails.Unchanged()) throw new Exception("A typing tail moved during the burst on " + key);
            }
            Wait(330);
            Check(tails.Unchanged(), "neither tail swings between, during or after rapid animated strokes");
            host.Close();
        }

        static void Frames(string dir)
        {
            foreach (int width in new[] { 64, 92, 240 })
            {
                int height = (int)Math.Round(width * 455.0 / 480);
                var anchor = new FrameworkElement();
                var keyboard = new PetArt.TypingKeyboard(false);
                var scene = PetArt.Draw(false, PetPose.Typing, anchor, keyboard);
                Capture(scene, width, height);
                var restAnchor = new FrameworkElement();
                var idleView = PetArt.Draw(false, PetPose.Rest, restAnchor);
                var sitting = Capture(idleView, width, height);
                var ear = anchor.TranslatePoint(new Point(), scene);
                var idleEar = restAnchor.TranslatePoint(new Point(), idleView);
                Check((ear - idleEar).Length < .2 && ear.X > width * .72 && ear.Y < height * .23,
                    "typing keeps the seated right-ear anchor at " + width + " DIPs");
                var transforms = keyboard.TransformToAncestor(scene);
                var keys = new[] { "C", "A", "P" };
                var images = new List<BitmapSource>();
                var bounds = new List<Rect>();
                foreach (string key in keys)
                {
                    Rect cap;
                    Check(PetArt.TypingKeyboard.TryGetKeyBounds(key, out cap), "drawn key exists: " + key);
                    var actual = transforms.TransformBounds(cap);
                    bounds.Add(actual);
                    keyboard.Press(key, false);
                    keyboard.SetPressDepth(0);
                    var released = Capture(scene, width, height);
                    keyboard.SetPressDepth(1);
                    var pressed = Capture(scene, width, height);
                    images.Add(pressed);
                    Check(Differences(released, pressed, actual) > 0,
                        key + " has a visible press at its own keycap at " + width + " DIPs");
                    keyboard.SetPressDepth(0);
                    var rebound = Capture(scene, width, height);
                    Check(Differences(released, rebound, new Rect(0, 0, width, height)) == 0,
                        key + " returns to the released frame");
                    var caption = Caption(keyboard);
                    var captionBounds = caption.TransformToAncestor(scene).TransformBounds(new Rect(caption.RenderSize));
                    Check(caption.Text == key && new Rect(0, 0, width, height).Contains(captionBounds) &&
                        (width != 92 || captionBounds.Height >= 12),
                        key + " caption fits the frame and is readable at default size; " + captionBounds);
                    if (width == 92)
                    {
                        caption.Visibility = Visibility.Hidden;
                        var withoutLetter = Capture(scene, width, height);
                        caption.Visibility = Visibility.Visible;
                        int ink = Differences(rebound, withoutLetter, captionBounds);
                        Check(ink >= 14, key + " paints a visible single glyph at the default pet size: " + ink + " pixels");
                    }
                    Check(Differences(sitting, pressed, new Rect(0, 0, width * .75, height * .62)) == 0,
                        "key feedback preserves the seated face");
                }
                Check(bounds[1].Right < bounds[0].Left && bounds[0].Right < bounds[2].Left,
                    "A and C have distinct left-hand positions and P is on the right");
                var pawBand = transforms.TransformBounds(new Rect(190, 370, 400, 55));
                Check(Differences(images[0], images[1], pawBand) > 0 && Differences(images[1], images[2], pawBand) > 0,
                    "same-side and cross-side presses visibly move the paws at " + width + " DIPs");
                keyboard.Press(null, false);
                Check(string.IsNullOrEmpty(Caption(keyboard).Text), "anonymous press erases the previous caption");
                if (width != 240 || dir == null) continue;
                var preview = new DrawingVisual();
                using (var draw = preview.RenderOpen())
                    for (int i = 0; i < images.Count; i++) draw.DrawImage(images[i], new Rect(i * width, 0, width, height));
                var result = new RenderTargetBitmap(width * images.Count, height, 96, 96, PixelFormats.Pbgra32);
                result.Render(preview);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(result));
                Directory.CreateDirectory(dir);
                using (var file = File.Create(Path.Combine(dir, "TypingPaws.png"))) png.Save(file);
            }
        }

        static void DeskActions()
        {
            const int width = 480, height = 455;
            var keys = new PetArt.TypingKeyboard(false);
            var scene = PetArt.Draw(false, PetPose.Typing, null, keys);
            var neutral = Capture(scene, width, height);
            var map = keys.TransformToAncestor(scene);
            var board = map.TransformBounds(new Rect(198, 419, 366, 81));
            var tails = new TailState(scene);
            var mouse = map.TransformBounds(new Rect(570, 430, 64, 68));
            foreach (string key in new[] { "Space", "Enter", "Bksp", "Esc", "Tab", "Ctrl", "A", "P", "1", "0", ";", "←", "F12", "Num0" })
            {
                keys.Press(key, true);
                var down = Capture(scene, width, height);
                Rect cap;
                string drawnCap = key == "F12" || key == "Num0" ? "Fn" : key;
                if (!PetArt.TypingKeyboard.TryGetKeyBounds(drawnCap, out cap)) throw new Exception("Missing " + drawnCap);
                var onCap = map.TransformBounds(cap);
                bool expectedSide = key == "Space" ? Math.Abs(keys.StrikeSide) == 1 :
                    keys.StrikeSide == (cap.X + cap.Width / 2 < 377 ? -1 : 1);
                Check(expectedSide && Caption(keys).Text == key &&
                    FurPixels(down, onCap) > FurPixels(neutral, onCap) + 4,
                    key + " has real front-paw contact on its own cap with reduced motion");
                Check(tails.Unchanged(), key + " leaves the original two tail silhouettes in place");
                keys.Rest();
                Check(Differences(neutral, Capture(scene, width, height), board) == 0 && tails.Unchanged(),
                    key + " releases the keyboard completely");
            }
            keys.Press("A", true);
            keys.Mouse(0, 0, 0, 0);
            var reach = Capture(scene, width, height);
            Check(keys.StrikeSide == 1 && Caption(keys).Text == "" && Differences(neutral, reach, mouse) >= 15,
                "pointer motion clears the letter and reaches a separate mouse with the right paw");
            keys.Rest();
            keys.Mouse(0, 0, 1, 0);
            var left = Capture(scene, width, height);
            keys.Rest();
            keys.Mouse(0, 0, 2, 0);
            var right = Capture(scene, width, height);
            Check(Differences(left, right, map.TransformBounds(new Rect(580, 440, 42, 26))) >= 20,
                "left and right mouse presses light different halves of the mouse");
            keys.Rest();
            keys.Mouse(0, 0, 4, 1);
            var up = Capture(scene, width, height);
            keys.Mouse(0, 0, 4, -1);
            var downWheel = Capture(scene, width, height);
            Check(Differences(up, downWheel, mouse) >= 4, "wheel feedback distinguishes scroll direction");
            keys.Press("P", true);
            Check(keys.StrikeSide == -1 && Caption(keys).Text == "P", "left paw handles typing while right paw holds the mouse");
            keys.Press("U", false);
            Rect u;
            if (!PetArt.TypingKeyboard.TryGetKeyBounds("U", out u)) throw new Exception("Missing U");
            var mixed = Capture(scene, width, height);
            var uCap = map.TransformBounds(u);
            Check(keys.StrikeSide == -1 && Caption(keys).Text == "U" && FurPixels(mixed, uCap) > FurPixels(neutral, uCap) + 4 &&
                Differences(downWheel, mixed, mouse) == 0 && tails.Unchanged(),
                "successive mixed input keeps the right paw on the mouse and puts the left paw on U");
            keys.Press(null, true);
            keys.Mouse(1, -1, 0, 0);
            Check(Caption(keys).Text == "", "switching to anonymous input and mouse leaves no old letter");
            keys.Rest();
            keys.Press("A", true);
            keys.Hold("A");
            keys.Press("P", true);
            keys.Hold("P");
            keys.Release("A");
            Check(keys.IsKeyHeld && keys.StrikeSide == 1 && Caption(keys).Text == "P", "only the latest physical hold is retained");
            keys.Release(null);
            Check(!keys.IsKeyHeld && Caption(keys).Text == "", "anonymous release erases a held key");
            foreach (string heldKey in new[] { "Shift", "A", "P", "Space" })
            {
                keys.Rest();
                keys.Press(heldKey, true);
                keys.Hold(heldKey);
                keys.Mouse(1, 1, 0, 0);
                keys.Mouse(1, 1, 1, 0);
                Rect cap;
                if (!PetArt.TypingKeyboard.TryGetKeyBounds(heldKey, out cap)) throw new Exception("Missing " + heldKey);
                var heldArea = map.TransformBounds(cap);
                var duringClick = Capture(scene, width, height);
                Check(keys.IsKeyHeld && Caption(keys).Text == "" && Differences(neutral, duringClick, heldArea) >= 4,
                    heldKey + " stays physically pressed during mouse motion and a click without repeating the letter");
                keys.Release(heldKey);
                Check(!keys.IsKeyHeld && Differences(duringClick, Capture(scene, width, height), heldArea) >= 4,
                    "matching key-up releases " + heldKey + " while the mouse is still held");
            }
            keys.Rest();
            Check(Differences(neutral, Capture(scene, width, height), new Rect(0, 0, width, height)) == 0,
                "keyboard, mouse, both paws and tail return to their original desk layout");
            Rect quick;
            if (!PetArt.TypingKeyboard.TryGetKeyBounds("Enter", out quick)) throw new Exception("Missing Enter");
            quick = map.TransformBounds(quick);
            int upFur = FurPixels(neutral, quick);
            keys.Press("Enter", false);
            keys.Hold("Enter");
            keys.Release("Enter");
            var landed = Capture(scene, width, height);
            Check(!keys.IsKeyHeld && FurPixels(landed, quick) > upFur + 4 && tails.Unchanged(),
                "a sub-frame reduced-motion tap displays a static front paw on Enter");
            Wait(25);
            Check(FurPixels(Capture(scene, width, height), quick) > upFur + 4, "the reduced-motion tap remains visible briefly");
            Wait(100);
            Check(FurPixels(Capture(scene, width, height), quick) == upFur && tails.Unchanged(),
                "the brief reduced-motion contact clears without a moving tail");
            var companion = new PetArt.CompanionMotion();
            var idle = PetArt.Draw(false, PetPose.Rest, null, null, companion);
            Capture(idle, width, height);
            var idleTails = new TailState(idle);
            for (int i = 0; i < 20; i++) companion.Tick(.033, 0, 0, 0);
            Check(!idleTails.Unchanged(), "the normal seated tail can still move independently of typing");
            var beforeTyping = new TailState(idle);
            var activeKeys = new PetArt.TypingKeyboard(false);
            var active = PetArt.Draw(false, PetPose.Typing, null, activeKeys, companion);
            Capture(active, width, height);
            var frozen = new TailState(active);
            Check(beforeTyping.Matches(frozen), "entering typing freezes the exact last seated tail position on the first frame");
            for (int i = 0; i < 18; i++)
            {
                activeKeys.Press(i % 2 == 0 ? "Enter" : "A", true);
                companion.Tick(.04, .65, .2, 1);
            }
            Check(frozen.Unchanged(), "typing and pointer tracking preserve that non-neutral tail angle");
            idle = PetArt.Draw(false, PetPose.Rest, null, null, companion);
            Capture(idle, width, height);
            var resumed = new TailState(idle);
            Check(frozen.Matches(resumed), "returning to rest starts from the frozen angle");
            for (int i = 0; i < 15; i++) companion.Tick(.033, 0, 0, 0);
            Check(!resumed.Unchanged(), "independent seated tail motion resumes after typing");
        }

        [STAThread] static int Main(string[] args)
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Frames(args.Length == 0 ? null : args[0]);
                DeskActions();
                AnimatedRebound();
                Play();
                app.Shutdown();
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
