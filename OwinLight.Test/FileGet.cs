using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OwinLight.Test
{
    [Rewrite("/api/FileGet/{fileid}/{filename}")]
    public class FileGet
    {
        public int fileid { get; set; }
        public string filename { get; set; }
        public bool download { get; set; }
    }
}
