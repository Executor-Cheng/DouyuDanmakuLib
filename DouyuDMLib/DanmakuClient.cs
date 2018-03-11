//Written by Executor丶 at 2018-03-11 16:22:10 Used 6 Hours. BiliLive 2721650
using System;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DouyuDMLib
{
    /// <summary>
    /// 消息类型
    /// <para><see cref="CLIENT_TO_SERVER"/> : 指示是客户端发送到服务器的消息</para>
    /// <para><see cref="SERVER_TO_CLIENT"/> : 指示是服务器发送给客户端的消息</para>
    /// </summary>
    public enum MessageType : short { CLIENT_TO_SERVER = 689, SERVER_TO_CLIENT = 690 }

    //测试客户端
    /*
    public class TestClass
    {
        public static void Main(string[] args)
        {
            DouyuClient dc = new DouyuClient();
            dc.ReceivedMessageEvt += Dc_ReceivedMessage;
            Console.WriteLine(dc.Connect(61372));
            while (true) Console.ReadLine();
        }

        private static void Dc_ReceivedMessage(object sender, Danmaku e)
        {
            if (e.MsgType == MsgTypeEnum.ChatMsg)
            {
                Console.WriteLine($"{e.TimeStamp.ToString("yyyy-MM-dd HH:mm:ss")} @{e.UserName} Says: {e.CommentText}");
            }
            else if (e.MsgType == MsgTypeEnum.GiftSend)
            {
                Console.WriteLine($"{e.TimeStamp.ToString("yyyy-MM-dd HH:mm:ss")} @{e.UserName} Sended {e.GiftId} x {e.GiftCount} Hits {e.GiftHits}");
            }
        }
    }
    */

    /// <summary>
    /// 客户端类
    /// </summary>
    public class DouyuClient
    {
        /// <summary>
        /// 创建一个客户端的实例
        /// </summary>
        public DouyuClient() { }

        /// <summary>
        /// 支持通信的Socket
        /// </summary>
        private Socket Client { get; set; }
        /// <summary>
        /// 心跳定时发生器
        /// </summary>
        private Timer Ticker { get; set; }
        /// <summary>
        /// 要连接的域名/IP
        /// </summary>
        public string Host { get; set; } = "danmu.douyutv.com"; //"openbarrage.douyutv.com";
        /// <summary>
        /// 要连接的端口号
        /// </summary>
        public Int32 Port { get; set; } = 12604; //8601;
        /// <summary>
        /// 监听消息的房间号
        /// </summary>
        public int RoomId { get; private set; }
        /// <summary>
        /// 上一个异常
        /// </summary>
        public Exception LastException { get; set; }
        /// <summary>
        /// 连接状态
        /// </summary>
        public bool Connected { get; private set; }
        /// <summary>
        /// 收到消息的委托
        /// </summary>
        /// <param name="sender">引发事件的客户端实例</param>
        /// <param name="e">弹幕模型</param>
        public delegate void ReceivedMessageHandler(object sender, Danmaku e);
        /// <summary>
        /// 收到消息时引发的事件
        /// </summary>
        public event ReceivedMessageHandler ReceivedMessageEvt;
        /// <summary>
        /// 意外断开连接的委托
        /// </summary>
        /// <param name="sender">引发事件的客户端实例</param>
        /// <param name="e">导致断开连接的异常实例</param>
        public delegate void DisconnectedHandler(object sender, Exception e);
        /// <summary>
        /// 意外断开连接时引发的事件
        /// </summary>
        public event DisconnectedHandler DisconnectedEvt;
        /// <summary>
        /// 连接房间成功的委托
        /// </summary>
        /// <param name="sender">引发事件的客户端实例</param>
        /// <param name="roomId">已连接到的房间号</param>
        public delegate void ConnectedHandler(object sender, int roomId);
        /// <summary>
        /// 连接成功时引发的事件
        /// </summary>
        public event ConnectedHandler ConnectedEvt;

        /// <summary>
        /// 连接到直播间
        /// </summary>
        /// <param name="roomId">要连接的房间号</param>
        /// <returns>连接成功与否</returns>
        public bool Connect(int roomId)
        {
            if (this.Client?.Connected == true)
            {
                throw new InvalidOperationException($"当前已经连接{RoomId}房间");
            }
            this.RoomId = roomId;
            try
            {
                Client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                Client.Connect(Host, Port);
                Login();
                JoinGroup();
                Ticker = new Timer(p => { Tick(); }, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
                new Thread(ReceiveMessageLoop) { IsBackground = true }.Start();
                Connected = true;
                ConnectedEvt?.Invoke(this, RoomId);
                return true;
            }
            catch (Exception Ex)
            {
                this.LastException = Ex;
                _disconnect();
                return false;
            }
        }

        public bool Connect(string roomName)
        {
            return Connect(Utility.GetRoomId(roomName));
        }

        /// <summary>
        /// 手动关闭连接
        /// </summary>
        public void Disconnect()
        {
            Connected = false;
            Ticker?.Dispose();
            if (Client != null && Client.Connected)
            {
                Logout();
                Client.Close();
            }
        }

        /// <summary>
        /// 异常掉线调用的方法
        /// </summary>
        private void _disconnect()
        {
            Connected = false;
            Ticker?.Dispose();
            Client.Close();
            DisconnectedEvt?.Invoke(this, LastException);
        }

        /// <summary>
        /// 接收消息
        /// </summary>
        public void ReceiveMessageLoop()
        {
            try
            {
                byte[] stableBuffer = new byte[12];
                while (Client.Connected)
                {
                    Client.ReadBytes(stableBuffer, 0, 12);
                    int pktSize = BitConverter.ToInt32(stableBuffer, 0) - 8;
                    byte[] buffer = new byte[pktSize];
                    Client.ReadBytes(buffer, 0, pktSize);
                    string rawData = Encoding.UTF8.GetString(buffer);
                    ReceivedMessageEvt?.Invoke(this, Danmaku.Parse(rawData));
                }
            }
            catch (Exception Ex)
            {
                LastException = Ex;
                _disconnect();
            }
        }

        #region Login/out&KeepAlive Methods
        /// <summary>
        /// 发送登录请求
        /// </summary>
        private void Login()
        {
            Send(MessageType.CLIENT_TO_SERVER, 0, 0, Utility.Serialize(new Dictionary<string, object> { { "type", "loginreq" }, { "roomid", RoomId } }));
        }

        /// <summary>
        /// 发送入组请求
        /// </summary>
        private void JoinGroup()
        {
            Send(MessageType.CLIENT_TO_SERVER, 0, 0, Utility.Serialize(new Dictionary<string, object> { { "type", "joingroup" }, { "rid", RoomId }, { "gid", -9999 } }));
        }

        /// <summary>
        /// 客户端心跳
        /// </summary>
        private void Tick()
        {
            try
            {
                Send(MessageType.CLIENT_TO_SERVER, 0, 0, Utility.Serialize(new Dictionary<string, object> { { "type", "mkrl" } }));
            }
            catch (Exception Ex)
            {
                LastException = Ex;
                Disconnect();
            }
        }

        /// <summary>
        /// 发送登出请求
        /// </summary>
        private void Logout()
        {
            try
            {
                Send(MessageType.CLIENT_TO_SERVER, 0, 0, Utility.Serialize(new Dictionary<string, object> { { "type", "logout" } }));
            }
            catch
            {

            }
        }
        #endregion

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="mType">消息去向</param>
        /// <param name="cipter">加密字段(暂时无用)</param>
        /// <param name="reserve">保留字段(暂时无用)</param>
        /// <param name="msg">要发送的UTF-8字符串</param>
        public void Send(MessageType mType, byte cipter, byte reserve, string msg)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                byte[] buffer = Encoding.UTF8.GetBytes(msg);
                int pktSize = 9 + buffer.Length;
                bw.Write(pktSize);
                bw.Write(pktSize);
                bw.Write((short)mType);
                bw.Write(cipter);
                bw.Write(reserve);
                bw.Write(buffer);
                bw.Write((byte)0);
                Client.Send(ms.ToArray());
            }
        }

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="mType">消息去向</param>
        /// <param name="cipter">加密字段(暂时无用)</param>
        /// <param name="reserve">保留字段(暂时无用)</param>
        /// <param name="body">要发送的bytes（不会自动添加\0）</param>
        public void Send(MessageType mType, byte cipter, byte reserve, params byte[] body)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                int pktSize = 9 + body.Length;
                bw.Write(pktSize);
                bw.Write(pktSize);
                bw.Write((short)mType);
                bw.Write(cipter);
                bw.Write(reserve);
                if (body.Length > 0) bw.Write(body);
                Client.Send(ms.ToArray());
            }
        }
    }

    public enum MsgTypeEnum
    {
        None,
        ChatMsg,
        GiftSend,
        UserEnter,
        UserBuyDeserve,
        LiveStart,
        LiveEnd,
        SuperDanmaku,
        UserGotPacket,
    }

    public enum GiftStyleEnum { Plane, Rocket }

    /// <summary>
    /// 基本信息
    /// </summary>
    public interface IBaseInformation
    {
        /// <summary>
        /// 组ID
        /// </summary>
        int GroupId { get; set; }
        /// <summary>
        /// 房间ID
        /// </summary>
        int RoomId { get; set; }
    }

    /// <summary>
    /// 用户信息接口
    /// </summary>
    public interface IUserInfo
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        int UserId { get; set; }
        /// <summary>
        /// 用户昵称
        /// </summary>
        string UserName { get; set; }
        /// <summary>
        /// 用户等级
        /// </summary>
        int UserLevel { get; set; }
        /// <summary>
        /// 房间权限
        /// </summary>
        int RoomPermission { get; set; }
        /// <summary>
        /// 平台权限
        /// </summary>
        int PlatformPermission { get; set; }
        /// <summary>
        /// 贵族等级
        /// </summary>
        int NobleLevel { get; set; }
        /// <summary>
        /// 徽章名字
        /// </summary>
        string MedalName { get; set; }
        /// <summary>
        /// 徽章等级
        /// </summary>
        int MedalLevel { get; set; }
    }

    /// <summary>
    /// 礼物信息接口
    /// </summary>
    public interface IGiftInfo
    {
        /// <summary>
        /// 礼物名称
        /// </summary>
        string GiftName { get; set; }
        /// <summary>
        /// 礼物ID
        /// </summary>
        int GiftId { get; set; }
        /// <summary>
        /// 礼物个数
        /// </summary>
        int GiftCount { get; set; }
        /// <summary>
        /// 礼物连击次数
        /// </summary>
        int GiftHits { get; set; }
        /// <summary>
        /// 礼物特效ID
        /// </summary>
        int GiftEffectId { get; set; }
        /// <summary>
        /// 礼物攻击力 //？？？没看懂
        /// </summary>
        int GiftForce { get; set; }
        /// <summary>
        /// 是否大礼物
        /// </summary>
        bool IsBigGift { get; set; }
        /// <summary>
        /// 受赠者昵称
        /// </summary>
        string GiftDestinationUserName { get; set; }
        /// <summary>
        /// 受赠者房间号
        /// </summary>
        int GiftDestinationRoomId { get; set; }
    }

    /// <summary>
    /// 直播状态更改信息接口
    /// </summary>
    public interface ILiveStatusChangedInfo
    {
        /// <summary>
        /// 开关播原因
        /// </summary>
        string LiveStatusOperateReason { get; set; }
        /// <summary>
        /// 开关播类型代码
        /// </summary>
        int LiveStatusOperateCode { get; set; }
        /// <summary>
        /// 开关播通知类型
        /// </summary>
        int LiveOperateNotify { get; set; }
    }

    /// <summary>
    /// 消息模型,可以根据 <see cref="MsgType"/> 自行转换为
    /// <list type="bullet">
    /// <item><see cref="IBaseInformation"/></item>
    /// <item><see cref="IUserInfo"/></item>
    /// <item><see cref="IGiftInfo"/></item>
    /// <item><see cref="ILiveStatusChangedInfo"/></item>
    /// </list>
    /// 4个接口,方便使用
    /// </summary>
    public class Danmaku : IBaseInformation, IUserInfo, IGiftInfo, ILiveStatusChangedInfo
    {
        #region --------- 弹幕 ---------
        /// <summary>
        /// 弹幕类型
        /// </summary>
        public MsgTypeEnum MsgType { get; set; }
        /// <summary>
        /// 组ID
        /// </summary>
        public int GroupId { get; set; }
        /// <summary>
        /// 房间ID
        /// </summary>
        public int RoomId { get; set; }
        /// <summary>
        /// 用户ID
        /// </summary>
        public int UserId { get; set; }
        /// <summary>
        /// 用户昵称
        /// </summary>
        public string UserName { get; set; }
        /// <summary>
        /// 弹幕内容
        /// </summary>
        public string CommentText { get; set; }
        /// <summary>
        /// 弹幕ID
        /// </summary>
        public string CommentId { get; set; }
        /// <summary>
        /// 用户等级
        /// </summary>
        public int UserLevel { get; set; }
        /// <summary>
        /// 礼物头衔
        /// </summary>
        public int GiftTitle { get; set; }
        /// <summary>
        /// 弹幕颜色
        /// </summary>
        public int CommentColor { get; set; }
        /// <summary>
        /// 客户端类型
        /// </summary>
        public int ClientType { get; set; }
        /// <summary>
        /// 房间权限
        /// </summary>
        public int RoomPermission { get; set; } = 1;
        /// <summary>
        /// 平台权限
        /// </summary>
        public int PlatformPermission { get; set; } = 1;
        /// <summary>
        /// 弹幕类型
        /// </summary>
        public int CommentType { get; set; }
        /// <summary>
        /// 贵族等级
        /// </summary>
        public int NobleLevel { get; set; }
        /// <summary>
        /// 是否贵族弹幕
        /// </summary>
        public bool IsNobleComment { get; set; }
        /// <summary>
        /// 弹幕发送时间 //GateIn
        /// </summary>
        public DateTime TimeStamp { get; set; }
        /// <summary>
        /// 徽章名字
        /// </summary>
        public string MedalName { get; set; }
        /// <summary>
        /// 徽章等级
        /// </summary>
        public int MedalLevel { get; set; }
        /// <summary>
        /// 徽章房间ID
        /// </summary>
        public int MedalRoomId { get; set; }
        /// <summary>
        /// 是否反向弹幕
        /// </summary>
        public bool IsReverseComment { get; set; }
        /// <summary>
        /// 是否高亮弹幕
        /// </summary>
        public bool IsHighLightComment { get; set; }
        /// <summary>
        /// 是否粉丝弹幕
        /// </summary>
        public bool IsFansComment { get; set; }
        #endregion
        #region --------- 礼物 ---------
        /// <summary>
        /// 礼物名称
        /// </summary>
        public string GiftName { get; set; }
        /// <summary>
        /// 礼物ID
        /// </summary>
        public int GiftId { get; set; }
        /// <summary>
        /// 礼物个数
        /// </summary>
        public int GiftCount { get; set; }
        /// <summary>
        /// 礼物连击次数
        /// </summary>
        public int GiftHits { get; set; }
        /// <summary>
        /// 礼物特效ID
        /// </summary>
        public int GiftEffectId { get; set; }
        /// <summary>
        /// 礼物攻击力 //？？？没看懂
        /// </summary>
        public int GiftForce { get; set; }
        /// <summary>
        /// 是否大礼物
        /// </summary>
        public bool IsBigGift { get; set; }
        #endregion
        #region --------- 进房 ---------
        /// <summary>
        /// 用户战斗力
        /// </summary>
        public int UserStrength { get; set; }
        /// <summary>
        /// 用户栏目上周排名
        /// </summary>
        public int UserLastWeekRank { get; set; }
        #endregion
        #region --------- 酬勤 ---------
        /// <summary>
        /// 酬勤等级
        /// </summary>
        public int DeserveLevel { get; set; }
        /// <summary>
        /// 酬勤数量
        /// </summary>
        public int DeserveCount { get; set; }
        /// <summary>
        /// 最高酬勤等级
        /// </summary>
        public int MostDeserveLevel { get; set; }
        #endregion
        #region -------- 开关播 --------
        /// <summary>
        /// 开关播原因
        /// </summary>
        public string LiveStatusOperateReason { get; set; }
        /// <summary>
        /// 开关播类型代码
        /// </summary>
        public int LiveStatusOperateCode { get; set; }
        /// <summary>
        /// 开关播通知类型
        /// </summary>
        public int LiveOperateNotify { get; set; }
        #endregion
        #region ------- 超级弹幕 -------
        /// <summary>
        /// 超级弹幕跳转URL
        /// </summary>
        public string Url { get; set; }
        /// <summary>
        /// 超级弹幕URL跳转类型
        /// </summary>
        public int JumpType { get; set; }
        /// <summary>
        /// 超级弹幕跳转房间号
        /// </summary>
        public int JumpTargetRoomId { get; set; }
        #endregion
        #region ---- 房间内礼物广播 ----
        /// <summary>
        /// 受赠者昵称
        /// </summary>
        public string GiftDestinationUserName { get; set; }
        /// <summary>
        /// 受赠者房间号
        /// </summary>
        public int GiftDestinationRoomId { get; set; }
        /// <summary>
        /// 礼物广播样式
        /// </summary>
        public int GiftBroadcastType { get; set; }
        /// <summary>
        /// 有无礼包
        /// </summary>
        public bool HasPacket { get; set; }
        /// <summary>
        /// 广播展现样式
        /// </summary>
        public GiftStyleEnum GiftStyle { get; set; }
        #endregion

        [Obsolete("不建议直接实例化,请使用Parse方法或者构造函数的另一个重载实例化")]
        public Danmaku() { }

        public Danmaku(string rawData)
        {
            Dictionary<string, string> dict = Utility.Deserialize(rawData);
            switch (dict["type"])
            {
                case "chatmsg":
                    {
                        this.MsgType = MsgTypeEnum.ChatMsg;
                        if (dict.ContainsKey("rid"))
                        {
                            this.RoomId = int.Parse(dict["rid"]);
                        }
                        if (dict.ContainsKey("gid"))
                        {
                            this.GroupId = int.Parse(dict["gid"]);
                        }
                        if (dict.ContainsKey("uid"))
                        {
                            this.UserId = int.Parse(dict["uid"]);
                        }
                        if (dict.ContainsKey("nn"))
                        {
                            this.UserName = dict["nn"];
                        }
                        if (dict.ContainsKey("txt"))
                        {
                            this.CommentText = dict["txt"];
                        }
                        if (dict.ContainsKey("cid"))
                        {
                            this.CommentId = dict["cid"];
                        }
                        if (dict.ContainsKey("level"))
                        {
                            this.UserLevel = int.Parse(dict["level"]);
                        }
                        if (dict.ContainsKey("gt"))
                        {
                            this.GiftTitle = int.Parse(dict["gt"]);
                        }
                        if (dict.ContainsKey("col"))
                        {
                            this.CommentColor = int.Parse(dict["col"]);
                        }
                        if (dict.ContainsKey("rg"))
                        {
                            this.RoomPermission = int.Parse(dict["rg"]);
                        }
                        if (dict.ContainsKey("pg"))
                        {
                            this.PlatformPermission = int.Parse(dict["pg"]);
                        }
                        if (dict.ContainsKey("dlv"))
                        {
                            this.DeserveLevel = int.Parse(dict["dlv"]);
                        }
                        if (dict.ContainsKey("dc"))
                        {
                            this.DeserveCount = int.Parse(dict["dc"]);
                        }
                        if (dict.ContainsKey("bdlv"))
                        {
                            this.MostDeserveLevel = int.Parse(dict["bdlv"]);
                        }
                        if (dict.ContainsKey("cmt"))
                        {
                            this.CommentType = int.Parse(dict["cmt"]);
                        }
                        if (dict.ContainsKey("nl"))
                        {
                            this.NobleLevel = int.Parse(dict["nl"]);
                        }
                        if (dict.ContainsKey("nc"))
                        {
                            this.IsNobleComment = int.Parse(dict["nc"]) == 1;
                        }
                        if (dict.ContainsKey("gatin"))
                        {
                            this.TimeStamp = DateTime.Now.ToUniversalTime().AddSeconds(int.Parse(dict["gatin"]));
                        }
                        else
                        {
                            this.TimeStamp = DateTime.Now;
                        }
                        if (dict.ContainsKey("bnn"))
                        {
                            this.MedalName = dict["bnn"];
                        }
                        if (dict.ContainsKey("bl"))
                        {
                            this.MedalLevel = int.Parse(dict["bl"]);
                        }
                        if (dict.ContainsKey("brid"))
                        {
                            this.MedalRoomId = int.Parse(dict["brid"]);
                        }
                        if (dict.ContainsKey("rev"))
                        {
                            this.IsReverseComment = int.Parse(dict["rev"]) == 1;
                        }
                        if (dict.ContainsKey("hl"))
                        {
                            this.IsHighLightComment = int.Parse(dict["hl"]) == 1;
                        }
                        if (dict.ContainsKey("ifs"))
                        {
                            this.IsFansComment = int.Parse(dict["ifs"]) == 1;
                        }
                        break;
                    }
                case "dgb":
                    {
                        this.MsgType = MsgTypeEnum.GiftSend;
                        if (dict.ContainsKey("rid"))
                        {
                            this.RoomId = int.Parse(dict["rid"]);
                        }
                        if (dict.ContainsKey("gid"))
                        {
                            this.GroupId = int.Parse(dict["gid"]);
                        }
                        if (dict.ContainsKey("gfid"))
                        {
                            this.GiftId = int.Parse(dict["gfid"]);
                        }
                        if (dict.ContainsKey("uid"))
                        {
                            this.UserId = int.Parse(dict["uid"]);
                        }
                        if (dict.ContainsKey("nn"))
                        {
                            this.UserName = dict["nn"];
                        }
                        if (dict.ContainsKey("bg"))
                        {
                            this.IsBigGift = int.Parse(dict["bg"]) != 0;
                        }
                        if (dict.ContainsKey("eid"))
                        {
                            this.GiftEffectId = int.Parse(dict["eid"]);
                        }
                        if (dict.ContainsKey("level"))
                        {
                            this.UserLevel = int.Parse(dict["level"]);
                        }
                        this.GiftCount = dict.ContainsKey("gfcnt") ? int.Parse(dict["gfcnt"]) : 1;
                        this.GiftHits = dict.ContainsKey("hits") ? int.Parse(dict["hits"]) : 1;
                        if (dict.ContainsKey("dlv"))
                        {
                            this.DeserveLevel = int.Parse(dict["dlv"]);
                        }
                        if (dict.ContainsKey("dc"))
                        {
                            this.DeserveCount = int.Parse(dict["dc"]);
                        }
                        if (dict.ContainsKey("bdl"))
                        {
                            this.MostDeserveLevel = int.Parse(dict["bdl"]);
                        }
                        if (dict.ContainsKey("rg"))
                        {
                            this.RoomPermission = int.Parse(dict["rg"]);
                        }
                        if (dict.ContainsKey("pg"))
                        {
                            this.PlatformPermission = int.Parse(dict["pg"]);
                        }
                        if (dict.ContainsKey("nl"))
                        {
                            this.NobleLevel = int.Parse(dict["nl"]);
                        }
                        if (dict.ContainsKey("bnn"))
                        {
                            this.MedalName = dict["bnn"];
                        }
                        if (dict.ContainsKey("bl"))
                        {
                            this.MedalLevel = int.Parse(dict["bl"]);
                        }
                        if (dict.ContainsKey("brid"))
                        {
                            this.MedalRoomId = int.Parse(dict["brid"]);
                        }
                        if (dict.ContainsKey("fc"))
                        {
                            this.GiftForce = int.Parse(dict["fc"]);
                        }
                        if (dict.ContainsKey("gatin"))
                        {
                            this.TimeStamp = DateTime.Now.ToUniversalTime().AddSeconds(int.Parse(dict["gatin"]));
                        }
                        else
                        {
                            this.TimeStamp = DateTime.Now;
                        }
                        break;
                    }
                case "uenter":
                    {
                        this.MsgType = MsgTypeEnum.UserEnter;
                        if (dict.ContainsKey("rid"))
                        {
                            this.RoomId = int.Parse(dict["rid"]);
                        }
                        if (dict.ContainsKey("gid"))
                        {
                            this.GroupId = int.Parse(dict["gid"]);
                        }
                        if (dict.ContainsKey("uid"))
                        {
                            this.UserId = int.Parse(dict["uid"]);
                        }
                        if (dict.ContainsKey("nn"))
                        {
                            this.UserName = dict["nn"];
                        }
                        if (dict.ContainsKey("level"))
                        {
                            this.UserLevel = int.Parse(dict["level"]);
                        }
                        if (dict.ContainsKey("gt"))
                        {
                            this.GiftTitle = int.Parse(dict["gt"]);
                        }
                        if (dict.ContainsKey("rg"))
                        {
                            this.RoomPermission = int.Parse(dict["rg"]);
                        }
                        if (dict.ContainsKey("pg"))
                        {
                            this.PlatformPermission = int.Parse(dict["pg"]);
                        }
                        if (dict.ContainsKey("dlv"))
                        {
                            this.DeserveLevel = int.Parse(dict["dlv"]);
                        }
                        if (dict.ContainsKey("dc"))
                        {
                            this.DeserveCount = int.Parse(dict["dc"]);
                        }
                        if (dict.ContainsKey("bdl"))
                        {
                            this.MostDeserveLevel = int.Parse(dict["bdl"]);
                        }
                        if (dict.ContainsKey("nl"))
                        {
                            this.NobleLevel = int.Parse(dict["nl"]);
                        }
                        if (dict.ContainsKey("crw"))
                        {
                            this.UserLastWeekRank = int.Parse(dict["crw"]);
                        }
                        break;
                    }
                case "bc_buy_deserve":
                    {
                        this.MsgType = MsgTypeEnum.UserBuyDeserve;
                        Debug.WriteLine(rawData);
                        if (dict.ContainsKey("rid"))
                        {
                            this.RoomId = int.Parse(dict["rid"]);
                        }
                        if (dict.ContainsKey("gid"))
                        {
                            this.GroupId = int.Parse(dict["gid"]);
                        }
                        if (dict.ContainsKey("level"))
                        {
                            this.UserLevel = int.Parse(dict["level"]);
                        }
                        if (dict.ContainsKey("cnt"))
                        {
                            this.GiftCount = int.Parse(dict["cnt"]);
                        }
                        if (dict.ContainsKey("hits"))
                        {
                            this.GiftHits = int.Parse(dict["hits"]);
                        }
                        if (dict.ContainsKey("lev"))
                        {
                            this.DeserveLevel = int.Parse(dict["lev"]);
                        }
                        break;
                    }
                case "rss":
                    {
                        Debug.WriteLine(rawData);
                        if (dict.ContainsKey("rid"))
                        {
                            this.RoomId = int.Parse(dict["rid"]);
                        }
                        if (dict.ContainsKey("gid"))
                        {
                            this.GroupId = int.Parse(dict["gid"]);
                        }
                        if (dict.ContainsKey("ss"))
                        {
                            this.MsgType = int.Parse(dict["ss"]) == 1 ? MsgTypeEnum.LiveStart : MsgTypeEnum.LiveEnd;
                        }
                        if (dict.ContainsKey("rt"))
                        {
                            this.LiveStatusOperateReason = dict["rt"];
                        }
                        if (dict.ContainsKey("rtv"))
                        {
                            this.LiveStatusOperateCode = int.Parse(dict["rtv"]);
                        }
                        if (dict.ContainsKey("notify"))
                        {
                            this.LiveOperateNotify = int.Parse(dict["notify"]);
                        }
                        break;
                    }
                case "ssd":
                    {
                        Debug.WriteLine(rawData);
                        this.MsgType = MsgTypeEnum.SuperDanmaku;
                        if (dict.ContainsKey("rid"))
                        {
                            this.RoomId = int.Parse(dict["rid"]);
                        }
                        if (dict.ContainsKey("gid"))
                        {
                            this.GroupId = int.Parse(dict["gid"]);
                        }
                        if (dict.ContainsKey("content"))
                        {
                            this.CommentText = dict["content"];
                        }
                        if (dict.ContainsKey("sdid"))
                        {
                            this.CommentId = dict["sdid"];
                        }
                        if (dict.ContainsKey("url"))
                        {
                            this.Url = dict["url"];
                        }
                        if (dict.ContainsKey("clitp"))
                        {
                            this.ClientType = int.Parse(dict["clitp"]);
                        }
                        if (dict.ContainsKey("jmptp"))
                        {
                            this.JumpType = int.Parse(dict["jmptp"]);
                        }
                        if (dict.ContainsKey("trid"))
                        {
                            this.JumpTargetRoomId = int.Parse(dict["trid"]);
                        }
                        break;
                    }
                case "spbc":
                    {
                        Debug.WriteLine(rawData);
                        if (dict.ContainsKey("rid"))
                        {
                            this.RoomId = int.Parse(dict["rid"]);
                        }
                        if (dict.ContainsKey("gid"))
                        {
                            this.GroupId = int.Parse(dict["gid"]);
                        }
                        if (dict.ContainsKey("sn"))
                        {
                            this.UserName = dict["sn"];
                        }
                        if (dict.ContainsKey("dn"))
                        {
                            this.GiftDestinationUserName = dict["dn"];
                        }
                        if (dict.ContainsKey("gn"))
                        {
                            this.GiftName = dict["gn"];
                        }
                        if (dict.ContainsKey("gc"))
                        {
                            this.GiftCount = int.Parse(dict["gc"]);
                        }
                        if (dict.ContainsKey("drid"))
                        {
                            this.GiftDestinationRoomId = int.Parse(dict["drid"]);
                        }
                        if (dict.ContainsKey("gs"))
                        {
                            this.GiftBroadcastType = int.Parse(dict["gs"]);
                        }
                        if (dict.ContainsKey("gb"))
                        {
                            this.HasPacket = int.Parse(dict["gb"]) == 1;
                        }
                        if (dict.ContainsKey("es"))
                        {
                            this.GiftStyle = int.Parse(dict["es"]) == 1 ? GiftStyleEnum.Rocket : GiftStyleEnum.Plane;
                        }
                        if (dict.ContainsKey("gfid"))
                        {
                            this.GiftId = int.Parse(dict["gfid"]);
                        }
                        if (dict.ContainsKey("eid"))
                        {
                            this.GiftEffectId = int.Parse(dict["eid"]);
                        }
                        break;
                    }
                case "ggbb":
                    {
                        //TODO:等一个实例来分析
                        Debug.WriteLine(rawData);
                        this.MsgType = MsgTypeEnum.UserGotPacket;
                        break;
                    }
            }
        }

        public static Danmaku Parse(string rawData)
        {
            return new Danmaku(rawData);
        }
    }
}