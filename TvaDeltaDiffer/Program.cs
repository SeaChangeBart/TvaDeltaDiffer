using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace TvaDeltaDiffer
{
    class Program
    {
        public class Dated<T>
        {
            public T Value { get; set; }
            public DateTime DateTime { get; set; }
        }

        private static readonly XNamespace NsTva2010 = "urn:tva:metadata:2010";

        private static readonly Dictionary<string, Dated<Dictionary<string, Dated<XElement>>>> ParsedFilesDictionary = new Dictionary<string, Dated<Dictionary<string, Dated<XElement>>>>();

        public static Dated<Dictionary<string, Dated<XElement>>> ReadTvaToFragmentDictionary(string fn)
        {
            Dated<Dictionary<string, Dated<XElement>>> retVal;
            if (ParsedFilesDictionary.TryGetValue(fn, out retVal)) return retVal;
            var docdt = new FileInfo(fn).LastWriteTimeUtc;
            retVal =
                new Dated<Dictionary<string, Dated<XElement>>> 
                {
                    DateTime = docdt,
                    Value =
                        XDocument.Load(fn)
                            .Descendants()
                            .Where(IsFragment)
                            .ToDictionary(GetFragmentId,
                                el => new Dated<XElement> {Value = el, DateTime = docdt})
                };
            Console.WriteLine("Read {0}", Path.GetFileName(fn));
            return ParsedFilesDictionary[fn] = retVal;
        }

        private static string GetFragmentId(XElement el)
        {
            if (el.Attributes("fragmentId").Any())
                return el.Attribute("fragmentId").Value;
            return GetIdentity(el);
        }

        private static string GetIdentity(XElement el)
        {
            try
            {
                if (el.Name.LocalName.Equals("ProgramInformation"))
                    return el.Attribute("programId").Value;
                if (el.Name.LocalName.Equals("GroupInformation"))
                    return el.Attribute("groupId").Value;
                if (el.Name.LocalName.Equals("BroadcastEvent"))
                    return el.Elements(NsTva2010 + "InstanceMetadataId").Single().Value;
                if (el.Name.LocalName.Equals("OnDemandProgram"))
                    return el.Elements(NsTva2010 + "InstanceMetadataId").Single().Value;
                if (el.Name.LocalName.Equals("GroupInformation"))
                    return el.Attribute("groupId").Value;
                if (el.Name.LocalName.Equals("ServiceInformation"))
                    return el.Attribute("serviceId").Value;
                if (el.Name.LocalName.Equals("Schedule"))
                    return el.Attribute("serviceId").Value;
                if (el.Attributes("fragmentId").Any())
                    return el.Attribute("fragmentId").Value;
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsFragment(XElement el)
        {
            return GetFragmentId(el) != null;
        }

        private static Dated<XElement> FindFragmentInDictionaries(string fragmentId,
            IEnumerable<Dictionary<string, Dated<XElement>>> datedDics)
        {
            return
                (datedDics
                    .Where(dic => dic.ContainsKey(fragmentId))
                    .Select(dic => dic[fragmentId])).FirstOrDefault();
        }

        static void Main(string[] args)
        {
            var tvaDir = args.FirstOrDefault() ?? @"C:\d\temp\Com Hem\AtomDebug";
            var pattern = args.Skip(1).FirstOrDefault() ?? "????????T??????Z_*.xml";
            var tvaFiles = Directory.EnumerateFiles(tvaDir, pattern).OrderByDescending(_ => _);
            var dictionaries = tvaFiles.Select(ReadTvaToFragmentDictionary);
            foreach (var delta in tvaFiles)
            {
                var deltaDic = ReadTvaToFragmentDictionary(delta);
                var refTime = deltaDic.DateTime;
                var olderDics = dictionaries.Where(d => d.DateTime < refTime).Select(dd => dd.Value);
                if (!olderDics.Any())
                    continue;

                var logfn = Path.ChangeExtension(delta, ".log");
                File.WriteAllLines(logfn, new[]{"Analysis for " + delta});
                foreach (var fragPair in deltaDic.Value)
                {
                    var currentElement = fragPair.Value;
                    var previousElement = FindFragmentInDictionaries(fragPair.Key, olderDics);
                    var lines = DumpDiff(currentElement, previousElement).Concat(new[]{""});
                    File.AppendAllLines(logfn, lines);
                }
            }
        }

        private static IEnumerable<string> DumpDiff(Dated<XElement> currentElement, Dated<XElement> previousElement)
        {
            yield return DumpElementHeader(currentElement.Value);
            if (previousElement == null || IsExpired(previousElement))
            {
                yield return "Created in this diff";
            }
            else if (IsExpired(currentElement))
            {
                yield return "Deleted in this diff";
            }
            else
            {
                var diffs =
                    XmlSpecificationCompare.XmlSpecificationEquality.AreEqual(currentElement.Value,
                        previousElement.Value).ToArray();
                if (!diffs.Any())
                {
                    yield return "Identical";
                }

                foreach (
                    var l in
                        diffs.Select(
                            diff =>
                                string.Format("{0}: {1}",
                                    AsXpathish(diff.FailObject, currentElement.Value, previousElement.Value),
                                    diff.ErrorMessage)))
                    yield
                        return l;
            }
        }

        private static bool IsExpired(Dated<XElement> currentElement)
        {
            var expirationTimes = currentElement.Value.Attributes("fragmentExpirationDate")
                .Select(a => DateTime.Parse(a.Value, null, DateTimeStyles.RoundtripKind)).Take(1).ToArray();
            if (!expirationTimes.Any())
                return false;
            return expirationTimes[0] <= currentElement.DateTime;
        }

        private static string XpathishName(XObject xobject)
        {
            switch (xobject.NodeType)
            {
                case XmlNodeType.Attribute:
                    return "@"+((XAttribute) xobject).Name.LocalName;
                case XmlNodeType.Element:
                    return @"\"+((XElement) xobject).Name.LocalName;
                default:
                    return xobject.ToString();
            }
        }

        private static string AsXpathish(XObject xobject, XElement rootA, XElement rootB)
        {
            return AsXpathish(xobject, rootA) ?? AsXpathish(xobject, rootB) ?? xobject.ToString();
        }

        private static string AsXpathish(XObject xobject, XElement root)
        {
            string retVal = String.Empty;
            var iter = xobject;
            while (iter != root)
            {
                retVal = XpathishName(iter) + retVal;
                iter = iter.Parent;
                if (iter == null)
                    return null;
            }
            return retVal;
        }

        private static string DumpElementHeader(XElement currentElement)
        {
            var type = currentElement.Name.LocalName;
            var id = GetIdentity(currentElement);
            return string.Format("<{0} ..id=\"{1}\">", type, id);
            var attributes = currentElement.Attributes().Where(att => !att.Name.LocalName.StartsWith("fragment")).Select(att => string.Format("{0}='{1}'", att.Name.LocalName, att.Value));
            var all = new[] {type}.Concat(attributes);
            return string.Format("<{0}>", string.Join(" ", all));
        }
    }
}
