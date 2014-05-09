//Eli Algranti Copyright �  2013
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace TvaDeltaDiffer.XmlSpecificationCompare
{
    /// <summary>
    /// Loosely compares XML documents for equality:
    /// <list type="bullet">
    /// <item>Order of siblings in an element is ignored.</item>
    /// <item>Text nodes are the only node in at the bottom of the tree so sibling text nodes are merged for comparison.</item>
    /// <item>The prefix used for a namespace is ignored.</item>
    /// <item>Comments are ignored.</item>
    /// </list>
    /// This type of comparison is useful when comparing the XML documents used as messages, configuration, etc. in various specifications.
    /// </summary>
    public class XmlSpecificationEquality
    {
        public static IEnumerable<InternalResult> AreEqual(string xmlA, string xmlB)
        {
            return AreEqual(ParseXml(xmlA).Root, ParseXml(xmlB).Root);
        }


        public static IEnumerable<InternalResult> AreEqual(XElement xmlA, XElement xmlB)
        {
            if (xmlA == null)
                throw new ArgumentNullException("xmlA", "The input Xml cannot be null");

            if (xmlB == null)
                throw new ArgumentNullException("xmlB", "The input Xml cannot be null");

            if (!xmlA.Name.Equals(xmlB.Name))
            {
                yield return new InternalResult
                {
                    ErrorMessage = "Root elements do not match.",
                    FailObject = xmlA
                };
            }

            var results = AreObjectsEqual(xmlA, xmlB)/*.Select(result => new XmlEqualityResult
            {
                ErrorMessage = result.ErrorMessage,
                FailObject = result.FailObject
            })*/;
            foreach (var r in results)
                yield return r;
        }

        private static IEnumerable<InternalResult> AreObjectsEqual(XElement xmlA, XElement xmlB)
        {
            return AreAttributesEqual(xmlA, xmlB).Concat(AreChildrenEqual(xmlA, xmlB)).Concat(AreLeafEqual(xmlA, xmlB));
        }

        private static IEnumerable<InternalResult> AreAttributesEqual(XElement xmlA, XElement xmlB)
        {
            var attributesB = xmlB.Attributes().Where(a => !a.IsNamespaceDeclaration).ToDictionary(a => a.Name);

            var attributesA = xmlA.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();

            if (attributesA.Count != attributesB.Count)
            {
                yield return new InternalResult
                {
                    FailObject = xmlA,
                    ErrorMessage = "Element has different number of attributes"
                };
            }

            foreach (var attributeA in attributesA)
            {
                XAttribute attributeB;
                if (!attributesB.TryGetValue(attributeA.Name, out attributeB))
                {
                    yield return new InternalResult
                    {
                        FailObject = attributeA,
                        ErrorMessage = "No matching attribute found."
                    };
                    continue;
                }
                if (attributeA.Value == attributeB.Value)
                    continue;

                yield return new InternalResult
                {
                    FailObject = attributeA,
                    ErrorMessage =
                        string.Format("Value changed from '{0}' to '{1}'", attributeB.Value, attributeA.Value)
                };
            }
        }

        private static IEnumerable<InternalResult> AreLeafEqual(XElement xmlA, XElement xmlB)
        {
            var valueA = GetElementValue(xmlA);
            if (valueA != GetElementValue(xmlB))
                yield return new InternalResult
                {
                    FailObject = string.IsNullOrEmpty(valueA)
                        ? xmlA
                        : xmlA.Nodes().First(n => n is XText)
                };
        }

        private static string GetElementValue(XElement element)
        {
            return string.Join("",
                               element.Nodes()
                                   .Where(n => n is XText)
                                   .Cast<XText>()
                                   .Select(t => t is XCData ? t.Value : t.Value.Trim()));
        }

        private static IEnumerable<InternalResult> AreChildrenEqual(XElement xmlA, XElement xmlB)
        {
            var elementsBpool = xmlB.Elements().ToList();
            foreach (var childA in xmlA.Elements())
            {
                var candidates = xmlB.Elements(childA.Name).Intersect(elementsBpool).ToArray();
                if (!candidates.Any())
                {
                    yield return new InternalResult
                    {
                        FailObject = childA,
                        ErrorMessage = "Doesn't exist in older"
                    };
                }
                else
                {
                    var a = childA;
                    var bestCandidates =
                        candidates.Select(e => new {B = e, Diffs = AreEqual(a, e).ToArray()})
                            .OrderBy(dif => dif.Diffs.Length)
                            .GroupBy(d => d.Diffs.Length).First();
                    var byLength = bestCandidates.OrderBy(p => string.Concat(p.Diffs.Select(d => d.ErrorMessage)).Length);
                    var best = byLength.First();
                    foreach (var dd in best.Diffs)
                        yield return dd;
                    elementsBpool.Remove(best.B);
                }
            }

            foreach (var leftBs in elementsBpool)
            {
                yield return new InternalResult
                {
                    FailObject = leftBs,
                    ErrorMessage = "Doesn't exist in newer"
                };
            }
        }

        public static XDocument ParseXml(string xml)
        {
            try
            {
                return XDocument.Parse(xml);
            }
            catch (Exception e)
            {
                throw new ArgumentException("The string provided is not a valid XML Document.", e);
            }
        }

        public struct InternalResult
        {
            public string ErrorMessage;
            public XObject FailObject;
        }

    }
}
