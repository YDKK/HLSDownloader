using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HLSDownloader
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/77.0.3865.65 Safari/537.36 Edg/77.0.235.20");

            Console.WriteLine("HLSDownloader https://github.com/YDKK/HLSDownloader");
            Console.WriteLine();

            Console.Write("m3u8 url: ");
            var m3u8Url = Console.ReadLine();
            Console.Write("video name: ");
            var name = Console.ReadLine();

            var m3u8File = (await (await client.GetAsync(m3u8Url)).Content.ReadAsStringAsync()).Replace(",\n", ",").Split('\n');

            var keyInfo = m3u8File.FirstOrDefault(x => x.StartsWith("#EXT-X-KEY:"));
            byte[] key = null;
            byte[] iv = null;
            if (keyInfo != null)
            {
                var regexIv = new Regex("IV=0x(?<IV>.*?)(,|$)", RegexOptions.Compiled);
                var regexUri = new Regex("URI=\"(?<URI>.*?)\"(,|$)", RegexOptions.Compiled);

                IEnumerable<byte> GetBytes(string str)
                {
                    for (int i = 0; i < str.Length / 2; i++)
                    {
                        yield return Convert.ToByte(str.AsSpan().Slice(i * 2, 2).ToString(), 16);
                    }
                }

                iv = GetBytes(regexIv.Match(keyInfo).Groups["IV"].Value).ToArray();
                var keyUri = regexUri.Match(keyInfo).Groups["URI"].Value;
                if (!keyUri.StartsWith("http"))
                {
                    var baseUri = m3u8Url.Split('/');
                    baseUri[baseUri.Length - 1] = keyUri;
                    keyUri = string.Join('/', baseUri);
                }
                key = await (await client.GetAsync(keyUri)).Content.ReadAsByteArrayAsync();
            }

            var targetTsList = m3u8File.Where(x => x.StartsWith("#EXTINF:")).Select(x => new string(x.SkipWhile(y => y != ',').Skip(1).ToArray()));
            targetTsList = targetTsList.Select(x =>
            {
                if (!x.StartsWith("http"))
                {
                    var baseUri = m3u8Url.Split('/');
                    baseUri[baseUri.Length - 1] = x;
                    x = string.Join('/', baseUri);
                }
                return x;
            });

            using (var resultFile = File.OpenWrite($"{name}.ts"))
            {
                var current = 0;
                foreach (var targetTsUrl in targetTsList)
                {
                    current++;
                    Console.WriteLine($"Downloading {current}/{targetTsList.Count()} ({100.0 * current / targetTsList.Count():f2}%)");
                    using (var targetTs = await (await client.GetAsync(targetTsUrl)).Content.ReadAsStreamAsync())
                    {
                        if (key != null)
                        {
                            using (var aes = Aes.Create())
                            using (var memory = new MemoryStream())
                            using (var crypto = new CryptoStream(memory, aes.CreateDecryptor(key, iv), CryptoStreamMode.Read))
                            {
                                targetTs.CopyTo(memory);
                                memory.Seek(0, SeekOrigin.Begin);
                                crypto.CopyTo(resultFile);
                            }
                        }
                        else
                        {
                            targetTs.CopyTo(resultFile);
                        }
                    }
                }
            }
        }
    }
}
