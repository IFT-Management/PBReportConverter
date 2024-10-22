using DevExpress.XtraReports.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace PBReportConverter.Converters
{
    class RepxToJsonConverter(string inputDir, string outputDir, RepxToJsonConverterConfig config)
    {
        private class FileToConvert(string fileName, string path, string category)
        {
            public string FileName { get; } = fileName;
            public string Path { get; } = path;
            public string Category { get; set; } = category;
            public bool Processed { get; set; } = false;

            public override string ToString()
            {
                return $"{(Processed ? "(X)" : "( )")} {FileName}";
            }
        }

        private readonly string _inputDir = inputDir;
        private readonly string _outputDir = outputDir;
        private readonly Dictionary<string, string[]> _mainReports = config.MainReports;



        internal void ConvertToJson(IEnumerable<string> files)
        {
            var allFiles = files.Select(path =>
            {
                var fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                var category = _mainReports.FirstOrDefault(x => x.Value.Contains(fileName)).Key;
                return new FileToConvert(fileName, path, category);
            }).ToArray();

            var mainReports = allFiles.Where(f => f.Category != null).ToList();

            foreach (var mainReport in mainReports)
            {
                var directSubreports = ConvertToJson(mainReport.Path, mainReport.Category, isSubreport: false);
                foreach (var subreport in directSubreports)
                {
                    var subreportFile = allFiles.FirstOrDefault(f => f.FileName == subreport);
                    if (subreportFile != null)
                    {
                        if(string.IsNullOrEmpty(subreportFile.Category))
                            subreportFile.Category = mainReport.Category;
                    }
                    else
                    { 
                        Console.WriteLine($"Report {mainReport.FileName} depends on subreport {subreport} but the .repx file for this subreport wasn't found");
                    }
                }
                mainReport.Processed = true;
            }
            var cnt = 0;
            while (cnt < 25)
            {
                var queue = allFiles.Where(c => !c.Processed && !string.IsNullOrEmpty(c.Category)).ToArray();
                if (queue.Length == 0)
                {
                    break;
                }
                foreach (var current in queue)
                {
                    var directSubreports = ConvertToJson(current.Path, current.Category, isSubreport: true);
                    foreach (var subreport in directSubreports)
                    {
                        var subreportFile = allFiles.FirstOrDefault(f => f.FileName == subreport);
                        if (subreportFile != null)
                        {
                            if (string.IsNullOrEmpty(subreportFile.Category))
                                subreportFile.Category = current.Category;
                        }
                        else
                        {
                            Console.WriteLine($"Report {current.Path} depends on subreport {subreport} but the .repx file for this subreport wasn't found");
                        }
                    }
                    current.Processed = true;
                }
            }

            foreach (var f in allFiles.Where(f => !f.Processed))
            {
                Console.WriteLine($"Report {f.Path} wasn't processed, it isn't a known report or used as a subreport");
            }
        }


        private IEnumerable<string> ConvertToJson(string path, string category, bool isSubreport)
        {
            List<string> subreportNames = new();

            var fileName = Path.GetFileNameWithoutExtension(path);
            Console.Write($"Converting {category} {(isSubreport ? "subreport" : "master report")} {fileName}.repx to .json");

            var outputPath = Path.Combine(_outputDir, $"{fileName}.json");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                XtraReport report = new XtraReport();
                report.LoadLayoutFromXml(path);

                var reportId = FileNameToGuid(fileName).ToString();
                string reportDisplayName = FileNameToDisplayName(fileName);
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
                    var name = string.IsNullOrEmpty(subReport.ReportSource.Name) ? subReport.Name : subReport.ReportSource.Name;
                    if (string.IsNullOrEmpty(name))
                    {
                        throw new InvalidOperationException($"The subreport '{subReport.Name}' configuration on master '{report.Name}' is not supported, it is missing the Name attribute");
                    }
                    subreportNames.Add(name);
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
                    category,
                    Icon = "fa fa-print",
                    Data = new
                    {
                        IsPinned = !isSubreport,
                        DisplayName = reportDisplayName,
                        Description = reportDisplayName,
                        Layout = layout,
                        QueryConfiguration_LobstaQueryConfigurationId = (string?)null,
                        PermissionsTypes = "[]",
                        category,
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
                    }).OrderBy(sId => sId.Id).ToArray(),
                    Version = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz"),
                    ParameterRequirements = new string[0],
                    IsPinned = !isSubreport,
                    CanPin = true
                };


                if (File.Exists(outputPath))
                {
                    var existingJson = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(outputPath));
                    //find the layout
                    var existingLayout = existingJson!["Data"]!["Layout"]!.Value<string>();
                    if (layout == existingLayout)
                    {
                        Console.Write(" - Skipped, layout unchanged \n");
                        return subreportNames;
                    }
                }
                File.WriteAllText(outputPath, JsonConvert.SerializeObject(json, Newtonsoft.Json.Formatting.Indented));

                Console.Write(" - Done\n");
            }
            catch (Exception e)
            {
                Console.Write($" - Failed\n\t{e.Message}");
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
            return subreportNames;
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



    class RepxToJsonConverterConfig
    {
        public Dictionary<string, string[]> MainReports { get; set; }
    }
}
