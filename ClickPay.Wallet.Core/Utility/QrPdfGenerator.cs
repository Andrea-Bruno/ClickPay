using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.Blockchain.Bitcoin;
using ClickPay.Wallet.Core.Wallet;
using Microsoft.Extensions.Options;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using QRCoder;
using SkiaSharp;
using Svg.Skia;

namespace ClickPay.Wallet.Core.Utility
{
 public static class QrPdfGenerator
 {
 /// <summary>
 /// Generates a 5.5x5.5cm PDF with a title and QR code, returns the file as a byte array.
 /// </summary>
 /// <param name="appName">Application name to show as title</param>
 /// <param name="secureStore">Secure store to retrieve the private key</param>
 /// <param name="baseUrl">Base URL for the QR code (default: https://tc0.it)</param>
 /// <returns>Byte array of the generated PDF</returns>
 public static async Task<byte[]> GeneratePersonalQrPdfAsync(string appName, ClickPay.Wallet.Core.Services.ILocalSecureStore secureStore, string baseUrl = "https://tc0.it")
 {
 // Retrieve the private key
 var vault = await WalletKeyUtility.GetVaultAsync(secureStore);
 if (vault is null)
 {
 throw new InvalidOperationException("Wallet vault not found.");
 }

 // Derive the private key from mnemonic using Bitcoin service
 var bitcoinService = new BitcoinWalletService(Options.Create(BitcoinWalletOptions.Default));
 var account = bitcoinService.DeriveAccount(vault.Mnemonic, vault.Passphrase ?? string.Empty, vault.AccountIndex);
 var privateKeyBytes = Encoding.UTF8.GetBytes(account.AccountXprv);

 // Compute SHA256 hash and take first 6 bytes as hex
 using var sha256 = SHA256.Create();
 var hash = sha256.ComputeHash(privateKeyBytes);
 var hex = BitConverter.ToString(hash.Take(6).ToArray()).Replace("-", "").ToLower();
 var qrUrl = $"{baseUrl}?{hex}";

 // Dimensions in points (1 cm ?28.35 pt)
            const double cmToPt = 28.35;
            double widthPt = 5.5 * cmToPt;
            double heightPt = 5.5 * cmToPt;

 using var document = new PdfDocument();
 var page = document.AddPage();
 page.Width = widthPt;
 page.Height = heightPt;

 using var gfx = XGraphics.FromPdfPage(page);

 // Title at the top, centered, bold
            var titleFont = new XFont("Arial", 14, XFontStyle.Bold);
 var title = appName ?? "App";
 var titleSize = gfx.MeasureString(title, titleFont);
 gfx.DrawString(
 title,
 titleFont,
 XBrushes.Black,
                new XRect(0, 10, widthPt, titleSize.Height),
 XStringFormats.TopCenter);

 // Generate QR code as SVG
 using var qrGenerator = new QRCodeGenerator();
 using var qrData = qrGenerator.CreateQrCode(qrUrl, QRCodeGenerator.ECCLevel.Q);
 var svgQr = new SvgQRCode(qrData);
 string svg = svgQr.GetGraphic(8, "#000000", "#ffffff", drawQuietZones: false);

 // Rasterize SVG to PNG using Svg.Skia
 using var svgStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svg));
 var skSvg = new SKSvg();
 skSvg.Load(svgStream);
            if (skSvg.Picture == null)
 {
                throw new InvalidOperationException("Failed to load SVG picture for QR code");
 }

            int qrPixelSize = 512; // High resolution for quality
            using var bitmap = new SKBitmap(qrPixelSize, qrPixelSize);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.White);
                float scale = Math.Min(qrPixelSize / skSvg.Picture!.CullRect.Width, qrPixelSize / skSvg.Picture!.CullRect.Height);
                canvas.Scale(scale);
                canvas.DrawPicture(skSvg.Picture);
            }
 using var image = SKImage.FromBitmap(bitmap);
 using var pngStream = new MemoryStream();
            image.Encode(SKEncodedImageFormat.Png, 100).SaveTo(pngStream);
            pngStream.Position = 0;
 var qrImage = XImage.FromStream(() => pngStream);

 // Calculate QR position (centered below the title)
            double qrSize = widthPt * 0.7; //70% of width
            double qrX = (widthPt - qrSize) / 2;
            double qrY = titleSize.Height + 20;

 gfx.DrawImage(qrImage, qrX, qrY, qrSize, qrSize);

 // Export PDF to memory
 using var outStream = new MemoryStream();
 document.Save(outStream, false);
 return outStream.ToArray();
 }
 }
}

