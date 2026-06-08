namespace InfraWatch.Docs;

/// <summary>
/// The physical-asset reference layer that live monitoring cannot measure — rack position,
/// model, service tag, warranty, purpose. Sourced from the hand-maintained Confluence
/// "Servers, VMs &amp; Rack Inventory" page and folded into the State of the Network so a tech
/// sees health *and* what/where the hardware is (and whether it's past warranty) in one view.
/// Bound from the "Assets" configuration section.
/// </summary>
public sealed class AssetCatalogOptions
{
    /// <summary>Link to the parent "Infrastructure Documentation" Confluence page.</summary>
    public string InfrastructureDocUrl { get; set; } = "";

    /// <summary>Link to the authoritative "Servers, VMs &amp; Rack Inventory" page (full detail:
    /// FC WWPNs, storage WWNs, archived VMs, networking).</summary>
    public string InventoryPageUrl { get; set; } = "";

    /// <summary>Physical server hosts.</summary>
    public List<AssetRecord> Servers { get; set; } = new();

    /// <summary>Storage arrays / SAN.</summary>
    public List<AssetRecord> Storage { get; set; } = new();

    public bool HasData =>
        Servers.Count > 0 || Storage.Count > 0
        || !string.IsNullOrWhiteSpace(InventoryPageUrl) || !string.IsNullOrWhiteSpace(InfrastructureDocUrl);
}

/// <summary>One physical asset (server or storage array).</summary>
public sealed class AssetRecord
{
    public string Name { get; set; } = "";
    public string Fqdn { get; set; } = "";
    public string Model { get; set; } = "";
    public string Os { get; set; } = "";
    public string Rack { get; set; } = "";
    public string ServiceTag { get; set; } = "";

    /// <summary>Warranty expiry, MM/DD/YYYY. Empty = unknown.</summary>
    public string Warranty { get; set; } = "";

    public string Ram { get; set; } = "";

    /// <summary>Usable capacity (storage arrays).</summary>
    public string Capacity { get; set; } = "";

    public string Purpose { get; set; } = "";
}
