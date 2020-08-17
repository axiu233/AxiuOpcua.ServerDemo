using Opc.Ua;
using Opc.Ua.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Axiu.Opcua.Demo.Service
{
    public class DiscoveryManagement
    {
        /// <summary>
        /// 启动一个Discovery服务端
        /// </summary>
        public void StartDiscovery()
        {
            try
            {
                var config = new ApplicationConfiguration()
                {
                    ApplicationName = "Axiu UA Discovery",
                    ApplicationUri = Utils.Format(@"urn:{0}:AxiuUADiscovery", System.Net.Dns.GetHostName()),
                    ApplicationType = ApplicationType.DiscoveryServer,
                    ServerConfiguration = new ServerConfiguration()
                    {
                        BaseAddresses = { "opc.tcp://localhost:4840/" },
                        MinRequestThreadCount = 5,
                        MaxRequestThreadCount = 100,
                        MaxQueuedRequestCount = 200
                    },
                    DiscoveryServerConfiguration = new DiscoveryServerConfiguration()
                    {
                        BaseAddresses = { "opc.tcp://localhost:4840/" },
                        ServerNames = { "OpcuaDiscovery" }
                    },
                    SecurityConfiguration = new SecurityConfiguration
                    {
                        ApplicationCertificate = new CertificateIdentifier { StoreType = @"Directory", StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\MachineDefault", SubjectName = Utils.Format(@"CN={0}, DC={1}", "AxiuOpcua", System.Net.Dns.GetHostName()) },
                        TrustedIssuerCertificates = new CertificateTrustList { StoreType = @"Directory", StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\UA Certificate Authorities" },
                        TrustedPeerCertificates = new CertificateTrustList { StoreType = @"Directory", StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\UA Applications" },
                        RejectedCertificateStore = new CertificateTrustList { StoreType = @"Directory", StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\RejectedCertificates" },
                        AutoAcceptUntrustedCertificates = true,
                        AddAppCertToTrustedStore = true
                    },
                    TransportConfigurations = new TransportConfigurationCollection(),
                    TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
                    ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
                    TraceConfiguration = new TraceConfiguration()
                };
                config.Validate(ApplicationType.DiscoveryServer).GetAwaiter().GetResult();
                if (config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                {
                    config.CertificateValidator.CertificateValidation += (s, e) => { e.Accept = (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted); };
                }

                var application = new ApplicationInstance
                {
                    ApplicationName = "Axiu UA Discovery",
                    ApplicationType = ApplicationType.DiscoveryServer,
                    ApplicationConfiguration = config
                };
                //application.CheckApplicationInstanceCertificate(false, 2048).GetAwaiter().GetResult();
                bool certOk = application.CheckApplicationInstanceCertificate(false, 0).Result;
                if (!certOk)
                {
                    Console.WriteLine("证书验证失败!");
                }

                var server = new DiscoveryServer();
                // start the server.
                application.Start(server).Wait();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("启动OPC-UA Discovery服务端触发异常:" + ex.Message);
                Console.ResetColor();
            }
        }
    }

    /// <summary>
    /// 已注册服务列表
    /// </summary>
    /// <remarks>
    /// 主要用于保存服务注册信息,客户端获取列表时将对应的信息返回给客户端
    /// </remarks>
    public class RegisteredServerTable
    {
        public string ServerUri { get; set; }

        public string ProductUri { get; set; }

        public LocalizedTextCollection ServerNames { get; set; }

        public ApplicationType ServerType { get; set; }

        public string GatewayServerUri { get; set; }

        public StringCollection DiscoveryUrls { get; set; }

        public string SemaphoreFilePath { get; set; }

        public bool IsOnline { get; set; }

        /// <summary>
        /// 最后一次注册时间
        /// </summary>
        public DateTime LastRegistered { get; set; }
    }
}
