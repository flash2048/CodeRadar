using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace CodeRadar.Views
{
    public partial class ImageViewerWindow : Window
    {
        private readonly byte[] _bytes;
        private readonly string _format;
        private readonly string _caption;

        public ImageViewerWindow(byte[] imageBytes, string format, string caption)
        {
            _bytes = imageBytes ?? throw new ArgumentNullException(nameof(imageBytes));
            _format = string.IsNullOrWhiteSpace(format) ? "IMG" : format.ToUpperInvariant();
            _caption = caption ?? string.Empty;

            InitializeComponent();
            Title = string.IsNullOrWhiteSpace(caption)
                ? "Image Viewer"
                : "Image Viewer - " + caption;

            LoadImage();
        }

        private void LoadImage()
        {
            try
            {
                var bmp = new BitmapImage();
                using (var ms = new MemoryStream(_bytes))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                }
                ImageControl.Source = bmp;
                FormatBadge.Text = _format;
                InfoText.Text = $"{bmp.PixelWidth} x {bmp.PixelHeight}   -   {FormatBytes(_bytes.Length)}";
            }
            catch (Exception ex)
            {
                FormatBadge.Text = "ERR";
                InfoText.Text = "Failed to decode: " + ex.Message;
            }
        }

        private string DefaultExtension()
        {
            switch (_format)
            {
                case "JPEG": return "jpg";
                case "WEBP": return "webp";
                case "TIFF": return "tiff";
                case "ICO":  return "ico";
                default:     return _format.ToLowerInvariant();
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var ext = DefaultExtension();
            var dlg = new SaveFileDialog
            {
                Title = "Save image",
                Filter = $"{_format} image (*.{ext})|*.{ext}|All files (*.*)|*.*",
                DefaultExt = ext,
                FileName = SuggestedFileName(ext),
                OverwritePrompt = true,
                AddExtension = true
            };
            if (dlg.ShowDialog(this) == true)
            {
                try
                {
                    File.WriteAllBytes(dlg.FileName, _bytes);
                    StatusText.Text = "Saved to " + dlg.FileName;
                }
                catch (Exception ex)
                {
                    StatusText.Text = "Save failed: " + ex.Message;
                }
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ImageControl.Source is BitmapSource src)
                {
                    Clipboard.SetImage(src);
                    StatusText.Text = "Copied to clipboard.";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Copy failed: " + ex.Message;
            }
        }

        private string SuggestedFileName(string ext)
        {
            var name = (_caption ?? "image").Trim();
            var safe = new System.Text.StringBuilder(name.Length);
            foreach (var c in name)
                safe.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            if (safe.Length == 0) safe.Append("image");
            safe.Append('.').Append(ext);
            return safe.ToString();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("0.00") + " MB";
        }
    }
}
