using System.Drawing;

namespace Jietu
{
    static class ColorText
    {
        public static string Rgb(Color color)
        {
            return "rgb(" + color.R + ", " + color.G + ", " + color.B + ")";
        }

        public static string Hex(Color color)
        {
            return string.Format("#{0:x2}{1:x2}{2:x2}", color.R, color.G, color.B);
        }
    }
}
