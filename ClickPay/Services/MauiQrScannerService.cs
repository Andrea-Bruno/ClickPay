#if ANDROID || IOS || MACCATALYST
using ClickPay.Wallet.Core.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace ClickPay.Services
{
    public sealed class MauiQrScannerService : IQrScannerService
    {
        public async Task<QrScanResult> ScanAsync(QrScanRequest request, CancellationToken cancellationToken = default)
        {
            var navigation = GetNavigation();
            if (navigation is null)
            {
                return QrScanResult.Failed("QrScan_Error_NotAvailable");
            }

            if (!await EnsureCameraPermissionAsync().ConfigureAwait(false))
            {
                return QrScanResult.Failed("QrScan_Error_PermissionDenied");
            }

            var completionSource = new TaskCompletionSource<QrScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            NavigationPage? modalHost = null;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var page = new QrScannerPage(request, completionSource, cancellationToken);
                modalHost = new NavigationPage(page)
                {
                    BarBackgroundColor = Colors.Black,
                    BarTextColor = Colors.White
                };

                await navigation.PushModalAsync(modalHost);
            });

            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                if (!completionSource.Task.IsCompleted)
                {
                    completionSource.TrySetResult(QrScanResult.Canceled());
                }
            });

            var result = await completionSource.Task.ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (modalHost is not null && navigation.ModalStack.Contains(modalHost))
                {
                    await navigation.PopModalAsync();
                }
            });

            return result;
        }

        private static async Task<bool> EnsureCameraPermissionAsync()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
                if (status == PermissionStatus.Granted)
                {
                    return true;
                }

                status = await Permissions.RequestAsync<Permissions.Camera>();
                return status == PermissionStatus.Granted;
            }
            catch
            {
                return false;
            }
        }

        private static INavigation? GetNavigation()
        {
            var window = Application.Current?.Windows.FirstOrDefault();
            return window?.Page?.Navigation;
        }

        private sealed class QrScannerPage : ContentPage
        {
            private readonly TaskCompletionSource<QrScanResult> _completionSource;
            private readonly CancellationTokenRegistration _cancellationRegistration;
            private readonly CameraBarcodeReaderView _cameraView;
            private bool _completed;

            public QrScannerPage(QrScanRequest request, TaskCompletionSource<QrScanResult> completionSource, CancellationToken cancellationToken)
            {
                _completionSource = completionSource;

                Title = string.IsNullOrWhiteSpace(request.PromptTitle) ? "Scansione QR" : request.PromptTitle;
                BackgroundColor = Colors.Black;

                _cameraView = new CameraBarcodeReaderView
                {
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill,
                    AutomationId = "QrScannerCameraView",
                    Options = new BarcodeReaderOptions
                    {
                        Formats = BarcodeFormat.QrCode,
                        AutoRotate = true,
                        Multiple = false,
                        TryHarder = true
                    }
                };

                _cameraView.BarcodesDetected += HandleBarcodesDetected;

                var overlay = BuildOverlay(request);

                var layout = new Grid
                {
                    RowDefinitions = new RowDefinitionCollection
                    {
                        new RowDefinition(GridLength.Star),
                        new RowDefinition(GridLength.Auto)
                    },
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new ColumnDefinition(GridLength.Star)
                    }
                };

                layout.Children.Add(_cameraView);
                layout.Children.Add(overlay);

                Content = layout;

                _cancellationRegistration = cancellationToken.Register(() => Complete(QrScanResult.Canceled()));
            }

            protected override void OnAppearing()
            {
                base.OnAppearing();
                _cameraView.IsDetecting = true;
            }

            protected override void OnDisappearing()
            {
                base.OnDisappearing();
                if (!_completed)
                {
                    Complete(QrScanResult.Canceled());
                }
            }

            private View BuildOverlay(QrScanRequest request)
            {
                var promptStack = new VerticalStackLayout
                {
                    Spacing = 12,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.End,
                    Padding = new Thickness(24, 24, 24, 48)
                };

                if (!string.IsNullOrWhiteSpace(request.PromptMessage))
                {
                    promptStack.Add(new Label
                    {
                        Text = request.PromptMessage,
                        TextColor = Colors.White,
                        HorizontalTextAlignment = TextAlignment.Center,
                        FontSize = 18,
                        Margin = new Thickness(0, 0, 0, 12)
                    });
                }

                var cancelButton = new Button
                {
                    Text = string.IsNullOrWhiteSpace(request.CancelLabel) ? "Annulla" : request.CancelLabel,
                    HorizontalOptions = LayoutOptions.Center,
                    WidthRequest = 200,
                    BackgroundColor = Colors.White.WithAlpha(0.15f),
                    TextColor = Colors.White,
                    BorderColor = Colors.White,
                    BorderWidth = 1,
                    CornerRadius = 24,
                    AutomationId = "QrScannerCancelButton"
                };

                cancelButton.Clicked += (_, _) => Complete(QrScanResult.Canceled());
                promptStack.Add(cancelButton);

                return new Grid
                {
                    Background = new LinearGradientBrush
                    {
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromRgba(0, 0, 0, 0.0), 0.0f),
                            new GradientStop(Color.FromRgba(0, 0, 0, 0.0), 0.6f),
                            new GradientStop(Color.FromRgba(0, 0, 0, 0.65), 1.0f)
                        },
                        EndPoint = new Point(0, 1)
                    },
                    Children = { promptStack }
                };
            }

            private void HandleBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
            {
                var payload = e.Results?.FirstOrDefault()?.Value;
                if (string.IsNullOrWhiteSpace(payload))
                {
                    return;
                }

                MainThread.BeginInvokeOnMainThread(() => Complete(QrScanResult.Completed(payload)));
            }

            private void Complete(QrScanResult result)
            {
                if (_completed)
                {
                    return;
                }

                _completed = true;
                _cameraView.IsDetecting = false;
                _cameraView.Handler?.DisconnectHandler();
                _cameraView.BarcodesDetected -= HandleBarcodesDetected;
                _cancellationRegistration.Dispose();
                _completionSource.TrySetResult(result);
            }
        }
    }
}
#endif
