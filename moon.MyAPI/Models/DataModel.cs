using System.Collections.Generic;

namespace moon.MyAPI.Models
{
    public class DataModel
    {
        /// <summary>
        /// 设备IP地址
        /// </summary>
        public string Ip { get; set; }

        /// <summary>
        /// 设备数据字典，用于存放读取到的所有数据
        /// </summary>
        public Dictionary<string, object> Data { get; set; }

        /// <summary>
        /// 读取数据的时间戳
        /// </summary>
        public string Date { get; set; }
    }


    public class S7DataModel
    {
        /// <summary>
        /// 设备IP地址
        /// </summary>
        public string Ip { get; set; }

        /// <summary>
        /// 设备数据字典，用于存放读取到的所有数据
        /// </summary>
        public Dictionary<string, byte[]> Data { get; set; }

        /// <summary>
        /// 读取数据的时间戳
        /// </summary>
        public string Date { get; set; }
    }
}
