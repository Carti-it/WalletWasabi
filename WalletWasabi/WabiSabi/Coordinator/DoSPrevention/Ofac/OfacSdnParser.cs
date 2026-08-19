using System.IO;
using System.Xml.Linq;

namespace WalletWasabi.WabiSabi.Coordinator.DoSPrevention.Ofac;

/// <summary>
/// Parses a list of sanctioned Bitcoin addresses from the OFAC sanctions list in XML format.
/// </summary>
/// <seealso href="https://sanctionslist.ofac.treas.gov/Home/SdnList"/>
public class OfacSdnParser
{
	private const string BtcFeatureTypeText = "Digital Currency Address - XBT";

	private static readonly XNamespace SdnNamespace = "https://sanctionslistservice.ofac.treas.gov/api/PublicationPreview/exports/ADVANCED_XML";

	public OfacSdnParser()
	{
	}

	/// <summary>
	/// Gets sanctioned BTC addresses.
	/// </summary>
	public async Task<List<string>> GetSanctionedBtcAddressesAsync(Stream xmlStream, CancellationToken cancellationToken)
	{
		var document = await XDocument.LoadAsync(xmlStream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
		var list = ParseBtcAddresses(document);

		return list;
	}

	/// <summary>
	/// Parses the XML document for BTC addresses.
	/// </summary>
	private static List<string> ParseBtcAddresses(XDocument document)
	{
		var root = document.Root ?? throw new InvalidOperationException("SDN XML document has no root element.");

		// Find the FeatureType ID whose text is "Digital Currency Address - XBT". The line looks like this:
		// ```
		// <FeatureType ID="344" FeatureTypeGroupID="1">Digital Currency Address - XBT</FeatureType>
		// ```
		var featureTypeTag = root.Descendants(SdnNamespace + "FeatureType").FirstOrDefault(e => (string?)e == BtcFeatureTypeText) ??
			throw new InvalidOperationException($"Could not find FeatureType '{BtcFeatureTypeText}' in reference value sets.");

		var idAttribute = featureTypeTag.Attribute("ID") ??
			throw new InvalidOperationException($"Could not find ID attribute for FeatureType '{BtcFeatureTypeText}'.");

		// 2. Walk all Feature elements under DistinctParties whose
		//    FeatureTypeID matches, and pull the VersionDetail text (the
		//    actual address value) out of each.
		// Example:
		// ```
		// <Feature ID="31723" FeatureTypeID="344">
		//   <FeatureVersion ID="29462" ReliabilityID="1">
		//     <Comment />
		//     <VersionDetail DetailTypeID="1432">12QtD5BFwRsdNsAZY76UVE1xyCGNTojH9h</VersionDetail>
		//   </FeatureVersion>
		//   <IdentityReference IdentityID="17012" IdentityFeatureLinkTypeID="1" />
		// </Feature>
		// ```
		var btcAddresses = root
			.Descendants(SdnNamespace + "DistinctParties")
			.Descendants()
			.Where(e => e.Attribute("FeatureTypeID")?.Value == idAttribute.Value)
			.SelectMany(feature => feature.Descendants(SdnNamespace + "VersionDetail"))
			.Select(v => v.Value.Trim())
			.Where(v => v.Length > 0)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(v => v, StringComparer.Ordinal)
			.ToList();

		return btcAddresses;
	}
}
