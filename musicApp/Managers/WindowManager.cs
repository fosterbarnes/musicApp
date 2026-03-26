using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace musicApp
{
    public class WindowManager
    {
        // ===========================================
        // WINDOWS API IMPORTS FOR TASKBAR DETECTION
        // ===========================================
        [DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        // ===========================================
        // WINDOWS API IMPORTS FOR MINIMIZE/RESTORE ANIMATION WORKAROUND
        // ===========================================
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int wMsg, IntPtr wParam, IntPtr lParam);

        // ===========================================
        // API CONSTANTS FOR MINIMIZE/RESTORE ANIMATION WORKAROUND
        // ===========================================
        internal class ApiCodes
        {
            public const int SC_MINIMIZE = 0xF020;
            public const int SC_CLOSE = 0xF060;
            public const int WM_SYSCOMMAND = 0x0112;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const uint SPI_GETWORKAREA = 0x0030;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int SW_MAXIMIZE = 3;

        // ===========================================
        // WINDOW STATE TRACKING
        // ===========================================
        private bool isCustomMaximized = false;
        private Rect normalWindowBounds;
        private Window window;
        private UserControl titleBarPlayer;
        private IntPtr hWnd; // Window handle for API calls
        private HwndSource? hwndSource;
        private bool _windowNativeLifecycleWired;
        private DispatcherTimer? saveTimer;
        private bool isDirty = false;
        private bool isRestoring = false; // Flag to prevent bounds updates during restore
        private bool isMaximizing = false; // Flag to prevent bounds updates during maximize

        // Event to notify when window state should be saved
        public event EventHandler? WindowStateChanged;

        public bool IsCustomMaximized => isCustomMaximized;
        public Rect NormalWindowBounds => normalWindowBounds;

        public WindowManager(Window window, UserControl titleBarPlayer)
        {
            this.window = window;
            this.titleBarPlayer = titleBarPlayer;
            
            // Get the window handle when the window is loaded
            window.Loaded += Window_Loaded;
        }

        // ===========================================
        // WINDOW LOADED EVENT HANDLER
        // ===========================================
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_windowNativeLifecycleWired)
                return;
            _windowNativeLifecycleWired = true;

            hWnd = new WindowInteropHelper(window).Handle;
            
            if (hWnd != IntPtr.Zero)
            {
                hwndSource = HwndSource.FromHwnd(hWnd);
                hwndSource.AddHook(WindowProc);
            }

            window.Closed += Window_Closed;
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            window.Closed -= Window_Closed;
            if (saveTimer != null)
            {
                try
                {
                    saveTimer.Stop();
                    saveTimer.Tick -= SaveTimer_Tick;
                }
                catch { /* ignore */ }
                saveTimer = null;
            }

            if (hwndSource != null)
            {
                try { hwndSource.RemoveHook(WindowProc); } catch { /* ignore */ }
                hwndSource = null;
            }
        }

        // ===========================================
        // WINDOW PROCEDURE HOOK FOR ANIMATION WORKAROUND
        // ===========================================
        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == ApiCodes.WM_SYSCOMMAND)
            {
                if (wParam.ToInt32() == ApiCodes.SC_MINIMIZE)
                {
                    // Temporarily change window style to enable minimize animation
                    window.WindowStyle = WindowStyle.SingleBorderWindow;
                    window.WindowState = WindowState.Minimized;
                    handled = true;
                }
                else if (wParam.ToInt32() == ApiCodes.SC_CLOSE)
                {
                    // Temporarily change window style to enable close animation
                    window.WindowStyle = WindowStyle.SingleBorderWindow;
                    
                    // Use a dispatcher callback to close the window after the animation starts
                    // This ensures the animation plays before the window actually closes
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (DispatcherOperationCallback)delegate (object unused)
                    {
                        window.Close();
                        return null;
                    }, null);
                    
                    handled = true;
                }
                // Don't handle SC_RESTORE here - let Windows handle it naturally
                // The OnActivated event in MainWindow will restore the window style
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Sets the initial window position before the window is shown
        /// This prevents the window from appearing in the center first
        /// </summary>
        public void SetInitialPosition(double left, double top, double width, double height)
        {
            window.Left = left;
            window.Top = top;
            window.Width = width;
            window.Height = height;
            
            // Store these as initial bounds
            normalWindowBounds = new Rect(left, top, width, height);
        }

        /// <summary>
        /// Initializes the window state tracking
        /// </summary>
        public void InitializeWindowState()
        {
            // Store initial window bounds
            normalWindowBounds = new Rect(window.Left, window.Top, window.Width, window.Height);
            
            // Check if window starts maximized
            if (window.WindowState == WindowState.Maximized)
            {
                isCustomMaximized = true;
                UpdateWindowStateIcon(WindowState.Maximized);
            }
        }

        /// <summary>
        /// Minimizes the window using the animation workaround
        /// </summary>
        public void MinimizeWindow()
        {
            System.Diagnostics.Debug.WriteLine("WindowManager: Minimizing window with animation workaround");
            
            if (hWnd != IntPtr.Zero)
            {
                // Use the system command to trigger the minimize animation
                SendMessage(hWnd, ApiCodes.WM_SYSCOMMAND, new IntPtr(ApiCodes.SC_MINIMIZE), IntPtr.Zero);
            }
            else
            {
                // Fallback to direct minimize if handle is not available
                System.Diagnostics.Debug.WriteLine("WindowManager: MinimizeWindow - No window handle available, using fallback");
                window.WindowState = WindowState.Minimized;
            }
        }

        /// <summary>
        /// Closes the window using the animation workaround
        /// </summary>
        public void CloseWindowWithAnimation()
        {
            System.Diagnostics.Debug.WriteLine("WindowManager: Closing window with animation workaround");
            
            if (hWnd != IntPtr.Zero)
            {
                // Use the system command to trigger the close animation
                SendMessage(hWnd, ApiCodes.WM_SYSCOMMAND, new IntPtr(ApiCodes.SC_CLOSE), IntPtr.Zero);
            }
            else
            {
                // Fallback to direct close if handle is not available
                System.Diagnostics.Debug.WriteLine("WindowManager: CloseWindowWithAnimation - No window handle available, using fallback");
                window.Close();
            }
        }

        /// <summary>
        /// Maximizes or restores the window based on current state
        /// </summary>
        public void ToggleMaximize()
        {
            if (isCustomMaximized)
            {
                RestoreWindow();
            }
            else
            {
                MaximizeToWorkArea();
            }
        }

        /// <summary>
        /// Maximizes the window to the work area (screen area excluding taskbar)
        /// </summary>
        public void MaximizeToWorkArea()
        {
            // Set maximizing flag to prevent OnLocationChanged/OnSizeChanged from updating normalWindowBounds
            isMaximizing = true;
            
            // Store current window bounds for restoration before maximizing
            normalWindowBounds = new Rect(window.Left, window.Top, window.Width, window.Height);
            
            // Get the work area (screen area excluding taskbar)
            RECT workArea = new RECT();
            if (SystemParametersInfo(SPI_GETWORKAREA, 0, ref workArea, 0))
            {
                // Ensure we're in normal state for custom positioning
                window.WindowState = WindowState.Normal;
                
                // Extend the window by 6 pixels on each side to compensate for gaps
                const int gapCompensation = 6;
                const int topExtraCompensation = 6;
                const int rightExtraCompensation = -12;
                const int bottomExtraCompensation = topExtraCompensation - 18;
                
                window.Left = workArea.Left;
                window.Top = workArea.Top;
                window.Width = (workArea.Right - workArea.Left) + (gapCompensation * 2) + rightExtraCompensation; 
                window.Height = (workArea.Bottom - workArea.Top) + (gapCompensation * 2) + bottomExtraCompensation;
                
                isCustomMaximized = true;
                UpdateWindowStateIcon(WindowState.Maximized);
                
                // Clear the maximizing flag after a short delay
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (DispatcherOperationCallback)delegate (object unused)
                {
                    isMaximizing = false;
                    return null;
                }, null);
            }
            else
            {
                // Fallback to standard maximize
                window.WindowState = WindowState.Maximized;
                UpdateWindowStateIcon(WindowState.Maximized);
                isMaximizing = false;
            }
        }

        /// <summary>
        /// Restores the window to its previous normal state
        /// </summary>
        public void RestoreWindow()
        {
            // Validate that normalWindowBounds has valid values
            if (normalWindowBounds.Width <= 0 || normalWindowBounds.Height <= 0 || 
                double.IsNaN(normalWindowBounds.Width) || double.IsNaN(normalWindowBounds.Height) ||
                double.IsInfinity(normalWindowBounds.Width) || double.IsInfinity(normalWindowBounds.Height))
            {
                // Use default bounds if stored bounds are invalid
                normalWindowBounds = new Rect(100, 100, 1200, 700);
            }
            
            // Set the restoring flag to prevent bounds updates during restore
            isRestoring = true;
            isCustomMaximized = false;
            
            // Ensure we're in normal state first
            window.WindowState = WindowState.Normal;
            window.UpdateLayout();
            
            // Restore to previous bounds
            window.Left = normalWindowBounds.Left;
            window.Top = normalWindowBounds.Top;
            window.Width = normalWindowBounds.Width;
            window.Height = normalWindowBounds.Height;
            
            window.UpdateLayout();
            
            // Update the window state icon
            UpdateWindowStateIcon(WindowState.Normal);
            
            // Clear the restoring flag after a short delay
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (DispatcherOperationCallback)delegate (object unused)
            {
                isRestoring = false;
                return null;
            }, null);
        }

        /// <summary>
        /// Checks if the window is currently maximized (either custom or standard)
        /// </summary>
        /// <returns>True if the window is maximized, false otherwise</returns>
        public bool IsWindowMaximized()
        {
            return isCustomMaximized || window.WindowState == WindowState.Maximized;
        }

        /// <summary>
        /// Updates the window state tracking when the window state changes externally
        /// </summary>
        public void OnStateChanged()
        {
            // Skip processing during restore operations
            if (isRestoring)
            {
                return;
            }
            
            // If the window state was changed externally (e.g., by double-clicking title bar),
            // update our custom tracking
            if (window.WindowState == WindowState.Normal && isCustomMaximized)
            {
                isCustomMaximized = false;
                UpdateWindowStateIcon(WindowState.Normal);
            }
            else if (window.WindowState == WindowState.Maximized && !isCustomMaximized)
            {
                isCustomMaximized = true;
                UpdateWindowStateIcon(WindowState.Maximized);
            }
            else if (window.WindowState == WindowState.Normal && !isCustomMaximized)
            {
                // Window is in normal state, but check if it's actually visually maximized
                // This can happen when restoring a minimized maximized window
                CheckIfWindowIsVisuallyMaximized();
            }
        }

        /// <summary>
        /// Checks if the window is visually maximized even though WindowState is Normal
        /// This can happen when restoring a minimized maximized window
        /// </summary>
        public void CheckIfWindowIsVisuallyMaximized()
        {
            // Skip processing during restore operations
            if (isRestoring)
            {
                return;
            }
            
            // Get the work area to compare against
            RECT workArea = new RECT();
            if (SystemParametersInfo(SPI_GETWORKAREA, 0, ref workArea, 0))
            {
                // Calculate the expected maximized dimensions (with our gap compensation)
                const int gapCompensation = 6;
                const int topExtraCompensation = 6;
                const int rightExtraCompensation = -12;
                const int bottomExtraCompensation = topExtraCompensation - 18;
                
                double expectedWidth = (workArea.Right - workArea.Left) + (gapCompensation * 2) + rightExtraCompensation;
                double expectedHeight = (workArea.Bottom - workArea.Top) + (gapCompensation * 2) + bottomExtraCompensation;
                
                // Check if current window dimensions match maximized dimensions (with some tolerance)
                const double tolerance = 5.0;
                bool isVisuallyMaximized = Math.Abs(window.Width - expectedWidth) <= tolerance &&
                                         Math.Abs(window.Height - expectedHeight) <= tolerance &&
                                         Math.Abs(window.Left - workArea.Left) <= tolerance &&
                                         Math.Abs(window.Top - workArea.Top) <= tolerance;
                
                if (isVisuallyMaximized && !isCustomMaximized)
                {
                    isCustomMaximized = true;
                    UpdateWindowStateIcon(WindowState.Maximized);
                }
                else if (!isVisuallyMaximized && isCustomMaximized)
                {
                    isCustomMaximized = false;
                    UpdateWindowStateIcon(WindowState.Normal);
                }
            }
        }

        /// <summary>
        /// Handles window location and size changes to update state tracking
        /// </summary>
        public void OnLocationChanged()
        {
            // If the window is moved while custom maximized, it should be restored
            if (isCustomMaximized && window.WindowState != WindowState.Maximized)
            {
                isCustomMaximized = false;
                UpdateWindowStateIcon(WindowState.Normal);
            }
            
            // Update normal window bounds when window is moved (but not during restore or maximize operations)
            if (window.WindowState == WindowState.Normal && !isRestoring && !isMaximizing)
            {
                normalWindowBounds = new Rect(window.Left, window.Top, window.Width, window.Height);
                MarkDirty();
            }
        }

        /// <summary>
        /// Handles window size changes to update state tracking
        /// </summary>
        public void OnSizeChanged()
        {
            // Update normal window bounds when window is resized (but not during restore or maximize operations)
            if (window.WindowState == WindowState.Normal && !isRestoring && !isMaximizing)
            {
                normalWindowBounds = new Rect(window.Left, window.Top, window.Width, window.Height);
                MarkDirty();
            }
        }

        /// <summary>
        /// Closes the window
        /// </summary>
        public void CloseWindow()
        {
            System.Diagnostics.Debug.WriteLine("WindowManager: CloseWindow - Closing window with animation");
            CloseWindowWithAnimation();
        }

        /// <summary>
        /// Restores window state from settings
        /// </summary>
        public void RestoreWindowState(double width, double height, double left, double top, bool isMaximized)
        {
            System.Diagnostics.Debug.WriteLine($"WindowManager: RestoreWindowState - Restoring from settings: Width={width}, Height={height}, Left={left}, Top={top}, IsMaximized={isMaximized}");
            
            // Only update if the position has changed significantly to prevent unnecessary flicker
            bool positionChanged = Math.Abs(window.Left - left) > 1 || Math.Abs(window.Top - top) > 1;
            bool sizeChanged = Math.Abs(window.Width - width) > 1 || Math.Abs(window.Height - height) > 1;
            
            if (positionChanged || sizeChanged)
            {
                System.Diagnostics.Debug.WriteLine($"WindowManager: RestoreWindowState - Applying changes: Position={positionChanged}, Size={sizeChanged}");
                
                window.Width = width;
                window.Height = height;
                window.Left = left;
                window.Top = top;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("WindowManager: RestoreWindowState - No significant changes, skipping update");
            }
            
            // Store these bounds as our normal window bounds
            normalWindowBounds = new Rect(window.Left, window.Top, window.Width, window.Height);
            System.Diagnostics.Debug.WriteLine($"WindowManager: RestoreWindowState - Stored normal bounds: {normalWindowBounds}");
            
            if (isMaximized)
            {
                System.Diagnostics.Debug.WriteLine("WindowManager: RestoreWindowState - Window was maximized, applying maximize");
                // Use MaximizeToWorkArea to apply the same gap compensation adjustments
                MaximizeToWorkArea();
            }
        }

        /// <summary>
        /// Resets window state to default values
        /// </summary>
        public void ResetWindowState()
        {
            System.Diagnostics.Debug.WriteLine("WindowManager: ResetWindowState - Resetting to default values");
            window.Width = 1200;
            window.Height = 700;
            window.Left = 100;
            window.Top = 100;
            window.WindowState = WindowState.Normal;
            isCustomMaximized = false;
            System.Diagnostics.Debug.WriteLine($"WindowManager: ResetWindowState - Window reset to: Left={window.Left}, Top={window.Top}, Width={window.Width}, Height={window.Height}");
            UpdateWindowStateIcon(WindowState.Normal);
        }

        /// <summary>
        /// Updates the window state icon in the title bar player
        /// </summary>
        private void UpdateWindowStateIcon(WindowState state)
        {
            // Use reflection to call the UpdateWindowStateIcon method on the titleBarPlayer
            var method = titleBarPlayer.GetType().GetMethod("UpdateWindowStateIcon");
            if (method != null)
            {
                method.Invoke(titleBarPlayer, new object[] { state });
            }
        }

        /// <summary>
        /// Gets the current window state for saving
        /// </summary>
        public SettingsManager.WindowStateSettings GetCurrentWindowState()
        {
            return new SettingsManager.WindowStateSettings
            {
                IsMaximized = isCustomMaximized,
                Width = normalWindowBounds.Width,
                Height = normalWindowBounds.Height,
                Left = normalWindowBounds.Left,
                Top = normalWindowBounds.Top
            };
        }


        /// <summary>
        /// Forces a refresh of the window state icon
        /// </summary>
        public void ForceRefreshWindowStateIcon()
        {
            UpdateWindowStateIcon(isCustomMaximized ? WindowState.Maximized : WindowState.Normal);
        }


        /// <summary>
        /// Marks the window state as dirty and starts the save timer
        /// </summary>
        public void MarkDirty()
        {
            isDirty = true;
            
            // Start the save timer if it's not already running
            if (saveTimer == null)
            {
                saveTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, SaveTimer_Tick, Dispatcher.CurrentDispatcher);
            }
            
            if (saveTimer != null && !saveTimer.IsEnabled)
            {
                saveTimer.Start();
            }
        }

        /// <summary>
        /// Timer callback to save the window state
        /// </summary>
        private void SaveTimer_Tick(object? sender, EventArgs e)
        {
            if (isDirty)
            {
                isDirty = false;
                saveTimer?.Stop();
                WindowStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
