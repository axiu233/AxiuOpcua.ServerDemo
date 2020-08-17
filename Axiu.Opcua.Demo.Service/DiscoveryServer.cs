using Opc.Ua;
using Opc.Ua.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Axiu.Opcua.Demo.Service
{
    public class DiscoveryServer : StandardServer, IDiscoveryServer
    {
        private ServerInternalData m_serverInternal;
        /// <summary>
        /// 清理掉线服务端定时器
        /// </summary>
        private static Timer m_timer = null;
        /// <summary>
        /// 已注册服务端列表
        /// </summary>
        List<RegisteredServerTable> _serverTable = new List<RegisteredServerTable>();

        /// <summary>
        /// 加载服务属性(这里采用直接写死,不用配置文件的方式)
        /// </summary>
        /// <returns></returns>
        protected override ServerProperties LoadServerProperties()
        {
            ServerProperties properties = new ServerProperties();

            properties.ManufacturerName = "OPC Foundation";
            properties.ProductName = "CET Discovery Server";
            properties.ProductUri = "http://opcfoundation.org/Quickstart/ReferenceServer/v1.04";
            properties.SoftwareVersion = Utils.GetAssemblySoftwareVersion();
            properties.BuildNumber = Utils.GetAssemblyBuildNumber();
            properties.BuildDate = Utils.GetAssemblyTimestamp();

            return properties;
        }

        /// <summary>
        /// 获取终结点实例(此方法必须重写,Discovery服务端必须创建DiscoveryEndpoint终结点,而实时数据服务端必须创建SessionEndpoint终结点)
        /// </summary>
        /// <param name="server"></param>
        /// <returns></returns>
        protected override EndpointBase GetEndpointInstance(ServerBase server)
        {
            return new DiscoveryEndpoint(server);//SessionEndpoint
        }

        /// <summary>
        /// 启动服务
        /// </summary>
        /// <param name="configuration"></param>
        protected override void StartApplication(ApplicationConfiguration configuration)
        {
            lock (m_lock)
            {
                try
                {
                    // create the datastore for the instance.
                    m_serverInternal = new ServerInternalData(
                        ServerProperties,
                        configuration,
                        MessageContext,
                        new CertificateValidator(),
                        InstanceCertificate);

                    // create the manager responsible for providing localized string resources.                    
                    ResourceManager resourceManager = CreateResourceManager(m_serverInternal, configuration);

                    // create the manager responsible for incoming requests.
                    RequestManager requestManager = new RequestManager(m_serverInternal);

                    // create the master node manager.
                    MasterNodeManager masterNodeManager = new MasterNodeManager(m_serverInternal, configuration, null);

                    // add the node manager to the datastore. 
                    m_serverInternal.SetNodeManager(masterNodeManager);

                    // put the node manager into a state that allows it to be used by other objects.
                    masterNodeManager.Startup();

                    // create the manager responsible for handling events.
                    EventManager eventManager = new EventManager(m_serverInternal, (uint)configuration.ServerConfiguration.MaxEventQueueSize);

                    // creates the server object. 
                    m_serverInternal.CreateServerObject(
                        eventManager,
                        resourceManager,
                        requestManager);


                    // create the manager responsible for aggregates.
                    m_serverInternal.AggregateManager = CreateAggregateManager(m_serverInternal, configuration);

                    // start the session manager.
                    SessionManager sessionManager = new SessionManager(m_serverInternal, configuration);
                    sessionManager.Startup();

                    // start the subscription manager.
                    SubscriptionManager subscriptionManager = new SubscriptionManager(m_serverInternal, configuration);
                    subscriptionManager.Startup();

                    // add the session manager to the datastore. 
                    m_serverInternal.SetSessionManager(sessionManager, subscriptionManager);

                    ServerError = null;

                    // set the server status as running.
                    SetServerState(ServerState.Running);

                    // monitor the configuration file.
                    if (!String.IsNullOrEmpty(configuration.SourceFilePath))
                    {
                        var m_configurationWatcher = new ConfigurationWatcher(configuration);
                        m_configurationWatcher.Changed += new EventHandler<ConfigurationWatcherEventArgs>(this.OnConfigurationChanged);
                    }

                    CertificateValidator.CertificateUpdate += OnCertificateUpdate;
                    //60s后开始清理过期服务列表,此后每60s检查一次
                    m_timer = new Timer(ClearNoliveServer, null, 60000, 60000);
                    Console.WriteLine("Discovery服务已启动完成,请勿退出程序!!!");
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error starting application");
                    m_serverInternal = null;
                    ServiceResult error = ServiceResult.Create(e, StatusCodes.BadInternalError, "Unexpected error starting application");
                    ServerError = error;
                    throw new ServiceResultException(error);
                }
            }
        }

        /// <summary>
        /// 初始化服务监听(此方法可以不重写)
        /// </summary>
        /// <param name="configuration"></param>
        /// <param name="serverDescription"></param>
        /// <param name="endpoints"></param>
        /// <returns></returns>
        protected override IList<Task> InitializeServiceHosts(
            ApplicationConfiguration configuration,
            out ApplicationDescription serverDescription,
            out EndpointDescriptionCollection endpoints)
        {
            serverDescription = null;
            endpoints = null;

            Dictionary<string, Task> hosts = new Dictionary<string, Task>();

            // ensure at least one security policy exists.
            if (configuration.ServerConfiguration.SecurityPolicies.Count == 0)
            {
                configuration.ServerConfiguration.SecurityPolicies.Add(new ServerSecurityPolicy());
            }

            // ensure at least one user token policy exists.
            if (configuration.ServerConfiguration.UserTokenPolicies.Count == 0)
            {
                UserTokenPolicy userTokenPolicy = new UserTokenPolicy();

                userTokenPolicy.TokenType = UserTokenType.Anonymous;
                userTokenPolicy.PolicyId = userTokenPolicy.TokenType.ToString();

                configuration.ServerConfiguration.UserTokenPolicies.Add(userTokenPolicy);
            }

            // set server description.
            serverDescription = new ApplicationDescription();

            serverDescription.ApplicationUri = configuration.ApplicationUri;
            serverDescription.ApplicationName = new LocalizedText("en-US", configuration.ApplicationName);
            serverDescription.ApplicationType = configuration.ApplicationType;
            serverDescription.ProductUri = configuration.ProductUri;
            serverDescription.DiscoveryUrls = GetDiscoveryUrls();

            endpoints = new EndpointDescriptionCollection();
            IList<EndpointDescription> endpointsForHost = null;

            // create UA TCP host.
            endpointsForHost = CreateUaTcpServiceHost(
                hosts,
                configuration,
                configuration.ServerConfiguration.BaseAddresses,
                serverDescription,
                configuration.ServerConfiguration.SecurityPolicies);

            endpoints.InsertRange(0, endpointsForHost);

            // create HTTPS host.
#if !NO_HTTPS
            endpointsForHost = CreateHttpsServiceHost(
                hosts,
                configuration,
                configuration.ServerConfiguration.BaseAddresses,
                serverDescription,
                configuration.ServerConfiguration.SecurityPolicies);

            endpoints.AddRange(endpointsForHost);
#endif
            return new List<Task>(hosts.Values);
        }

        /// <summary>
        /// 发现服务(由客户端请求)
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="endpointUrl"></param>
        /// <param name="localeIds"></param>
        /// <param name="serverUris"></param>
        /// <param name="servers"></param>
        /// <returns></returns>
        public override ResponseHeader FindServers(
            RequestHeader requestHeader,
            string endpointUrl,
            StringCollection localeIds,
            StringCollection serverUris,
            out ApplicationDescriptionCollection servers)
        {
            servers = new ApplicationDescriptionCollection();

            ValidateRequest(requestHeader);

            Console.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ":请求查找服务...");
            string hostName = Dns.GetHostName();

            lock (_serverTable)
            {
                foreach (var item in _serverTable)
                {
                    StringCollection urls = new StringCollection();
                    foreach (var url in item.DiscoveryUrls)
                    {
                        if (url.Contains("localhost"))
                        {
                            string str = url.Replace("localhost", hostName);
                            urls.Add(str);
                        }
                        else
                        {
                            urls.Add(url);
                        }
                    }

                    servers.Add(new ApplicationDescription()
                    {
                        ApplicationName = item.ServerNames.FirstOrDefault(),
                        ApplicationType = item.ServerType,
                        ApplicationUri = item.ServerUri,
                        DiscoveryProfileUri = item.SemaphoreFilePath,
                        DiscoveryUrls = urls,
                        ProductUri = item.ProductUri,
                        GatewayServerUri = item.GatewayServerUri
                    });
                }
            }

            return CreateResponse(requestHeader, StatusCodes.Good);
        }

        /// <summary>
        /// 获得终结点列表
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="endpointUrl"></param>
        /// <param name="localeIds"></param>
        /// <param name="profileUris"></param>
        /// <param name="endpoints"></param>
        /// <returns></returns>
        public override ResponseHeader GetEndpoints(
            RequestHeader requestHeader,
            string endpointUrl,
            StringCollection localeIds,
            StringCollection profileUris,
            out EndpointDescriptionCollection endpoints)
        {
            endpoints = null;

            ValidateRequest(requestHeader);

            try
            {
                lock (m_lock)
                {
                    // filter by profile.
                    IList<BaseAddress> baseAddresses = FilterByProfile(profileUris, BaseAddresses);

                    // get the descriptions.
                    Uri parsedEndpointUrl = Utils.ParseUri(endpointUrl);

                    if (parsedEndpointUrl != null)
                    {
                        baseAddresses = FilterByEndpointUrl(parsedEndpointUrl, baseAddresses);
                    }

                    // check if nothing to do.
                    if (baseAddresses.Count != 0)
                    {
                        // localize the application name if requested.
                        LocalizedText applicationName = this.ServerDescription.ApplicationName;

                        if (localeIds != null && localeIds.Count > 0)
                        {
                            applicationName = m_serverInternal.ResourceManager.Translate(localeIds, applicationName);
                        }

                        // translate the application description.
                        ApplicationDescription application = TranslateApplicationDescription(
                            parsedEndpointUrl,
                            base.ServerDescription,
                            baseAddresses,
                            applicationName);

                        // translate the endpoint descriptions.
                        endpoints = TranslateEndpointDescriptions(
                            parsedEndpointUrl,
                            baseAddresses,
                            this.Endpoints,
                            application);
                    }
                }
                return CreateResponse(requestHeader, StatusCodes.Good);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("获取终结点列表GetEndpoints()触发异常:" + ex.Message);
                Console.ResetColor();
            }
            return CreateResponse(requestHeader, StatusCodes.BadUnexpectedError);
        }

        /// <summary>
        /// 验证请求是否有效
        /// </summary>
        /// <param name="requestHeader"></param>
        protected override void ValidateRequest(RequestHeader requestHeader)
        {
            // check for server error.
            ServiceResult error = ServerError;

            if (ServiceResult.IsBad(error))
            {
                throw new ServiceResultException(error);
            }

            // check server state.
            ServerInternalData serverInternal = m_serverInternal;

            if (serverInternal == null || !serverInternal.IsRunning)
            {
                throw new ServiceResultException(StatusCodes.BadServerHalted);
            }

            if (requestHeader == null)
            {
                throw new ServiceResultException(StatusCodes.BadRequestHeaderInvalid);
            }
        }

        /// <summary>
        /// 发现网络中的服务端(主要为其他PC端获取本机的服务列表使用)
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="startingRecordId"></param>
        /// <param name="maxRecordsToReturn"></param>
        /// <param name="serverCapabilityFilter"></param>
        /// <param name="lastCounterResetTime"></param>
        /// <param name="servers"></param>
        /// <returns></returns>
        public override ResponseHeader FindServersOnNetwork(
            RequestHeader requestHeader,
            uint startingRecordId,
            uint maxRecordsToReturn,
            StringCollection serverCapabilityFilter,
            out DateTime lastCounterResetTime,
            out ServerOnNetworkCollection servers)
        {
            lastCounterResetTime = DateTime.MinValue;
            servers = null;

            ValidateRequest(requestHeader);

            try
            {
                lock (_serverTable)
                {
                    uint rId = 0;
                    foreach (var item in _serverTable)
                    {
                        rId++;
                        servers.Add(new ServerOnNetwork()
                        {
                            DiscoveryUrl = item.DiscoveryUrls.FirstOrDefault(),
                            RecordId = rId,
                            ServerName = item.ServerNames.FirstOrDefault()?.Text,
                            ServerCapabilities = { "Realtime Data", "Events & Alarms" },
                        });
                    }
                }

                lastCounterResetTime = DateTime.Now;
                return CreateResponse(requestHeader, StatusCodes.Good);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("查找网络中的服务FindServersOnNetwork()时触发异常:" + ex.Message);
                Console.ResetColor();
            }
            return CreateResponse(requestHeader, StatusCodes.BadUnexpectedError);
        }

        /// <summary>
        /// 注册服务(每个在本机启动的服务端都会主动调用此方法进行注册)
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="server"></param>
        /// <returns></returns>
        public virtual ResponseHeader RegisterServer(
            RequestHeader requestHeader,
            RegisteredServer server)
        {
            ValidateRequest(requestHeader);

            // Insert implementation.
            try
            {
                Console.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ":服务注册:" + server.DiscoveryUrls.FirstOrDefault());
                RegisteredServerTable model = _serverTable.Where(d => d.ServerUri == server.ServerUri).FirstOrDefault();
                if (model != null)
                {
                    model.LastRegistered = DateTime.Now;
                }
                else
                {
                    model = new RegisteredServerTable()
                    {
                        DiscoveryUrls = server.DiscoveryUrls,
                        GatewayServerUri = server.GatewayServerUri,
                        IsOnline = server.IsOnline,
                        LastRegistered = DateTime.Now,
                        ProductUri = server.ProductUri,
                        SemaphoreFilePath = server.SemaphoreFilePath,
                        ServerNames = server.ServerNames,
                        ServerType = server.ServerType,
                        ServerUri = server.ServerUri
                    };
                    _serverTable.Add(model);
                }
                return CreateResponse(requestHeader, StatusCodes.Good);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("客户端调用RegisterServer()注册服务时触发异常:" + ex.Message);
                Console.ResetColor();
            }
            return CreateResponse(requestHeader, StatusCodes.BadUnexpectedError);
        }

        /// <summary>
        /// 注册服务(每个在本机启动的服务端都会主动调用此方法进行注册)
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="server"></param>
        /// <param name="discoveryConfiguration"></param>
        /// <param name="configurationResults"></param>
        /// <param name="diagnosticInfos"></param>
        /// <returns></returns>
        public virtual ResponseHeader RegisterServer2(
            RequestHeader requestHeader,
            RegisteredServer server,
            ExtensionObjectCollection discoveryConfiguration,
            out StatusCodeCollection configurationResults,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            configurationResults = null;
            diagnosticInfos = null;

            ValidateRequest(requestHeader);

            // Insert implementation.
            try
            {
                Console.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ":服务注册:" + server.DiscoveryUrls.FirstOrDefault());
                RegisteredServerTable model = _serverTable.Where(d => d.ServerUri == server.ServerUri).FirstOrDefault();
                if (model != null)
                {
                    model.LastRegistered = DateTime.Now;
                }
                else
                {
                    model = new RegisteredServerTable()
                    {
                        DiscoveryUrls = server.DiscoveryUrls,
                        GatewayServerUri = server.GatewayServerUri,
                        IsOnline = server.IsOnline,
                        LastRegistered = DateTime.Now,
                        ProductUri = server.ProductUri,
                        SemaphoreFilePath = server.SemaphoreFilePath,
                        ServerNames = server.ServerNames,
                        ServerType = server.ServerType,
                        ServerUri = server.ServerUri
                    };
                    _serverTable.Add(model);
                }
                configurationResults = new StatusCodeCollection() { StatusCodes.Good };
                return CreateResponse(requestHeader, StatusCodes.Good);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("客户端调用RegisterServer2()注册服务时触发异常:" + ex.Message);
                Console.ResetColor();
            }
            return CreateResponse(requestHeader, StatusCodes.BadUnexpectedError);
        }

        /// <summary>
        /// 创建服务器使用的聚合管理器
        /// </summary>
        /// <param name="server"></param>
        /// <param name="configuration"></param>
        /// <returns></returns>
        protected override AggregateManager CreateAggregateManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            AggregateManager manager = new AggregateManager(server);

            manager.RegisterFactory(ObjectIds.AggregateFunction_Interpolative, BrowseNames.AggregateFunction_Interpolative, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_Average, BrowseNames.AggregateFunction_Average, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_TimeAverage, BrowseNames.AggregateFunction_TimeAverage, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_TimeAverage2, BrowseNames.AggregateFunction_TimeAverage2, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_Total, BrowseNames.AggregateFunction_Total, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_Total2, BrowseNames.AggregateFunction_Total2, Aggregators.CreateStandardCalculator);

            manager.RegisterFactory(ObjectIds.AggregateFunction_Minimum, BrowseNames.AggregateFunction_Minimum, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_Maximum, BrowseNames.AggregateFunction_Maximum, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_MinimumActualTime, BrowseNames.AggregateFunction_MinimumActualTime, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_MaximumActualTime, BrowseNames.AggregateFunction_MaximumActualTime, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_Range, BrowseNames.AggregateFunction_Range, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_Minimum2, BrowseNames.AggregateFunction_Minimum2, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_Maximum2, BrowseNames.AggregateFunction_Maximum2, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_MinimumActualTime2, BrowseNames.AggregateFunction_MinimumActualTime2, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_MaximumActualTime2, BrowseNames.AggregateFunction_MaximumActualTime2, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_Range2, BrowseNames.AggregateFunction_Range2, Aggregators.CreateStandardCalculator);

            manager.RegisterFactory(ObjectIds.AggregateFunction_Count, BrowseNames.AggregateFunction_Count, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_AnnotationCount, BrowseNames.AggregateFunction_AnnotationCount, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_DurationInStateZero, BrowseNames.AggregateFunction_DurationInStateZero, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_DurationInStateNonZero, BrowseNames.AggregateFunction_DurationInStateNonZero, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_NumberOfTransitions, BrowseNames.AggregateFunction_NumberOfTransitions, Aggregators.CreateStandardCalculator);

            manager.RegisterFactory(ObjectIds.AggregateFunction_Start, BrowseNames.AggregateFunction_Start, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_End, BrowseNames.AggregateFunction_End, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_Delta, BrowseNames.AggregateFunction_Delta, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_StartBound, BrowseNames.AggregateFunction_StartBound, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_EndBound, BrowseNames.AggregateFunction_EndBound, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_DeltaBounds, BrowseNames.AggregateFunction_DeltaBounds, Aggregators.CreateStandardCalculator);

            manager.RegisterFactory(ObjectIds.AggregateFunction_DurationGood, BrowseNames.AggregateFunction_DurationGood, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_DurationBad, BrowseNames.AggregateFunction_DurationBad, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_PercentGood, BrowseNames.AggregateFunction_PercentGood, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_PercentBad, BrowseNames.AggregateFunction_PercentBad, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_WorstQuality, BrowseNames.AggregateFunction_WorstQuality, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_WorstQuality2, BrowseNames.AggregateFunction_WorstQuality2, Aggregators.CreateStandardCalculator);

            manager.RegisterFactory(ObjectIds.AggregateFunction_StandardDeviationPopulation, BrowseNames.AggregateFunction_StandardDeviationPopulation, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_VariancePopulation, BrowseNames.AggregateFunction_VariancePopulation, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_StandardDeviationSample, BrowseNames.AggregateFunction_StandardDeviationSample, Aggregators.CreateStandardCalculator);
            manager.RegisterFactory(ObjectIds.AggregateFunction_VarianceSample, BrowseNames.AggregateFunction_VarianceSample, Aggregators.CreateStandardCalculator);

            return manager;
        }

        /// <summary>
        /// 为服务器创建资源管理器
        /// </summary>
        /// <param name="server"></param>
        /// <param name="configuration"></param>
        /// <returns></returns>
        protected override ResourceManager CreateResourceManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            ResourceManager resourceManager = new ResourceManager(server, configuration);

            System.Reflection.FieldInfo[] fields = typeof(StatusCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            foreach (System.Reflection.FieldInfo field in fields)
            {
                uint? id = field.GetValue(typeof(StatusCodes)) as uint?;

                if (id != null)
                {
                    resourceManager.Add(id.Value, "en-US", field.Name);
                }
            }

            return resourceManager;
        }

        /// <summary>
        /// 设置服务状态
        /// </summary>
        /// <param name="state"></param>
        protected override void SetServerState(ServerState state)
        {
            lock (m_lock)
            {
                if (ServiceResult.IsBad(ServerError))
                {
                    throw new ServiceResultException(ServerError);
                }

                if (m_serverInternal == null)
                {
                    throw new ServiceResultException(StatusCodes.BadServerHalted);
                }

                m_serverInternal.CurrentState = state;
            }
        }

        /// <summary>
        /// 配置变更事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        protected override async void OnConfigurationChanged(object sender, ConfigurationWatcherEventArgs args)
        {
            try
            {
                ApplicationConfiguration configuration = await ApplicationConfiguration.Load(
                    new FileInfo(args.FilePath),
                    Configuration.ApplicationType,
                    Configuration.GetType());

                OnUpdateConfiguration(configuration);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Could not load updated configuration file from: {0}", args);
            }
        }

        /// <summary>
        /// 清理掉线服务
        /// </summary>
        /// <param name="obj"></param>
        private void ClearNoliveServer(object obj)
        {
            try
            {
                var tmpList = _serverTable.Where(d => d.LastRegistered < DateTime.Now.AddMinutes(-1) || !d.IsOnline).ToList();
                if (tmpList.Count > 0)
                {
                    lock (_serverTable)
                    {
                        foreach (var item in tmpList)
                        {
                            Console.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ":清理服务:" + item.DiscoveryUrls.FirstOrDefault());
                            _serverTable.Remove(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("清理掉线服务ClearNoliveServer()时触发异常:" + ex.Message);
                Console.ResetColor();
            }
        }
    }
}
