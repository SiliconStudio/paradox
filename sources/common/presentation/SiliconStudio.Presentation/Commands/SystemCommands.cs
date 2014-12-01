﻿using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

using SiliconStudio.Presentation.Extensions;

namespace SiliconStudio.Presentation.Commands
{
    /// <summary>
    /// A static class containing system commands to control a window.
    /// </summary>
    public static class SystemCommands
    {
        static SystemCommands()
        {
            MinimizeWindowCommand = new SystemCommand(CanMinimize, Minimize);
            MaximizeWindowCommand = new SystemCommand(CanMaximize, Maximize);
            RestoreWindowCommand = new SystemCommand(CanRestore, Restore);
            CloseWindowCommand = new SystemCommand(CanClose, Close);
            ShowSystemMenuCommand = new SystemCommand(CanShowSystemMenu, ShowSystemMenu);
        }

        /// <summary>
        /// Gets a command that minimizes the window passed as parameter.
        /// </summary>
        public static ICommand MinimizeWindowCommand { get; private set; }

        /// <summary>
        /// Gets a command that maximizes the window passed as parameter.
        /// </summary>
        public static ICommand MaximizeWindowCommand { get; private set; }

        /// <summary>
        /// Gets a command that restores the window passed as parameter.
        /// </summary>
        public static ICommand RestoreWindowCommand { get; private set; }

        /// <summary>
        /// Gets a command that closes the window passed as parameter.
        /// </summary>
        public static ICommand CloseWindowCommand { get; private set; }

        /// <summary>
        /// Gets a command that show the system menu of the window passed as parameter.
        /// </summary>
        public static ICommand ShowSystemMenuCommand { get; private set; }

        private static bool CanMinimize(Window window)
        {
            return HasFlag(window, NativeHelper.WS_MINIMIZEBOX);
        }

        private static void Minimize(Window window)
        {
            System.Windows.SystemCommands.MinimizeWindow(window);
        }

        private static bool CanMaximize(Window window)
        {
            return HasFlag(window, NativeHelper.WS_MAXIMIZEBOX);
        }

        private static void Maximize(Window window)
        {
            System.Windows.SystemCommands.MaximizeWindow(window);
        }

        private static bool CanRestore(Window window)
        {
            return HasFlag(window, NativeHelper.WS_MAXIMIZEBOX);
        }

        private static void Restore(Window window)
        {
            System.Windows.SystemCommands.RestoreWindow(window);
        }

        private static bool CanClose(Window window)
        {
            return true;
        }

        private static void Close(Window window)
        {
            System.Windows.SystemCommands.CloseWindow(window);
        }

        private static bool CanShowSystemMenu(Window window)
        {
            return window != null && HasFlag(window, NativeHelper.WS_SYSMENU);
        }

        private static void ShowSystemMenu(Window window)
        {
            // Note: as we fetch for the content presenter, this command is a bit dependent of the window control template.
            // But both our template and the default (Aero) seems to use one so this is probably ok.
            var presenter = window.FindVisualChildrenOfType<ContentPresenter>().FirstOrDefault(x => Equals(x.FindVisualParentOfType<Control>(), window));
            if (presenter == null)
                throw new InvalidOperationException("The given window does not contain a ContentPresenter.");

            System.Windows.SystemCommands.ShowSystemMenu(window, presenter.PointToScreen(new Point(0, 0)));
        }

        private static bool HasFlag(Window window, int flag)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            bool hasFlag = (NativeHelper.GetWindowLong(hwnd, NativeHelper.GWL_STYLE) & flag) != 0;
            return hasFlag;
        }
    }
}
