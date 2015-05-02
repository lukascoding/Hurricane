﻿using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Hurricane.Model.Skin;
using MahApps.Metro.Controls;
using WindowSettings = Hurricane.Model.Skin.WindowSettings;
using Hurricane.MagicArrow;
using Hurricane.Utilities;

namespace Hurricane.Controls
{
    public partial class MagicMetroWindow : MetroWindow
    {
        public static readonly DependencyProperty AdvancedViewSkinProperty = DependencyProperty.Register(
            "AdvancedViewSkin", typeof (IWindowSkin), typeof (MagicMetroWindow), new PropertyMetadata(default(IWindowSkin)));

        public static readonly DependencyProperty DockViewSkinProperty = DependencyProperty.Register(
            "DockViewSkin", typeof(IWindowSkin), typeof(MagicMetroWindow), new PropertyMetadata(default(IWindowSkin)));

        public static readonly DependencyProperty WindowSettingsProperty = DependencyProperty.Register(
            "WindowSettings", typeof(WindowSettings), typeof(MagicMetroWindow), new PropertyMetadata(default(WindowSettings)));

        public static readonly DependencyProperty ShowMagicArrowBelowCursorProperty = DependencyProperty.Register(
            "ShowMagicArrowBelowCursor", typeof(bool), typeof(MagicMetroWindow), new PropertyMetadata(default(bool)));

        public static readonly DependencyProperty CloseCommandProperty = DependencyProperty.Register(
            "CloseCommand", typeof (ICommand), typeof (MagicMetroWindow), new PropertyMetadata(default(ICommand)));

        public static readonly DependencyProperty MinimizeToTrayProperty = DependencyProperty.Register(
            "MinimizeToTray", typeof (bool), typeof (MagicMetroWindow), new PropertyMetadata(default(bool)));

        public static readonly DependencyProperty MinimizeToTrayMessageProperty = DependencyProperty.Register(
            "MinimizeToTrayMessage", typeof (string), typeof (MagicMetroWindow), new PropertyMetadata(default(string)));

        public static readonly DependencyProperty ShowMessageWhenMinimizeToTrayProperty = DependencyProperty.Register(
            "ShowMessageWhenMinimizeToTray", typeof (bool), typeof (MagicMetroWindow), new PropertyMetadata(default(bool)));

        public static readonly DependencyProperty WindowWidthProperty = DependencyProperty.Register(
            "WindowWidth", typeof (double), typeof (MagicMetroWindow), new PropertyMetadata(default(double),
                (o, args) =>
                {
                    var window = o as Window;
                    if (window == null) throw new ArgumentException(o.ToString());
                    var newWidth = (double) args.NewValue;
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    if (window.Width != newWidth) window.Width = newWidth;
                }));

        public static readonly DependencyProperty WindowHeightProperty = DependencyProperty.Register(
            "WindowHeight", typeof (double), typeof (MagicMetroWindow), new PropertyMetadata(default(double),
                (o, args) =>
                {
                    var window = o as Window;
                    if (window == null) throw new ArgumentException(o.ToString());
                    var newHeight = (double) args.NewValue;
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    if (window.Height != newHeight) window.Height = newHeight;
                }));

        private MagicArrowService _magicArrow;

        public MagicMetroWindow()
        {
            SourceInitialized += MagicMetroWindow_SourceInitialized;
            Closing += MagicMetroWindow_Closing;
            StateChanged += MagicMetroWindow_StateChanged;
        }

