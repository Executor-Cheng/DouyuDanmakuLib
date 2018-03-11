using System;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Net;
using System.IO;
using System.Text;

namespace DouyuDMLib
{
    public static class Utility
    {
        public static int UnixTimeStamp => (int)((DateTime.Now.ToUniversalTime().Ticks - 621355968000000000) / 10000000);

        public static int GetRoomId(string roomName)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create($"http://open.douyucdn.cn/api/RoomApi/room/{roomName}");
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (Stream stream = res.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string json = reader.ReadToEnd();
                    if (Regex.Match(json, @"(?<=""error\"":)\d+?").Value == "0")
                    {
                        return int.Parse(Regex.Match(json, @"(?<=""room_id\"":"")\d+?(?="")").Value);
                    }
                    else throw new ArgumentException($"获取失败,服务器返回了以下信息:{Regex.Match(json, @"(?<=""data\"":"")\S+?(?="")")}", "roomName");
                    //JObject j = JObject.Parse(json);
                    //if (j["error"].ToObject<int>() == 0)
                    //    return j["data"]["room_id"].ToObject<int>();
                    //else throw new ArgumentException($"获取失败,服务器返回了以下信息:{j["data"]}", "roomName");
                    //--------------------------------------------------------------------------
                    //本处使用原生.NET框架实现解析Json,所以就不引用Newtonsoft.Json.dll
                    //如有需要,请使用NuGet安装 Newtonsoft.Json, 并在头部添加 using Newtonsoft.Json.Linq;
                }
            }
            //catch (Newtonsoft.Json.JsonReaderException Ex)
            //{
            //    throw new ApplicationException("解析Json失败");
            //}
            catch (WebException Ex)
            {
                throw new WebException($"向斗鱼服务器请求房间号时出错:{Ex.Message}", Ex.InnerException, Ex.Status, Ex.Response);
            }
            catch (FormatException)
            {
                throw new ApplicationException("解析Json失败");
            }
            catch (Exception Ex)
            {
                throw new Exception($"发生了意外的错误:{Ex.Message}", Ex.InnerException);
            }
        }

        public static string Serialize(Dictionary<string, object> data)
        {
            return string.Join(string.Empty, data.Select(p => $"{Escape(p.Key)}@={Escape(p.Value.ToString())}/"));
        }

        public static Dictionary<string, string> Deserialize(string data)
        {
            MatchCollection mc = Regex.Matches(data, @"(?<key>.+?)\@\=(?<value>.*?)\/");
            Dictionary<string, string> dict = new Dictionary<string, string>();
            foreach (Match m in mc)
            {
                dict[Descape(m.Groups["key"].Value)] = Descape(m.Groups["value"].Value);
            }
            return dict;
        }

        public static string GetDicValue(this IDictionary<string, string> dict, string key) => dict.ContainsKey(key) ? dict[key] : null;

        public static string Escape(string s)
        {
            return s.Replace("@", "@A").Replace("/", "@S");
        }

        public static string Descape(string s)
        {
            return s.Replace("@S", "/").Replace("@A", "@");
        }

        public static void ReadBytes(this Socket stream, byte[] buffer, int offset, int count)
        {
            if (offset + count > buffer.Length)
                throw new ArgumentException();
            int read = 0;
            while (read < count)
            {
                var available = stream.Receive(buffer, offset, count - read, SocketFlags.None);
                if (available == 0)
                {
                    throw new ObjectDisposedException(null);
                }
                read += available;
                offset += available;
            }
        }
    }
}
