using System.Xml.Linq;

namespace Unicore.Common.OpenTelemetry.Helpers;

public static class XmlMaskingHelper
{
    public static string MaskSensitiveData(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;

            if (root == null)
                return xml;

            var typeName = root.Name.LocalName;
            var maskedNames = LogMaskedRegistry.GetMaskedMembers(typeName);

            if (maskedNames == null)
                return xml;

            foreach (var elem in root.Descendants())
            {
                if (maskedNames.Contains(elem.Name.LocalName))
                {
                    elem.Value = "****";
                }
            }

            return doc.ToString();
        }
        catch
        {
            return xml; // fallback
        }
    }
}