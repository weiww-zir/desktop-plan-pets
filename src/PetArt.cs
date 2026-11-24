using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Xiaohei
{
    internal enum PetPose { Rest, Tilt, Wave, Hop, Nap, Stretch, Meow, TailKittens, SpiritOrbit }

    internal static partial class PetArt
    {
        private static readonly Brush Ink = ColorBrush(12, 11, 10);
        private static readonly Brush Outline = ColorBrush(61, 42, 32);
        private static readonly Brush Ear = ColorBrush(171, 193, 150);
        private static readonly Brush Ivory = ColorBrush(235, 229, 210);

        private static Brush ColorBrush(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        private static void Shape(Canvas layer, string contour, Brush fill, double edge, Brush stroke = null)
        {
            var geometry = Geometry.Parse(contour);
            geometry.Freeze();
            layer.Children.Add(new Path {
                Data = geometry, Fill = fill, Stroke = edge > 0 ? (stroke ?? Outline) : null,
                StrokeThickness = edge, StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            });
        }

        public static FrameworkElement Create()
        {
            return Create(false);
        }

        public static FrameworkElement Create(bool animate)
        {
            return Draw(animate, PetPose.Rest);
        }

        // Coordinates are shared by all poses so the paws stay planted when the face changes.
        internal static FrameworkElement Draw(bool animate, PetPose pose, FrameworkElement earAnchor = null)
        {
            var frame = new Canvas { Width = 480, Height = 455 };
            var view = new Viewbox { Stretch = Stretch.Uniform, Child = frame };
            bool motion = animate && SystemParameters.ClientAreaAnimation;
            if (pose == PetPose.SpiritOrbit)
            {
                frame.Children.Add(new SpiritOrbit(motion, earAnchor));
                return view;
            }
            var cat = new Canvas { RenderTransform = new TranslateTransform(-159, -50) };
            frame.Children.Add(cat);
            if (pose == PetPose.Meow || pose == PetPose.Nap || pose == PetPose.Stretch)
            {
                if (pose == PetPose.Meow) { DrawMeow(cat, motion); MarkEar(cat, earAnchor, 590, 260); }
                else if (pose == PetPose.Nap) { DrawNap(cat); MarkEar(cat, earAnchor, 419, 275); }
                else { DrawStretch(cat); MarkEar(cat, earAnchor, 392, 239); }
                return view;
            }

            var tail = new Canvas();
            cat.Children.Add(tail);
            Shape(tail, "M 414,407 C 451,398 497,383 548,383 C 587,382 619,391 621,413 C 624,430 617,437 595,443 C 565,452 497,451 432,450 Q 405,450 404,431 Q 403,413 414,407 Z", Ink, 10);
            Shape(tail, "M 448,416 C 488,415 542,419 571,405", null, 9);
            var leftTip = new Canvas();
            cat.Children.Add(leftTip);
            Shape(leftTip, "M 318,402 C 297,399 278,391 269,407 C 259,426 273,445 302,440 L 320,434 Z", Ink, 10);

            if (pose == PetPose.Tilt)
            {
                Shape(cat, "M 337,338 C 332,366 303,398 302,441 C 298,469 322,479 346,483 Q 362,490 378,482 Q 406,491 431,476 C 473,462 458,427 446,400 L 411,341 Z", Ink, 11);
            }
            var body = cat;
            cat = new Canvas();
            body.Children.Add(cat);
            if (pose == PetPose.Tilt)
            {
                var tilt = new RotateTransform(7, 323, 326);
                cat.RenderTransform = tilt;
                if (motion) Play(tilt, RotateTransform.AngleProperty, 1.15, 0,6,7,7,6,0);
                Shape(cat, "M 324,94 C 283,72 232,59 200,71 C 175,76 175,96 178,139 C 181,198 190,238 207,257 C 216,307 245,346 293,358 C 332,373 411,373 471,347 C 502,330 521,303 531,269 C 554,245 570,184 572,133 C 575,108 572,87 557,83 C 522,74 462,80 424,94 C 391,84 354,86 324,94 Z", Ink, 11);
            }
            else
            Shape(cat, "M 324,94 C 283,72 232,59 200,71 C 175,76 175,96 178,139 C 181,198 190,238 207,257 C 216,307 245,346 293,358 C 308,362 320,362 335,364 L 324,366 C 318,383 308,406 303,430 C 297,454 303,468 326,475 C 338,485 357,490 372,482 C 380,480 392,487 407,485 C 420,485 425,478 433,476 C 455,470 461,453 455,430 C 450,406 430,382 424,364 L 405,364 C 428,361 451,358 471,347 C 502,330 521,303 531,269 C 554,245 570,184 572,133 C 575,108 572,87 557,83 C 522,74 462,80 424,94 C 391,84 354,86 324,94 Z", Ink, 11);

            // The inner-ear edge follows the forehead rather than meeting it at a corner.
            MarkEar(cat, earAnchor, 557, 79);
            Shape(cat, "M 186,92 C 197,80 223,105 248,134 C 227,156 210,189 201,232 C 190,209 184,170 183,137 C 182,117 182,100 186,92 Z", Ear, 0);
            Shape(cat, "M 561,98 C 551,88 529,106 504,140 C 524,171 536,204 541,242 C 556,211 566,169 566,129 C 567,111 566,103 561,98 Z", Ear, 0);
            Shape(cat, "M 248,136 C 270,111 296,100 325,94 M 424,94 C 452,103 480,118 504,141", null, 10);
            Shape(cat, "M 192,86 C 208,81 231,102 251,131 M 497,136 C 514,111 536,88 554,90", null, 10);

            var eyes = new Canvas();
            cat.Children.Add(eyes);
            Shape(eyes, "M 286,131 C 326,128 350,157 361,202 C 371,250 359,293 330,313 C 315,323 292,326 266,322 C 229,317 211,288 209,250 C 206,206 224,161 250,143 C 261,135 271,132 286,131 Z", Ivory, 11);
            Shape(eyes, "M 441,129 C 477,126 506,148 521,183 C 539,222 534,267 515,301 C 506,319 488,325 464,326 C 425,328 395,312 381,280 C 366,248 365,213 378,177 C 389,147 413,131 441,129 Z", Ivory, 11);
            Shape(eyes, "M 282,167 C 307,164 327,186 329,218 C 330,249 319,277 295,280 C 267,286 248,268 246,236 C 244,203 254,172 274,168 Z", Ink, 12);
            Shape(eyes, "M 448,162 C 472,162 484,186 485,214 C 487,247 478,276 457,278 C 431,280 416,260 414,232 C 412,200 422,167 444,163 Z", Ink, 12);

            // Rounded brush-shaped contours keep each paw legible at tray-icon sizes.
            cat = body;
            Shape(cat, "M 319,410 C 320,405 325,407 325,414 C 326,436 328,454 339,471 L 325,470 C 317,455 313,431 319,410 Z", Outline, 0);
            Shape(cat, "M 359,418 C 363,410 368,415 370,425 L 381,474 L 367,478 C 369,455 355,430 359,418 Z", Outline, 0);
            Shape(cat, "M 386,416 C 389,408 394,409 395,420 C 397,442 393,457 391,478 L 378,479 C 384,459 382,434 386,416 Z", Outline, 0);
            Shape(cat, "M 425,410 C 426,403 432,404 435,416 C 442,443 437,462 429,474 L 414,477 C 425,461 428,434 425,410 Z", Outline, 0);

            if (pose == PetPose.Wave)
            {
                var paw = new Canvas();
                cat.Children.Add(paw);
                Shape(paw, "M 335,445 Q 311,459 288,435 L 229,381 C 217,373 214,359 219,347 Q 222,339 230,340 Q 230,329 240,329 Q 250,327 254,335 Q 265,327 273,340 Q 284,350 280,370 L 328,401 Q 346,418 335,445 Z", Ink, 10);
                var pad = ColorBrush(150,115,98);
                Shape(paw,"M 234,366 C 230,360 237,356 239,352 Q 244,346 250,350 L 262,357 Q 270,364 261,369 Q 254,366 248,369 Q 238,374 234,366 Z",pad,0);
                Shape(paw,"M 226,350 L 228,347 M 240,340 L 242,337 M 259,343 L 261,341",null,5,pad);
                var lift = new RotateTransform(3,327,427);
                paw.RenderTransform = lift;
                if (motion) Play(lift, RotateTransform.AngleProperty, .83, -18,6,-5,8,-4,-15);
            }
            if (pose == PetPose.Hop)
            {
                var shrink = new ScaleTransform(.89,.89,380,485);
                var jump = new TranslateTransform(0,-17);
                var transforms = new TransformGroup();
                transforms.Children.Add(shrink);
                transforms.Children.Add(jump);
                transforms.Children.Add(new TranslateTransform(-159,-50));
                cat.RenderTransform = transforms;
                if (motion)
                {
                    Play(shrink, ScaleTransform.ScaleXProperty, .60, 1,1.03,.90,.89,.93,1.02,1);
                    Play(shrink, ScaleTransform.ScaleYProperty, .60, 1,.91,.92,.89,.94,.94,1);
                    Play(jump, TranslateTransform.YProperty, .60, 0,0,-33,-54,-35,0,0);
                }
            }
            if (pose == PetPose.TailKittens)
            {
                cat.Children.Add(new TailLitter(leftTip, tail, eyes, animate));
                return view;
            }
            if (motion)
            {
                var eyelids = new ScaleTransform(1, 1, 369, 232);
                var tailAngle = new RotateTransform(0, 422, 427);
                eyes.RenderTransform = eyelids;
                tail.RenderTransform = tailAngle;
                var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(6.3), RepeatBehavior = RepeatBehavior.Forever };
                blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(5.87))));
                blink.KeyFrames.Add(new LinearDoubleKeyFrame(0.065, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(5.96))));
                blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(6.10))));
                var flick = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(4.7), RepeatBehavior = RepeatBehavior.Forever };
                flick.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.2))));
                flick.KeyFrames.Add(new SplineDoubleKeyFrame(-5, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.95)), new KeySpline(.4, 0, .4, 1)));
                flick.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.85)), new KeySpline(.4, 0, .4, 1)));
                Timeline.SetDesiredFrameRate(flick, 18);
                Timeline.SetDesiredFrameRate(blink, 24);
                frame.Loaded += delegate {
                    eyelids.BeginAnimation(ScaleTransform.ScaleYProperty, blink);
                    tailAngle.BeginAnimation(RotateTransform.AngleProperty, flick);
                };
                frame.Unloaded += delegate {
                    eyelids.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    tailAngle.BeginAnimation(RotateTransform.AngleProperty, null);
                };
            }

            return view;
        }

        private static void MarkEar(Canvas layer, FrameworkElement anchor, double x, double y)
        {
            if (anchor == null) return;
            Canvas.SetLeft(anchor, x);
            Canvas.SetTop(anchor, y);
            layer.Children.Add(anchor);
        }

        private static void Play(Animatable target, DependencyProperty property, double seconds, params double[] values)
        {
            var sequence = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds) };
            for (int i = 0; i < values.Length; i++)
                sequence.KeyFrames.Add(new SplineDoubleKeyFrame(values[i], KeyTime.FromPercent((double)i / (values.Length - 1)),
                    new KeySpline(.28,0,.45,1)));
            Timeline.SetDesiredFrameRate(sequence, 30);
            target.BeginAnimation(property, sequence);
        }

        private static void DrawMeow(Canvas cat, bool motion)
        {
            // Drooped ears, two curved lids, and the little blue-green triangle from the reference.
            Shape(cat, "M 403,432 Q 464,450 493,434 C 521,416 536,442 525,456 Q 506,481 425,469 Z", Ink, 10);
            Shape(cat, "M 310,389 C 281,365 255,399 263,443 Q 263,478 281,480 L 335,479 L 332,408 Z", Ink, 10);
            Shape(cat, "M 322,338 C 298,371 309,413 303,447 Q 293,481 327,483 L 402,484 C 466,486 467,454 456,418 Q 449,376 426,347 Z", Ink, 10);
            Shape(cat, "M 348,390 C 347,422 329,442 328,460 Q 327,480 345,482 M 360,475 C 356,447 356,421 370,411 Q 380,404 392,412 Q 404,424 402,445 M 392,412 Q 410,403 426,426", null, 10);
            Shape(cat, "M 461,113 C 503,127 550,175 580,234 Q 600,274 580,288 Q 555,306 513,309 L 461,247 Z", Ink, 10);
            Shape(cat, "M 518,234 Q 554,246 581,269 Q 564,293 514,299 Z", Ear, 7);
            Shape(cat, "M 311,351 C 233,347 177,311 174,253 C 167,181 206,113 277,89 C 340,65 402,73 453,100 C 492,121 522,168 522,221 C 523,282 486,339 409,352", Ink, 10);
            var faceLine = ColorBrush(100,65,49);
            Shape(cat, "M 201,145 Q 242,146 268,156 M 336,157 Q 393,132 489,151", null, 10, faceLine);
            var mouth = new Canvas();
            cat.Children.Add(mouth);
            Shape(mouth, "M 275,188 Q 274,186 277,186 L 331,186 Q 334,186 333,189 L 307,275 Q 305,280 303,274 Z", ColorBrush(130,168,165), 7, faceLine);
            if (motion)
            {
                var open = new ScaleTransform(1,1,304,187);
                mouth.RenderTransform = open;
                Play(open,ScaleTransform.ScaleYProperty,1.10,.05,.66,1,.98,.96,.53,.04);
            }
        }

        private static void DrawNap(Canvas cat)
        {
            Shape(cat,"M 240,432 C 234,372 315,316 408,318 C 486,313 552,345 572,391 C 596,445 547,478 454,482 L 299,481 Q 233,473 240,432 Z", Ink, 10);
            Shape(cat,"M 222,361 C 210,337 203,294 212,283 Q 224,275 262,306 Q 314,278 370,304 Q 404,271 420,279 Q 433,294 423,346 C 446,383 422,432 371,444 C 320,457 246,451 222,415 Q 206,391 222,361 Z", Ink, 10);
            Shape(cat,"M 217,294 Q 230,294 251,311 L 225,337 Z M 383,309 Q 402,289 415,289 L 409,328 Z",Ear, 0);
            Shape(cat,"M 237,384 Q 257,399 281,389 M 318,388 Q 340,400 362,377",null,9);
            Shape(cat,"M 565,386 C 592,412 578,450 543,466 C 482,491 337,490 274,467 Q 246,456 256,438 Q 264,418 296,429 C 368,452 467,454 509,432",Ink,10);
        }

        private static void DrawStretch(Canvas cat)
        {
            Shape(cat,"M 486,335 C 531,332 537,274 527,235 C 520,210 530,195 546,203 C 570,214 578,284 562,329 Q 545,367 499,375 Z",Ink,10);
            Shape(cat,"M 313,381 C 356,369 400,320 450,300 Q 502,283 524,321 C 544,358 528,415 508,440 Q 538,448 527,466 Q 506,484 460,471 L 454,408 Q 420,433 360,436 L 297,469 Q 269,488 238,477 C 221,468 235,450 261,443 L 316,413 Z", Ink,10);
            Shape(cat,"M 360,427 L 306,466 Q 287,482 268,477 M 470,350 Q 456,374 453,407",null,8);
            Shape(cat,"M 206,333 Q 178,279 188,254 Q 202,241 256,285 Q 291,264 341,275 Q 368,245 392,243 Q 411,253 395,311 C 419,338 412,383 383,405 C 345,433 270,444 226,415 Q 197,392 206,333 Z",Ink,10);
            Shape(cat,"M 196,263 Q 216,268 241,288 L 209,314 Z M 352,280 Q 372,256 387,253 L 383,298 Z",Ear,0);
            Shape(cat,"M 224,357 Q 251,342 278,350 M 319,341 Q 349,321 377,331",null,9);
        }
    }
}
