using moon.MyAPI.Models;
using S7.Net;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Web.Http;

namespace moon.MyAPI.Controllers
{
    public class Siemens1200Controller : ApiController
    {
        private readonly List<string> List;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<ushort>>> AddressDict;
        private readonly ConcurrentDictionary<string, DataModel> DataDict;


        /// <summary>
        /// 构造函数，初始化控制器
        /// </summary>
        public Siemens1200Controller()
        {
            // 从配置文件读取设备IP地址和访问地址信息
            var data = Tools.ReadIni();
            List = new List<string>();
            List = data["Siemens1200"]["IP"].Split(',', (char)StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
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
        private async Task<Dictionary<string, object>> ReadDataAsync(string ip, Plc plc)
        {
            await plc.OpenAsync();

            var address = AddressDict[ip];
            var result = new Dictionary<string, object>();

            foreach (var kvp in address)
            {
                var value = kvp.Value;

                var data = await plc.ReadBytesAsync(DataType.DataBlock, value[0], value[1], value[2]).ConfigureAwait(false);

                result.Add(kvp.Key, data);
            }
            plc.Close();
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

            using (var plc = new Plc(CpuType.S71200, ip, 0, 1)) // 建立连接
            {
                try
                {
                    var axisData = await ReadDataAsync(ip, plc).ConfigureAwait(false);

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