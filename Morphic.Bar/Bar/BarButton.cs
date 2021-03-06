// BarButton.cs: Button widget on the bar
//
// Copyright 2020 Raising the Floor - International
//
// Licensed under the New BSD license. You may not use this file except in
// compliance with this License.
//
// You may obtain a copy of the License at
// https://github.com/GPII/universal/blob/master/LICENSE.txt

namespace Morphic.Bar.Bar
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Runtime.CompilerServices;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using System.Xml;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using SharpVectors.Converters;
    using SharpVectors.Dom.Svg;
    using SharpVectors.Renderers.Wpf;
    using UI.BarControls;

    /// <summary>
    /// Button bar item.
    /// </summary>
    [JsonTypeName("button")]
    [BarControl(typeof(ButtonBarControl))]
    public class BarButton : BarItem, INotifyPropertyChanged
    {
        private string? imagePath;
        private string? imageValue;
        private ImageSource? imageSource;
        private Uri? remoteImage;

        /// <summary>
        /// The original image, as defined in json.
        /// </summary>
        [JsonProperty("configuration.image_url")]
        public string? ImageValue
        {
            get => this.imageValue;
            set
            {
                this.imageValue = value ?? string.Empty;

                Uri.TryCreate(this.imageValue, UriKind.Absolute, out Uri? uri);

                if (uri != null && !uri.IsFile)
                {
                    // Download later.
                    this.RemoteImage = new Uri(this.imageValue);
                }
                else if (string.IsNullOrEmpty(this.imageValue))
                {
                    this.ImagePath = string.Empty;
                }
                else
                {
                    // Check if it points to a file within the assets directory.
                    string safe = new Regex(@"\.\.|[^-a-zA-Z0-9./]+", RegexOptions.Compiled)
                        .Replace(this.imageValue, "_")
                        .Trim('/')
                        .Replace('/', Path.DirectorySeparatorChar);
                    string assetFile = AppPaths.GetAssetFile("bar-icons\\" + safe);
                    string[] extensions = {"", ".svg", ".png", ".ico", ".jpg", ".jpeg", ".gif"};

                    string? foundFile = extensions.Select(extension => assetFile + extension)
                        .FirstOrDefault(File.Exists);

                    if (foundFile == null)
                    {
                        this.Logger.LogInformation("No local image for '{imageValue}'", this.imageValue);
                        this.ImagePath = string.Empty;
                    }
                    else
                    {
                        this.ImagePath = foundFile;
                    }
                }
            }
        }

        /// <summary>
        /// The image to use.
        /// </summary>
        public ImageSource? ImageSource
        {
            get => this.imageSource;
            set
            {
                this.imageSource = value;
                this.OnPropertyChanged();
            }
        }

        /// <summary>
        /// The real local path of the item's image.
        /// </summary>
        public string ImagePath
        {
            get => this.imagePath ?? string.Empty;
            private set => this.imagePath = value;
        }

        // Limit the concurrent downloads.
        private static SemaphoreSlim downloads = new SemaphoreSlim(8);
        private static HashSet<string> downloaded = new HashSet<string>();

        /// <summary>
        /// Loads the image specified by ImagePath.
        /// </summary>
        /// <returns>true on success.</returns>
        public async Task<bool> LoadImage()
        {
            bool success = false;

            // Download the remote image.
            if (this.DownloadRequired && this.RemoteImage != null)
            {
                using WebClient wc = new WebClient();
                string tempFile = this.ImagePath + ".new";
                try
                {
                    try
                    {
                        await downloads.WaitAsync();
                        bool done = false;
                        lock (downloaded)
                        {
                            if (downloaded.Contains(this.ImagePath))
                            {
                                done = true;
                            }
                            else
                            {
                                downloaded.Add(this.ImagePath);
                            }
                        }

                        if (!done)
                        {
                            this.Logger.LogDebug("Downloading {remoteImage}", this.RemoteImage);
                            await wc.DownloadFileTaskAsync(this.RemoteImage, tempFile);
                        }
                    }
                    finally
                    {
                        downloads.Release();
                    }
                    FileInfo fileInfo = new FileInfo(tempFile);

                    if (fileInfo.Exists && fileInfo.Length > 0)
                    {
                        File.Move(tempFile, this.ImagePath, true);
                    }
                }
                catch (Exception e) when (!(e is OutOfMemoryException))
                {
                    // Ignore
                    this.Logger.LogWarning(e, "Download failed {remoteImage}", this.RemoteImage);
                }
                finally
                {
                    File.Delete(tempFile);
                }
            }

            // Load the local image.
            if (!string.IsNullOrEmpty(this.ImagePath) && File.Exists(this.ImagePath))
            {
                try
                {
                    if (Path.GetExtension(this.ImagePath) == ".svg")
                    {
                        using FileSvgReader svg = new FileSvgReader(new WpfDrawingSettings());
                        DrawingGroup drawingGroup = svg.Read(this.ImagePath);
                        this.ImageSource = new DrawingImage(drawingGroup);

                    }
                    else
                    {
                        BitmapImage image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.UriSource = new Uri(this.ImagePath);
                        image.EndInit();

                        this.ImageSource = image;
                    }
                    success = true;
                }
                catch (Exception e) when (e is NotSupportedException || e is XmlException || e is SvgException)
                {
                    // Do nothing
                    this.Logger.LogInformation(e, "Unable to load image {imageFile}", this.ImagePath);
                }
            }

            // Fallback to a default image.
            if (!success)
            {
                ImageSource? source = this.Action.DefaultImageSource;
                if (source != null)
                {
                    this.ImageSource = source;
                    success = true;
                }
                else
                {
                    Uri? defaultUri = this.Action.DefaultImageUri;
                    if (defaultUri != null && this.RemoteImage != defaultUri)
                    {
                        this.RemoteImage = defaultUri;
                        success = await this.LoadImage();
                    }
                }
            }

            return success;
        }

        /// <summary>
        /// true if downloading a new copy of a remote image is needed.
        /// </summary>
        public bool DownloadRequired { get; set; }

        /// <summary>
        /// The URL to the remote image.
        /// </summary>
        public Uri? RemoteImage
        {
            get => this.remoteImage;
            private set
            {
                this.remoteImage = value;
                if (this.remoteImage != null)
                {
                    this.ImagePath = AppPaths.GetCacheFile(this.remoteImage, out bool exists);
                    this.DownloadRequired = !exists
                        || (DateTime.Now - File.GetLastWriteTime(this.ImagePath)).TotalDays > 2;
                }
            }
        }

        public override void Deserialized(BarData bar)
        {
            base.Deserialized(bar);

            _ = this.LoadImage();
        }

        public bool ShowIcon => true;// string.IsNullOrEmpty(this.IconPath);

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }
}
