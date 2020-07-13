using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Axiu.Opcua.Demo.Client
{
    public class OpcuaClientService
    {
        private string url = "opc.tcp://localhost:8020/AxiuOpcua/DemoServer";
        //ns=2;s=Scalar_CreateTime

        public void Execte()
        {
            var config = new ApplicationConfiguration()
            {
                ApplicationName = "CET-Opcua",
                ApplicationUri = Utils.Format(@"urn:{0}:CET-Opcua", System.Net.Dns.GetHostName()),
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier { StoreType = @"Directory", StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\MachineDefault", SubjectName = Utils.Format(@"CN={0}, DC={1}", "MyHomework", System.Net.Dns.GetHostName()) },
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
            config.Validate(ApplicationType.Client).GetAwaiter().GetResult();
            if (config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                config.CertificateValidator.CertificateValidation += (s, e) => { e.Accept = (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted); };
            }
            var application = new ApplicationInstance
            {
                ApplicationName = "CET-Opcua",
                ApplicationType = ApplicationType.Client,
                ApplicationConfiguration = config
            };
            application.CheckApplicationInstanceCertificate(false, 2048).GetAwaiter().GetResult();
            var selectedEndpoint = CoreClientUtils.SelectEndpoint(url, useSecurity: true, 15000);
            Console.WriteLine("配置已准备完毕,即将打开链接会话...");

            using (var session = Session.Create(config, new ConfiguredEndpoint(null, selectedEndpoint, EndpointConfiguration.Create(config)), false, "", 60000, null, null).GetAwaiter().GetResult())
            {
                //读取目录下所有节点
                ReferenceDescriptionCollection nextRefs;
                byte[] nextCp;
                session.Browse(null, null, "ns=2;s=11", 0u, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences, true, (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method, out nextCp, out nextRefs);
                foreach (var nextRd in nextRefs)
                {
                    Console.WriteLine("{0}: {1}, {2}", nextRd.DisplayName, nextRd.BrowseName, nextRd.NodeClass);
                }

                //读单个节点
                ReadValueIdCollection nodesToRead = new ReadValueIdCollection
                {
                    new ReadValueId( )
                    {
                        NodeId = "ns=2;s=111",
                        AttributeId = Attributes.Value
                    }
                };
                // read the current value
                session.Read(null, 0, TimestampsToReturn.Neither, nodesToRead, out DataValueCollection results, out DiagnosticInfoCollection diagnosticInfos);
                ClientBase.ValidateResponse(results, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
                Console.WriteLine("取到s=111结果: " + results[0].ToString());

                //读取历史数据
                HistoryReadValueIdCollection historyReads = new HistoryReadValueIdCollection()
                {
                    new HistoryReadValueId( )
                    {
                        NodeId = "ns=2;s=111"
                    }
                };
                ExtensionObject extension = new ExtensionObject();
                ReadProcessedDetails details = new ReadProcessedDetails() { StartTime = DateTime.Now.AddMinutes(-5), EndTime = DateTime.Now };
                extension.Body = details;
                session.HistoryRead(null, extension, TimestampsToReturn.Neither, true, historyReads, out HistoryReadResultCollection hresults, out DiagnosticInfoCollection hdiagnosticInfos);
                foreach (var item in hresults)
                {
                    var res = item.HistoryData.Body as Opc.Ua.KeyValuePair;
                    Console.WriteLine("历史数据:" + res.Key.Name + " --- " + res.Value);
                }

                Console.WriteLine("即将开始订阅固定节点消息...");
                var subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = 3000 };
                var list = new List<MonitoredItem>
                {
                    new MonitoredItem(subscription.DefaultItem) { DisplayName = "非常好", StartNodeId = "ns=2;s=111" }
                };
                list.ForEach(i => i.Notification += OnNotification);
                subscription.AddItems(list);
                session.AddSubscription(subscription);
                subscription.Create();

                Console.WriteLine("指定节点消息已订阅,请勿关闭程序...");
                Console.ReadKey(true);
                subscription.DeleteItems();
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey(true);
        }
        private static void OnNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            foreach (var value in item.DequeueValues())
            {
                Console.WriteLine("{0}: {1}, {2}", item.DisplayName, value.Value, value.StatusCode);
            }
        }
    }
}
