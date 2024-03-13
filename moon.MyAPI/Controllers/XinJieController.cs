using Modbus.Device;
using moon.MyAPI.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Web.Http;

namespace moon.MyAPI.Controllers
{
    public class XinJieController : ApiController
    {
        private readonly List<string> List;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<ushort>>> AddressDict;
        private readonly ConcurrentDictionary<string, DataModel> DataDict;


        /// <summary>
        /// 构造函数，初始化控制器
        /// </summary>
        public XinJieController()
        {
            // 从配置文件读取设备IP地址和访问地址信息
            var data = Tools.ReadIni();
            List = new List<string>();
            List = data["XinJie"]["IP"].Split(',', (char)StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
            AddressDict = new ConcurrentDictionary<string, ConcurrentDictionary<string, List<ushort>>>();

            // 使用并行处理每个设备的访问地址
            Parallel.ForEach(List, ip =>
            {
                var subDict = new ConcurrentDictionary<string, List<ushort>>(data[ip].Select(key => new KeyValuePair<string, List<ushort>>(key.KeyName, key.Value.Split(',').Select(ushort.Parse).ToList())));
                AddressDict.TryAdd(ip, subDict);
            });

            DataDict = new ConcurrentDictionary<string, DataModel>();
        }


        /// <summary>
        /// 异步读取设备的数据
        /// </summary>
        private async Task<Dictionary<string, object>> ReadDataAsync(string ip, ModbusIpMaster adapter)
        {
            var address = AddressDict[ip];
            var result = new Dictionary<string, object>();
            foreach (var kvp in address)
            {
                switch (kvp.Key)
                {
                    case "REAL":
                        var value = kvp.Value;
                        // 读取机器人位置数据
                        var msg = await adapter.ReadHoldingRegistersAsync(0x01, value[0], value[1]).ConfigureAwait(false);
                        // 初始化一个float类型的列表来存储所有的floatValue
                        List<float> floatValues = new List<float>();
                        // 假设msg数组长度是偶数，每两个寄存器组成一个float值
                        for (int i = 0; i < msg.Length; i += 2)
                        {
                            // 根据新逻辑，偶数索引i对应低位寄存器，奇数索引i+1对应高位寄存器
                            // 注意：这里的描述与要求相反，因为实际操作中msg[i]是低位，msg[i+1]是高位
                            int combined = (msg[i + 1] << 16) | msg[i]; // 使用Little-endian顺序组合

                            byte[] bytes = BitConverter.GetBytes(combined);

                            // 如果系统不是Little-endian，则反转字节顺序以匹配float的内部表示
                            if (!BitConverter.IsLittleEndian)
                            {
                                Array.Reverse(bytes);
                            }

                            // 将字节数组转换为float
                            float floatValue = BitConverter.ToSingle(bytes, 0);
                            // 将计算出的floatValue添加到floatValues列表中
                            floatValues.Add(floatValue);
                        }
                        result.Add(kvp.Key, floatValues);
                        break;
                    case "INT":
                        var value1 = kvp.Value;
                        var msg1 = await adapter.ReadHoldingRegistersAsync(0x01, value1[0], value1[1]).ConfigureAwait(false); // 读取机器人位置数据
                        var shortValue = msg1.Select(x => (short)x);
                        result.Add(kvp.Key, shortValue);
                        break;
                    default:
                        break;
                }
            }
            return result;
        }

        /// <summary>
        /// 获取设备数据
        /// </summary>
        private async Task<DataModel> GetDataAsync(string ip)
        {
            if (DataDict.TryGetValue(ip, out var cachedResult))
            {
                return cachedResult;
            }
            // 检查设备是否在线
            var pingResult = await new Ping().SendPingAsync(ip, 100);
            if (pingResult.Status != IPStatus.Success)
            {
                // 如果设备不在线，返回空数据
                cachedResult = new DataModel
                {
                    Ip = ip,
                    Data = null,
                    Date = DateTime.Now.ToString("G")
                };
                DataDict.TryAdd(ip, cachedResult);

                return cachedResult;
            }

            using (var client = new TcpClient(ip, 502)) // 建立连接
            {
                var adapter = ModbusIpMaster.CreateIp(client); // 创建Modbus适配器

                try
                {
                    var axisData = await ReadDataAsync(ip, adapter).ConfigureAwait(false);

                    cachedResult = new DataModel
                    {
                        Ip = ip,
                        Data = axisData,
                        Date = DateTime.Now.ToString("G")
                    };
                    DataDict.TryAdd(ip, cachedResult);
                }
                catch (Exception)
                {
                    // 如果发生异常，返回空数据
                    cachedResult = new DataModel
                    {
                        Ip = ip,
                        Data = null,


                        Date = DateTime.Now.ToString("G")
                    };
                    DataDict.TryAdd(ip, cachedResult);
                }
            }
            return cachedResult;
        }

        /// <summary>
        /// 获取单个设备数据
        /// </summary>
        /// <param name="ip">设备IP地址</param>
        /// <returns>单个设备的数据</returns>
        public async Task<DataModel> Get(string ip)
        {
            try
            {
                var result = await GetDataAsync(ip);
                return result;
            }
            catch (Exception)
            {
                return new DataModel
                {
                    Ip = ip,
                    Data = null,
                    Date = DateTime.Now.ToString("G")
                };
            }
        }

        /// <summary>
        /// 获取全部设备数据
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<DataModel>> GetAll()
        {
            try
            {
                var tasks = List.AsParallel().Select(ip => GetDataAsync(ip));
                var results = await Task.WhenAll(tasks);

                return results;
            }
            catch (Exception)
            {
                var errorModels = List.AsParallel().Select(ip => new DataModel
                {
                    Ip = ip,
                    Data = null,
                    Date = DateTime.Now.ToString("G")
                });

                return errorModels;
            }
        }
    }
}