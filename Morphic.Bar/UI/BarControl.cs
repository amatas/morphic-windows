﻿// BarControl.cs: The control for a bar.
//
// Copyright 2020 Raising the Floor - International
//
// Licensed under the New BSD license. You may not use this file except in
// compliance with this License.
//
// You may obtain a copy of the License at
// https://github.com/GPII/universal/blob/master/LICENSE.txt

namespace Morphic.Bar.UI
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;
    using Bar;
    using BarControls;

    /// <summary>
    /// This is the thing that contains bar items.
    /// </summary>
    public class BarControl : WrapPanel, INotifyPropertyChanged
    {
        public static readonly DependencyProperty ItemSpacingProperty = DependencyProperty.Register("ItemSpacing", typeof(double), typeof(BarControl), new PropertyMetadata(default(double)));

        public BarControl()
        {
            this.Bar = new BarData();
        }

        public BarData Bar { get; private set; }
        public bool IsPrimary { get; set; }

        public bool FixedSize => this.IsPrimary && this.Bar.Overflow != BarOverflow.Wrap;
        public bool IsHorizontal { get; set; }

        public double Scale { get; set; }

        public void ApplyScale()
        {
            this.LayoutTransform = new ScaleTransform(this.Scale, this.Scale);
        }

        public double ScaledItemWidth => Math.Ceiling(this.ItemWidth * this.Scale);
        public double ScaledItemHeight => Math.Ceiling(this.ItemHeight * this.Scale);

        public double ItemSpacing
        {
            get => (double)this.GetValue(ItemSpacingProperty);
            set => this.SetValue(ItemSpacingProperty, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? BarLoaded;

        /// <summary>
        /// Raised when tabbing through items has gone beyond the last item.
        /// </summary>
        public event EventHandler? EndTab;

        private Size GetChildSize(UIElement child)
        {
            return this.GetChildSize(child.DesiredSize);
        }
        private Size GetChildSize(Size desiredSize)
        {
            return new Size(
                double.IsNaN(this.ItemWidth) ? desiredSize.Width : this.ItemWidth,
                double.IsNaN(this.ItemHeight) ? desiredSize.Height : this.ItemHeight
            );
        }

        protected override Size MeasureOverride(Size constraint)
        {
            return this.MeasureArrange(constraint, true);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            return this.MeasureArrange(finalSize, false);
        }

        /// <summary>
        /// Measure or arrange the child items.
        /// </summary>
        /// <param name="finalSize">The suggested size.</param>
        /// <param name="measure">true to only measure, otherwise arrange then items.</param>
        /// <param name="orientationOverride">Override the controls orientation.</param>
        /// <returns>A size that fits the arranged items.</returns>
        public Size MeasureArrange(Size finalSize, bool measure, Orientation? orientationOverride = null)
        {
            Orientation orientation = orientationOverride ?? this.Orientation;

            double rowHeight = 0;

            CorrectedCoords pos = new CorrectedCoords(0, 0, orientation);
            CorrectedCoords size = new CorrectedCoords(finalSize, orientation);
            CorrectedCoords actualSize = new CorrectedCoords(0, 0, orientation);

            CorrectedCoords itemSize = new CorrectedCoords(this.ItemWidth, this.ItemHeight, orientation);

            List<BarItemControl> children = this.Children.OfType<BarItemControl>().Where(c => c != null).ToList();
            if (!children.Any())
            {
                return finalSize;
            }

            if (measure)
            {
                Size childAvailableSize = this.GetChildSize(finalSize);
                children.ForEach(c => c.Measure(childAvailableSize));
            }

            // Get the widest
            double widest = children.Select(c => new CorrectedCoords(c.DesiredSize, orientation).Width).Max();

            size.Width = Math.Max(size.Width, widest);

            // first item of the row
            bool firstItem = true;

            foreach (BarItemControl child in children)
            {
                CorrectedCoords childSize = new CorrectedCoords(this.GetChildSize(child), orientation);

                if (!this.FixedSize && pos.X + childSize.Width >= size.Width)
                {
                    // new row
                    firstItem = true;
                    pos.X = 0;
                    pos.Y += rowHeight + this.ItemSpacing;

                    rowHeight = 0;
                }

                if (!firstItem)
                {
                    pos.X += this.ItemSpacing;
                }

                if (!measure)
                {
                    child.Arrange(new Rect(pos.ToPoint(), childSize.ToSize()));
                }

                rowHeight = Math.Max(rowHeight, childSize.Height);
                pos.X += childSize.Width;

                actualSize.Width = Math.Max(actualSize.Width, pos.X);
                actualSize.Height = pos.Y + rowHeight;

                firstItem = false;
            }

            actualSize.Width = Math.Ceiling(actualSize.Width);
            actualSize.Height = Math.Ceiling(actualSize.Height);
            return actualSize.ToSize();
        }

        public Size GetSize(Size size, Orientation? orientationOverride = null)
        {
            Size newSize = this.MeasureArrange(size, true, orientationOverride);
            newSize.Width *= this.Scale;
            newSize.Height *= this.Scale;
            return newSize;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            if (this.FixedSize)
            {
                this.Orientation = sizeInfo.NewSize.Width > sizeInfo.NewSize.Height
                    ? Orientation.Horizontal
                    : Orientation.Vertical;
            }
            else
            {
                this.Orientation = Orientation.Vertical;
            }
            base.OnRenderSizeChanged(sizeInfo);
        }

        public void LoadBar(BarData bar, bool isPrimary)
        {
            this.IsPrimary = isPrimary;
            this.RemoveItems();
            this.Bar = bar;
            
            this.LayoutTransform = new ScaleTransform(this.Scale, this.Scale);
            
            this.AddItems(isPrimary ? this.Bar.PrimaryItems : this.Bar.SecondaryItems);
            this.OnBarLoaded();
        }

        public void ReloadItems()
        {
            this.RemoveItems();
            this.AddItems(this.IsPrimary ? this.Bar.PrimaryItems : this.Bar.SecondaryItems);
        }

        public void RemoveItems()
        {
            this.Children.Clear();
        }

        /// <summary>
        /// Load some items.
        /// </summary>
        /// <param name="items"></param>
        public void AddItems(IEnumerable<BarItem> items)
        {
            foreach (BarItem item in items)
            {
                this.AddItem(item);
            }

            this.AddEndTabControl();
        }

        /// <summary>
        /// Add a bar item to the control.
        /// </summary>
        /// <param name="item"></param>
        /// <returns>The bar item control.</returns>
        public BarItemControl AddItem(BarItem item)
        {
            BarItemControl control = BarItemControl.From(item);
            control.Style = new Style(control.GetType(), this.Resources["BarItemStyle"] as Style);
            this.Children.Add(control);
            return control;
        }

        protected virtual void OnBarLoaded()
        {
            this.BarLoaded?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Add a secret control as the last item. This is to move focus to the other bar.
        /// </summary>
        private void AddEndTabControl()
        {
            Button tb = new Button()
            {
                Focusable = true,
                IsTabStop = true,
                TabIndex = int.MaxValue,
                Width = 10,
                Height = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            };

            tb.GotFocus += (o, a) =>
            {
                this.OnEndTab();
            };

            this.Children.Add(tb);
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected virtual void OnEndTab()
        {
            this.EndTab?.Invoke(this, EventArgs.Empty);
        }
    }

    public struct CorrectedCoords
    {
        public readonly Orientation Orientation;
        private readonly bool swap;

        public CorrectedCoords(Size size, Orientation orientation) : this(size.Width, size.Height, orientation)
        {
        }
        public CorrectedCoords(Point size, Orientation orientation) : this(size.X, size.Y, orientation)
        {
        }
        public CorrectedCoords(Orientation orientation) : this(0, 0, orientation)
        {
        }

        public override string ToString()
        {
            return $"{this.Width}x{this.Height}{(this.swap ? "(swap)" : "")}";
        }

        public CorrectedCoords(double x, double y, Orientation orientation)
        {
            this.Orientation = orientation;
            this.swap = this.Orientation == Orientation.Vertical;
            this.X = this.swap ? y : x;
            this.Y = this.swap ? x : y;
        }

        public static implicit operator Size(CorrectedCoords size) => size.ToSize();

        public Size ToSize()
        {
            return this.swap
                ? new Size(this.Height, this.Width)
                : new Size(this.Width, this.Height);
        }

        public Point ToPoint()
        {
            return this.swap
                ? new Point(this.Y, this.X)
                : new Point(this.X, this.Y);
        }

        public double X { get; set; }
        public double Y { get; set; }

        public double Width
        {
            get => this.X;
            set => this.X = value;
        }

        public double Height
        {
            get => this.Y;
            set => this.Y = value;
        }

        public double CorrectedWidth => this.swap ? this.Height : this.Width;
        public double CorrectedHeight => this.swap ? this.Width : this.Height;
        public double CorrectedX => this.swap ? this.Y : this.X;
        public double CorrectedY => this.swap ? this.X : this.Y;
    }


}
