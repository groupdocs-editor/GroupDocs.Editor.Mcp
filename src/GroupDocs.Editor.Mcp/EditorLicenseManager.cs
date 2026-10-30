using GroupDocs.Mcp.Core;
using GroupDocs.Mcp.Core.Licensing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GroupDocs.Editor.Mcp;

public class EditorLicenseManager : LicenseManager
{
    public EditorLicenseManager(IOptions<McpConfig> config, ILogger<LicenseManager> logger)
        : base(config, logger)
    {
    }

    // Identifies the engine in get_license_status. Without it the tool would report the
    // server's own version, because this class lives in the server assembly.
    protected override Type? EngineMarkerType => typeof(GroupDocs.Editor.License);

    protected override void SetLicenseFromPath(string licensePath)
    {
        new GroupDocs.Editor.License().SetLicense(licensePath);
    }

    protected override void SetMeteredKeyCore(string publicKey, string privateKey)
    {
        new GroupDocs.Editor.Metered().SetMeteredKey(publicKey, privateKey);
    }

    protected override MeteredConsumption ReadConsumptionCore()
    {
        // Static on the engine and only meaningful once a metered key is applied - Core
        // guarantees this runs in metered mode only.
        return new MeteredConsumption
        {
            Quantity = GroupDocs.Editor.Metered.GetConsumptionQuantity(),
            Credit = GroupDocs.Editor.Metered.GetConsumptionCredit()
        };
    }
}
