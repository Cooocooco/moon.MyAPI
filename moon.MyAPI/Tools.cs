using IniParser;
using IniParser.Model;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace moon.MyAPI
{
    /// <summary>
    /// 工具
    /// </summary>
    public class Tools
    {
        /// <summary>
        /// 解析Ini文件
        /// </summary>
        /// <returns></returns>
        public static IniData ReadIni()
        {
            //将文件文本编辑工具保存为 utf-8 + bom 格式即可
            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");

            //创建一个ini文件解析器的实例
            var parser = new FileIniDataParser();

            //多注释字符（# ;）,剔除开头注释和内联注释
            parser.Parser.Configuration.CommentRegex = new Regex(@"(#|;)(.*)");

            // 这将加载INI文件，读取ini中包含的数据，并解析该数据
            IniData data = parser.ReadFile(file);

            return data;
        }
    }
}
