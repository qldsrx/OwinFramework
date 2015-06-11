using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OwinLight
{
    public class Debug
    {
        /// <summary>
        /// 使用时请自行调整输出位置，默认linux下面输出到/tmp目录下面，而windows下面直接输出到控制台，因为windows下面一般是自承载调试的。
        /// </summary>
        /// <param name="msg">消息</param>
        /// <param name="pre">前缀</param>
        public static void Write(string msg, string pre = "null")
        {
            if (System.Environment.NewLine == "\r\n")
            {
                Console.WriteLine(DateTime.Now.ToString() + "(" + pre + "):" + msg + "\r\n");
            }
            else
            {
                File.AppendAllText("/tmp/OwinLight.log", DateTime.Now.ToString() + "(" + pre + "):" + msg + "\r\n");
            }
        }
    }
}
