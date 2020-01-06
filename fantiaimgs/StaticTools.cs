using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace fantiaimgs
{
    class StaticTools
    {
        static internal string SafeFilename(string fname)
        {
            string inv = new string(System.IO.Path.GetInvalidFileNameChars());
            foreach (char c in inv)
            {
                fname = fname.Replace(c.ToString(), "_");
            }
            return fname;
        }
    }
}
