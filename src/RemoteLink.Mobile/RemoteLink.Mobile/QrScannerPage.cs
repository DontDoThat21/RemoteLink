using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace RemoteLink.Mobile;

/// <summary>
/// Modal page that shows a camera barcode reader.
/// Fires <see cref="QrCodeScanned"/> with the decoded text on first successful read.
/// </summary>
public class QrScannerPage : ContentPage
{
    public event EventHandler<string>? QrCodeScanned;

    private readonly CameraBarcodeReaderView _barcodeReader;
    private bool _scanned;

    public QrScannerPage()
    {
        Title = "Scan QR Code";
        BackgroundColor = Colors.Black;

        _barcodeReader = new CameraBarcodeReaderView
        {
            Options = new BarcodeReaderOptions
            {
                Formats = BarcodeFormat.QrCode,
                AutoRotate = true,
                Multiple = false
            },
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        _barcodeReader.BarcodesDetected += OnBarcodesDetected;

        var cancelButton = new Button
        {
            Text = "Cancel",
            FontSize = 16,
            BackgroundColor = Color.FromArgb("#C62828"),
            TextColor = Colors.White,
            CornerRadius = 8,
            HeightRequest = 48,
            Margin = new Thickness(24, 0, 24, 24),
            VerticalOptions = LayoutOptions.End
        };
        cancelButton.Clicked += async (_, _) => await Navigation.PopModalAsync();

        var instructionLabel = new Label
        {
            Text = "Point camera at the QR code on the desktop host",
            FontSize = 14,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(24, 48, 24, 12),
            VerticalOptions = LayoutOptions.Start
        };

        Content = new Grid
        {
            Children =
            {
                _barcodeReader,
                instructionLabel,
                cancelButton
            }
        };
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (_scanned) return;

        var result = e.Results?.FirstOrDefault();
        if (result == null || string.IsNullOrWhiteSpace(result.Value))
            return;

        _scanned = true;
        QrCodeScanned?.Invoke(this, result.Value);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _barcodeReader.BarcodesDetected -= OnBarcodesDetected;
    }
}
