using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Axiu.Opcua.Demo.Client
{
    public class OpcuaClientService
    {
        private static volatile int ntCount = 0;
        private string url = "opc.tcp://172.17.6.15:48020";
        //ns=2;s=Scalar_CreateTime

        public void Execte()
        {
            string storePath = AppContext.BaseDirectory;
            var config = new ApplicationConfiguration()
            {
                ApplicationName = "Axiu-Opcua",
                ApplicationUri = Utils.Format(@"urn:{0}:Axiu-Opcua", System.Net.Dns.GetHostName()),
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier { StoreType = @"Directory", StorePath = Path.Combine(storePath, @"OPC Foundation/CertificateStores/MachineDefault"), SubjectName = Utils.Format(@"CN={0}, DC={1}", "Axiu-Opcua", System.Net.Dns.GetHostName()) },
                    TrustedIssuerCertificates = new CertificateTrustList { StoreType = @"Directory", StorePath = Path.Combine(storePath, @"OPC Foundation/CertificateStores/UA Certificate Authorities") },
                    TrustedPeerCertificates = new CertificateTrustList { StoreType = @"Directory", StorePath = Path.Combine(storePath, @"OPC Foundation/CertificateStores/UA Applications") },
                    RejectedCertificateStore = new CertificateTrustList { StoreType = @"Directory", StorePath = Path.Combine(storePath, @"OPC Foundation/CertificateStores/RejectedCertificates") },
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
                ApplicationName = "Axiu-Opcua",
                ApplicationType = ApplicationType.Client,
                ApplicationConfiguration = config
            };
            application.CheckApplicationInstanceCertificate(false, 2048).GetAwaiter().GetResult();
            var selectedEndpoint = CoreClientUtils.SelectEndpoint(url, useSecurity: true, int.MaxValue);
            Console.WriteLine("配置已准备完毕,即将打开链接会话...");

            using (var session = Session.Create(config, new ConfiguredEndpoint(null, selectedEndpoint, EndpointConfiguration.Create(config)), false, "", int.MaxValue, null, null).GetAwaiter().GetResult())
            {
                #region 读取目录下所有节点
                //读取目录下所有节点
                List<string> idList = new List<string>();
                ReferenceDescriptionCollection nextRefs;
                byte[] nextCp;
                ////session.Browse(null, null, "ns=2;s=1_6_20", 0u, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences, true, (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method, out nextCp, out nextRefs);
                ////foreach (var nextRd in nextRefs)
                ////{
                ////    //Console.WriteLine("{0}: {1}, {2}", nextRd.DisplayName, nextRd.BrowseName, nextRd.NodeClass);
                ////    idList.Add(nextRd.NodeId.ToString());
                ////}

                Console.Write("请输入监听通道ID:");
                string devStr = Console.ReadLine();
                if (string.IsNullOrEmpty(devStr)) return;
                List<string> folders = new List<string>();
                string nodeId = "ns=2;s=1_" + devStr;
                session.Browse(null, null, nodeId, 0u, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences, true, (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method, out nextCp, out nextRefs);
                foreach (var nextRd in nextRefs)
                {
                    //Console.WriteLine("{0}: {1}, {2}", nextRd.DisplayName, nextRd.BrowseName, nextRd.NodeClass);
                    folders.Add(nextRd.NodeId.ToString());
                }
                List<string> nodes = new List<string>();
                foreach (var item in folders)
                {
                    session.Browse(null, null, item, 0u, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences, true, (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method, out nextCp, out nextRefs);
                    foreach (var nextRd in nextRefs)
                    {
                        //Console.WriteLine("{0}: {1}, {2}", nextRd.DisplayName, nextRd.BrowseName, nextRd.NodeClass);
                        nodes.Add(nextRd.NodeId.ToString());
                    }
                }
                Console.WriteLine("通道总节点数:" + nodes.Count + "; 第一个:" + nodes[0]);
                #endregion

                #region 读单个节点
                //读单个节点
                //ReadValueIdCollection nodesToRead = new ReadValueIdCollection
                //{
                //    new ReadValueId( )
                //    {
                //        NodeId = "ns=2;s=0_0_0_1_7",
                //        AttributeId = Attributes.Value
                //    }
                //};
                //// read the current value
                //session.Read(null, 0, TimestampsToReturn.Neither, nodesToRead, out DataValueCollection results, out DiagnosticInfoCollection diagnosticInfos);
                //ClientBase.ValidateResponse(results, nodesToRead);
                //ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
                //Console.WriteLine("取到单值结果: " + results[0].ToString()); 
                #endregion

                #region 读取历史数据
                //读取历史数据
                //HistoryReadValueIdCollection historyReads = new HistoryReadValueIdCollection()
                //{
                //    new HistoryReadValueId( )
                //    {
                //        NodeId = "ns=2;s=0_0_0_1_7"
                //    }
                //};
                //ExtensionObject extension = new ExtensionObject();
                //ReadProcessedDetails details = new ReadProcessedDetails() { StartTime = DateTime.Now.AddMinutes(-5), EndTime = DateTime.Now };
                //extension.Body = details;
                //session.HistoryRead(null, extension, TimestampsToReturn.Neither, true, historyReads, out HistoryReadResultCollection hresults, out DiagnosticInfoCollection hdiagnosticInfos);
                //foreach (var item in hresults)
                //{
                //    var res = item.HistoryData.Body as Opc.Ua.KeyValuePair;
                //    Console.WriteLine("历史数据:" + res.Key.Name + " --- " + res.Value);
                //} 
                #endregion

                #region 读取历史事件
                //HistoryReadValueIdCollection historyEvents = new HistoryReadValueIdCollection()
                //{
                //    new HistoryReadValueId( )
                //    {
                //        NodeId = "ns=2;s=1_1_1"
                //    }
                //};
                //EventFilter hFilter = new EventFilter();
                //hFilter.AddSelectClause(ObjectTypeIds.BaseEventType, new QualifiedName("id=0101"));
                //ExtensionObject extension2 = new ExtensionObject();
                //ReadEventDetails details2 = new ReadEventDetails() { StartTime = DateTime.Now.AddDays(-1), EndTime = DateTime.Now, Filter = hFilter };
                //extension2.Body = details2;
                //session.HistoryRead(null, extension2, TimestampsToReturn.Neither, false, historyEvents, out HistoryReadResultCollection hresults2, out DiagnosticInfoCollection hdiagnosticInfos2);
                //foreach (var item in hresults2)
                //{
                //    if (item.HistoryData.Body!=null)
                //    {
                //        var res = item.HistoryData.Body as byte[];
                //        Console.WriteLine(Encoding.UTF8.GetString(res));
                //    }
                //} 
                #endregion

                #region 订阅事件
                var subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = 3000 };
                EventFilter eventFilter = new EventFilter();
                eventFilter.AddSelectClause(ObjectTypeIds.BaseEventType, new QualifiedName(BrowseNames.Message));

                List<MonitoredItem> list = new List<MonitoredItem>();
                foreach (var item in folders)
                {
                    MonitoredItem model = new MonitoredItem(subscription.DefaultItem) { DisplayName = item+"的事件", StartNodeId = item, Filter = eventFilter, AttributeId = 12 };
                    model.Notification += OnEventNotification;
                    list.Add(model);
                    break;
                }
                subscription.AddItems(list);
                bool radd = session.AddSubscription(subscription);
                subscription.Create();
                #endregion

                #region 订阅测点
                Console.WriteLine("即将开始订阅固定节点消息...");
                //var submeas = new Subscription(session.DefaultSubscription) { PublishingInterval = 1000 };
                //List<MonitoredItem> measlist = new List<MonitoredItem>();
                //int index = 0, jcount = 0;
                //foreach (var item in nodes)
                //{
                //    index++; jcount++;
                //    MonitoredItem monitor = new MonitoredItem(submeas.DefaultItem) { DisplayName = "测点:" + item, StartNodeId = item };
                //    monitor.Notification += OnNotification;
                //    measlist.Add(monitor);
                //    if (index > 5999)
                //    {
                //        submeas.AddItems(measlist);
                //        session.AddSubscription(submeas);
                //        submeas.Create();
                //        index = 0;
                //        submeas = new Subscription(session.DefaultSubscription) { PublishingInterval = 1000 };
                //        measlist.Clear();
                //    }
                //}
                //if (measlist.Count > 0)
                //{
                //    submeas.AddItems(measlist);
                //    session.AddSubscription(submeas);
                //    submeas.Create();
                //}
                //Console.WriteLine("订阅节点数:" + jcount);

                //var subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = 1000 };
                //List<MonitoredItem> list = new List<MonitoredItem>();
                //foreach (var item in nodes.Take(5000))
                //{
                //    MonitoredItem monitor = new MonitoredItem(subscription.DefaultItem) { DisplayName = "测点:" + item, StartNodeId = item };
                //    list.Add(monitor);
                //}
                //list.ForEach(i => i.Notification += OnNotification);
                //subscription.AddItems(list);
                //session.AddSubscription(subscription);
                //subscription.Create();
                #endregion

                #region 写入内容
                //WriteValueCollection writeValues = new WriteValueCollection()
                //{
                //    new WriteValue()
                //    {
                //        NodeId="ns=2;s=1_ctr_1",
                //        AttributeId=Attributes.Value,
                //        Value=new DataValue(){ Value="false" }
                //    }
                //};
                //session.Write(null, writeValues, out StatusCodeCollection wresults, out DiagnosticInfoCollection wdiagnosticInfos);
                //foreach (var item in wresults)
                //{
                //    Console.WriteLine("写入结果:" + item.ToString());
                //}
                #endregion

                Console.WriteLine("指定节点消息已订阅,请勿关闭程序...");
                while (true)
                {
                    Thread.Sleep(3000);
                    Console.WriteLine("测点更新次数:"+ntCount);
                    ntCount = 0;
                }
                Console.ReadKey(true);
                //subscription.DeleteItems();
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey(true);
        }
        private static void OnNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            Interlocked.Increment(ref ntCount);
            //foreach (var value in item.DequeueValues())
            //{
            //    Console.WriteLine("{0}: {1}, {2}", item.DisplayName, value.Value, value.StatusCode);
            //}
            //var evts = item.LastValue as EventFieldList;
            //if (evts != null)
            //{
            //    var fields = evts.EventFields;
            //    foreach (var field in fields)
            //    {
            //        Console.WriteLine("事件信息:" + field.Value.ToString());
            //    }
            //}
        }

        private static void OnEventNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            var evts = item.LastValue as EventFieldList;
            if (evts != null)
            {
                var fields = evts.EventFields;
                foreach (var field in fields)
                {
                    Console.WriteLine("事件信息:" + field.Value.ToString());
                }
            }
        }
    }
}
