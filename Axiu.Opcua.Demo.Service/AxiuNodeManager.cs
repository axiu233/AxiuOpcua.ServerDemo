using Axiu.Opcua.Demo.Common;
using Axiu.Opcua.Demo.Model;
using Opc.Ua;
using Opc.Ua.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Axiu.Opcua.Demo.Service
{
    /// <summary>
    /// 以下备注中  测点即代表最叶子级节点
    /// 目前设计是 只有测点有数据  其余节点都是目录
    /// </summary>
    public class AxiuNodeManager : CustomNodeManager2
    {
        /// <summary>
        /// 配置修改次数  主要用来识别菜单树是否有变动  如果发生变动则修改菜单树对应节点  测点的实时数据变化不算在内
        /// </summary>
        private int cfgCount = -1;
        private IList<IReference> _references;
        /// <summary>
        /// 测点集合,实时数据刷新时,直接从字典中取出对应的测点,修改值即可
        /// </summary>
        private Dictionary<string, BaseDataVariableState> _nodeDic = new Dictionary<string, BaseDataVariableState>();
        /// <summary>
        /// 目录集合,修改菜单树时需要(我们需要知道哪些菜单需要修改,哪些需要新增,哪些需要删除)
        /// </summary>
        private Dictionary<string, FolderState> _folderDic = new Dictionary<string, FolderState>();

        public AxiuNodeManager(IServerInternal server, ApplicationConfiguration configuration) : base(server, configuration, "http://opcfoundation.org/Quickstarts/ReferenceApplications")
        {
        }

        /// <summary>
        /// 重写NodeId生成方式(目前采用'_'分隔,如需更改,请修改此方法)
        /// </summary>
        /// <param name="context"></param>
        /// <param name="node"></param>
        /// <returns></returns>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            BaseInstanceState instance = node as BaseInstanceState;

            if (instance != null && instance.Parent != null)
            {
                string id = instance.Parent.NodeId.Identifier as string;

                if (id != null)
                {
                    return new NodeId(id + "_" + instance.SymbolicName, instance.Parent.NodeId.NamespaceIndex);
                }
            }

            return node.NodeId;
        }

        /// <summary>
        /// 重写获取节点句柄的方法
        /// </summary>
        /// <param name="context"></param>
        /// <param name="nodeId"></param>
        /// <param name="cache"></param>
        /// <returns></returns>
        protected override NodeHandle GetManagerHandle(ServerSystemContext context, NodeId nodeId, IDictionary<NodeId, NodeState> cache)
        {
            lock (Lock)
            {
                // quickly exclude nodes that are not in the namespace. 
                if (!IsNodeIdInNamespace(nodeId))
                {
                    return null;
                }

                NodeState node = null;

                if (!PredefinedNodes.TryGetValue(nodeId, out node))
                {
                    return null;
                }

                NodeHandle handle = new NodeHandle();

                handle.NodeId = nodeId;
                handle.Node = node;
                handle.Validated = true;

                return handle;
            }
        }

        /// <summary>
        /// 重写节点的验证方式
        /// </summary>
        /// <param name="context"></param>
        /// <param name="handle"></param>
        /// <param name="cache"></param>
        /// <returns></returns>
        protected override NodeState ValidateNode(ServerSystemContext context, NodeHandle handle, IDictionary<NodeId, NodeState> cache)
        {
            // not valid if no root.
            if (handle == null)
            {
                return null;
            }

            // check if previously validated.
            if (handle.Validated)
            {
                return handle.Node;
            }
            // TBD
            return null;
        }

        /// <summary>
        /// 重写创建基础目录
        /// </summary>
        /// <param name="externalReferences"></param>
        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out _references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = _references = new List<IReference>();
                }

                try
                {
                    //TODO: 获取节点树
                    List<OpcuaNode> nodes = new List<OpcuaNode>()
                    {
                        /*
                         * OpcuaNode类由于个人业务相关  定义成如下格式, 
                         * 可根据自身数据便利 修改相应的数据结构
                         * 只需保证能明确知道各个节点的从属关系即可
                         */
                        new OpcuaNode(){NodeId=1,NodeName="模拟根节点",NodePath="1",NodeType=NodeType.Scada,ParentPath="",IsTerminal=false },
                        new OpcuaNode(){NodeId=11,NodeName="子目录1",NodePath="11",NodeType=NodeType.Channel,ParentPath="1",IsTerminal=false },
                        new OpcuaNode(){NodeId=12,NodeName="子目录2",NodePath="12",NodeType=NodeType.Device,ParentPath="1",IsTerminal=false },
                        new OpcuaNode(){NodeId=111,NodeName="叶子节点1",NodePath="111",NodeType=NodeType.Measure,ParentPath="11", IsTerminal=true },
                        new OpcuaNode(){NodeId=112,NodeName="叶子节点2",NodePath="112",NodeType=NodeType.Measure,ParentPath="11",IsTerminal=true },
                        new OpcuaNode(){NodeId=113,NodeName="叶子节点3",NodePath="113",NodeType=NodeType.Measure,ParentPath="11",IsTerminal=true },
                        new OpcuaNode(){NodeId=114,NodeName="叶子节点4",NodePath="114",NodeType=NodeType.Measure,ParentPath="11",IsTerminal=true },
                        new OpcuaNode(){NodeId=121,NodeName="叶子节点1",NodePath="121",NodeType=NodeType.Measure,ParentPath="12",IsTerminal=true },
                        new OpcuaNode(){NodeId=122,NodeName="叶子节点2",NodePath="122",NodeType=NodeType.Measure,ParentPath="12",IsTerminal=true }
                    };
                    //开始创建节点的菜单树
                    GeneraterNodes(nodes, _references);
                    //实时更新测点的数据
                    UpdateVariableValue();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("调用接口初始化触发异常:" + ex.Message);
                    Console.ResetColor();
                }
            }
        }

        /// <summary>
        /// 生成根节点(由于根节点需要特殊处理,此处单独出来一个方法)
        /// </summary>
        /// <param name="nodes"></param>
        /// <param name="references"></param>
        private void GeneraterNodes(List<OpcuaNode> nodes, IList<IReference> references)
        {
            var list = nodes.Where(d => d.NodeType == NodeType.Scada);
            foreach (var item in list)
            {
                try
                {
                    FolderState root = CreateFolder(null, item.NodePath, item.NodeName);
                    root.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
                    references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, root.NodeId));
                    root.EventNotifier = EventNotifiers.SubscribeToEvents;
                    AddRootNotifier(root);
                    CreateNodes(nodes, root, item.NodePath);
                    _folderDic.Add(item.NodePath, root);
                    //添加引用关系
                    AddPredefinedNode(SystemContext, root);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("创建OPC-UA根节点触发异常:" + ex.Message);
                    Console.ResetColor();
                }
            }
        }

        /// <summary>
        /// 递归创建子节点(包括创建目录和测点)
        /// </summary>
        /// <param name="nodes"></param>
        /// <param name="parent"></param>
        private void CreateNodes(List<OpcuaNode> nodes, FolderState parent, string parentPath)
        {
            var list = nodes.Where(d => d.ParentPath == parentPath);
            foreach (var node in list)
            {
                try
                {
                    if (!node.IsTerminal)
                    {
                        FolderState folder = CreateFolder(parent, node.NodePath, node.NodeName);
                        _folderDic.Add(node.NodePath, folder);
                        CreateNodes(nodes, folder, node.NodePath);
                    }
                    else
                    {
                        BaseDataVariableState variable = CreateVariable(parent, node.NodePath, node.NodeName, DataTypeIds.Double, ValueRanks.Scalar);
                        //此处需要注意  目录字典是以目录路径作为KEY 而 测点字典是以测点ID作为KEY  为了方便更新实时数据
                        _nodeDic.Add(node.NodeId.ToString(), variable);
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("创建OPC-UA子节点触发异常:" + ex.Message);
                    Console.ResetColor();
                }
            }
        }

        /// <summary>
        /// 创建目录
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="path"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        private FolderState CreateFolder(NodeState parent, string path, string name)
        {
            FolderState folder = new FolderState(parent);

            folder.SymbolicName = name;
            folder.ReferenceTypeId = ReferenceTypes.Organizes;
            folder.TypeDefinitionId = ObjectTypeIds.FolderType;
            folder.NodeId = new NodeId(path, NamespaceIndex);
            folder.BrowseName = new QualifiedName(path, NamespaceIndex);
            folder.DisplayName = new LocalizedText("en", name);
            folder.WriteMask = AttributeWriteMask.None;
            folder.UserWriteMask = AttributeWriteMask.None;
            folder.EventNotifier = EventNotifiers.None;

            if (parent != null)
            {
                parent.AddChild(folder);
            }

            return folder;
        }

        /// <summary>
        /// 创建节点
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="path"></param>
        /// <param name="name"></param>
        /// <param name="dataType"></param>
        /// <param name="valueRank"></param>
        /// <returns></returns>
        private BaseDataVariableState CreateVariable(NodeState parent, string path, string name, NodeId dataType, int valueRank)
        {
            BaseDataVariableState variable = new BaseDataVariableState(parent);

            variable.SymbolicName = name;
            variable.ReferenceTypeId = ReferenceTypes.Organizes;
            variable.TypeDefinitionId = VariableTypeIds.BaseDataVariableType;
            variable.NodeId = new NodeId(path, NamespaceIndex);
            variable.BrowseName = new QualifiedName(path, NamespaceIndex);
            variable.DisplayName = new LocalizedText("en", name);
            variable.WriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description;
            variable.UserWriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description;
            variable.DataType = dataType;
            variable.ValueRank = valueRank;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            //variable.Value = GetNewValue(variable);
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.Now;
            variable.OnWriteValue = OnWriteDataValue;

            if (valueRank == ValueRanks.OneDimension)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0 });
            }
            else if (valueRank == ValueRanks.TwoDimensions)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0, 0 });
            }

            if (parent != null)
            {
                parent.AddChild(variable);
            }

            return variable;
        }

        /// <summary>
        /// 实时更新节点数据
        /// </summary>
        public void UpdateVariableValue()
        {
            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        /*
                         * 此处仅作示例代码  所以不修改节点树 故将UpdateNodesAttribute()方法跳过
                         * 在实际业务中  请根据自身的业务需求决定何时修改节点菜单树
                         */
                        int count = 0;
                        //配置发生更改时,重新生成节点树
                        if (count > 0 && count != cfgCount)
                        {
                            cfgCount = count;
                            List<OpcuaNode> nodes = new List<OpcuaNode>();
                            /*
                             * 此处有想过删除整个菜单树,然后重建 保证各个NodeId仍与原来的一直
                             * 但是 后来发现这样会导致原来的客户端订阅信息丢失  无法获取订阅数据
                             * 所以  只能一级级的检查节点  然后修改属性
                             */
                            UpdateNodesAttribute(nodes);
                        }
                        //模拟获取实时数据
                        BaseDataVariableState node = null;
                        /*
                         * 在实际业务中应该是根据对应的标识来更新固定节点的数据
                         * 这里  我偷个懒  全部测点都更新为一个新的随机数
                         */
                        foreach (var item in _nodeDic)
                        {
                            node = item.Value;
                            node.Value = RandomLibrary.GetRandomInt(0, 99);
                            node.Timestamp = DateTime.Now;
                            //变更标识  只有执行了这一步,订阅的客户端才会收到新的数据
                            node.ClearChangeMasks(SystemContext, false);
                        }
                        //1秒更新一次
                        Thread.Sleep(1000 * 1);
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("更新OPC-UA节点数据触发异常:" + ex.Message);
                        Console.ResetColor();
                    }
                }
            });
        }

        /// <summary>
        /// 修改节点树(添加节点,删除节点,修改节点名称)
        /// </summary>
        /// <param name="nodes"></param>
        public void UpdateNodesAttribute(List<OpcuaNode> nodes)
        {
            //修改或创建根节点
            var scadas = nodes.Where(d => d.NodeType == NodeType.Scada);
            foreach (var item in scadas)
            {
                FolderState scadaNode = null;
                if (!_folderDic.TryGetValue(item.NodePath, out scadaNode))
                {
                    //如果根节点都不存在  那么整个树都需要创建
                    FolderState root = CreateFolder(null, item.NodePath, item.NodeName);
                    root.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
                    _references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, root.NodeId));
                    root.EventNotifier = EventNotifiers.SubscribeToEvents;
                    AddRootNotifier(root);
                    CreateNodes(nodes, root, item.NodePath);
                    _folderDic.Add(item.NodePath, root);
                    AddPredefinedNode(SystemContext, root);
                    continue;
                }
                else
                {
                    scadaNode.DisplayName = item.NodeName;
                    scadaNode.ClearChangeMasks(SystemContext, false);
                }
            }
            //修改或创建目录(此处设计为可以有多级目录,上面是演示数据,所以我只写了三级,事实上更多级也是可以的)
            var folders = nodes.Where(d => d.NodeType != NodeType.Scada && !d.IsTerminal);
            foreach (var item in folders)
            {
                FolderState folder = null;
                if (!_folderDic.TryGetValue(item.NodePath, out folder))
                {
                    var par = GetParentFolderState(nodes, item);
                    folder = CreateFolder(par, item.NodePath, item.NodeName);
                    AddPredefinedNode(SystemContext, folder);
                    par.ClearChangeMasks(SystemContext, false);
                    _folderDic.Add(item.NodePath, folder);
                }
                else
                {
                    folder.DisplayName = item.NodeName;
                    folder.ClearChangeMasks(SystemContext, false);
                }
            }
            //修改或创建测点
            //这里我的数据结构采用IsTerminal来代表是否是测点  实际业务中可能需要根据自身需要调整
            var paras = nodes.Where(d => d.IsTerminal);
            foreach (var item in paras)
            {
                BaseDataVariableState node = null;
                if (_nodeDic.TryGetValue(item.NodeId.ToString(), out node))
                {
                    node.DisplayName = item.NodeName;
                    node.Timestamp = DateTime.Now;
                    node.ClearChangeMasks(SystemContext, false);
                }
                else
                {
                    FolderState folder = null;
                    if (_folderDic.TryGetValue(item.ParentPath, out folder))
                    {
                        node = CreateVariable(folder, item.NodePath, item.NodeName, DataTypeIds.Double, ValueRanks.Scalar);
                        AddPredefinedNode(SystemContext, node);
                        folder.ClearChangeMasks(SystemContext, false);
                        _nodeDic.Add(item.NodeId.ToString(), node);
                    }
                }
            }

            /*
             * 将新获取到的菜单列表与原列表对比
             * 如果新菜单列表中不包含原有的菜单  
             * 则说明这个菜单被删除了  这里也需要删除
             */
            List<string> folderPath = _folderDic.Keys.ToList();
            List<string> nodePath = _nodeDic.Keys.ToList();
            var remNode = nodePath.Except(nodes.Where(d => d.IsTerminal).Select(d => d.NodeId.ToString()));
            foreach (var str in remNode)
            {
                BaseDataVariableState node = null;
                if (_nodeDic.TryGetValue(str, out node))
                {
                    var parent = node.Parent;
                    parent.RemoveChild(node);
                    _nodeDic.Remove(str);
                }
            }
            var remFolder = folderPath.Except(nodes.Where(d => !d.IsTerminal).Select(d => d.NodePath));
            foreach (string str in remFolder)
            {
                FolderState folder = null;
                if (_folderDic.TryGetValue(str, out folder))
                {
                    var parent = folder.Parent;
                    if (parent != null)
                    {
                        parent.RemoveChild(folder);
                        _folderDic.Remove(str);
                    }
                    else
                    {
                        RemoveRootNotifier(folder);
                        RemovePredefinedNode(SystemContext, folder, new List<LocalReference>());
                    }
                }
            }
        }

        /// <summary>
        /// 创建父级目录(请确保对应的根目录已创建)
        /// </summary>
        /// <param name="nodes"></param>
        /// <param name="currentNode"></param>
        /// <returns></returns>
        public FolderState GetParentFolderState(IEnumerable<OpcuaNode> nodes, OpcuaNode currentNode)
        {
            FolderState folder = null;
            if (!_folderDic.TryGetValue(currentNode.ParentPath, out folder))
            {
                var parent = nodes.Where(d => d.NodePath == currentNode.ParentPath).FirstOrDefault();
                if (!string.IsNullOrEmpty(parent.ParentPath))
                {
                    var pFol = GetParentFolderState(nodes, parent);
                    folder = CreateFolder(pFol, parent.NodePath, parent.NodeName);
                    pFol.ClearChangeMasks(SystemContext, false);
                    AddPredefinedNode(SystemContext, folder);
                    _folderDic.Add(currentNode.ParentPath, folder);
                }
            }
            return folder;
        }

        /// <summary>
        /// 客户端写入值时触发(绑定到节点的写入事件上)
        /// </summary>
        /// <param name="context"></param>
        /// <param name="node"></param>
        /// <param name="indexRange"></param>
        /// <param name="dataEncoding"></param>
        /// <param name="value"></param>
        /// <param name="statusCode"></param>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        private ServiceResult OnWriteDataValue(ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding,
            ref object value, ref StatusCode statusCode, ref DateTime timestamp)
        {
            BaseDataVariableState variable = node as BaseDataVariableState;
            try
            {
                //验证数据类型
                TypeInfo typeInfo = TypeInfo.IsInstanceOfDataType(
                    value,
                    variable.DataType,
                    variable.ValueRank,
                    context.NamespaceUris,
                    context.TypeTable);

                if (typeInfo == null || typeInfo == TypeInfo.Unknown)
                {
                    return StatusCodes.BadTypeMismatch;
                }
                if (typeInfo.BuiltInType == BuiltInType.Double)
                {
                    double number = Convert.ToDouble(value);
                    value = TypeInfo.Cast(number, typeInfo.BuiltInType);
                }
                return ServiceResult.Good;
            }
            catch (Exception)
            {
                return StatusCodes.BadTypeMismatch;
            }
        }

        /// <summary>
        /// 读取历史数据
        /// </summary>
        /// <param name="context"></param>
        /// <param name="details"></param>
        /// <param name="timestampsToReturn"></param>
        /// <param name="releaseContinuationPoints"></param>
        /// <param name="nodesToRead"></param>
        /// <param name="results"></param>
        /// <param name="errors"></param>
        public override void HistoryRead(OperationContext context, HistoryReadDetails details, TimestampsToReturn timestampsToReturn, bool releaseContinuationPoints,
            IList<HistoryReadValueId> nodesToRead, IList<HistoryReadResult> results, IList<ServiceResult> errors)
        {
            ReadProcessedDetails readDetail = details as ReadProcessedDetails;
            //假设查询历史数据  都是带上时间范围的
            if (readDetail == null || readDetail.StartTime == DateTime.MinValue || readDetail.EndTime == DateTime.MinValue)
            {
                errors[0] = StatusCodes.BadHistoryOperationUnsupported;
                return;
            }
            for (int ii = 0; ii < nodesToRead.Count; ii++)
            {
                int sss = readDetail.StartTime.Millisecond;
                double res = sss + DateTime.Now.Millisecond;
                //这里  返回的历史数据可以是多种数据类型  请根据实际的业务来选择
                Opc.Ua.KeyValuePair keyValue = new Opc.Ua.KeyValuePair()
                {
                    Key = new QualifiedName(nodesToRead[ii].NodeId.Identifier.ToString()),
                    Value = res
                };
                results[ii] = new HistoryReadResult()
                {
                    StatusCode = StatusCodes.Good,
                    HistoryData = new ExtensionObject(keyValue)
                };
                errors[ii] = StatusCodes.Good;
                //切记,如果你已处理完了读取历史数据的操作,请将Processed设为true,这样OPC-UA类库就知道你已经处理过了 不需要再进行检查了
                nodesToRead[ii].Processed = true;
            }
        }
    }
}
