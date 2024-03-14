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
    public class EliteController : ApiController
    {
        private readonly List<string> List;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<ushort>>> AddressDict;
        private readonly ConcurrentDictionary<string, DataModel> DataDict;


        /// <summary>
        /// 构造函数，初始化控制器
        /// </summary>
        public EliteController()
        {
            // 从配置文件读取设备IP地址和访问地址信息
            var data = Tools.ReadIni();
            List = new List<string>();
            List = data["Elite"]["IP"].Split(',', (char)StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
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
                Console.WriteLine($"{kvp.Key}");
                switch (kvp.Key)
                {
                    case "Joint":
                        var Joint_value = kvp.Value;
                        Console.WriteLine($"{Joint_value[0]}, {Joint_value[1]}");
                        var msg = await adapter.ReadHoldingRegistersAsync(0x01, Joint_value[0], Joint_value[1]).ConfigureAwait(false);
                        var ushorts = msg.Select(x => (short)x / 5000.0 * 180 / Math.PI);   //根据艾利特文档获取的值除以5000.0得到的弧度，转成度
                        result.Add(kvp.Key, ushorts);
                        break;
                    case "DI":
                        var DI_value = kvp.Value;
                        Console.WriteLine($"{DI_value[0]}, {DI_value[1]}");
                        var DI_msg = await adapter.ReadHoldingRegistersAsync(0x01, DI_value[0], DI_value[1]).ConfigureAwait(false);
                        var DI_BinaryValues = DI_msg.Select(x => Convert.ToString(x, 2)).ToArray(); //根据艾利特文档将获取的值转成二进制
                        var DI_BinaryValues16 = DI_BinaryValues.Select(x => DI_PadBinaryValue(x)).ToArray();   //处理每个二进制字符串
                        string DI_PadBinaryValue(string binaryValue)    //如果长度大于等于16位，则将其颠倒； 如果不足16位，则在前面补0直到达到16位，然后将其颠倒。 
                        {
                            if (binaryValue.Length >= 16)
                            {
                                var reversedChars = binaryValue.Reverse().ToArray();
                                string reversedBinaryValue = new string(reversedChars);
                                return reversedBinaryValue;
                            }
                            else
                            {
                                var binaryValue16 = binaryValue.PadLeft(16, '0');
                                var reversedChars = binaryValue16.Reverse().ToArray();
                                string reversedBinaryValue = new string(reversedChars);
                                return reversedBinaryValue;
                            }
                        }
                        var DI_concatenatedBinaryValue = string.Join("", DI_BinaryValues16);   //将数组中的每个值连接在一起，形成一个新的字符串
                        result.Add(kvp.Key, DI_concatenatedBinaryValue);
                        break;
                    case "DO":
                        var DO_value = kvp.Value;
                        Console.WriteLine($"{DO_value[0]}, {DO_value[1]}");
                        var DO_msg = await adapter.ReadHoldingRegistersAsync(0x01, DO_value[0], DO_value[1]).ConfigureAwait(false);
                        var DO_BinaryValues = DO_msg.Select(x => Convert.ToString(x, 2)).ToArray(); //根据艾利特文档将获取的值转成二进制
                        var DO_BinaryValues16 = DO_BinaryValues.Select(x => DO_PadBinaryValue(x)).ToArray();   //处理每个二进制字符串
                        string DO_PadBinaryValue(string binaryValue)    //如果长度大于等于16位，则将其颠倒； 如果不足16位，则在前面补0直到达到16位，然后将其颠倒。 
                        {
                            if (binaryValue.Length >= 16)
                            {
                                var reversedChars = binaryValue.Reverse().ToArray();
                                string reversedBinaryValue = new string(reversedChars);
                                return reversedBinaryValue;
                            }
                            else
                            {
                                var binaryValue16 = binaryValue.PadLeft(16, '0');
                                var reversedChars = binaryValue16.Reverse().ToArray();
                                string reversedBinaryValue = new string(reversedChars);
                                return reversedBinaryValue;
                            }
                        }
                        var DO_concatenatedBinaryValue = string.Join("", DO_BinaryValues16);   //将数组中的每个值连接在一起，形成一个新的字符串
                        result.Add(kvp.Key, DO_concatenatedBinaryValue);
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

            using (var client = new TcpClient(ip, 502)) // 建立TCP连接
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
        [HttpGet]
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
        [HttpGet]
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