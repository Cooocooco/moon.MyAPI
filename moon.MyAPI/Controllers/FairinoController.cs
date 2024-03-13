using fairino;
using moon.MyAPI.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Web.Http;

namespace moon.MyAPI.Controllers
{
    public class FairinoController : ApiController
    {
        private readonly List<string> List; // 存储不重复的设备IP地址
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<ushort>>> AddressDict; // 存储多个设备访问地址
        private readonly ConcurrentDictionary<string, DataModel> DataDict; // 缓存已读取的设备数据
        public FairinoController()
        {
            // 从配置文件读取设备IP地址和访问地址信息
            var data = Tools.ReadIni();
            List = new List<string>();
            List = data["Fairino"]["IP"].Split(',', (char)StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
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
        /// 同步读取设备的数据
        /// </summary>
        private Dictionary<string, object> ReadRobotData(string ip, Robot robot)
        {
            var address = AddressDict[ip];
            var result = new Dictionary<string, object>();
            foreach (var kvp in address)
            {
                switch (kvp.Key)
                {
                    case "Joint":
                        byte flag = 0;
                        JointPos j_deg = new JointPos(0, 0, 0, 0, 0, 0);
                        robot.GetActualJointPosDegree(flag, ref j_deg);//获取机器人关节位置
                        List<float> j_deg_list = new List<float>();
                        foreach (double jointValue in j_deg.jPos)
                        {
                            j_deg_list.Add((float)jointValue);
                        }
                        result.Add(kvp.Key, j_deg_list);
                        break;
                    case "DI":
                        var DIvalue = kvp.Value;
                        byte block = 0;
                        byte di = 0;
                        List<int> di_list = new List<int>();
                        for (int i = DIvalue[0]; i < DIvalue[0] + DIvalue[1]; i++)
                        {
                            robot.GetDI(i, block, ref di);//获取数字输入
                            di_list.Add(di);
                        }
                        result.Add(kvp.Key, di_list);
                        break;
                    default:
                        break;
                }
                robot.CloseRPC();
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

            var robot = new Robot();
            robot.RPC(ip);
            try
            {
                var axisData = ReadRobotData(ip, robot);

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
