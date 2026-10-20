using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditorInternal;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>
    /// Writes where the last player build's bytes went, per source asset, to Logs/build-size.tsv
    /// plus a by-type/by-folder summary in Logs/build-size-summary.txt.
    ///
    /// <para>Reads Library/LastBuild.buildreport, so it needs no new build. The Editor.log
    /// "Build Report" lists only the top assets; this lists every one, which is what a size pass
    /// needs to see the long tail of icons and sprites.</para>
    ///
    /// Menu: Tools → Push Stars → Dump Last Build Size. Batch: -executeMethod
    /// PushStars.Editor.BuildSizeReport.DumpLast
    /// </summary>
    public static class BuildSizeReport
    {
        private const string ReportPath = "Library/LastBuild.buildreport";

        [MenuItem("Tools/Push Stars/Dump Last Build Size", priority = 206)]
        public static void DumpLast()
        {
            var report = InternalEditorUtility.LoadSerializedFileAndForget(ReportPath)
                .OfType<BuildReport>().FirstOrDefault();
            if (report == null)
            {
                Debug.LogError("[BuildSizeReport] No BuildReport in " + ReportPath);
                return;
            }

            var bySource = new Dictionary<string, (ulong size, string type)>();
            foreach (var packed in report.packedAssets)
            foreach (var info in packed.contents)
            {
                string path = string.IsNullOrEmpty(info.sourceAssetPath) ? "(built-in)" : info.sourceAssetPath;
                bySource.TryGetValue(path, out var prev);
                string type = info.type != null ? info.type.Name : "?";
                bySource[path] = (prev.size + info.packedSize, prev.type ?? type);
            }

            Directory.CreateDirectory("Logs");
            var rows = bySource.OrderByDescending(kv => kv.Value.size).ToList();
            File.WriteAllLines("Logs/build-size.tsv",
                rows.Select(kv => $"{kv.Value.size}\t{kv.Value.type}\t{kv.Key}"));

            var sb = new StringBuilder();
            sb.AppendLine($"{report.summary.platform} {report.summary.outputPath} total {Mb(report.summary.totalSize)} MB");
            sb.AppendLine("-- by type");
            foreach (var g in rows.GroupBy(kv => kv.Value.type).OrderByDescending(g => Sum(g)))
                sb.AppendLine($"{Mb(Sum(g)),8} MB  {g.Count(),5}  {g.Key}");
            sb.AppendLine("-- by folder");
            foreach (var g in rows.GroupBy(kv => Folder(kv.Key)).OrderByDescending(g => Sum(g)).Take(40))
                sb.AppendLine($"{Mb(Sum(g)),8} MB  {g.Count(),5}  {g.Key}");
            sb.AppendLine("-- files");
            foreach (var file in report.GetFiles().OrderByDescending(f => f.size).Take(40))
                sb.AppendLine($"{Mb(file.size),8} MB  {file.role}  {file.path}");
            File.WriteAllText("Logs/build-size-summary.txt", sb.ToString());
            Debug.Log("[BuildSizeReport] " + rows.Count + " assets → Logs/build-size.tsv\n" + sb);
        }

        private static ulong Sum(IEnumerable<KeyValuePair<string, (ulong size, string type)>> rows)
        {
            ulong total = 0;
            foreach (var kv in rows) total += kv.Value.size;
            return total;
        }

        private static string Folder(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash > 0 ? path.Substring(0, slash) : path;
        }

        private static string Mb(ulong bytes) => (bytes / 1048576.0).ToString("0.0");
    }
}
