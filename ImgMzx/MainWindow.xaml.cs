﻿using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SixLabors.ImageSharp.Processing;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace ImgMzx
{
    public sealed partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            Trace.Listeners.Add(new ConsoleTraceListener());
        }

        private void WindowLoaded(object sender, RoutedEventArgs e)
        {
            WindowLoaded();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            OnStateChanged();
        }

        private void PictureLeftBoxMouseClick(object sender, MouseEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed) {
                PictureLeftBoxMouseClick();
            }
        }

        private void PictureRightBoxMouseClick(object sender, MouseEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed) {
                PictureRightBoxMouseClick();
            }
        }

        private void ButtonLeftNextMouseClick(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) {
                ButtonLeftNextMouseClick();
            }
        }

        private void ButtonRightNextMouseClick(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) {
                ButtonRightNextMouseClick();
            }
        }

        private void RotateClick(object sender, EventArgs e)
        {
            var rotatemode = (RotateMode)Enum.Parse(typeof(RotateMode), (string)((MenuItem)sender).Tag);
            RotateClick(rotatemode);
        }

        private void FlipClick(object sender, EventArgs e)
        {
            var flipmode = (FlipMode)Enum.Parse(typeof(FlipMode), (string)((MenuItem)sender).Tag);
            FlipClick(flipmode);
        }

        private void ExitClick(object sender, EventArgs e)
        {
            CloseApp();
        }

        private void ImportClick(object sender, RoutedEventArgs e)
        {
            ImportClick();
        }

        private void ExportClick(object sender, RoutedEventArgs e)
        {
            ExportClick();
        }

        private void RefreshClick(object sender, RoutedEventArgs e)
        {
            RefreshClick();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            OnKeyDown(e.Key);
        }

        private void ToggleXorClick(object sender, RoutedEventArgs e)
        {
            ToggleXorClick();
        }

        private void FamilyAddClick(object sender, RoutedEventArgs e)
        {
            FamilyAddClick();
        }

        private void FamilyRemoveClick(object sender, RoutedEventArgs e)
        {
            FamilyRemoveClick();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            ReleaseResources();
        }
    }
}
