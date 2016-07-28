﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SiliconStudio.Core.Mathematics;
using SiliconStudio.Presentation.Extensions;
using Point = System.Windows.Point;

namespace SiliconStudio.Presentation.Controls
{
    /// <summary>
    /// A <see cref="FrameworkElement"/> that can host a game engine window. This control is faster than <see cref="HwndHost"/> but might behave
    /// a bit less nicely on certain cases (such as resize, etc.).
    /// </summary>
    public class GameEngineHost : FrameworkElement, IDisposable, IWin32Window, IKeyboardInputSink
    {
        private readonly List<HwndSource> contextMenuSources = new List<HwndSource>();
        private bool updateRequested;
        private int mouseMoveCount;
        private Point contextMenuPosition;
        private Int4 lastBoundingBox;
        private bool attached;

        static GameEngineHost()
        {
            FocusableProperty.OverrideMetadata(typeof(GameEngineHost), new FrameworkPropertyMetadata(true));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GameEngineHost"/> class.
        /// </summary>
        /// <param name="childHandle">The hwnd of the child (hosted) window.</param>
        public GameEngineHost(IntPtr childHandle)
        {
            Handle = childHandle;
            MinWidth = 32;
            MinHeight = 32;
            Loaded += OnLayoutUpdated;
            Unloaded += OnUnloaded;
            LayoutUpdated += OnLayoutUpdated;
            IsVisibleChanged += OnIsVisibleChanged;
        }

        public IntPtr Handle { get; }

        IKeyboardInputSite IKeyboardInputSink.KeyboardInputSite { get; set; }

        public void Dispose()
        {
            NativeHelper.SetParent(Handle, IntPtr.Zero);
            NativeHelper.DestroyWindow(Handle);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Detach();
        }

        private void OnLayoutUpdated(object sender, EventArgs e)
        {
            // Remark: this callback is invoked a lot. It is critical to do minimum work of no update is needed.
            Attach();
            UpdateWindowPosition();
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var newValue = (bool)e.NewValue;
            if (newValue)
            {
                Attach();
                UpdateWindowPosition();
            }
            else
            {
                Detach();
            }
        }

        private void Attach()
        {
            if (!attached)
            {
                var hwndParent = GetParentHwnd();
                if (hwndParent == IntPtr.Zero)
                    return;

                int style = NativeHelper.GetWindowLong(Handle, NativeHelper.GWL_STYLE);
                // Removes Caption bar and the sizing border
                // Must be a child window to be hosted
                style |= NativeHelper.WS_CHILD;

                NativeHelper.SetWindowLong(Handle, NativeHelper.GWL_STYLE, style);
                NativeHelper.ShowWindow(Handle, NativeHelper.SW_HIDE);

                // Update the parent to be the parent of the host
                NativeHelper.SetParent(Handle, hwndParent);

                // Register keyboard sink to make shortcuts work
                var source = PresentationSource.FromVisual(this) as IKeyboardInputSink;
                if (source != null)
                {
                    ((IKeyboardInputSink)this).KeyboardInputSite = source.RegisterKeyboardInputSink(this);
                }
                attached = true;
            }
        }

        private void Detach()
        {
            if (attached)
            {
                // Hide window, clear parent
                NativeHelper.ShowWindow(Handle, NativeHelper.SW_HIDE);
                NativeHelper.SetParent(Handle, IntPtr.Zero);

                // Unregister keyboard sink
                var site = ((IKeyboardInputSink)this).KeyboardInputSite;
                ((IKeyboardInputSink)this).KeyboardInputSite = null;
                site?.Unregister();

                // Make sure we will actually attach next time Attach() is called
                lastBoundingBox = Int4.Zero;
                attached = false;
            }
        }

        private void UpdateWindowPosition()
        {
            if (updateRequested)
                return;

            updateRequested = true;

            Dispatcher.InvokeAsync(() =>
            {
                updateRequested = false;
                // We do not use Window.GetParent because this method can return a window that is not yet the actual ancestor of this element.
                var parentWindow = this.FindVisualParentOfType<Window>();
                if (parentWindow == null)
                    return;

                // Find proper position for the game
                var positionTransform = TransformToAncestor(parentWindow);
                var areaPosition = positionTransform.Transform(new Point(0, 0));
                var boundingBox = new Int4((int)areaPosition.X, (int)areaPosition.Y, (int)ActualWidth, (int)ActualHeight);
                if (boundingBox == lastBoundingBox)
                    return;

                lastBoundingBox = boundingBox;

                // Move the window asynchronously, without activating it, without touching the Z order
                // TODO: do we want SWP_NOCOPYBITS?
                const int flags = NativeHelper.SWP_ASYNCWINDOWPOS | NativeHelper.SWP_NOACTIVATE | NativeHelper.SWP_NOZORDER;
                NativeHelper.SetWindowPos(Handle, NativeHelper.HWND_TOP, boundingBox.X, boundingBox.Y, boundingBox.Z, boundingBox.W, flags);
                NativeHelper.ShowWindow(Handle, NativeHelper.SW_SHOWNOACTIVATE);
            });
        }

        /// <summary>
        /// Forwards a message that comes from the hosted window to the WPF window. This method can be used for example to forward keyboard events.
        /// </summary>
        /// <param name="hwnd">The hwnd of the hosted window.</param>
        /// <param name="msg">The message identifier.</param>
        /// <param name="wParam">The word parameter of the message.</param>
        /// <param name="lParam">The long parameter of the message.</param>
        public void ForwardMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            DispatcherOperation task;
            switch (msg)
            {
                case NativeHelper.WM_RBUTTONDOWN:
                    mouseMoveCount = 0;
                    task = Dispatcher.InvokeAsync(() =>
                    {
                        RaiseMouseButtonEvent(Mouse.PreviewMouseDownEvent, MouseButton.Right);
                        RaiseMouseButtonEvent(Mouse.MouseDownEvent, MouseButton.Right);
                    });
                    task.Wait(TimeSpan.FromSeconds(1.0f));
                    break;
                case NativeHelper.WM_RBUTTONUP:
                    task = Dispatcher.InvokeAsync(() =>
                    {
                        RaiseMouseButtonEvent(Mouse.PreviewMouseUpEvent, MouseButton.Right);
                        RaiseMouseButtonEvent(Mouse.MouseUpEvent, MouseButton.Right);
                    });
                    task.Wait(TimeSpan.FromSeconds(1.0f));
                    break;
                case NativeHelper.WM_LBUTTONDOWN:
                    task = Dispatcher.InvokeAsync(() =>
                    {
                        RaiseMouseButtonEvent(Mouse.PreviewMouseDownEvent, MouseButton.Left);
                        RaiseMouseButtonEvent(Mouse.MouseDownEvent, MouseButton.Left);
                    });
                    task.Wait(TimeSpan.FromSeconds(1.0f));
                    break;
                case NativeHelper.WM_LBUTTONUP:
                    task = Dispatcher.InvokeAsync(() =>
                    {
                        RaiseMouseButtonEvent(Mouse.PreviewMouseUpEvent, MouseButton.Left);
                        RaiseMouseButtonEvent(Mouse.MouseUpEvent, MouseButton.Left);
                    });
                    task.Wait(TimeSpan.FromSeconds(1.0f));
                    break;
                case NativeHelper.WM_MOUSEMOVE:
                    ++mouseMoveCount;
                    break;
                case NativeHelper.WM_CONTEXTMENU:
                    // TODO: Tracking drag offset would be better, but might be difficult since we replace the mouse to its initial position each time it is moved.
                    if (mouseMoveCount < 3)
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            DependencyObject dependencyObject = this;
                            while (dependencyObject != null)
                            {
                                var element = dependencyObject as FrameworkElement;
                                if (element?.ContextMenu != null)
                                {
                                    element.Focus();
                                    // Data context will not be properly set if the popup is open this way, so let's set it ourselves
                                    element.ContextMenu.SetCurrentValue(DataContextProperty, element.DataContext);
                                    element.ContextMenu.IsOpen = true;
                                    var source = (HwndSource)PresentationSource.FromVisual(element.ContextMenu);
                                    if (source != null)
                                    {
                                        source.AddHook(ContextMenuWndProc);
                                        contextMenuPosition = Mouse.GetPosition(this);
                                        lock (contextMenuSources)
                                        {
                                            contextMenuSources.Add(source);
                                        }
                                    }
                                    break;
                                }
                                dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
                            }
                        });
                    }
                    break;
                default:
                    var parent = NativeHelper.GetParent(hwnd);
                    NativeHelper.PostMessage(parent, msg, wParam, lParam);
                    break;
            }
        }

        private void RaiseMouseButtonEvent(RoutedEvent routedEvent, MouseButton button)
        {
            RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button)
            {
                RoutedEvent = routedEvent,
                Source = this,
            });
        }

        private IntPtr ContextMenuWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case NativeHelper.WM_LBUTTONDOWN:
                case NativeHelper.WM_RBUTTONDOWN:
                    // We need to change from the context menu coordinates to the HwndHost coordinates and re-encode lParam
                    var position = new Point(-(short)(lParam.ToInt64() & 0xFFFF), -((lParam.ToInt64() & 0xFFFF0000) >> 16));
                    var offset = contextMenuPosition - position;
                    lParam = new IntPtr((short)offset.X + (((short)offset.Y) << 16));
                    var threadId = NativeHelper.GetWindowThreadProcessId(Handle, IntPtr.Zero);
                    NativeHelper.PostThreadMessage(threadId, msg, wParam, lParam);
                    break;
                case NativeHelper.WM_DESTROY:
                    lock (contextMenuSources)
                    {
                        var source = contextMenuSources.First(x => x.Handle == hwnd);
                        source.RemoveHook(ContextMenuWndProc);
                    }
                    break;
            }
            return IntPtr.Zero;
        }

        private IntPtr GetParentHwnd()
        {
            var panelHwnd = (HwndSource)PresentationSource.FromVisual(this);
            return panelHwnd?.Handle ?? IntPtr.Zero;
        }

        IKeyboardInputSite IKeyboardInputSink.RegisterKeyboardInputSink(IKeyboardInputSink sink)
        {
            throw new NotSupportedException();
        }

        bool IKeyboardInputSink.TranslateAccelerator(ref MSG msg, ModifierKeys modifiers)
        {
            return false;
        }

        bool IKeyboardInputSink.TabInto(TraversalRequest request)
        {
            return false;
        }

        bool IKeyboardInputSink.OnMnemonic(ref MSG msg, ModifierKeys modifiers)
        {
            return false;
        }

        bool IKeyboardInputSink.TranslateChar(ref MSG msg, ModifierKeys modifiers)
        {
            return false;
        }

        bool IKeyboardInputSink.HasFocusWithin()
        {
            var focus = NativeHelper.GetFocus();
            return Handle != IntPtr.Zero && (focus == Handle || NativeHelper.IsChild(Handle, focus));
        }
    }
}
