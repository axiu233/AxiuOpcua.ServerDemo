using System;

namespace Axiu.Opcua.Demo.Model
{
    public class OpcuaNode
    {
        /// <summary>
        /// 节点路径(逐级拼接)
        /// </summary>
        public string NodePath { get; set; }
        /// <summary>
        /// 父节点路径(逐级拼接)
        /// </summary>
        public string ParentPath { get; set; }
        /// <summary>
        /// 节点编号 (在我的业务系统中的节点编号并不完全唯一,但是所有测点Id都是不同的)
        /// </summary>
        public int NodeId { get; set; }
        /// <summary>
        /// 节点名称(展示名称)
        /// </summary>
        public string NodeName { get; set; }
        /// <summary>
        /// 是否端点(最底端子节点)
        /// </summary>
        public bool IsTerminal { get; set; }
        /// <summary>
        /// 节点类型
        /// </summary>
        public NodeType NodeType { get; set; }
    }
    public enum NodeType
    {
        /// <summary>
        /// 根节点
        /// </summary>
        Scada = 1,
        /// <summary>
        /// 目录
        /// </summary>
        Channel = 2,
        /// <summary>
        /// 目录
        /// </summary>
        Device = 3,
        /// <summary>
        /// 测点
        /// </summary>
        Measure = 4
    }
}