        void MagicMetroWindow_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && MinimizeToTray)
            {
                Hide();
            }
        }

        void MagicMetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (CurrentWindowState == CurrentWindowState.Normal) //We save the position
            {
                WindowHeight = Height;
                WindowWidth = Width;
            }

            if (CloseCommand != null) CloseCommand.Execute(e);
            _magicArrow.Dispose();
        }

        void MagicMetroWindow_SourceInitialized(object sender, EventArgs e)
        {
            _magicArrow = new MagicArrowService(this);
            _magicArrow.DockManager.Docked += (s, args) =>
            {
                CurrentWindowState = CurrentWindowState.Docked;
                ApplyWindowSkin();
            };
            _magicArrow.DockManager.Undocked += (o, args) =>
            {
                CurrentWindowState = CurrentWindowState.Normal;
                ApplyWindowSkin();
            };
            _magicArrow.DockManager.DragStopped += DockManager_DragStopped;

            WindowHelper.DisableAeroSnap(new WindowInteropHelper(this).Handle);
        }

        public IWindowSkin AdvancedViewSkin
        {
            get { return (IWindowSkin) GetValue(AdvancedViewSkinProperty); }
            set { SetValue(AdvancedViewSkinProperty, value); }
        }

        public IWindowSkin DockViewSkin
        {
            get { return (IWindowSkin) GetValue(DockViewSkinProperty); }
            set { SetValue(DockViewSkinProperty, value); }
        }

        public WindowSettings WindowSettings
        {
            get { return (WindowSettings)GetValue(WindowSettingsProperty); }
            set { SetValue(WindowSettingsProperty, value); }
        }

        public bool ShowMagicArrowBelowCursor
        {
            get { return (bool)GetValue(ShowMagicArrowBelowCursorProperty); }
            set { SetValue(ShowMagicArrowBelowCursorProperty, value); }
        }

        public ICommand CloseCommand
        {
            get { return (ICommand)GetValue(CloseCommandProperty); }
            set { SetValue(CloseCommandProperty, value); }
        }

        public bool MinimizeToTray
        {
            get { return (bool)GetValue(MinimizeToTrayProperty); }
            set { SetValue(MinimizeToTrayProperty, value); }
        }

        public string MinimizeToTrayMessage
        {
            get { return (string)GetValue(MinimizeToTrayMessageProperty); }
            set { SetValue(MinimizeToTrayMessageProperty, value); }
        }

        public bool ShowMessageWhenMinimizeToTray
        {
            get { return (bool)GetValue(ShowMessageWhenMinimizeToTrayProperty); }
            set { SetValue(ShowMessageWhenMinimizeToTrayProperty, value); }
        }

        public double WindowWidth
        {
            get { return (double)GetValue(WindowWidthProperty); }
            set { SetValue(WindowWidthProperty, value); }
        }

        public double WindowHeight
        {
            get { return (double)GetValue(WindowHeightProperty); }
            set { SetValue(WindowHeightProperty, value); }
        }

        public IWindowSkin CurrentView { get; set; }
        public CurrentWindowState CurrentWindowState { get; set; }

        protected void ApplyWindowSkin()
        {
            var newWindowSkin = CurrentWindowState == CurrentWindowState.Normal ? AdvancedViewSkin : DockViewSkin;
            if (CurrentView == newWindowSkin)
                return;

            if (CurrentView != null)
            {
                CurrentView.DragMoveStart -= WindowSkin_DragMoveStart;
                CurrentView.DragMoveStop -= WindowSkin_DragMoveStop;
                CurrentView.ToggleWindowState -= WindowSkin_ToggleWindowState;
                CurrentView.TitleBarMouseMove -= WindowSkin_TitleBarMouseMove;
                CurrentView.DisableSkin();


            }

            //Handle events
            newWindowSkin.CloseRequest += WindowSkin_CloseRequest;
            newWindowSkin.DragMoveStart += WindowSkin_DragMoveStart;
            newWindowSkin.DragMoveStop += WindowSkin_DragMoveStop;
            newWindowSkin.ToggleWindowState += WindowSkin_ToggleWindowState;
            newWindowSkin.TitleBarMouseMove += WindowSkin_TitleBarMouseMove;

            if (!_isDragging)
                ResizeMode = newWindowSkin.Configuration.IsResizable ? ResizeMode.CanResize : ResizeMode.NoResize;

            if (CurrentWindowState == CurrentWindowState.Normal)
            {
                Height = WindowHeight;
                Width = WindowWidth;
            }
            else
            {
                WindowHeight = Height;
                WindowWidth = Width;
            }

            //Set properties
            MaxHeight = newWindowSkin.Configuration.MaxHeight;
            MinHeight = newWindowSkin.Configuration.MinHeight;
            MaxWidth = newWindowSkin.Configuration.MaxWidth;
            MinWidth = newWindowSkin.Configuration.MinWidth;
            ShowTitleBar = newWindowSkin.Configuration.ShowTitleBar;
            ShowSystemMenuOnRightClick = newWindowSkin.Configuration.ShowSystemMenuOnRightClick;
        }

        private void WindowSkin_ToggleWindowState(object sender, EventArgs e)
        {
            WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        }

        private void WindowSkin_CloseRequest(object sender, EventArgs e)
        {
            Close();
        }
    }

    public enum CurrentWindowState
    {
        Normal,
        Docked
    }
}