using System;
using System.Reflection;
using System.Windows;
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

        private static T Field<T>(PetActor actor, string name)
        {
            return (T)typeof(PetActor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor);
        }

        [STAThread]
        private static int Main()
        {
            try
            {
                PointerGestures();
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var pet = new PetActor();
                var host = new Window { Width = 120, Height = 125, Content = pet,
                    ShowActivated = false, ShowInTaskbar = false };
                host.Show();
                Wait(60);
                var desk = Field<PetArt.TypingKeyboard>(pet, "typingKeyboard");
                var frames = Field<DispatcherTimer>(pet, "frames");
                Require(pet.Pose == PetPose.Rest && desk != null, "keyboard and mouse are present before the first input");
                pet.TapKeyboard("Enter", false, true);
                Require(desk.IsKeyHeld && desk.StrikeSide == 0, "the tail presses and holds Enter");
                Wait(900);
                Require(pet.Pose == PetPose.Typing && desk.IsKeyHeld, "a held key survives the normal pause");
                pet.ReleaseKeyboard("A");
                Require(desk.IsKeyHeld, "releasing another key does not lift the tail");
                pet.ReleaseKeyboard("Enter");
                Wait(915);
                Require(pet.Pose == PetPose.Rest && ReferenceEquals(desk, Field<PetArt.TypingKeyboard>(pet, "typingKeyboard")), "returning to rest keeps the same desk");
                pet.PointerActivity(0, 0, 1, 0);
                Wait(865);
                Require(pet.Pose == PetPose.Typing && desk.StrikeSide == 1, "the mouse paw stays in position while held");
                pet.PointerActivity(0, 0, 8, 0);
                Wait(900);
                Require(pet.Pose == PetPose.Rest, "mouse release eventually retracts the paw");
                pet.PointerActivity(0, 0, 0, -120);
                Require(pet.Pose == PetPose.Typing && desk.StrikeSide == 1, "scrolling operates the miniature mouse");
                pet.PointerActivity(0, 0, 1, 0);
                pet.TapKeyboard("Ctrl", false, true);
                pet.ResetTransientInput();
                Require(pet.Pose == PetPose.Rest && !desk.IsKeyHeld && Field<int>(pet, "heldButtons") == 0,
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
