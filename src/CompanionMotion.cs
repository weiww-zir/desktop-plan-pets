using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Xiaohei
{
    internal static partial class PetArt
    {
        internal sealed class CompanionMotion
        {
            // A critically damped aim for the eyes, a softer follow-through for weight and ears.
            private sealed class Spring
            {
                internal double Value, Velocity;
                internal void Aim(double target, double dt, double frequency, double damping)
                {
                    int steps = Math.Max(1, (int)Math.Ceiling(dt * 120));
                    double step = dt / steps, w = 2 * Math.PI * frequency;
                    for (int i = 0; i < steps; i++)
                    {
                        Velocity += (w * w * (target - Value) - 2 * damping * w * Velocity) * step;
                        Value += Velocity * step;
                    }
                }
            }

            private readonly Spring lookX = new Spring(), lookY = new Spring(), lean = new Spring();
            private readonly Spring earLeft = new Spring(), earRight = new Spring();
            private double time, idle, interest, contact, weight, heat, happy, petTime = 10, petHold;
            private double privateHold, privateLids, peekAge = 5, peekSide = 1;
            private double expressionClosure, shownClosure;
            private int nextFidget, currentFidget;
            private double nextFidgetAt = 4.8, fidgetAge = 3, lastThump = -1;
            private readonly List<Action<double, double, double>> flexibleEdges = new List<Action<double, double, double>>();
            private double lastLeft, lastRight, lastFold;
            private MatrixTransform posture;
            private TranslateTransform gaze, earTip;
            private Eyelids eyelids;
            private RotateTransform tailAngle, liftedAngle;
            private Canvas lifted;
            private Canvas[] particles;
            private TypingKeyboard workbench;
            private bool sleeping;

            internal void Detach()
            {
                if (workbench != null) workbench.IdlePawRaised = false;
                workbench = null;
                posture = null;
                lifted = null;
                particles = null;
                gaze = earTip = null;
                eyelids = null;
                liftedAngle = tailAngle = null;
                flexibleEdges.Clear();
                RestartFidgetDelay();
            }

            internal void AttachSitting(Canvas body, Canvas face, Canvas eyes, Canvas tail, FrameworkElement anchor, TypingKeyboard keyboard)
            {
                sleeping = false;
                workbench = keyboard;
                posture = new MatrixTransform();
                var transforms = new TransformGroup();
                if (face.RenderTransform != Transform.Identity) transforms.Children.Add(face.RenderTransform);
                transforms.Children.Add(posture);
                face.RenderTransform = transforms;
                foreach (UIElement child in face.Children)
                {
                    var path = child as Path;
                    if (path != null) FlexEars(path);
                }
                lastLeft = lastRight = lastFold = 0;
                earTip = new TranslateTransform();
                if (anchor != null)
                {
                    anchor.RenderTransform = earTip;
                    Point tip = new Point(Canvas.GetLeft(anchor), Canvas.GetTop(anchor));
                    TrackPoint(tip, p => { earTip.X = p.X - tip.X; earTip.Y = p.Y - tip.Y; });
                }
                // Bind a replacement pose at the previous gaze and lid phase, including mid-blink.
                gaze = new TranslateTransform(Clamp(lookX.Value * 16.5, -18, 18), Clamp(lookY.Value * 13.5, -14, 14));
                eyelids = new Eyelids(eyes, gaze);
                eyelids.Set(shownClosure, happy);

                tailAngle = null;
                if (tail != null)
                {
                    tailAngle = new RotateTransform(0, 422, 427);
                    tail.RenderTransform = tailAngle;
                }
                liftedAngle = new RotateTransform(0, 330, 401);
                lifted = new Canvas { Opacity = 0, RenderTransform = liftedAngle };
                body.Children.Add(lifted);
                Shape(lifted,"M 340,399 C 337,414 324,419 310,407 Q 296,395 279,363 C 274,355 276,349 281,346 Q 285,342 290,345 Q 298,340 302,347 C 311,351 312,360 307,366 Q 316,383 332,385",Ink,6);
                Shape(lifted,"M 284,354 Q 286,350 289,351 M 293,352 Q 295,349 297,350",null,3,FaceLine);
                particles = MakeParticles(body, false);
            }

            internal void AttachNap(Canvas frame, Canvas sleeper)
            {
                sleeping = true;
                posture = new MatrixTransform();
                sleeper.RenderTransform = posture;
                particles = MakeParticles(frame, true);
            }

            private static Canvas[] MakeParticles(Canvas frame, bool nap)
            {
                var shapes = new Canvas[2];
                for (int i = 0; i < shapes.Length; i++)
                {
                    var particle = new Canvas { Opacity = 0, RenderTransform = new MatrixTransform() };
                    if (nap)
                    {
                        Shape(particle, "M 1,1 L 16,1 L 1,15 L 18,15", null, 6, Ivory);
                        Shape(particle, "M 1,1 L 16,1 L 1,15 L 18,15", null, 3.2, FaceLine);
                    }
                    else
                        Shape(particle, "M 13,25 C 9,21 -3,14 0,6 C 2,-1 9,-1 13,5 C 18,-2 25,0 27,5 C 31,13 20,21 13,25 Z",
                            ColorBrush(224, 143, 127), 2.8, FaceLine);
                    frame.Children.Add(particle);
                    shapes[i] = particle;
                }
                return shapes;
            }

            // The published outline is one continuous path from ears to feet. Bend only the
            // original ear control points, so the fur edge, sage inlay and badge tip stay together.
            private void TrackPoint(Point original, Action<Point> write)
            {
                bool left = original.X < 375;
                double outside = left ? (305 - original.X) / 66 : (original.X - 453) / 65;
                double influence = Clamp(outside, 0, 1) * Clamp((236 - original.Y) / 104, 0, 1);
                if (influence <= 0) return;
                double dx = (230 - original.Y) * influence * Math.PI / 180;
                double dy = (original.X - (left ? 231 : 521)) * influence * Math.PI / 180;
                flexibleEdges.Add((l, r, fold) =>
                {
                    double angle = left ? l : r;
                    write(new Point(original.X + dx * angle, original.Y + dy * angle + influence * 21 * fold));
                });
            }

            private void FlexEars(Path path)
            {
                var geometry = PathGeometry.CreateFromGeometry(path.Data).Clone();
                foreach (PathFigure figure in geometry.Figures)
                {
                    PathFigure destination = figure;
                    TrackPoint(figure.StartPoint, p => destination.StartPoint = p);
                    foreach (PathSegment segment in figure.Segments)
                    {
                        var cubic = segment as BezierSegment;
                        var quadratic = segment as QuadraticBezierSegment;
                        var line = segment as LineSegment;
                        if (cubic != null)
                        {
                            TrackPoint(cubic.Point1, p => cubic.Point1 = p);
                            TrackPoint(cubic.Point2, p => cubic.Point2 = p);
                            TrackPoint(cubic.Point3, p => cubic.Point3 = p);
                        }
                        else if (quadratic != null)
                        {
                            TrackPoint(quadratic.Point1, p => quadratic.Point1 = p);
                            TrackPoint(quadratic.Point2, p => quadratic.Point2 = p);
                        }
                        else if (line != null) TrackPoint(line.Point, p => line.Point = p);
                        else
                        {
                            PointCollection points = segment is PolyBezierSegment ? ((PolyBezierSegment)segment).Points :
                                segment is PolyQuadraticBezierSegment ? ((PolyQuadraticBezierSegment)segment).Points :
                                segment is PolyLineSegment ? ((PolyLineSegment)segment).Points : null;
                            if (points == null) continue;
                            for (int i = 0; i < points.Count; i++)
                            {
                                int at = i;
                                TrackPoint(points[at], p => points[at] = p);
                            }
                        }
                    }
                }
                path.Data = geometry;
            }

            internal void ResetIdle()
            {
                RestartFidgetDelay();
                if (workbench != null) workbench.IdlePawRaised = false;
                if (lifted != null) lifted.Opacity = 0;
            }

            private void RestartFidgetDelay()
            {
                idle = 0;
                nextFidgetAt = 4.8;
                fidgetAge = 3;
                // Keep nextFidget across interruptions and naps; even short idle spells vary.
            }

            internal void Pulse(double side)
            {
                ResetIdle();
                side = Clamp(side, -1, 1);
                contact = 1;
                weight = side;
                heat = Math.Min(1, heat + .23);
                if (workbench != null && side != 0 && heat >= .76 && time - lastThump > .48)
                {
                    workbench.ThumpTail();
                    lastThump = time;
                }
                lean.Velocity = Clamp(lean.Velocity + side * 11, -22, 22);
                earLeft.Velocity = Clamp(earLeft.Velocity + 18 - side * 10, -40, 40);
                earRight.Velocity = Clamp(earRight.Velocity - 18 - side * 10, -40, 40);
            }

            internal void Pet()
            {
                ResetIdle();
                if (petTime > .95) petTime = 0;
                petHold = 1.05;
            }

            internal void PrivateInput()
            {
                ResetIdle();
                privateHold = .8;
            }

            internal void Peek()
            {
                if (peekAge < .42) return;
                peekSide = Math.Abs(lookX.Value) > .15 ? Math.Sign(lookX.Value) : -peekSide;
                peekAge = 0;
            }

            private static double Clamp(double a, double min, double max) { return Math.Max(min, Math.Min(max, a)); }
            private static double Envelope(double t) { return t <= 0 || t >= 1 ? 0 : Math.Sin(Math.PI * t); }

            internal void Tick(double dtSeconds, double pointerX, double pointerY, double speed)
            {
                if (posture == null || dtSeconds <= 0) return;
                double dt = Math.Min(.1, dtSeconds);
                time += dt;
                idle += dt;
                petTime += dt;
                petHold = Math.Max(0, petHold - dt);
                if (sleeping)
                {
                    double breath = Math.Sin(time * 1.45);
                    var nap = Matrix.Identity;
                    nap.ScaleAt(1 - .005 * breath, 1 + .018 * breath, 391, 476);
                    posture.Matrix = nap;
                    DrawParticles((time % 6) - 1, true);
                    return;
                }

                if (speed > .78 && interest < .48) Peek();
                peekAge += dt;
                interest = Math.Max(Clamp(speed, 0, 1), interest * Math.Exp(-3.8 * dt));
                privateHold = Math.Max(0, privateHold - dt);
                privateLids += (Math.Min(1, privateHold / .18) - privateLids) * (1 - Math.Exp(-dt * 36));
                contact *= Math.Exp(-9 * dt);
                heat = Math.Max(0, heat - .77 * dt);
                weight *= Math.Exp(-5.3 * dt);
                happy += ((petHold > 0 ? 1 : 0) - happy) * (1 - Math.Exp(-dt * (petHold > 0 ? 12 : 5)));
                if (idle >= nextFidgetAt)
                {
                    currentFidget = nextFidget;
                    nextFidget = (nextFidget + 1) % 5;
                    nextFidgetAt = idle + 7.4;
                    fidgetAge = 0;
                }
                else fidgetAge += dt;
                int kind = currentFidget;
                double phase = fidgetAge / 2.15;
                double fidget = Envelope(phase);
                double groom = kind == 0 ? fidget : 0, stretch = kind == 1 ? fidget : 0;
                double inspect = kind == 2 ? fidget : 0, twitch = kind == 3 ? fidget : 0;
                double slowBlink = kind == 4 ? fidget : 0;
                double paw = groom + inspect;
                double lift = paw + stretch;
                lifted.Opacity = Clamp(lift * 4, 0, 1);
                if (workbench != null) workbench.IdlePawRaised = lift > .11;
                liftedAngle.Angle = (1 - lift) * -43 + groom * (6 + 8 * Math.Sin(phase * Math.PI * 5)) - inspect * 21 - stretch * 94;

                double gx = Clamp(pointerX, -1, 1), gy = Clamp(pointerY, -1, 1);
                lookX.Aim(gx * (1 - paw) - paw * .79 + weight * .22, dt, 3.2, .91);
                lookY.Aim(gy * (1 - paw) + paw * .94 + contact * .36, dt, 3.15, .94);
                gaze.X = Clamp(lookX.Value * 16.5, -18, 18);
                gaze.Y = Clamp(lookY.Value * 13.5, -14, 14);
                double rub = happy * Math.Sin(petTime * 8.3);
                double peek = Envelope(peekAge / 1.32) * peekSide;
                lean.Aim(gx * (.65 + 1.45 * interest) + peek * 2.45 + weight * 1.75 - groom * 1.85 - inspect * 2.1 + rub * 1.9,
                    dt, 2.4, .69);
                double angle = Clamp(lean.Value, -3.1, 3.1);
                var body = Matrix.Identity;
                double breathWave = Math.Sin(time * 1.72);
                body.ScaleAt(1 - .0028 * breathWave - .008 * stretch + .006 * contact,
                    1 + .0055 * breathWave + .014 * stretch - .009 * contact, 374, 472);
                body.RotateAt(angle, 371, 252);
                body.Translate(.9 * gx + 3.2 * rub, Math.Abs(angle) * .85 + contact * 1.5 + groom * (2 + 2 * Math.Sin(phase * 17)));
                posture.Matrix = body;

                double expression = Clamp(1.06 * Math.Max(privateLids, happy) + .46 * groom + .34 * stretch
                    + .42 * slowBlink + .30 * heat - .085 * interest, 0, 1);
                expressionClosure += (expression - expressionClosure) * (1 - Math.Exp(-dt * 16));
                double phaseBlink = time % 4.93 - 4.56;
                double blink = phaseBlink < .09 ? Clamp(phaseBlink / .09, 0, 1) : Clamp((.27 - phaseBlink) / .145, 0, 1);
                shownClosure = Math.Max(blink * blink * (3 - 2 * blink), expressionClosure);
                eyelids.Set(shownClosure, happy);
                double flap = twitch * Math.Sin(phase * 21);
                double fold = Clamp((heat - .46) / .54, 0, 1);
                earLeft.Aim(-1.5 * happy + 2.6 * flap + .9 * interest + .9 * contact - 2.4 * fold, dt, 4.2, .46);
                earRight.Aim(1.7 * happy - 1.7 * flap - 1.3 * interest - 1.2 * contact + 2.4 * fold, dt, 3.7, .49);
                double l = Clamp(earLeft.Value, -3.2, 3.2), r = Clamp(earRight.Value, -3.3, 3.3);
                if (Math.Abs(l - lastLeft) + Math.Abs(r - lastRight) + 8 * Math.Abs(fold - lastFold) > .055)
                {
                    foreach (var bend in flexibleEdges) bend(l, r, fold);
                    lastLeft = l;
                    lastRight = r;
                    lastFold = fold;
                }
                if (tailAngle != null)
                    tailAngle.Angle = -3 * Math.Sin(time * 1.7) - 6 * interest - 4.8 * happy - 4 * contact - 4 * fidget;
                DrawParticles(petTime, false);
            }

            private void DrawParticles(double clock, bool nap)
            {
                for (int i = 0; i < particles.Length; i++)
                {
                    double t = (clock - i * (nap ? .65 : .21)) / (nap ? 1.85 : .94);
                    double show = Envelope(t);
                    particles[i].Opacity = show;
                    if (show <= 0) continue;
                    var transform = Matrix.Identity;
                    double size = (nap ? .95 : .71) + .42 * t;
                    transform.Scale(size, size);
                    transform.Rotate((nap ? -10 : -16) + 12 * t);
                    transform.Translate(nap ? 450 + 51 * t + i * 7 : 208 - 37 * t - i * 7,
                        nap ? 274 - 65 * t : 339 - 53 * t - i * 10);
                    ((MatrixTransform)particles[i].RenderTransform).Matrix = transform;
                }
            }
        }
    }
}
