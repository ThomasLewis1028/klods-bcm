using System.ComponentModel;
using System.Runtime.CompilerServices;
using Klods.Mobile.Services;

namespace Klods.Mobile.Pages;

public partial class BomPage : ContentPage
{
    private readonly string _setId;
    private readonly int _setIndex;
    private readonly ApiClient _api;

    public BomPage(string setId, int setIndex, string setName, ApiClient api)
    {
        InitializeComponent();
        _setId = setId;
        _setIndex = setIndex;
        _api = api;
        Title = $"{setName} — Copy {setIndex}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync(firstLoad: true);
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadAsync(firstLoad: false);
        Refresher.IsRefreshing = false;
    }

    private async void OnRetryClicked(object? sender, EventArgs e) =>
        await LoadAsync(firstLoad: true);

    private async Task LoadAsync(bool firstLoad)
    {
        if (firstLoad)
        {
            Loader.IsVisible = true;
            ErrorView.IsVisible = false;
            Refresher.IsVisible = false;
        }

        var bom = await _api.GetBomAsync(_setId, _setIndex);
        Loader.IsVisible = false;

        if (bom is null)
        {
            ErrorLabel.Text = "Could not load bricks.\nCheck your connection and try again.";
            ErrorView.IsVisible = true;
            Refresher.IsVisible = false;
            return;
        }

        ErrorView.IsVisible = false;
        Refresher.IsVisible = true;

        BrickList.ItemsSource = bom.Bricks
            .OrderBy(b => b.SetStock >= b.Count)   // incomplete first
            .ThenBy(b => b.Name)
            .Select(b => new BrickItem
            {
                PartNum    = b.PartNum,
                ColorId    = b.ColorId,
                Name       = b.Name,
                PartImgUrl = _api.ResolveImageUrl(b.PartImg),
                ColorName  = b.ColorName ?? b.ColorId,
                HexColor   = b.HexColor,
                Count      = b.Count,
                SpareCount = b.SpareCount,
                LooseStock = b.LooseStock,
                BricklinkId = b.BricklinkId,
                SetStock   = b.SetStock,
            })
            .ToList();
    }

    private void OnPlusClicked(object? sender, EventArgs e)
    {
        if (sender is not VisualElement { BindingContext: BrickItem item }) return;
        if (item.SetStock >= item.Count) return;
        item.SetStock++;
        _ = _api.UpdateSetBrickStockAsync(_setId, _setIndex, item.PartNum, item.ColorId, item.SetStock);
    }

    private void OnMinusClicked(object? sender, EventArgs e)
    {
        if (sender is not VisualElement { BindingContext: BrickItem item }) return;
        if (item.SetStock <= 0) return;
        item.SetStock--;
        _ = _api.UpdateSetBrickStockAsync(_setId, _setIndex, item.PartNum, item.ColorId, item.SetStock);
    }

    private async void OnBrickTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not VisualElement { BindingContext: BrickItem item }) return;
        var info = new BrickDetailPage.BrickInfo(
            PartNum:     item.PartNum,
            ColorId:     item.ColorId,
            Name:        item.Name,
            PartImgUrl:  item.PartImgUrl,
            ColorName:   item.ColorName,
            HexColor:    item.HexColor,
            Count:       item.Count,
            SpareCount:  item.SpareCount,
            SetStock:    item.SetStock,
            LooseStock:  item.LooseStock,
            BricklinkId: item.BricklinkId);
        await Navigation.PushAsync(new BrickDetailPage(info, _api));
    }

    private sealed class BrickItem : INotifyPropertyChanged
    {
        public required string PartNum    { get; init; }
        public required string ColorId   { get; init; }
        public required string Name      { get; init; }
        public string? PartImgUrl        { get; init; }
        public string? ColorName         { get; init; }
        public string? HexColor          { get; init; }
        public int Count                 { get; init; }
        public int SpareCount            { get; init; }
        public int LooseStock            { get; init; }
        public string? BricklinkId       { get; init; }

        private int _setStock;
        public int SetStock
        {
            get => _setStock;
            set
            {
                if (_setStock == value) return;
                _setStock = value;
                Notify();
                Notify(nameof(StockLabel));
                Notify(nameof(IsComplete));
            }
        }

        public string StockLabel  => $"{SetStock}/{Count}";
        public bool   IsComplete  => SetStock >= Count;
        public string LooseLabel  => $"🔓 {LooseStock} loose";

        public SolidColorBrush SwatchBrush => new(
            HexColor is { Length: > 0 } h
                ? Color.FromArgb(h.StartsWith('#') ? h : "#" + h)
                : Colors.Gray);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? n = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
