namespace Morphic.Bar.UI
{
    using System;
    using System.Linq;
    using System.Windows;
    using System.Windows.Interop;
    using Windows.Native;
    using AppBarWindow;
    using Bar;
    using Microsoft.Win32;

    public sealed class PrimaryBarWindow : BarWindow
    {
        private SecondaryBarWindow? secondaryWindow;
        private ExpanderWindow? expanderWindow;

        public override BarWindow? OtherWindow => this.secondaryWindow;

        public event EventHandler? ExpandedChange;

        public override ExpanderWindow? ExpanderWindow => this.expanderWindow;

        public override bool IsExpanded
        {
            get => base.IsExpanded;
            set
            {
                base.IsExpanded = value;
                this.OnExpandedChange();
            }
        }

        public override double Scale
        {
            get => base.Scale;
            set
            {
                base.Scale = value;
                // Apply the same scale to the secondary bar.
                if (this.secondaryWindow != null)
                {
                    this.secondaryWindow.Scale = value;
                }
            }
        }

        public PrimaryBarWindow(BarData barData) : base(barData)
        {
#if TESTING
            // Accept bar files to be dropped.
            this.AllowDrop = true;
            this.Drop += (sender, args) =>
            {
                if (args.Data.GetDataPresent(DataFormats.FileDrop) &&
                    args.Data.GetData(DataFormats.FileDrop) is string[] files)
                {
                    string file = files.FirstOrDefault() ?? this.Bar.Source;
                    this.Bar = BarData.Load(file)!;
                }
            };
#endif
            this.Closed += this.OnClosed;
            this.Bar = barData;

            this.SourceInitialized += (sender, args) =>
            {
                // Start monitoring the active window.
                WindowInteropHelper nativeWindow = new WindowInteropHelper(this);
                HwndSource? hwndSource = HwndSource.FromHwnd(nativeWindow.Handle);
                SelectionReader.Default.Initialise(nativeWindow.Handle);
                hwndSource?.AddHook(SelectionReader.Default.WindowProc);
            };

            SystemEvents.DisplaySettingsChanged += this.SystemEventsOnDisplaySettingsChanged;
            this.Closed += (sender, args) => SystemEvents.DisplaySettingsChanged -= this.SystemEventsOnDisplaySettingsChanged;
        }

        private void SystemEventsOnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            this.SetInitialPosition();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            this.IsClosing = true;
            this.expanderWindow?.Close();
            this.secondaryWindow?.Close();
        }

        public bool IsClosing { get; set; }

        protected override void OnBarLoaded()
        {
            base.OnBarLoaded();
            this.LoadSecondaryBar();
        }

        /// <summary>
        /// Loads the secondary, if required.
        /// </summary>
        private void LoadSecondaryBar()
        {
            if (this.secondaryWindow == null && this.Bar.SecondaryItems.Any())
            {
                this.secondaryWindow = new SecondaryBarWindow(this, this.Bar);
                this.expanderWindow = new ExpanderWindow(this, this.secondaryWindow);

                this.secondaryWindow.Loaded += (s, a) => this.expanderWindow.Show();
                this.expanderWindow.Changed += (s, a) => this.IsExpanded = this.expanderWindow.IsExpanded;

                this.secondaryWindow.Show();
            }
        }


        protected override void SetInitialPosition()
        {
            Size size = this.GetGoodSize();
            size = this.Rescale(size, true);
            this.LoadSecondaryBar();

            this.AppBar.ApplyAppBar(this.Bar.Position.DockEdge);
            if (this.Bar.Position.DockEdge == Edge.None)
            {
                Rect workArea = SystemParameters.WorkArea;
                Point pos = this.Bar.Position.Primary.GetPosition(workArea, size);
                this.Left = pos.X;
                this.Top = pos.Y;
            }
        }

        private void OnExpandedChange()
        {
            if (this.IsExpanded)
            {
                this.secondaryWindow?.Show();
                this.secondaryWindow?.Activate();
            }
            else
            {
                this.Activate();
                this.secondaryWindow?.Hide();
            }
            
            this.ExpandedChange?.Invoke(this, EventArgs.Empty);
        }
    }
}
