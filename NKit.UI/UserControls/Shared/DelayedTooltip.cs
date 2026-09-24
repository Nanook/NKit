using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NKit.Ui.Models;
using Splat;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace NKit.Ui.UserControls.Shared
{
    /// <summary>
    /// Attached property to show a delayed tooltip popup for any Control.
    /// Usage: xmlns:dt="clr-namespace:NKit.Ui.UserControls.Shared" then dt:DelayedTooltip.Text="Your tooltip"
    /// Respects global UiSettings.ShowTooltips from ISettingsStore.
    /// Shows after 1 second hover and remains until pointer leaves the control.
    /// </summary>
    public class DelayedTooltip
    {
        private const int HoverDelayMs = 500;
        private const int PollIntervalMs = 50; // avoid a single cancellable Task.Delay to reduce exceptions
        private const int SuppressAfterCancelMs = 500; // short window after clicks/cancels to avoid instant re-show

        public static readonly AttachedProperty<string> TextProperty =
            AvaloniaProperty.RegisterAttached<DelayedTooltip, Control, string>("Text");

        // store per-control state
        private class State
        {
            public CancellationTokenSource Cts;
            public Popup Popup;
            public EventHandler<PointerEventArgs> MovedHandler;
            public EventHandler<PointerEventArgs> ExitedHandler;
            public EventHandler<VisualTreeAttachmentEventArgs> DetachedHandler;
            public EventHandler<VisualTreeAttachmentEventArgs> AttachedHandler;
            public EventHandler<PointerPressedEventArgs> PressedHandler;
            public EventHandler<RoutedEventArgs> GotFocusHandler;
            public EventHandler<RoutedEventArgs> LostFocusHandler;
            public long LastCancelTicksUtc;
        }

        private static readonly ConcurrentDictionary<Control, State> _states = new();

        // track the control that currently has an open popup so only one tooltip is visible at once
        private static Control _currentOpenControl;

        static DelayedTooltip()
        {
            TextProperty.Changed.AddClassHandler<Control>((control, e) => OnTextChanged(control, e));
        }

        public static void SetText(Control control, string value) => control.SetValue(TextProperty, value);
        public static string GetText(Control control) => control.GetValue(TextProperty);

        private static void OnTextChanged(Control control, AvaloniaPropertyChangedEventArgs e)
        {
            if (control == null) return;

            // detach existing handlers if any
            if (e.OldValue is string oldStr && !string.IsNullOrEmpty(oldStr))
            {
                Detach(control);
            }

            if (e.NewValue is string newText && !string.IsNullOrEmpty(newText))
            {
                Attach(control, newText);
            }
        }

        private static void Attach(Control control, string text)
        {
            if (control == null) return;

            State st = new State();

            st.MovedHandler = (s, e) => StartHover(control, text);
            st.ExitedHandler = (s, e) => CancelAndHide(control);
            // handle clicks - cancel tooltip and suppress immediate re-show
            st.PressedHandler = (s, e) => CancelAndHide(control);
            // hide tooltip when control gains or loses focus (covers combobox dropdown opening)
            st.GotFocusHandler = (s, e) => CancelAndHide(control);
            st.LostFocusHandler = (s, e) => CancelAndHide(control);
            // When the control is detached from visual tree (tab switch), just hide the popup and cancel timer,
            // but do NOT remove handlers - keep them so tooltips still work after re-attach.
            st.DetachedHandler = (s, e) => CancelAndHide(control);
            st.AttachedHandler = (s, e) => OnAttached(control);

            control.PointerMoved += st.MovedHandler;
            control.PointerExited += st.ExitedHandler;
            control.PointerPressed += st.PressedHandler;
            control.GotFocus += st.GotFocusHandler;
            control.LostFocus += st.LostFocusHandler;
            control.DetachedFromVisualTree += st.DetachedHandler;
            control.AttachedToVisualTree += st.AttachedHandler;

            _states[control] = st;

            // Remove state only when control is actually removed or the attached property cleared (handled in Detach)
        }

        private static void Detach(Control control)
        {
            if (control == null) return;
            if (_states.TryRemove(control, out State st))
            {
                try { if (st.MovedHandler != null) control.PointerMoved -= st.MovedHandler; } catch { }
                try { if (st.ExitedHandler != null) control.PointerExited -= st.ExitedHandler; } catch { }
                try { if (st.PressedHandler != null) control.PointerPressed -= st.PressedHandler; } catch { }
                try { if (st.GotFocusHandler != null) control.GotFocus -= st.GotFocusHandler; } catch { }
                try { if (st.LostFocusHandler != null) control.LostFocus -= st.LostFocusHandler; } catch { }
                try { if (st.DetachedHandler != null) control.DetachedFromVisualTree -= st.DetachedHandler; } catch { }
                try { if (st.AttachedHandler != null) control.AttachedToVisualTree -= st.AttachedHandler; } catch { }
                try { st.Cts?.Cancel(); } catch { }
                try { if (st.Popup != null) st.Popup.IsOpen = false; } catch { }
                // if this control was the current open, clear it
                if (ReferenceEquals(_currentOpenControl, control))
                {
                    _currentOpenControl = null;
                }
            }
        }

        private static void StartHover(Control control, string text)
        {
            if (control == null) return;
            if (!_states.TryGetValue(control, out State st))
            {
                st = new State();
                _states[control] = st;
            }

            // If there's already an active timer, don't start another one
            if (st.Cts != null && !st.Cts.IsCancellationRequested)
                return;

            // If we recently cancelled (e.g. due to click), suppress immediate restart
            if (st.LastCancelTicksUtc != 0)
            {
                long elapsed = DateTime.UtcNow.Ticks - st.LastCancelTicksUtc;
                if (elapsed < TimeSpan.FromMilliseconds(SuppressAfterCancelMs).Ticks)
                    return;
            }

            // Cancel any previous CTS and create a fresh one
            try { st.Cts?.Cancel(); } catch { }
            st.Cts = new CancellationTokenSource();
            CancellationToken ct = st.Cts.Token;

            Task.Run(async () =>
            {
                try
                {
                    int waited = 0;
                    while (waited < HoverDelayMs)
                    {
                        await Task.Delay(PollIntervalMs).ConfigureAwait(false);
                        if (ct.IsCancellationRequested)
                            return;
                        waited += PollIntervalMs;
                    }

                    // Check global setting
                    ISettingsStore settingsStore = Locator.Current.GetService<ISettingsStore>();
                    bool show = settingsStore?.UiSettings?.ShowTooltips ?? true;
                    if (!show) return;

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        // create popup if needed
                        if (st.Popup == null)
                        {
                            // create TextBlock programmatically so we can set TextWrapping without referencing enum type directly
                            TextBlock tb = new TextBlock();
                            tb.Text = text;
                            tb.Foreground = Avalonia.Media.Brushes.Black;
                            tb.FontSize = 13.0;
                            tb.MaxWidth = 400.0;
                            try
                            {
                                // set TextWrapping via attached property type to avoid referencing enum symbol directly
                                Type wrappingType = TextBlock.TextWrappingProperty.PropertyType;
                                object wrapValue = Enum.Parse(wrappingType, "Wrap");
                                tb.SetValue(TextBlock.TextWrappingProperty, wrapValue);
                            }
                            catch { }

                            st.Popup = new Popup
                            {
                                PlacementTarget = control,
                                Placement = PlacementMode.Bottom,
                                Child = new Border
                                {
                                    Background = Avalonia.Media.Brushes.LightYellow,
                                    Padding = new Thickness(8),
                                    Child = tb
                                },
                                IsLightDismissEnabled = false
                            };
                        }
                        else
                        {
                            if (st.Popup.Child is Border b && b.Child is TextBlock tb)
                                tb.Text = text;
                        }

                        // Close any other open tooltip so only one is visible at a time
                        if (_currentOpenControl != null && !ReferenceEquals(_currentOpenControl, control))
                        {
                            if (_states.TryGetValue(_currentOpenControl, out State prevSt) && prevSt?.Popup != null)
                            {
                                try { prevSt.Popup.IsOpen = false; } catch { }
                            }
                            _currentOpenControl = null;
                        }

                        st.Popup.IsOpen = true;
                        _currentOpenControl = control;
                    });
                }
                catch (OperationCanceledException) { }
                catch { }
            }, CancellationToken.None);
        }

        private static void CancelAndHide(Control control)
        {
            if (control == null) return;
            if (_states.TryGetValue(control, out State st))
            {
                try { st.Cts?.Cancel(); } catch { }
                // clear CTS so next PointerMoved can start a new timer
                st.Cts = null;
                // record cancel time to suppress immediate re-show
                st.LastCancelTicksUtc = DateTime.UtcNow.Ticks;
                if (st.Popup != null)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        try { st.Popup.IsOpen = false; } catch { }
                        // if this control was the current open, clear it
                        if (ReferenceEquals(_currentOpenControl, control))
                            _currentOpenControl = null;
                    });
                }
            }
        }

        private static void OnAttached(Control control)
        {
            if (control == null) return;
            if (_states.TryGetValue(control, out State st))
            {
                // Ensure popup has correct placement target when control is re-attached
                if (st.Popup != null)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        try { st.Popup.PlacementTarget = control; } catch { }
                    });
                }
            }
        }
    }
}