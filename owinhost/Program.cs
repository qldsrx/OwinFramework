using Microsoft.Owin.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace owinhost
{
    class Program
    {
        static void Main(string[] args)
        {
            var url = "http://+:8080";
            using (WebApp.Start<OwinLight.Adapter>(url))// WIN7以上需要管理员权限运行
            {
                Console.WriteLine("Running on {0}", url);
                Console.WriteLine("Press enter to exit");
                Console.ReadLine();
            }
        }
    }
}
