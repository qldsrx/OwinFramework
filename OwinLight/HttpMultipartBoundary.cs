using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OwinLight
{
    public class HttpMultipartBoundary
    {
        const byte LF = (byte)'\n';
        const byte CR = (byte)'\r';
        static Regex NameReg = new Regex(@"(?<=[:; ]name="")[^""]*", RegexOptions.Compiled);
        static Regex FilenameReg = new Regex(@"(?<=[:; ]filename="")[^""]*", RegexOptions.Compiled);

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpMultipartBoundary"/> class.
        /// </summary>
        /// <param name="boundaryStream">The stream that contains the boundary information.</param>
        public HttpMultipartBoundary(HttpMultipartSubStream boundaryStream)
        {
            this.Value = boundaryStream;
            this.ExtractHeaders();
        }

        /// <summary>
        /// Gets the contents type of the boundary value.
        /// </summary>
        /// <value>A <see cref="string"/> containing the name of the value if it is available; otherwise <see cref="string.Empty"/>.</value>
        public string ContentType { get; private set; }

        /// <summary>
        /// Gets or the filename for the boundary value.
        /// </summary>
        /// <value>A <see cref="string"/> containing the filename value if it is available; otherwise <see cref="string.Empty"/>.</value>
        /// <remarks>This is the RFC2047 decoded value of the filename attribute of the Content-Disposition header.</remarks>
        public string Filename { get; private set; }

        /// <summary>
        /// Gets name of the boundary value.
        /// </summary>
        /// <remarks>This is the RFC2047 decoded value of the name attribute of the Content-Disposition header.</remarks>
        public string Name { get; private set; }

        /// <summary>
        /// A stream containing the value of the boundary.
        /// </summary>
        /// <remarks>This is the RFC2047 decoded value of the Content-Type header.</remarks>
        public HttpMultipartSubStream Value { get; private set; }

        private void ExtractHeaders()
        {
            while (true)
            {
                var header = ReadLineFromStream(this.Value);

                if (string.IsNullOrEmpty(header))
                {
                    break;
                }
                //此处两个正则进行了BUG修复+优化，原先的有问题。
                if (header.StartsWith("Content-Disposition", StringComparison.CurrentCultureIgnoreCase))
                {
                    this.Name = NameReg.Match(header).Value;
                    this.Filename = FilenameReg.Match(header).Value;
                }

                if (header.StartsWith("Content-Type", StringComparison.InvariantCultureIgnoreCase))
                {
                    this.ContentType = header.Split(new[] { ':', ' ' }, StringSplitOptions.RemoveEmptyEntries).Last().Trim();
                }
            }

            this.Value.PositionStartAtCurrentLocation();
        }

        private static string ReadLineFromStream(Stream stream)
        {
            var readBuffer = new List<byte>();

            while (true)
            {
                var byteReadFromStream = stream.ReadByte();

                if (byteReadFromStream == -1)
                {
                    return null;
                }

                if (byteReadFromStream.Equals(LF))
                {
                    break;
                }

                readBuffer.Add((byte)byteReadFromStream);
            }

            return Encoding.UTF8.GetString(readBuffer.ToArray()).Trim((char)CR);
        }
    }
}
