using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGet.Test.Utility
{
    public class PathFixUtility
    {
        /// <summary>
        /// Converts '\' to Path.DirectorySerparatorChar.
        /// </summary>
        /// <remarks>
        /// By convention, all paths in unit tests use a backslash when they are defined.
        /// However, on non-windows, forward-slash is used. 
        /// This method is a shorthand to fix this when using the tests.
        /// </remarks>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string FixPath(string path)
        {
            return String.IsNullOrWhiteSpace(path) ? path : path.Replace('\\', Path.DirectorySeparatorChar);
        }

    }
}
