namespace WMS.ViewModels;

public sealed class DemoDataOptionViewModel
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string IconClass { get; set; } = "";
    public string AccentClass { get; set; } = "";
    public IReadOnlyList<string> Highlights { get; set; } = Array.Empty<string>();
}

public sealed class DemoDataPageViewModel
{
    public IReadOnlyList<DemoDataOptionViewModel> Options { get; set; } = Array.Empty<DemoDataOptionViewModel>();
    public int WarehouseCount { get; set; }
    public int ItemCount { get; set; }
    public int VoucherCount { get; set; }
    public int StockLocationCount { get; set; }
    public string? LastAppliedMessage { get; set; }
}
