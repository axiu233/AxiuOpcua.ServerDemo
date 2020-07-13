using Axiu.Opcua.Demo.Service;
using System;

namespace Axiu.Opcua.Demo.Entry
{
    class Program
    {
        static void Main(string[] args)
        {
            OpcuaManagement server = new OpcuaManagement();
            server.CreateServerInstance();
            Console.WriteLine("OPC-UA服务已启动...");
            Console.ReadLine();
        }
    }
}
