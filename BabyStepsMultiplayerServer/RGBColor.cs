using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BabyStepsMultiplayerServer
{
    public class RGBColor
    {
        public byte R, G, B;
        public RGBColor(byte r, byte g, byte b) { R = r; G = g; B = b; }
        public string GetString()
        {
            return $"[{R},{G},{B}]";
        }
    }
}
