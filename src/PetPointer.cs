using System;
using System.Windows;

namespace Xiaohei
{
    // Current look direction and the short reversal needed to distinguish petting from passing by.
    internal sealed class PetPointer
    {
        internal double GazeX, GazeY, MouseX, MouseY, Speed;
        internal bool Petting { get; private set; }
        private Point? screenPoint, strokeAnchor;
        private Vector strokeDirection;
        private int reversals;
        private double lastMove;

        private static double Axis(double position, double length)
        {
            return Math.Max(-1, Math.Min(1, 2 * position / Math.Max(1, length) - 1));
        }

        internal bool Sample(Point screen, Rect monitor, Point local, Size pet, double now,
            int dx, int dy, bool buttonsActive, double jitter)
        {
            GazeX = Axis(local.X, pet.Width);
            GazeY = Axis(local.Y, pet.Height);
            MouseX = Axis(screen.X - monitor.X, monitor.Width);
            MouseY = Axis(screen.Y - monitor.Y, monitor.Height);
            double travel = screenPoint.HasValue ? (screen - screenPoint.Value).Length : 0;
            travel = Math.Max(travel, new Vector(dx, dy).Length);
            if (!screenPoint.HasValue) screenPoint = screen;
            if (buttonsActive) ResetStroke();
            if (travel < jitter) return false;

            double elapsed = now - lastMove;
            Speed = elapsed > 0 && elapsed < .25
                ? Math.Min(1, travel / (Math.Max(.016, elapsed) * Math.Max(1, monitor.Width))) : 0;
            if (elapsed > .4) ResetStroke();
            lastMove = now;
            screenPoint = screen;
            // The cat's face, in its presentation box. Passing over the props or dragging is not a stroke.
            double x = (local.X / Math.Max(1, pet.Width) - .43) / .40;
            double y = (local.Y / Math.Max(1, pet.Height) - .37) / .31;
            if (buttonsActive || x * x + y * y > 1) ResetStroke();
            else UpdateStroke(local, Math.Max(3, pet.Width * .05));
            return true;
        }

        internal void Decay(double dt) { Speed *= Math.Exp(-5 * dt); }

        internal void Reset()
        {
            screenPoint = null;
            GazeX = GazeY = MouseX = MouseY = Speed = lastMove = 0;
            ResetStroke();
        }

        internal void ResetStroke()
        {
            strokeAnchor = null;
            strokeDirection = new Vector();
            reversals = 0;
            Petting = false;
        }

        private void UpdateStroke(Point local, double minimum)
        {
            if (!strokeAnchor.HasValue) { strokeAnchor = local; return; }
            Vector movement = local - strokeAnchor.Value;
            if (movement.Length < minimum) return;
            movement.Normalize();
            if (Vector.Multiply(movement, strokeDirection) < -.25) reversals++;
            strokeDirection = movement;
            strokeAnchor = local;
            Petting = reversals >= 2;
        }
    }
}
