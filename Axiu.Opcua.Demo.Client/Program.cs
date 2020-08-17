using PECSDBInterface;
using System;
using System.Data.SqlClient;

namespace Axiu.Opcua.Demo.Client
{
    class Program
    {
        static void Main(string[] args)
        {
            OpcuaClientService service = new OpcuaClientService();
            service.Execte();
        }
    }
}
