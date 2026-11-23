using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Xiaohei
{
    internal static class RenderIcon
    {
        [STAThread]
        static void Main(string[] args)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            var images = new List<byte[]>();
            foreach (int size in sizes)
            {
                FrameworkElement pet = PetArt.Create(false);
                pet.Measure(new Size(size, size));
                pet.Arrange(new Rect(0, 0, size, size));
                pet.UpdateLayout();
                var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(pet);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = new MemoryStream())
                {
                    encoder.Save(stream);
                    images.Add(stream.ToArray());
                }
            }
            using (var writer = new BinaryWriter(File.Create(args[0])))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)sizes.Length);
                int offset = 6 + sizes.Length * 16;
                for (int i = 0; i < sizes.Length; i++)
                {
                    writer.Write((byte)(sizes[i] % 256));
                    writer.Write((byte)(sizes[i] % 256));
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write(images[i].Length);
                    writer.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] img in images) writer.Write(img);
            }
            File.WriteAllBytes(args[1], images[images.Count - 1]);
        }
    }
}
