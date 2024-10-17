using DevExpress.XtraReports.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace PBReportConverter.Converters
{
    class RepxToJsonConverter(string inputDir, string outputDir)
    {
        private readonly string _inputDir = inputDir;
        private readonly string _outputDir = outputDir;

        public string Category { get; set; } = "Unknown";

        internal void ConvertToJson(string fileName)
        {
            Console.WriteLine($"Converting {fileName} to .json");
            Console.Write("Is this a subreport (hide from overviews)? (Y/N)");
            var k = Console.ReadKey(true).Key;
            bool isSubreport = k == ConsoleKey.Y || k == ConsoleKey.Enter;
            Console.WriteLine(isSubreport ? "- Yes" : "- No");

            Console.WriteLine($"Enter the category, or press Y to use '{Category}'");
            var firstLetter = Console.ReadKey(true).Key;
            if (firstLetter == ConsoleKey.Y || firstLetter == ConsoleKey.Enter)
            {
                Console.WriteLine(Category);
            }
            else
            {
                Console.Write(firstLetter);
                Category = firstLetter + Console.ReadLine();
            }

            Console.Write("Processing...");

            var inputPath = Path.Combine(_inputDir, fileName);
            var extensionIndex = fileName.LastIndexOf('.');
            var outputPath = Path.Combine(_outputDir, $"{fileName[..extensionIndex]}.json");
            var srdName = Path.GetFileNameWithoutExtension(inputPath);
            try
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                }

                XtraReport report = new XtraReport();
                report.LoadLayoutFromXml(inputPath);

                var reportId = FileNameToGuid(srdName).ToString();
                string reportDisplayName = FileNameToDisplayName(srdName);
                if (isSubreport)
                {
                    report.Name = reportDisplayName = $"Subreport: {reportDisplayName}";
                }
                report.Name = reportDisplayName;



                List<string> subreportIds = new();
                var subReports = report.AllControls<XRSubreport>().ToArray();
                foreach (var subReport in subReports)
                {
                    if (subReport.ReportSource == null)
                    {
                        throw new InvalidOperationException($"The subreport '{subReport.Name}' configuration on master '{report.Name}' is not supported");
                    }
                    var name = subReport.ReportSource.Name;
                    if (string.IsNullOrEmpty(name))
                    {
                        throw new InvalidOperationException($"The subreport '{subReport.Name}' configuration on master '{report.Name}' is not supported, it is missing the Name attribute");
                    }
                    subReport.ReportSourceUrl = FileNameToGuid(name).ToString();
                    subreportIds.Add(subReport.ReportSourceUrl);
                }

                MemoryStream str = new MemoryStream();
                report.SaveLayoutToXml(str);
                str.Position = 0;
                var sr = new StreamReader(str);
                var layout = sr.ReadToEnd();

                var json = new
                {
                    Identifier = new
                    {
                        Id = reportId,
                        ResourceType = "ReportConfiguration"
                    },
                    DisplayName = reportDisplayName,
                    Description = reportDisplayName,
                    Category,
                    Icon = "fa fa-print",
                    Data = new
                    {
                        IsPinned = !isSubreport,
                        DisplayName = reportDisplayName,
                        Description = reportDisplayName,
                        Layout = layout.Replace(Environment.NewLine, "\r\n").Replace("\"", "\\\""),
                        QueryConfiguration_LobstaQueryConfigurationId = (string?)null,
                        PermissionTypes = "[]",
                        Category,
                        VariationId = (string?)null,
                    },
                    Filter = new
                    {
                        UserNames = (string?)null,
                        Roles = (string?)null,
                        Subscriptions = (string?)null,
                        PermissionTypes = (string?)null
                    },
                    Dependencies = subreportIds.Select(sId => new
                    {

                        Id = sId,
                        ResourceType = "ReportConfiguration"
                    }).ToArray(),
                    Version = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz"),
                    ParameterRequirements = new string[0],
                    IsPinned = !isSubreport,
                    CanPin = true
                };

                File.WriteAllText(outputPath, JsonConvert.SerializeObject(json, Newtonsoft.Json.Formatting.Indented));

                Console.Write(" - Done\n");
            }
            catch (Exception e)
            {
                Console.WriteLine($" - Failed\n\t{e.Message}");
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
        }




        private Dictionary<string, Guid> _fileNameToGuidCache = new();
        internal Guid FileNameToGuid(string name)
        {
            name = name.ToLowerInvariant().Trim();
            if (_fileNameToGuidCache.TryGetValue(name, out var guid))
            {
                return guid;
            }
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(name));
                _fileNameToGuidCache[name] = new Guid(hash);
                return new Guid(hash);
            }
        }

        private Dictionary<string, string> _commonNameAbbreviations = new()
        {
            {"stmt", "Statement"}, 
            {"pop", "Percent of Proceeds"},
            {"wlhd", "Wellhead"},
            {"ga", "Gas Analysis"},
            {"pmnt", "Payment"},
            {"plt", "Plant"},
            {"no_plt", "PVR"},
            {"k", "Contract"},
            {"stndrd", "Standard"},
            {"smmry", "Summary"},
            {"smry", "Summary"},
            {"reserv", "Reservation"},
            {"multiprod", "Multiple Product Or Multi Product"},
            {"imb", "Imbalance"},
            {"sat", "Saturation"},
            {"oba", "OBA"},
            {"hub", "Hub"},
            {"shp", "Shipper"},
            {"hdr", "Header"},
            {"dtl", "Detail"},
            {"csh", "Cash Out"},
            {"fxd", "Fixed"},
            {"bilat", "Bilateral"},
            {"trn", "Transport"},
            {"pur", "Purchase"},
            {"sls", "Sales"}
        };
        internal string FileNameToDisplayName(string name)
        {
            name = name.ToLowerInvariant().Trim();
            if (name.StartsWith("d_"))
            {
                name = name.Substring(2);
            }
            var parts = name.Split('_');
            var sb = new StringBuilder();
            foreach (var part in parts)
            {
                if (_commonNameAbbreviations.TryGetValue(part, out var abbreviation))
                {
                    sb.Append(abbreviation);
                }
                else
                {
                    sb.Append(char.ToUpper(part[0]));
                    sb.Append(part.Substring(1));
                }
                sb.Append(' ');
            }
            return sb.ToString().Trim();
        }
    }
}
